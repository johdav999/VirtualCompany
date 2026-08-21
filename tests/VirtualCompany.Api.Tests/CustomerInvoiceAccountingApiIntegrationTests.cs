using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed class CustomerInvoiceAccountingApiIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Approved_invoice_is_previewed_submitted_exactly_once_posted_and_corrected_by_credit_note()
    {
        var seed = await SeedAsync();
        using var owner = Client(seed.OwnerSubject, seed.OwnerEmail);
        using var approver = Client(seed.ApproverSubject, seed.ApproverEmail);
        var accounting = new
        {
            fiscalPeriodId = seed.PeriodId,
            voucherSeriesCode = "G",
            exchangeRate = (decimal?)null,
            lines = new[] { new { description = "Consulting", amount = 100m, taxRuleKey = "generic-exempt" } }
        };

        using var preview = await owner.PostAsJsonAsync(Route(seed, "accounting/preview"), accounting);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        using var previewJson = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
        Assert.True(previewJson.RootElement.GetProperty("isReady").GetBoolean());
        Assert.Equal(100m, previewJson.RootElement.GetProperty("grossBaseAmount").GetDecimal());

        var submitRequest = new
        {
            accounting.fiscalPeriodId, accounting.voucherSeriesCode, accounting.exchangeRate, accounting.lines,
            expectedVersion = (long?)null, idempotencyKey = "invoice-submit-1"
        };
        using var submitted = await owner.PostAsJsonAsync(Route(seed, "accounting/submit"), submitRequest);
        var submittedBody = await submitted.Content.ReadAsStringAsync();
        Assert.True(submitted.IsSuccessStatusCode, submittedBody);
        using var submittedJson = JsonDocument.Parse(submittedBody);
        var approvalId = submittedJson.RootElement.GetProperty("approvalRequestId").GetGuid();
        var sourceVersion = submittedJson.RootElement.GetProperty("state").GetProperty("sourceVersion").GetInt64();

        using var replay = await owner.PostAsJsonAsync(Route(seed, "accounting/submit"), submitRequest);
        using var replayJson = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        Assert.True(replayJson.RootElement.GetProperty("isIdempotentReplay").GetBoolean());
        Assert.Equal(approvalId, replayJson.RootElement.GetProperty("approvalRequestId").GetGuid());

        using var staleSubmit = await owner.PostAsJsonAsync(Route(seed, "accounting/submit"), new
        {
            accounting.fiscalPeriodId, accounting.voucherSeriesCode, accounting.exchangeRate, accounting.lines,
            expectedVersion = sourceVersion + 1, idempotencyKey = "invoice-submit-stale"
        });
        Assert.Equal(HttpStatusCode.Conflict, staleSubmit.StatusCode);

        using var pendingPost = await owner.PostAsJsonAsync(Route(seed, "accounting/post"),
            new { expectedVersion = sourceVersion, idempotencyKey = "invoice-post-1" });
        Assert.Equal(HttpStatusCode.BadRequest, pendingPost.StatusCode);

        using var approval = await approver.GetAsync($"/api/companies/{seed.CompanyId:D}/approvals/{approvalId:D}");
        using var approvalJson = JsonDocument.Parse(await approval.Content.ReadAsStringAsync());
        var stepId = approvalJson.RootElement.GetProperty("steps")[0].GetProperty("id").GetGuid();
        using var decision = await approver.PostAsJsonAsync($"/api/companies/{seed.CompanyId:D}/approvals/{approvalId:D}/decisions",
            new { decision = "approve", stepId, comment = "Invoice accounting reviewed." });
        Assert.Equal(HttpStatusCode.OK, decision.StatusCode);

        var postRequest = new { expectedVersion = sourceVersion, idempotencyKey = "invoice-post-1" };
        using var posted = await owner.PostAsJsonAsync(Route(seed, "accounting/post"), postRequest);
        var postedBody = await posted.Content.ReadAsStringAsync();
        Assert.True(posted.IsSuccessStatusCode, postedBody);
        using var postedJson = JsonDocument.Parse(postedBody);
        Assert.Equal("posted", postedJson.RootElement.GetProperty("state").GetProperty("status").GetString());
        Assert.Equal("customer_invoice", postedJson.RootElement.GetProperty("journal").GetProperty("sourceType").GetString());

        using var postReplay = await owner.PostAsJsonAsync(Route(seed, "accounting/post"), postRequest);
        using var postReplayJson = JsonDocument.Parse(await postReplay.Content.ReadAsStringAsync());
        Assert.True(postReplayJson.RootElement.GetProperty("isIdempotentReplay").GetBoolean());

        using var reconciliation = await owner.GetAsync($"/internal/companies/{seed.CompanyId:D}/finance/accounting/reconciliation/receivables");
        using var reconciliationJson = JsonDocument.Parse(await reconciliation.Content.ReadAsStringAsync());
        Assert.True(reconciliationJson.RootElement.GetProperty("isReconciled").GetBoolean());
        Assert.Equal(100m, reconciliationJson.RootElement.GetProperty("postedDocumentReceivable").GetDecimal());
        Assert.Equal(100m, reconciliationJson.RootElement.GetProperty("postedJournalReceivable").GetDecimal());

        using var credit = await owner.PostAsJsonAsync(Route(seed, "credit-notes"), new
        {
            creditNoteNumber = "CN-1001", issueDate = seed.IssueDate, dueDate = seed.IssueDate.AddDays(14),
            reason = "Full cancellation", idempotencyKey = "credit-1001", accounting
        });
        var creditBody = await credit.Content.ReadAsStringAsync();
        Assert.True(credit.IsSuccessStatusCode, creditBody);
        using var creditJson = JsonDocument.Parse(creditBody);
        Assert.Equal(seed.InvoiceId, creditJson.RootElement.GetProperty("originalInvoiceId").GetGuid());
        Assert.Equal("awaiting_approval", creditJson.RootElement.GetProperty("status").GetString());

        var stored = await _factory.ExecuteDbContextAsync(async db => new
        {
            Journals = await db.LedgerEntries.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyId && x.SourceType == "customer_invoice"),
            Posted = await db.CustomerInvoiceAccountingProfiles.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyId && x.InvoiceId == seed.InvoiceId && x.Status == CustomerInvoiceAccountingStatuses.Posted),
            Credits = await db.CustomerInvoiceAccountingProfiles.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyId && x.OriginalInvoiceId == seed.InvoiceId)
        });
        Assert.Equal(1, stored.Journals);
        Assert.Equal(1, stored.Posted);
        Assert.Equal(1, stored.Credits);
    }

    [Fact]
    public async Task Invoice_accounting_write_is_tenant_scoped_and_requires_finance_approval_access()
    {
        var seed = await SeedAsync();
        using var employee = Client(seed.EmployeeSubject, seed.EmployeeEmail);
        using var owner = Client(seed.OwnerSubject, seed.OwnerEmail);
        var request = new { fiscalPeriodId = seed.PeriodId, voucherSeriesCode = "G", lines = new[] { new { description = "Line", amount = 100m, taxRuleKey = "generic-exempt" } }, idempotencyKey = "forbidden", expectedVersion = (long?)null };

        using var forbidden = await employee.PostAsJsonAsync(Route(seed, "accounting/submit"), request);
        using var foreign = await owner.GetAsync($"/internal/companies/{seed.OtherCompanyId:D}/finance/invoices/{seed.InvoiceId:D}/accounting");

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
    }

    private async Task<Seed> SeedAsync()
    {
        var seed = new Seed(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "invoice-owner", "invoice-owner@example.com",
            "invoice-approver", "invoice-approver@example.com", "invoice-employee", "invoice-employee@example.com", new DateOnly(2026, 8, 20));
        await _factory.SeedAsync(db =>
        {
            var now = seed.IssueDate.ToDateTime(new TimeOnly(8, 0), DateTimeKind.Utc);
            db.Companies.AddRange(new Company(seed.CompanyId, "Invoice Accounting Company"), new Company(seed.OtherCompanyId, "Other Company"));
            db.Users.AddRange(new User(seed.OwnerId, seed.OwnerEmail, "Owner", "dev-header", seed.OwnerSubject),
                new User(seed.ApproverId, seed.ApproverEmail, "Approver", "dev-header", seed.ApproverSubject),
                new User(seed.EmployeeId, seed.EmployeeEmail, "Employee", "dev-header", seed.EmployeeSubject));
            db.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), seed.CompanyId, seed.OwnerId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), seed.CompanyId, seed.ApproverId, CompanyMembershipRole.FinanceApprover, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), seed.CompanyId, seed.EmployeeId, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active));
            db.FinanceCounterparties.Add(new FinanceCounterparty(seed.CustomerId, seed.CompanyId, "Customer", "customer", createdUtc: now));
            db.FinanceAccounts.AddRange(
                Account(seed.ReceivableId, seed.CompanyId, "1100", FinanceAccountClassValues.Asset, FinanceNormalBalanceValues.Debit),
                Account(seed.RevenueId, seed.CompanyId, "4000", FinanceAccountClassValues.Income, FinanceNormalBalanceValues.Credit));
            var config = new AccountingConfiguration(seed.ConfigurationId, seed.CompanyId, "USD", 1, 1,
                AccountingPolicyPackDefaults.CountryNeutralPackKey, AccountingPolicyPackDefaults.CountryNeutralVersion,
                new DateOnly(2026, 1, 1), 2, AccountingRoundingModeValues.MidpointToEven, seed.OwnerId, now);
            config.SetSetupState(AccountingSetupStateValues.Ready, seed.OwnerId, now);
            db.AccountingConfigurations.Add(config);
            db.AccountingConfigurationAccountRoles.AddRange(
                new(Guid.NewGuid(), seed.CompanyId, seed.ConfigurationId, "accounts_receivable", seed.ReceivableId, now),
                new(Guid.NewGuid(), seed.CompanyId, seed.ConfigurationId, "revenue", seed.RevenueId, now));
            db.FiscalPeriods.Add(new FiscalPeriod(seed.PeriodId, seed.CompanyId, "August 2026",
                new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)));
            db.VoucherSeries.Add(new VoucherSeries(Guid.NewGuid(), seed.CompanyId, "G", "General", "G", true, now));
            db.CompanyKnowledgeDocuments.Add(new CompanyKnowledgeDocument(seed.DocumentId, seed.CompanyId, "Invoice evidence",
                CompanyKnowledgeDocumentType.Reference, "invoices/inv-1001.pdf", null, "inv-1001.pdf", "application/pdf", ".pdf", 512,
                new Dictionary<string, JsonNode?> { ["checksum_sha256"] = JsonValue.Create(new string('c', 64)) },
                new CompanyKnowledgeDocumentAccessScope(seed.CompanyId, CompanyKnowledgeDocumentAccessScope.CompanyVisibility)));
            db.FinanceInvoices.Add(new FinanceInvoice(seed.InvoiceId, seed.CompanyId, seed.CustomerId, "INV-1001", now, now.AddDays(30),
                100m, "USD", "approved", seed.DocumentId, now, now));
            return Task.CompletedTask;
        });
        return seed;
    }

    private static FinanceAccount Account(Guid id, Guid companyId, string code, string accountClass, string normalBalance) =>
        new(id, companyId, code, $"Account {code}", accountClass, "USD", 0m, DateTime.UtcNow,
            accountClass: accountClass, normalBalance: normalBalance, effectiveFrom: new DateOnly(2026, 1, 1), isPostingEnabled: true);
    private static string Route(Seed seed, string suffix) => $"/internal/companies/{seed.CompanyId:D}/finance/invoices/{seed.InvoiceId:D}/{suffix}";
    private HttpClient Client(string subject, string email)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.DisplayNameHeader, subject);
        return client;
    }

    private sealed record Seed(Guid CompanyId, Guid OtherCompanyId, Guid OwnerId, Guid ApproverId, Guid EmployeeId,
        Guid CustomerId, Guid InvoiceId, Guid PeriodId, Guid DocumentId, Guid ReceivableId, Guid RevenueId,
        string OwnerSubject, string OwnerEmail, string ApproverSubject, string ApproverEmail,
        string EmployeeSubject, string EmployeeEmail, DateOnly IssueDate)
    {
        public Guid ConfigurationId { get; } = Guid.NewGuid();
    }
}
