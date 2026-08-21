using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VirtualCompany.Api.Tests;

public sealed class AccountingLedgerApiIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Authorized_owner_can_preview_post_replay_read_and_reverse_a_balanced_journal()
    {
        var seed = await SeedAsync();
        using var client = CreateClient(seed.OwnerSubject, seed.OwnerEmail);
        var request = BuildRequest(seed, "source-1", "post:source-1:1");

        using var preview = await client.PostAsJsonAsync($"/internal/companies/{seed.CompanyId:D}/finance/accounting/journals/preview", request);
        using var post = await client.PostAsJsonAsync($"/internal/companies/{seed.CompanyId:D}/finance/accounting/journals", request);
        using var replay = await client.PostAsJsonAsync($"/internal/companies/{seed.CompanyId:D}/finance/accounting/journals", request);
        using var postedJson = JsonDocument.Parse(await post.Content.ReadAsStringAsync());
        var journalId = postedJson.RootElement.GetProperty("journal").GetProperty("id").GetGuid();
        using var detail = await client.GetAsync($"/internal/companies/{seed.CompanyId:D}/finance/accounting/journals/{journalId:D}");
        using var bySource = await client.GetAsync($"/internal/companies/{seed.CompanyId:D}/finance/accounting/journals/by-source?sourceType=api_test&sourceId=source-1&sourceVersion=1");
        using var list = await client.GetAsync($"/internal/companies/{seed.CompanyId:D}/finance/accounting/journals");
        using var reversal = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/accounting/journals/{journalId:D}/reversal",
            new { fiscalPeriodId = seed.FiscalPeriodId, voucherSeriesCode = "G", postingDate = seed.PostingDate, reason = "Correct the test posting", sourceVersion = "1", idempotencyKey = "reverse:source-1:1" });

        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bySource.StatusCode);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reversal.StatusCode);
        using var replayJson = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        Assert.True(replayJson.RootElement.GetProperty("isIdempotentReplay").GetBoolean());
        using var reversalJson = JsonDocument.Parse(await reversal.Content.ReadAsStringAsync());
        Assert.Equal(journalId, reversalJson.RootElement.GetProperty("journal").GetProperty("originalLedgerEntryId").GetGuid());
    }

    [Fact]
    public async Task Unbalanced_post_has_stable_error_and_tenant_and_edit_authorization_are_enforced()
    {
        var seed = await SeedAsync();
        using var owner = CreateClient(seed.OwnerSubject, seed.OwnerEmail);
        using var employee = CreateClient(seed.EmployeeSubject, seed.EmployeeEmail);
        var request = BuildRequest(seed, "unbalanced", "post:unbalanced:1", credit: 99m);

        using var rejected = await owner.PostAsJsonAsync($"/internal/companies/{seed.CompanyId:D}/finance/accounting/journals", request);
        using var rejectedJson = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        using var employeePost = await employee.PostAsJsonAsync($"/internal/companies/{seed.CompanyId:D}/finance/accounting/journals", BuildRequest(seed, "employee", "post:employee:1"));
        using var crossTenantRead = await owner.GetAsync($"/internal/companies/{seed.UnownedCompanyId:D}/finance/accounting/journals");
        using var crossTenantPost = await owner.PostAsJsonAsync($"/internal/companies/{seed.UnownedCompanyId:D}/finance/accounting/journals", BuildRequest(seed, "foreign", "post:foreign:1"));

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(AccountingPostingReasonCodes.UnbalancedEntry, rejectedJson.RootElement.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, employeePost.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenantRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenantPost.StatusCode);
        var journalCount = await _factory.ExecuteDbContextAsync(db => db.LedgerEntries.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, journalCount);
    }

    private async Task<Seed> SeedAsync()
    {
        var companyId = Guid.NewGuid();
        var unownedCompanyId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var fiscalPeriodId = Guid.NewGuid();
        var debitAccountId = Guid.NewGuid();
        var creditAccountId = Guid.NewGuid();
        var postingDate = new DateOnly(2026, 8, 19);
        const string ownerSubject = "ledger-owner";
        const string employeeSubject = "ledger-employee";
        const string ownerEmail = "ledger-owner@example.com";
        const string employeeEmail = "ledger-employee@example.com";

        await _factory.SeedAsync(db =>
        {
            db.Users.AddRange(
                new User(ownerId, ownerEmail, "Ledger Owner", "dev-header", ownerSubject),
                new User(employeeId, employeeEmail, "Ledger Employee", "dev-header", employeeSubject));
            db.Companies.AddRange(new Company(companyId, "Ledger Company"), new Company(unownedCompanyId, "Unowned Ledger Company"));
            db.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, ownerId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, employeeId, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active));
            db.FinanceAccounts.AddRange(
                CreateAccount(debitAccountId, companyId, "1000", FinanceAccountClassValues.Asset, FinanceNormalBalanceValues.Debit),
                CreateAccount(creditAccountId, companyId, "3000", FinanceAccountClassValues.Equity, FinanceNormalBalanceValues.Credit));
            db.FiscalPeriods.Add(new FiscalPeriod(fiscalPeriodId, companyId, "August 2026",
                new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)));
            db.VoucherSeries.Add(new VoucherSeries(Guid.NewGuid(), companyId, "G", "General journal", "G", true, DateTime.UtcNow));
            var config = new AccountingConfiguration(Guid.NewGuid(), companyId, "USD", 1, 1,
                AccountingPolicyPackDefaults.CountryNeutralPackKey, AccountingPolicyPackDefaults.CountryNeutralVersion,
                new DateOnly(2026, 1, 1), 2, AccountingRoundingModeValues.MidpointToEven, ownerId, DateTime.UtcNow);
            config.SetSetupState(AccountingSetupStateValues.Ready, ownerId, DateTime.UtcNow);
            db.AccountingConfigurations.Add(config);
            return Task.CompletedTask;
        });

        return new Seed(companyId, unownedCompanyId, fiscalPeriodId, debitAccountId, creditAccountId, postingDate,
            ownerSubject, ownerEmail, employeeSubject, employeeEmail);
    }

    private static object BuildRequest(Seed seed, string sourceId, string idempotencyKey, decimal credit = 100m) => new
    {
        fiscalPeriodId = seed.FiscalPeriodId,
        voucherSeriesCode = "G",
        documentDate = seed.PostingDate,
        postingDate = seed.PostingDate,
        postingType = LedgerPostingTypeValues.SourceDocument,
        description = "API ledger test",
        sourceType = "api_test",
        sourceId,
        sourceVersion = "1",
        idempotencyKey,
        lines = new[]
        {
            new { financeAccountId = seed.DebitAccountId, debitAmount = 100m, creditAmount = 0m, currency = "USD" },
            new { financeAccountId = seed.CreditAccountId, debitAmount = 0m, creditAmount = credit, currency = "USD" }
        }
    };

    private static FinanceAccount CreateAccount(Guid id, Guid companyId, string code, string accountClass, string normalBalance) =>
        new(id, companyId, code, $"Account {code}", accountClass, "USD", 0m, DateTime.UtcNow,
            accountClass: accountClass, normalBalance: normalBalance, effectiveFrom: new DateOnly(2026, 1, 1), isPostingEnabled: true);

    private HttpClient CreateClient(string subject, string email)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.DisplayNameHeader, subject);
        return client;
    }

    private sealed record Seed(
        Guid CompanyId, Guid UnownedCompanyId, Guid FiscalPeriodId, Guid DebitAccountId, Guid CreditAccountId, DateOnly PostingDate,
        string OwnerSubject, string OwnerEmail, string EmployeeSubject, string EmployeeEmail);
}
