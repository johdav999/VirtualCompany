using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Api.Tests;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;

internal static class AccountingFixture
{
    public static readonly Guid CompanyId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    public static readonly Guid August = Guid.Parse("07070707-0707-0707-0707-070707070801");
    public static readonly Guid September = Guid.Parse("07070707-0707-0707-0707-070707070901");
    public static readonly Guid October = Guid.Parse("07070707-0707-0707-0707-070707071001");
    private static readonly Guid AccountantMembership = Guid.Parse("07070707-0707-0707-0707-070707070007");

    public static async Task InitializeAsync(TestWebApplicationFactory factory)
    {
        await factory.SeedAsync(async db =>
        {
            var owner = await db.Users.IgnoreQueryFilters().SingleAsync(x => x.Email == "p01-owner@example.com");
            var accountant = new User(Guid.NewGuid(), "p07-accountant@example.com", "P07 Accountant", "dev-header", "p07-accountant");
            var reviewer = new User(Guid.NewGuid(), "p07-reviewer@example.com", "P07 Grant Reviewer", "dev-header", "p07-reviewer");
            var company = new Company(CompanyId, "P07 Ledger Company");
            company.UpdateWorkspaceProfile(company.Name, null, null, "Europe/Stockholm", "SEK", "en-GB", "SE");
            company.CompleteOnboarding(1, null, "{}");
            company.SetFinanceSeedStatus(FinanceSeedingState.Seeded, DateTime.UtcNow, DateTime.UtcNow);
            db.Companies.Add(company); db.Users.AddRange(accountant, reviewer);
            var member = new CompanyMembership(Guid.NewGuid(), CompanyId, owner.Id, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active);
            db.CompanyMemberships.AddRange(member, new CompanyMembership(AccountantMembership, CompanyId, accountant.Id, CompanyMembershipRole.Accountant, CompanyMembershipStatus.Active));
            db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), CompanyId, reviewer.Id, CompanyMembershipRole.Admin, CompanyMembershipStatus.Active));
            db.CompanyResponsibilityAssignments.Add(new CompanyResponsibilityAssignment(Guid.NewGuid(), CompanyId,
                ResponsibilityArea.CashAndAccounting, ResponsibilityAssignmentKind.Primary, member.Id, null,
                AgentAutonomyLevel.Level1, null, null));
            var now = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
            var cash = Guid.Parse("07070707-0707-0707-0707-070707071930");
            var revenue = Guid.Parse("07070707-0707-0707-0707-070707073001");
            var expense = Guid.Parse("07070707-0707-0707-0707-070707075010");
            db.FinanceAccounts.AddRange(Account(cash, "1930", "Bank", "asset", "debit"),
                Account(revenue, "3001", "Sales", "income", "credit"), Account(expense, "5010", "Rent", "expense", "debit"));
            db.FinancialStatementMappings.AddRange(
                new FinancialStatementMapping(Guid.NewGuid(), CompanyId, cash, FinancialStatementType.BalanceSheet, FinancialStatementReportSection.BalanceSheetAssets, FinancialStatementLineClassification.CurrentAsset),
                new FinancialStatementMapping(Guid.NewGuid(), CompanyId, revenue, FinancialStatementType.ProfitAndLoss, FinancialStatementReportSection.ProfitAndLossRevenue, FinancialStatementLineClassification.Revenue),
                new FinancialStatementMapping(Guid.NewGuid(), CompanyId, expense, FinancialStatementType.ProfitAndLoss, FinancialStatementReportSection.ProfitAndLossOperatingExpenses, FinancialStatementLineClassification.OperatingExpense));
            var configuration = new AccountingConfiguration(Guid.NewGuid(), CompanyId, "SEK", 1, 1,
                AccountingPolicyPackDefaults.CountryNeutralPackKey, AccountingPolicyPackDefaults.CountryNeutralVersion,
                new(2026, 1, 1), 2, AccountingRoundingModeValues.MidpointToEven, owner.Id, now);
            configuration.SetSetupState(AccountingSetupStateValues.Ready, owner.Id, now);
            db.AccountingConfigurations.Add(configuration);
            db.FiscalPeriods.AddRange(new FiscalPeriod(August, CompanyId, "August 2026", now, now.AddMonths(1),
                isClosed: true),
                new FiscalPeriod(September, CompanyId, "September 2026", now.AddMonths(1), now.AddMonths(2)),
                new FiscalPeriod(October, CompanyId, "October 2026 (no postings)", now.AddMonths(2), now.AddMonths(3)));
            db.VoucherSeries.Add(new VoucherSeries(Guid.NewGuid(), CompanyId, "G", "General", "G", true, now));
            foreach (var (period, date, sales, rent) in new[] { (August, now.AddDays(10), 1000m, 400m), (September, now.AddMonths(1).AddDays(10), 1500m, 500m) })
            {
                var journal = new LedgerEntry(Guid.NewGuid(), CompanyId, period, "P07-" + date.ToString("yyyyMM"), date,
                    LedgerEntryStatuses.Posted, "Retained P07 balanced test journal", documentDate: DateOnly.FromDateTime(date),
                    postingDate: DateOnly.FromDateTime(date), postingType: LedgerPostingTypeValues.Manual, baseCurrency: "SEK",
                    postedByUserId: owner.Id, policyPackKey: configuration.PolicyPackKey, policyPackVersion: configuration.PolicyPackVersion);
                db.LedgerEntries.Add(journal);
                db.LedgerEntryLines.AddRange(new LedgerEntryLine(Guid.NewGuid(), CompanyId, journal.Id, cash, sales - rent, 0m, "SEK"),
                    new LedgerEntryLine(Guid.NewGuid(), CompanyId, journal.Id, expense, rent, 0m, "SEK"),
                    new LedgerEntryLine(Guid.NewGuid(), CompanyId, journal.Id, revenue, 0m, sales, "SEK"));
            }
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-Auth-Subject", "p01-owner");
        client.DefaultRequestHeaders.Add("X-Dev-Auth-Email", "p01-owner@example.com");
        var grantRoute = $"/api/companies/{CompanyId}/accountant-collaboration/grants";
        using var grantResponse = await client.PostAsJsonAsync(grantRoute, new { membershipId = AccountantMembership,
            scopeKey = AccountantGrantScopes.AccountingReview, canViewDocuments = true, canRequestEvidence = true, canSignOff = false,
            effectiveFromUtc = DateTime.UtcNow.AddMinutes(-5), effectiveUntilUtc = DateTime.UtcNow.AddDays(1) });
        if (!grantResponse.IsSuccessStatusCode) throw new InvalidOperationException("P07 accountant grant failed: " + await grantResponse.Content.ReadAsStringAsync());
        using var grantJson = System.Text.Json.JsonDocument.Parse(await grantResponse.Content.ReadAsStringAsync());
        using var reviewerClient = factory.CreateClient();
        reviewerClient.DefaultRequestHeaders.Add("X-Dev-Auth-Subject", "p07-reviewer");
        reviewerClient.DefaultRequestHeaders.Add("X-Dev-Auth-Email", "p07-reviewer@example.com");
        using var grantApproval = await reviewerClient.PostAsJsonAsync($"{grantRoute}/{grantJson.RootElement.GetProperty("id").GetGuid()}/approve",
            new { expectedVersion = grantJson.RootElement.GetProperty("version").GetInt64() });
        if (!grantApproval.IsSuccessStatusCode) throw new InvalidOperationException("P07 accountant grant approval failed: " + await grantApproval.Content.ReadAsStringAsync());
        var route = $"/api/companies/{CompanyId}/finance/accounting-close";
        var input = new AccountingCloseTemplateInput("P07_MONTH_END", "P07 month-end review", "Local controlled accounting review", 0m, null,
            [new("review", "Review", 1, [
                new("review_sources", "Review journal and statement evidence", "Review the posted journal and record the permitted next step.", 1, 0, null, "owner", false, null, null),
                new("review_close", "Record close review", "Review the source evidence before completion.", 2, 0, null, "owner", false, null, null, null, ["review_sources"])])]);
        var template = await PostAsync<AccountingCloseTemplateDto>(client, route + "/templates", new { template = input, idempotencyKey = "p07-template" });
        template = await PostAsync<AccountingCloseTemplateDto>(client, $"{route}/templates/{template.Id}/versions/{template.Versions.Single().Id}/activate",
            new { expectedVersion = template.Version, idempotencyKey = "p07-activate" });
        await PostAsync<AccountingCloseDto>(client, route + "/instances", new { fiscalPeriodId = September,
            templateId = template.Id, templateVersionId = template.ActiveVersionId, idempotencyKey = "p07-start" });
        using var regenerate = await client.PostAsJsonAsync($"/internal/companies/{CompanyId}/finance/fiscal-periods/{August}/reporting/stored-statements/regenerate", new { runInBackground = false });
        if (!regenerate.IsSuccessStatusCode) throw new InvalidOperationException("P07 snapshot fixture failed: " + await regenerate.Content.ReadAsStringAsync());
        using var locked = await client.PostAsJsonAsync($"/internal/companies/{CompanyId}/finance/fiscal-periods/{August}/reporting/lock", new { });
        if (!locked.IsSuccessStatusCode) throw new InvalidOperationException("P07 locked fixture failed: " + await locked.Content.ReadAsStringAsync());
    }

    private static FinanceAccount Account(Guid id, string code, string name, string accountClass, string normal) =>
        new(id, CompanyId, code, name, accountClass, "SEK", 0m, new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            accountClass: accountClass, normalBalance: normal, effectiveFrom: new(2026, 1, 1), isPostingEnabled: true);

    private static async Task<T> PostAsync<T>(HttpClient client, string route, object body)
    {
        using var result = await client.PostAsJsonAsync(route, body);
        if (!result.IsSuccessStatusCode) throw new InvalidOperationException("P07 fixture command failed: " + await result.Content.ReadAsStringAsync());
        return (await result.Content.ReadFromJsonAsync<T>())!;
    }
}
