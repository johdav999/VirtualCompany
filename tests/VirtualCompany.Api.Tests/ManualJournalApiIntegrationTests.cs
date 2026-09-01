using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed class ManualJournalApiIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Draft_lifecycle_requires_exact_approval_then_posts_evidence_and_preserves_the_original()
    {
        var seed = await SeedAsync();
        using var owner = Client(seed.OwnerSubject, seed.OwnerEmail);
        using var approver = Client(seed.ApproverSubject, seed.ApproverEmail);
        var request = Request(seed, "create-1", 0, 120m);

        using var createdResponse = await owner.PostAsJsonAsync($"/internal/companies/{seed.CompanyId:D}/finance/accounting/manual-journals", request);
        Assert.Equal(HttpStatusCode.OK, createdResponse.StatusCode);
        using var createdJson = JsonDocument.Parse(await createdResponse.Content.ReadAsStringAsync());
        var draftId = createdJson.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(1, createdJson.RootElement.GetProperty("version").GetInt64());

        using var replayResponse = await owner.PostAsJsonAsync($"/internal/companies/{seed.CompanyId:D}/finance/accounting/manual-journals", request);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        using var replayJson = JsonDocument.Parse(await replayResponse.Content.ReadAsStringAsync());
        Assert.Equal(draftId, replayJson.RootElement.GetProperty("id").GetGuid());

        using var updateResponse = await owner.PutAsJsonAsync($"/internal/companies/{seed.CompanyId:D}/finance/accounting/manual-journals/{draftId:D}", Request(seed, "update-1", 1, 140m));
        var updateBody = await updateResponse.Content.ReadAsStringAsync();
        Assert.True(updateResponse.StatusCode == HttpStatusCode.OK, updateBody);
        using var updateJson = JsonDocument.Parse(updateBody);
        Assert.Equal(2, updateJson.RootElement.GetProperty("version").GetInt64());

        using var staleResponse = await owner.PutAsJsonAsync($"/internal/companies/{seed.CompanyId:D}/finance/accounting/manual-journals/{draftId:D}", Request(seed, "stale-1", 1, 150m));
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        using var staleJson = JsonDocument.Parse(await staleResponse.Content.ReadAsStringAsync());
        Assert.Equal(2, staleJson.RootElement.GetProperty("currentVersion").GetInt64());

        using var preview = await owner.PostAsJsonAsync($"/internal/companies/{seed.CompanyId:D}/finance/accounting/manual-journals/{draftId:D}/preview", new { expectedVersion = 2 });
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        using var previewJson = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
        Assert.True(previewJson.RootElement.GetProperty("postingPreview").GetProperty("isValid").GetBoolean());
        Assert.True(previewJson.RootElement.GetProperty("policy").GetProperty("requiresApproval").GetBoolean());

        using var submit = await owner.PostAsJsonAsync($"/internal/companies/{seed.CompanyId:D}/finance/accounting/manual-journals/{draftId:D}/submit", new { expectedVersion = 2, idempotencyKey = "submit-1" });
        var submitBody = await submit.Content.ReadAsStringAsync();
        Assert.True(submit.StatusCode == HttpStatusCode.OK, submitBody);
        using var submitJson = JsonDocument.Parse(submitBody);
        var approvalId = submitJson.RootElement.GetProperty("approvalRequestId").GetGuid();

        using var pendingPost = await owner.PostAsJsonAsync($"/internal/companies/{seed.CompanyId:D}/finance/accounting/manual-journals/{draftId:D}/post", new { expectedVersion = 2, idempotencyKey = "post-pending" });
        Assert.Equal(HttpStatusCode.BadRequest, pendingPost.StatusCode);

        using var approval = await approver.GetAsync($"/api/companies/{seed.CompanyId:D}/approvals/{approvalId:D}");
        using var approvalJson = JsonDocument.Parse(await approval.Content.ReadAsStringAsync());
        var stepId = approvalJson.RootElement.GetProperty("steps")[0].GetProperty("id").GetGuid();
        using var decision = await approver.PostAsJsonAsync($"/api/companies/{seed.CompanyId:D}/approvals/{approvalId:D}/decisions", new { decision = "approve", stepId, comment = "Evidence reviewed." });
        Assert.Equal(HttpStatusCode.OK, decision.StatusCode);

        using var posted = await owner.PostAsJsonAsync($"/internal/companies/{seed.CompanyId:D}/finance/accounting/manual-journals/{draftId:D}/post", new { expectedVersion = 2, idempotencyKey = "post-approved" });
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        using var postedJson = JsonDocument.Parse(await posted.Content.ReadAsStringAsync());
        var journalId = postedJson.RootElement.GetProperty("journal").GetProperty("id").GetGuid();
        Assert.Equal("posted", postedJson.RootElement.GetProperty("draft").GetProperty("status").GetString());
        Assert.Single(postedJson.RootElement.GetProperty("journal").GetProperty("evidence").EnumerateArray());

        using var forbiddenEdit = await owner.PutAsJsonAsync($"/internal/companies/{seed.CompanyId:D}/finance/accounting/manual-journals/{draftId:D}", Request(seed, "edit-posted", 2, 160m));
        Assert.Equal(HttpStatusCode.BadRequest, forbiddenEdit.StatusCode);

        var adjustmentRequest = Request(seed, "adjustment-1", 0, 140m, journalId,
            "Move the charge to the correct reporting category.", journalId);
        using var adjustment = await owner.PostAsJsonAsync($"/internal/companies/{seed.CompanyId:D}/finance/accounting/journals/{journalId:D}/adjustments", adjustmentRequest);
        Assert.Equal(HttpStatusCode.OK, adjustment.StatusCode);
        using var adjustmentJson = JsonDocument.Parse(await adjustment.Content.ReadAsStringAsync());
        Assert.Equal(journalId, adjustmentJson.RootElement.GetProperty("originalLedgerEntryId").GetGuid());
        var source = Assert.Single(adjustmentJson.RootElement.GetProperty("sourceRecords").EnumerateArray());
        Assert.Equal("ledger_journal", source.GetProperty("sourceType").GetString());
        Assert.Equal(journalId, source.GetProperty("recordId").GetGuid());
        Assert.Equal("source-version-1", source.GetProperty("sourceVersion").GetString());

        var stored = await _factory.ExecuteDbContextAsync(db => db.LedgerEntries.IgnoreQueryFilters().AsNoTracking().SingleAsync(item => item.Id == journalId));
        Assert.Equal("Manual accrual correction", stored.Description);
        var audit = await _factory.ExecuteDbContextAsync(db => db.AuditEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.CompanyId == seed.CompanyId && item.TargetId == draftId.ToString("N"))
            .Select(item => new { item.Action, item.CorrelationId }).ToArrayAsync());
        Assert.Contains(audit, item => item.Action == AuditEventActions.AccountingManualJournalDraftCreated);
        Assert.Contains(audit, item => item.Action == AuditEventActions.AccountingManualJournalApprovalRequested);
        Assert.All(audit, item => Assert.False(string.IsNullOrWhiteSpace(item.CorrelationId)));
    }

    [Fact]
    public async Task Manual_journal_endpoints_enforce_tenant_and_accounting_admin_boundaries()
    {
        var seed = await SeedAsync();
        using var employee = Client(seed.EmployeeSubject, seed.EmployeeEmail);
        using var owner = Client(seed.OwnerSubject, seed.OwnerEmail);

        using var forbiddenWrite = await employee.PostAsJsonAsync($"/internal/companies/{seed.CompanyId:D}/finance/accounting/manual-journals", Request(seed, "employee-write", 0, 100m));
        using var foreignRead = await owner.GetAsync($"/internal/companies/{seed.OtherCompanyId:D}/finance/accounting/manual-journals");

        Assert.Equal(HttpStatusCode.Forbidden, forbiddenWrite.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, foreignRead.StatusCode);
    }

    private async Task<Seed> SeedAsync()
    {
        var seed = new Seed(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), "manual-owner", "manual-owner@example.com", "manual-approver", "manual-approver@example.com",
            "manual-employee", "manual-employee@example.com", new DateOnly(2026, 8, 20));
        await _factory.SeedAsync(db =>
        {
            db.Companies.AddRange(new Company(seed.CompanyId, "Manual Journal Company"), new Company(seed.OtherCompanyId, "Other Company"));
            db.Users.AddRange(new User(seed.OwnerId, seed.OwnerEmail, "Owner", "dev-header", seed.OwnerSubject),
                new User(seed.ApproverId, seed.ApproverEmail, "Approver", "dev-header", seed.ApproverSubject),
                new User(seed.EmployeeId, seed.EmployeeEmail, "Employee", "dev-header", seed.EmployeeSubject));
            db.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), seed.CompanyId, seed.OwnerId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), seed.CompanyId, seed.ApproverId, CompanyMembershipRole.FinanceApprover, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), seed.CompanyId, seed.EmployeeId, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active));
            db.FinanceAccounts.AddRange(Account(seed.DebitAccountId, seed.CompanyId, "5000", FinanceAccountClassValues.Expense, FinanceNormalBalanceValues.Debit),
                Account(seed.CreditAccountId, seed.CompanyId, "2000", FinanceAccountClassValues.Liability, FinanceNormalBalanceValues.Credit));
            db.FiscalPeriods.Add(new FiscalPeriod(seed.FiscalPeriodId, seed.CompanyId, "August 2026", new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)));
            db.VoucherSeries.Add(new VoucherSeries(Guid.NewGuid(), seed.CompanyId, "G", "General journal", "G", true, DateTime.UtcNow));
            var config = new AccountingConfiguration(Guid.NewGuid(), seed.CompanyId, "USD", 1, 1,
                AccountingPolicyPackDefaults.CountryNeutralPackKey, AccountingPolicyPackDefaults.CountryNeutralVersion,
                new DateOnly(2026, 1, 1), 2, AccountingRoundingModeValues.MidpointToEven, seed.OwnerId, DateTime.UtcNow);
            config.SetSetupState(AccountingSetupStateValues.Ready, seed.OwnerId, DateTime.UtcNow);
            db.AccountingConfigurations.Add(config);
            db.FinancePolicyConfigurations.Add(new FinancePolicyConfiguration(Guid.NewGuid(), seed.CompanyId, "USD", 1000m, 100m, true));
            db.CompanyKnowledgeDocuments.Add(new CompanyKnowledgeDocument(seed.DocumentId, seed.CompanyId, "Accrual support", CompanyKnowledgeDocumentType.Reference,
                "manual-journal/accrual.pdf", null, "accrual.pdf", "application/pdf", ".pdf", 1024,
                new Dictionary<string, JsonNode?> { ["checksum_sha256"] = JsonValue.Create(new string('a', 64)) },
                new CompanyKnowledgeDocumentAccessScope(seed.CompanyId, CompanyKnowledgeDocumentAccessScope.CompanyVisibility)));
            return Task.CompletedTask;
        });
        return seed;
    }

    private static object Request(Seed seed, string key, long expectedVersion, decimal amount, Guid? originalId = null,
        string? correctionReason = null, Guid? sourceRecordId = null) => new
    {
        expectedVersion, idempotencyKey = key, fiscalPeriodId = seed.FiscalPeriodId, voucherSeriesCode = "G",
        documentDate = seed.PostingDate, postingDate = seed.PostingDate, explanation = "Manual accrual correction", currency = "USD",
        lines = new[] { new { financeAccountId = seed.DebitAccountId, debitAmount = amount, creditAmount = 0m, description = "Accrual expense" }, new { financeAccountId = seed.CreditAccountId, debitAmount = 0m, creditAmount = amount, description = "Accrued liability" } },
        evidenceDocumentIds = new[] { seed.DocumentId }, originalLedgerEntryId = originalId, correctionReason,
        sourceRecords = sourceRecordId.HasValue
            ? new[] { new { sourceType = "ledger_journal", recordId = sourceRecordId.Value, sourceVersion = "source-version-1" } }
            : []
    };
    private static FinanceAccount Account(Guid id, Guid companyId, string code, string accountClass, string normalBalance) => new(id, companyId, code, $"Account {code}", accountClass, "USD", 0m, DateTime.UtcNow, accountClass: accountClass, normalBalance: normalBalance, effectiveFrom: new DateOnly(2026, 1, 1), isPostingEnabled: true);
    private HttpClient Client(string subject, string email) { var client = _factory.CreateClient(); client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.SubjectHeader, subject); client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.EmailHeader, email); client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.DisplayNameHeader, subject); return client; }
    private sealed record Seed(Guid CompanyId, Guid OtherCompanyId, Guid OwnerId, Guid ApproverId, Guid EmployeeId, Guid FiscalPeriodId, Guid DocumentId,
        string OwnerSubject, string OwnerEmail, string ApproverSubject, string ApproverEmail, string EmployeeSubject, string EmployeeEmail, DateOnly PostingDate)
    {
        public Guid DebitAccountId { get; } = Guid.NewGuid(); public Guid CreditAccountId { get; } = Guid.NewGuid();
    }
}
