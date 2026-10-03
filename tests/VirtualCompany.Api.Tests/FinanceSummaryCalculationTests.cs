using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceSummaryCalculationTests
{
    [Theory]
    [InlineData(0, "unknown", false)]
    [InlineData(1, "critical", true)]
    [InlineData(2, "unknown", false)]
    public async Task Cash_risk_distinguishes_missing_and_mixed_evidence_from_measured_zero(int accountCount, string risk, bool lowCash)
    {
        var companyId = Guid.NewGuid();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        var company = new Company(companyId, "Cash evidence company");
        company.SetFinanceSeedStatus(VirtualCompany.Domain.Enums.FinanceSeedingState.Seeded, DateTime.UtcNow, DateTime.UtcNow);
        dbContext.Companies.Add(company);
        for (var i = 0; i < accountCount; i++)
            dbContext.FinanceAccounts.Add(new FinanceAccount(Guid.NewGuid(), companyId, $"193{i}", "Business bank account", "asset", i == 0 ? "SEK" : "USD", 0m,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await dbContext.SaveChangesAsync();
        var service = new CompanyFinanceReadService(dbContext);
        var result = await new CompanyFinanceCashPositionWorkflowService(dbContext, service).EvaluateAsync(new(companyId), default);
        Assert.Equal(risk, result.RiskLevel);
        Assert.Equal(lowCash, result.AlertState.IsLowCash);
        Assert.Equal(lowCash ? 1 : 0, await dbContext.Alerts.IgnoreQueryFilters().CountAsync());
        if (!lowCash)
        {
            Assert.Equal("cash_position_unavailable", result.Classification);
            Assert.Null(result.EstimatedRunwayDays);
            Assert.Contains("unavailable", result.Rationale);
            Assert.DoesNotContain("Available cash is 0", result.Rationale);
        }
    }

    [Fact]
    public async Task Invalid_legacy_cash_alert_is_retained_but_does_not_rank_and_other_company_is_excluded()
    {
        var company = Guid.NewGuid(); var other = Guid.NewGuid();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var context = new VirtualCompany.Infrastructure.Auth.RequestCompanyContextAccessor();
        await using var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options, context);
        await db.Database.EnsureCreatedAsync();
        db.Companies.AddRange(new Company(company, "Cash"), new Company(other, "Other"));
        var invalid = Guid.NewGuid(); var valid = Guid.NewGuid();
        foreach (var (id, tenant, currency) in new[] { (invalid, company, "MIXED"), (valid, company, "SEK"), (Guid.NewGuid(), other, "SEK") })
            db.Alerts.Add(new Alert(id, tenant, VirtualCompany.Domain.Enums.AlertType.Risk, VirtualCompany.Domain.Enums.AlertSeverity.Critical,
                "Low cash position", "Retained source", new Dictionary<string, System.Text.Json.Nodes.JsonNode?> { ["currency"] = System.Text.Json.Nodes.JsonValue.Create(currency) },
                "cash-test", $"finance-cash-position:{tenant:N}:low-cash:{currency}"));
        await db.SaveChangesAsync();
        context.SetCompanyId(company);
        var candidates = await new VirtualCompany.Infrastructure.Companies.FinanceAlertFocusCandidateSource(db)
            .GetCandidatesAsync(new(company, Guid.NewGuid()), default);
        Assert.Equal(valid.ToString("N"), Assert.Single(candidates).Id);
        Assert.Equal(3, await db.Alerts.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Cash_balance_matches_seeded_account_balance_source_data()
    {
        var companyId = Guid.NewGuid();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Companies.Add(new Company(companyId, "Seeded Finance Company"));
        FinanceSeedData.AddMockFinanceData(dbContext, companyId);
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceReadService(dbContext);
        var result = await service.GetCashBalanceAsync(new GetFinanceCashBalanceQuery(companyId, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)), CancellationToken.None);

        var operatingCash = await dbContext.FinanceAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId && x.Name == "Operating Cash");
        var expectedBalance = await dbContext.FinanceBalances
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.AccountId == operatingCash.Id)
            .Select(x => x.Amount)
            .SingleAsync();

        Assert.Equal(expectedBalance, result.Amount);
        Assert.Equal("USD", result.Currency);
    }

    [Fact]
    public async Task Cash_balance_includes_transactions_after_latest_balance_snapshot()
    {
        var companyId = Guid.NewGuid();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var accountId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var vendorId = Guid.Parse("88888888-8888-8888-8888-888888888888");

        dbContext.Companies.Add(new Company(companyId, "Ledger Finance Company"));
        dbContext.FinanceAccounts.Add(new FinanceAccount(
            accountId,
            companyId,
            "1000",
            "Operating Cash",
            "asset",
            "USD",
            0m,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        dbContext.FinanceCounterparties.Add(new FinanceCounterparty(vendorId, companyId, "Vendor", "vendor", "vendor@example.com"));
        dbContext.FinanceBalances.Add(new FinanceBalance(
            Guid.Parse("77777777-7777-7777-7777-777777777777"),
            companyId,
            accountId,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            1000m,
            "USD"));
        dbContext.FinanceTransactions.AddRange(
            new FinanceTransaction(
                Guid.Parse("66666666-6666-6666-6666-666666666666"),
                companyId,
                accountId,
                vendorId,
                null,
                null,
                new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
                "customer_payment",
                500m,
                "USD",
                "Customer receipt",
                "LEDGER-001"),
            new FinanceTransaction(
                Guid.Parse("55555555-5555-5555-5555-555555555555"),
                companyId,
                accountId,
                vendorId,
                null,
                null,
                new DateTime(2026, 1, 8, 0, 0, 0, DateTimeKind.Utc),
                "office_supplies",
                -200m,
                "USD",
                "Office supplies",
                "LEDGER-002"));
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceReadService(dbContext);
        var result = await service.GetCashBalanceAsync(new GetFinanceCashBalanceQuery(companyId, new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc)), CancellationToken.None);

        Assert.Equal(1300m, result.Amount);
    }

    [Fact]
    public async Task Cash_balance_uses_bank_accounts_instead_of_ten_series_assets()
    {
        var companyId = Guid.NewGuid();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var developmentAssetId = Guid.Parse("99999999-9999-9999-9999-999999999998");
        var bankAccountId = Guid.Parse("99999999-9999-9999-9999-999999999997");
        var customerId = Guid.Parse("88888888-8888-8888-8888-888888888887");

        dbContext.Companies.Add(new Company(companyId, "Swedish Ledger Company"));
        dbContext.FinanceAccounts.AddRange(
            new FinanceAccount(
                developmentAssetId,
                companyId,
                "1010",
                "Development expenditure",
                "asset",
                "SEK",
                0m,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            new FinanceAccount(
                bankAccountId,
                companyId,
                "1930",
                "Business bank account",
                "asset",
                "SEK",
                0m,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        dbContext.FinanceCounterparties.Add(new FinanceCounterparty(customerId, companyId, "Customer", "customer", "customer@example.com"));
        dbContext.FinanceTransactions.Add(new FinanceTransaction(
            Guid.Parse("66666666-6666-6666-6666-666666666665"),
            companyId,
            bankAccountId,
            customerId,
            null,
            null,
            new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            "customer_payment",
            318484m,
            "SEK",
            "Customer receipt",
            "BANK-001"));
        dbContext.FinanceTransactions.Add(new FinanceTransaction(
            Guid.Parse("66666666-6666-6666-6666-666666666664"),
            companyId,
            bankAccountId,
            customerId,
            null,
            null,
            new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc),
            "voucher",
            112500m,
            "SEK",
            "Customer invoice posting",
            "VOUCHER-001"));
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceReadService(dbContext);
        var result = await service.GetCashBalanceAsync(new GetFinanceCashBalanceQuery(companyId, new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc)), CancellationToken.None);

        Assert.Equal(318484m, result.Amount);
        Assert.Equal("SEK", result.Currency);
        Assert.DoesNotContain(result.Accounts, x => x.AccountCode == "1010");
        Assert.Contains(result.Accounts, x => x.AccountCode == "1930");
    }

    [Fact]
    public async Task Monthly_profit_and_loss_returns_positive_and_negative_net_income_months()
    {
        var companyId = Guid.NewGuid();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        SeedSummaryScenario(dbContext, companyId);
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceReadService(dbContext);
        var positiveMonth = await service.GetMonthlyProfitAndLossAsync(new GetFinanceMonthlyProfitAndLossQuery(companyId, 2026, 1), CancellationToken.None);
        var negativeMonth = await service.GetMonthlyProfitAndLossAsync(new GetFinanceMonthlyProfitAndLossQuery(companyId, 2026, 2), CancellationToken.None);

        Assert.Equal(10000m, positiveMonth.Revenue);
        Assert.Equal(3000m, positiveMonth.Expenses);
        Assert.Equal(7000m, positiveMonth.NetResult);
        Assert.Equal(1000m, negativeMonth.Revenue);
        Assert.Equal(2200m, negativeMonth.Expenses);
        Assert.Equal(-1200m, negativeMonth.NetResult);
    }

    [Fact]
    public async Task Expense_breakdown_groups_expenses_by_category_with_deterministic_totals()
    {
        var companyId = Guid.NewGuid();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        SeedSummaryScenario(dbContext, companyId);
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceReadService(dbContext);
        var breakdown = await service.GetExpenseBreakdownAsync(
            new GetFinanceExpenseBreakdownQuery(
                companyId,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)),
            CancellationToken.None);

        Assert.Equal(3000m, breakdown.TotalExpenses);
        Assert.Collection(
            breakdown.Categories,
            category =>
            {
                Assert.Equal("cloud_hosting", category.Category);
                Assert.Equal(2000m, category.Amount);
            },
            category =>
            {
                Assert.Equal("office_supplies", category.Category);
                Assert.Equal(1000m, category.Amount);
            });
    }

    private static VirtualCompanyDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite(connection)
            .Options);

    private static void SeedSummaryScenario(VirtualCompanyDbContext dbContext, Guid companyId)
    {
        var accountId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var customerId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var vendorId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var januaryInvoiceId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var februaryInvoiceId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

        dbContext.Companies.Add(new Company(companyId, "Summary Finance Company"));
        dbContext.FinanceAccounts.Add(new FinanceAccount(
            accountId,
            companyId,
            "1000",
            "Operating Cash",
            "asset",
            "USD",
            5000m,
            new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc)));
        dbContext.FinanceCounterparties.AddRange(
            new FinanceCounterparty(customerId, companyId, "Customer", "customer", "customer@example.com"),
            new FinanceCounterparty(vendorId, companyId, "Vendor", "vendor", "vendor@example.com"));
        dbContext.FinanceInvoices.AddRange(
            new FinanceInvoice(
                januaryInvoiceId,
                companyId,
                customerId,
                "INV-202601-001",
                new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 2, 5, 0, 0, 0, DateTimeKind.Utc),
                10000m,
                "USD",
                "open"),
            new FinanceInvoice(
                februaryInvoiceId,
                companyId,
                customerId,
                "INV-202602-001",
                new DateTime(2026, 2, 5, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc),
                1000m,
                "USD",
                "open"));
        dbContext.FinanceTransactions.AddRange(
            new FinanceTransaction(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                companyId,
                accountId,
                vendorId,
                null,
                null,
                new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                "cloud_hosting",
                -2000m,
                "USD",
                "Cloud hosting",
                "EXP-202601-001"),
            new FinanceTransaction(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                companyId,
                accountId,
                vendorId,
                null,
                null,
                new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc),
                "office_supplies",
                -1000m,
                "USD",
                "Office supplies",
                "EXP-202601-002"),
            new FinanceTransaction(
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                companyId,
                accountId,
                vendorId,
                null,
                null,
                new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc),
                "cloud_hosting",
                -2200m,
                "USD",
                "Cloud hosting",
                "EXP-202602-001"));
    }
}
