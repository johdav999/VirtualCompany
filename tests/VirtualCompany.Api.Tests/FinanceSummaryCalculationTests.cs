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
