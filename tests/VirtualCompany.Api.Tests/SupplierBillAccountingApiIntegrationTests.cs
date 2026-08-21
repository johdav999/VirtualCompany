using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed class SupplierBillAccountingApiIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Approved_bill_posts_natively_once_without_a_provider_connection_and_reconciles_payables()
    {
        var seed = await SeedAsync();
        using var owner = Client(seed.OwnerSubject, seed.OwnerEmail);
        using var approver = Client(seed.ApproverSubject, seed.ApproverEmail);
        var accounting = new
        {
            fiscalPeriodId = seed.PeriodId,
            voucherSeriesCode = "G",
            exchangeRate = (decimal?)null,
            lines = new[] { new { description = "Office supplies", amount = 100m, costAccountId = seed.ExpenseId, taxRuleKey = "generic-exempt" } }
        };

        using var preview = await owner.PostAsJsonAsync(Route(seed, "accounting/preview"), accounting);
        var previewBody = await preview.Content.ReadAsStringAsync();
        Assert.True(preview.IsSuccessStatusCode, previewBody);
        using var previewJson = JsonDocument.Parse(previewBody);
        Assert.True(previewJson.RootElement.GetProperty("isReady").GetBoolean());
        Assert.Equal(100m, previewJson.RootElement.GetProperty("grossBaseAmount").GetDecimal());

        using var submitted = await owner.PostAsJsonAsync(Route(seed, "accounting/submit"), new
        {
            accounting.fiscalPeriodId, accounting.voucherSeriesCode, accounting.exchangeRate, accounting.lines,
            expectedVersion = (long?)null, idempotencyKey = "supplier-bill-submit-1"
        });
        var submittedBody = await submitted.Content.ReadAsStringAsync();
        Assert.True(submitted.IsSuccessStatusCode, submittedBody);
        using var submittedJson = JsonDocument.Parse(submittedBody);
        var approvalId = submittedJson.RootElement.GetProperty("approvalRequestId").GetGuid();
        var version = submittedJson.RootElement.GetProperty("state").GetProperty("sourceVersion").GetInt64();

        using var approval = await approver.GetAsync($"/api/companies/{seed.CompanyId:D}/approvals/{approvalId:D}");
        using var approvalJson = JsonDocument.Parse(await approval.Content.ReadAsStringAsync());
        var stepId = approvalJson.RootElement.GetProperty("steps")[0].GetProperty("id").GetGuid();
        using var decision = await approver.PostAsJsonAsync($"/api/companies/{seed.CompanyId:D}/approvals/{approvalId:D}/decisions",
            new { decision = "approve", stepId, comment = "Supplier bill accounting reviewed." });
        Assert.Equal(HttpStatusCode.OK, decision.StatusCode);

        var postRequest = new { expectedVersion = version, idempotencyKey = "supplier-bill-post-1" };
        using var posted = await owner.PostAsJsonAsync(Route(seed, "accounting/post"), postRequest);
        var postedBody = await posted.Content.ReadAsStringAsync();
        Assert.True(posted.IsSuccessStatusCode, postedBody);
        using var postedJson = JsonDocument.Parse(postedBody);
        Assert.Equal("posted", postedJson.RootElement.GetProperty("state").GetProperty("status").GetString());
        Assert.Equal("supplier_bill", postedJson.RootElement.GetProperty("journal").GetProperty("sourceType").GetString());

        using var replay = await owner.PostAsJsonAsync(Route(seed, "accounting/post"), postRequest);
        using var replayJson = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        Assert.True(replayJson.RootElement.GetProperty("isIdempotentReplay").GetBoolean());

        using var reconciliation = await owner.GetAsync($"/internal/companies/{seed.CompanyId:D}/finance/accounting/reconciliation/payables");
        using var reconciliationJson = JsonDocument.Parse(await reconciliation.Content.ReadAsStringAsync());
        Assert.True(reconciliationJson.RootElement.GetProperty("isReconciled").GetBoolean());
        Assert.Equal(100m, reconciliationJson.RootElement.GetProperty("postedDocumentPayables").GetDecimal());
        Assert.Equal(100m, reconciliationJson.RootElement.GetProperty("postedJournalPayables").GetDecimal());

        var stored = await _factory.ExecuteDbContextAsync(db => db.LedgerEntries.IgnoreQueryFilters()
            .CountAsync(x => x.CompanyId == seed.CompanyId && x.SourceType == "supplier_bill"));
        Assert.Equal(1, stored);
    }

    [Fact]
    public async Task Supplier_bill_accounting_is_tenant_scoped_and_requires_finance_approval_access()
    {
        var seed = await SeedAsync();
        using var employee = Client(seed.EmployeeSubject, seed.EmployeeEmail);
        using var owner = Client(seed.OwnerSubject, seed.OwnerEmail);
        var request = new
        {
            fiscalPeriodId = seed.PeriodId, voucherSeriesCode = "G",
            lines = new[] { new { description = "Office supplies", amount = 100m, costAccountId = seed.ExpenseId, taxRuleKey = "generic-exempt" } },
            expectedVersion = (long?)null, idempotencyKey = "forbidden"
        };

        using var forbidden = await employee.PostAsJsonAsync(Route(seed, "accounting/submit"), request);
        using var foreign = await owner.GetAsync($"/internal/companies/{seed.OtherCompanyId:D}/finance/bills/{seed.BillId:D}/accounting");

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
    }

    [Fact]
    public async Task Duplicate_bill_evidence_blocks_accounting_before_approval()
    {
        var seed = await SeedAsync();
        await _factory.SeedAsync(db =>
        {
            db.BillDuplicateChecks.Add(new BillDuplicateCheck(Guid.NewGuid(), seed.CompanyId, "Supplier", null,
                "BILL-1001", 100m, "USD", true, [Guid.NewGuid()],
                "Supplier, bill number, amount, currency, and date matched an earlier intake."));
            return Task.CompletedTask;
        });
        using var owner = Client(seed.OwnerSubject, seed.OwnerEmail);

        using var response = await owner.PostAsJsonAsync(Route(seed, "accounting/preview"), AccountingRequest(seed));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        using var json = JsonDocument.Parse(body);
        Assert.False(json.RootElement.GetProperty("isReady").GetBoolean());
        Assert.NotEmpty(json.RootElement.GetProperty("duplicateEvidence").EnumerateArray());
        Assert.Contains(json.RootElement.GetProperty("issues").EnumerateArray(), issue =>
            issue.GetProperty("reasonCode").GetString() == "supplier_bill_duplicate_detected" &&
            issue.GetProperty("isBlocking").GetBoolean());
    }

    [Fact]
    public async Task Changed_reviewed_bill_facts_reject_stale_approval_without_a_partial_journal()
    {
        var seed = await SeedAsync();
        using var owner = Client(seed.OwnerSubject, seed.OwnerEmail);
        using var approver = Client(seed.ApproverSubject, seed.ApproverEmail);
        var version = await SubmitAndApproveAsync(seed, owner, approver, "stale-facts");

        await _factory.SeedAsync(async db =>
        {
            var bill = await db.FinanceBills.IgnoreQueryFilters().SingleAsync(x => x.Id == seed.BillId);
            bill.ApplySyncedSnapshot(bill.CounterpartyId, bill.ReceivedUtc, bill.DueUtc, 120m, bill.Currency,
                bill.Status, bill.SettlementStatus, bill.PostingStatus, bill.DueStatus, bill.DocumentKind,
                bill.ProviderStatus, bill.ProcessingStatus, bill.PaidAmount);
        });

        using var response = await owner.PostAsJsonAsync(Route(seed, "accounting/post"),
            new { expectedVersion = version, idempotencyKey = "stale-facts-post" });
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("supplier_bill_accounting_approval_stale", body, StringComparison.Ordinal);
        var journals = await _factory.ExecuteDbContextAsync(db => db.LedgerEntries.IgnoreQueryFilters()
            .CountAsync(x => x.CompanyId == seed.CompanyId && x.SourceType == "supplier_bill"));
        Assert.Equal(0, journals);
    }

    [Fact]
    public async Task Native_supplier_credit_posts_a_balanced_linked_correction_and_preserves_the_original()
    {
        var seed = await SeedAsync();
        using var owner = Client(seed.OwnerSubject, seed.OwnerEmail);
        using var approver = Client(seed.ApproverSubject, seed.ApproverEmail);
        var originalVersion = await SubmitAndApproveAsync(seed, owner, approver, "credit-original");
        using var originalPost = await owner.PostAsJsonAsync(Route(seed, "accounting/post"),
            new { expectedVersion = originalVersion, idempotencyKey = "credit-original-post" });
        var originalBody = await originalPost.Content.ReadAsStringAsync();
        Assert.True(originalPost.IsSuccessStatusCode, originalBody);
        using var originalJson = JsonDocument.Parse(originalBody);
        var originalJournalId = originalJson.RootElement.GetProperty("journal").GetProperty("id").GetGuid();

        using var created = await owner.PostAsJsonAsync(Route(seed, "native-credit-notes"), new
        {
            creditNoteNumber = "CN-BILL-1001",
            billDate = new DateOnly(2026, 8, 21),
            dueDate = new DateOnly(2026, 8, 21),
            reason = "Supplier corrected the full invoice.",
            idempotencyKey = "credit-note-create",
            accounting = AccountingRequest(seed)
        });
        var createdBody = await created.Content.ReadAsStringAsync();
        Assert.True(created.IsSuccessStatusCode, createdBody);
        using var createdJson = JsonDocument.Parse(createdBody);
        var creditBillId = createdJson.RootElement.GetProperty("billId").GetGuid();
        var creditVersion = createdJson.RootElement.GetProperty("sourceVersion").GetInt64();
        var creditApprovalId = createdJson.RootElement.GetProperty("approval").GetProperty("id").GetGuid();
        await ApproveRequestAsync(seed, approver, creditApprovalId);

        using var creditPost = await owner.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/bills/{creditBillId:D}/accounting/post",
            new { expectedVersion = creditVersion, idempotencyKey = "credit-note-post" });
        var creditBody = await creditPost.Content.ReadAsStringAsync();
        Assert.True(creditPost.IsSuccessStatusCode, creditBody);
        using var creditJson = JsonDocument.Parse(creditBody);
        var journal = creditJson.RootElement.GetProperty("journal");
        Assert.Equal(originalJournalId, journal.GetProperty("originalLedgerEntryId").GetGuid());
        Assert.Equal(journal.GetProperty("debitTotal").GetDecimal(), journal.GetProperty("creditTotal").GetDecimal());
        Assert.Equal(seed.BillId, creditJson.RootElement.GetProperty("state").GetProperty("originalBillId").GetGuid());
        var journals = await _factory.ExecuteDbContextAsync(db => db.LedgerEntries.IgnoreQueryFilters()
            .CountAsync(x => x.CompanyId == seed.CompanyId && x.SourceType == "supplier_bill"));
        Assert.Equal(2, journals);
    }

    private async Task<Seed> SeedAsync()
    {
        var seed = new Seed(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "bill-owner", "bill-owner@example.com", "bill-approver", "bill-approver@example.com",
            "bill-employee", "bill-employee@example.com");
        await _factory.SeedAsync(db =>
        {
            var now = new DateTime(2026, 8, 20, 8, 0, 0, DateTimeKind.Utc);
            db.Companies.AddRange(new Company(seed.CompanyId, "Supplier Accounting Company"), new Company(seed.OtherCompanyId, "Other Company"));
            db.Users.AddRange(new User(seed.OwnerId, seed.OwnerEmail, "Owner", "dev-header", seed.OwnerSubject),
                new User(seed.ApproverId, seed.ApproverEmail, "Approver", "dev-header", seed.ApproverSubject),
                new User(seed.EmployeeId, seed.EmployeeEmail, "Employee", "dev-header", seed.EmployeeSubject));
            db.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), seed.CompanyId, seed.OwnerId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), seed.CompanyId, seed.ApproverId, CompanyMembershipRole.FinanceApprover, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), seed.CompanyId, seed.EmployeeId, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active));
            db.FinanceCounterparties.Add(new FinanceCounterparty(seed.SupplierId, seed.CompanyId, "Supplier", "supplier", createdUtc: now));
            db.FinanceAccounts.AddRange(
                Account(seed.PayableId, seed.CompanyId, "2000", FinanceAccountClassValues.Liability, FinanceNormalBalanceValues.Credit, "accounts_payable"),
                Account(seed.ExpenseId, seed.CompanyId, "5000", FinanceAccountClassValues.Expense, FinanceNormalBalanceValues.Debit));
            var config = new AccountingConfiguration(seed.ConfigurationId, seed.CompanyId, "USD", 1, 1,
                AccountingPolicyPackDefaults.CountryNeutralPackKey, AccountingPolicyPackDefaults.CountryNeutralVersion,
                new DateOnly(2026, 1, 1), 2, AccountingRoundingModeValues.MidpointToEven, seed.OwnerId, now);
            config.SetSetupState(AccountingSetupStateValues.Ready, seed.OwnerId, now);
            db.AccountingConfigurations.Add(config);
            db.AccountingConfigurationAccountRoles.AddRange(
                new(Guid.NewGuid(), seed.CompanyId, seed.ConfigurationId, "accounts_payable", seed.PayableId, now),
                new(Guid.NewGuid(), seed.CompanyId, seed.ConfigurationId, "operating_expense", seed.ExpenseId, now));
            db.FiscalPeriods.Add(new FiscalPeriod(seed.PeriodId, seed.CompanyId, "August 2026",
                new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)));
            db.VoucherSeries.Add(new VoucherSeries(Guid.NewGuid(), seed.CompanyId, "G", "General", "G", true, now));
            db.CompanyKnowledgeDocuments.Add(new CompanyKnowledgeDocument(seed.DocumentId, seed.CompanyId, "Supplier bill evidence",
                CompanyKnowledgeDocumentType.Reference, "bills/bill-1001.pdf", null, "bill-1001.pdf", "application/pdf", ".pdf", 512,
                new Dictionary<string, JsonNode?> { ["checksum_sha256"] = JsonValue.Create(new string('d', 64)) },
                new CompanyKnowledgeDocumentAccessScope(seed.CompanyId, CompanyKnowledgeDocumentAccessScope.CompanyVisibility)));
            db.FinanceBills.Add(new FinanceBill(seed.BillId, seed.CompanyId, seed.SupplierId, "BILL-1001", now,
                now.AddDays(30), 100m, "USD", "approved", seed.DocumentId, now, now));
            return Task.CompletedTask;
        });
        return seed;
    }

    private static FinanceAccount Account(Guid id, Guid companyId, string code, string accountClass,
        string normalBalance, string? controlRole = null) =>
        new(id, companyId, code, $"Account {code}", accountClass, "USD", 0m, DateTime.UtcNow,
            accountClass: accountClass, normalBalance: normalBalance, effectiveFrom: new DateOnly(2026, 1, 1),
            isPostingEnabled: true, controlAccountRole: controlRole);
    private static string Route(Seed seed, string suffix) =>
        $"/internal/companies/{seed.CompanyId:D}/finance/bills/{seed.BillId:D}/{suffix}";

    private static object AccountingRequest(Seed seed) => new
    {
        fiscalPeriodId = seed.PeriodId,
        voucherSeriesCode = "G",
        exchangeRate = (decimal?)null,
        lines = new[]
        {
            new { description = "Office supplies", amount = 100m, costAccountId = seed.ExpenseId, taxRuleKey = "generic-exempt" }
        }
    };

    private async Task<long> SubmitAndApproveAsync(Seed seed, HttpClient owner, HttpClient approver, string key)
    {
        using var submitted = await owner.PostAsJsonAsync(Route(seed, "accounting/submit"), new
        {
            fiscalPeriodId = seed.PeriodId,
            voucherSeriesCode = "G",
            exchangeRate = (decimal?)null,
            lines = new[]
            {
                new { description = "Office supplies", amount = 100m, costAccountId = seed.ExpenseId, taxRuleKey = "generic-exempt" }
            },
            expectedVersion = (long?)null,
            idempotencyKey = $"{key}-submit"
        });
        var body = await submitted.Content.ReadAsStringAsync();
        Assert.True(submitted.IsSuccessStatusCode, body);
        using var submittedJson = JsonDocument.Parse(body);
        var approvalId = submittedJson.RootElement.GetProperty("approvalRequestId").GetGuid();
        var version = submittedJson.RootElement.GetProperty("state").GetProperty("sourceVersion").GetInt64();
        await ApproveRequestAsync(seed, approver, approvalId);
        return version;
    }

    private static async Task ApproveRequestAsync(Seed seed, HttpClient approver, Guid approvalId)
    {
        using var approval = await approver.GetAsync($"/api/companies/{seed.CompanyId:D}/approvals/{approvalId:D}");
        using var approvalJson = JsonDocument.Parse(await approval.Content.ReadAsStringAsync());
        var stepId = approvalJson.RootElement.GetProperty("steps")[0].GetProperty("id").GetGuid();
        using var decision = await approver.PostAsJsonAsync($"/api/companies/{seed.CompanyId:D}/approvals/{approvalId:D}/decisions",
            new { decision = "approve", stepId, comment = "Supplier bill accounting reviewed." });
        Assert.Equal(HttpStatusCode.OK, decision.StatusCode);
    }
    private HttpClient Client(string subject, string email)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.DisplayNameHeader, subject);
        return client;
    }

    private sealed record Seed(Guid CompanyId, Guid OtherCompanyId, Guid OwnerId, Guid ApproverId,
        Guid EmployeeId, Guid SupplierId, Guid BillId, Guid PeriodId, Guid DocumentId, Guid PayableId,
        Guid ExpenseId, string OwnerSubject, string OwnerEmail, string ApproverSubject, string ApproverEmail,
        string EmployeeSubject, string EmployeeEmail)
    {
        public Guid ConfigurationId { get; } = Guid.NewGuid();
    }
}
