using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinancePlanningPersistenceTests
{
    [Fact]
    public async Task Budget_configuration_rejects_duplicate_null_cost_center_entries()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        var companyId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        dbContext.Companies.Add(new Company(companyId, "Planning Co"));
        dbContext.FinanceAccounts.Add(new FinanceAccount(
            accountId,
            companyId,
            "4000",
            "Sales",
            "revenue",
            "USD",
            0m,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await dbContext.SaveChangesAsync();

        var periodStartUtc = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        dbContext.Budgets.Add(new Budget(Guid.NewGuid(), companyId, accountId, periodStartUtc, "baseline", 100m, "USD"));
        await dbContext.SaveChangesAsync();

        dbContext.Budgets.Add(new Budget(Guid.NewGuid(), companyId, accountId, periodStartUtc, "baseline", 200m, "USD"));

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Forecast_configuration_rejects_duplicate_null_cost_center_entries()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        var companyId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        dbContext.Companies.Add(new Company(companyId, "Planning Co"));
        dbContext.FinanceAccounts.Add(new FinanceAccount(
            accountId,
            companyId,
            "4000",
            "Sales",
            "revenue",
            "USD",
            0m,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await dbContext.SaveChangesAsync();

        var periodStartUtc = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        dbContext.Forecasts.Add(new Forecast(Guid.NewGuid(), companyId, accountId, periodStartUtc, "baseline", 100m, "USD"));
        await dbContext.SaveChangesAsync();

        dbContext.Forecasts.Add(new Forecast(Guid.NewGuid(), companyId, accountId, periodStartUtc, "baseline", 200m, "USD"));
        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Budget_configuration_allows_same_period_when_version_differs()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        var companyId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        dbContext.Companies.Add(new Company(companyId, "Planning Co"));
        dbContext.FinanceAccounts.Add(new FinanceAccount(
            accountId,
            companyId,
            "5000",
            "Payroll",
            "expense",
            "USD",
            0m,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await dbContext.SaveChangesAsync();

        var periodStartUtc = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        dbContext.Budgets.Add(new Budget(Guid.NewGuid(), companyId, accountId, periodStartUtc, "baseline", 100m, "USD"));
        dbContext.Budgets.Add(new Budget(Guid.NewGuid(), companyId, accountId, periodStartUtc, "working", 125m, "USD"));

        await dbContext.SaveChangesAsync();

        Assert.Equal(2, await dbContext.Budgets.CountAsync());
    }

    [Fact]
    public async Task Planning_baseline_service_backfills_missing_rows_for_existing_company()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        var companyId = Guid.NewGuid();
        var firstAccountId = Guid.NewGuid();
        var secondAccountId = Guid.NewGuid();
        var horizonStartUtc = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);

        var company = new Company(companyId, "Planning Co");
        company.SetFinanceSeedStatus(FinanceSeedingState.Seeded, DateTime.UtcNow, DateTime.UtcNow);
        dbContext.Companies.Add(company);
        dbContext.FinanceAccounts.AddRange(
            new FinanceAccount(firstAccountId, companyId, "1000", "Cash", "asset", "USD", 0m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            new FinanceAccount(secondAccountId, companyId, "4000", "Sales", "revenue", "USD", 0m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        dbContext.FiscalPeriods.Add(new FiscalPeriod(
            Guid.NewGuid(),
            companyId,
            "2026-04",
            horizonStartUtc,
            new DateTime(2026, 4, 30, 23, 59, 59, DateTimeKind.Utc)));
        dbContext.Budgets.Add(new Budget(Guid.NewGuid(), companyId, firstAccountId, horizonStartUtc, FinancePlanningVersions.Baseline, 100m, "USD"));
        dbContext.Forecasts.Add(new Forecast(Guid.NewGuid(), companyId, firstAccountId, horizonStartUtc, FinancePlanningVersions.Baseline, 120m, "USD"));
        await dbContext.SaveChangesAsync();

        var service = new PlanningBaselineService(dbContext);
        var inserted = await service.EnsureBaselineAsync(companyId, CancellationToken.None);

        Assert.Equal(46, inserted);
        Assert.Equal(24, await dbContext.Budgets.CountAsync());
        Assert.Equal(24, await dbContext.Forecasts.CountAsync());
        Assert.Equal(
            12,
            await dbContext.Budgets.CountAsync(x =>
                x.CompanyId == companyId &&
                x.FinanceAccountId == firstAccountId));
        Assert.Equal(
            12,
            await dbContext.Forecasts.CountAsync(x =>
                x.CompanyId == companyId &&
                x.FinanceAccountId == firstAccountId));
    }

    [Fact]
    public async Task Planning_baseline_service_is_idempotent()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        var companyId = Guid.NewGuid();

        var company = new Company(companyId, "Planning Co");
        company.SetFinanceSeedStatus(FinanceSeedingState.Seeded, DateTime.UtcNow, DateTime.UtcNow);
        dbContext.Companies.Add(company);
        dbContext.FinanceAccounts.AddRange(
            new FinanceAccount(
                Guid.NewGuid(),
                companyId,
                "1000",
                "Cash",
                "asset",
                "USD",
                0m,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            new FinanceAccount(
                Guid.NewGuid(),
                companyId,
                "4000",
                "Sales",
                "revenue",
                "USD",
                0m,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        dbContext.FiscalPeriods.Add(new FiscalPeriod(
            Guid.NewGuid(),
            companyId,
            "2026-04",
            new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 30, 23, 59, 59, DateTimeKind.Utc)));
        await dbContext.SaveChangesAsync();

        var service = new PlanningBaselineService(dbContext);

        var insertedFirstRun = await service.EnsureBaselineAsync(companyId, CancellationToken.None);
        var insertedSecondRun = await service.EnsureBaselineAsync(companyId, CancellationToken.None);

        Assert.True(insertedFirstRun > 0);
        Assert.Equal(0, insertedSecondRun);
        Assert.Equal(24, await dbContext.Budgets.CountAsync());
        Assert.Equal(24, await dbContext.Forecasts.CountAsync());
    }

    [Fact]
    public async Task Budget_command_rejects_cross_tenant_account_reference()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();
        var accountBId = Guid.NewGuid();

        dbContext.Companies.AddRange(
            new Company(companyAId, "Tenant A"),
            new Company(companyBId, "Tenant B"));
        dbContext.FinanceAccounts.Add(new FinanceAccount(
            accountBId,
            companyBId,
            "4000",
            "Sales",
            "revenue",
            "USD",
            0m,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceCommandService(dbContext);

        await Assert.ThrowsAsync<FinanceValidationException>(() =>
            service.CreateBudgetAsync(
                new CreateFinanceBudgetCommand(
                    companyAId,
                    new FinancePlanningEntryUpsertDto(
                        accountBId,
                        new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                        "baseline",
                        100m,
                        "USD")),
                CancellationToken.None));
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<VirtualCompanyDbContext> CreateDbContextAsync(SqliteConnection connection)
    {
        var dbContext = new VirtualCompanyDbContext(
            new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseSqlite(connection)
                .Options);
        await dbContext.Database.EnsureCreatedAsync();
        return dbContext;
    }
}