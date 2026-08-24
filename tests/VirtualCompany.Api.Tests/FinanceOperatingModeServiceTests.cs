using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceOperatingModeServiceTests
{
    [Fact]
    public async Task GetAsync_reports_missing_accounting_setup_without_using_seeded_records_as_authority()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var companyId = Guid.NewGuid();
        dbContext.Companies.Add(new Company(companyId, "Operating mode company"));
        await dbContext.SaveChangesAsync();

        var result = await CreateService(dbContext).GetAsync(
            new GetFinanceOperatingModeQuery(companyId, new DateOnly(2026, 8, 23)),
            CancellationToken.None);

        Assert.Equal("not_configured", result.AccountingAuthority);
        Assert.Equal(FinanceDataSources.Operational, result.AllowedReadSource);
        Assert.Equal("none", result.AllowedPostingSource);
        Assert.False(result.IsReadyForOperationalPosting);
        Assert.Contains(result.Issues, issue => issue.Code == "accounting_setup_missing");
        Assert.Contains(result.Issues, issue => issue.Code == "accounting_authority_missing");
    }

    [Fact]
    public async Task GetAsync_blocks_operational_posting_while_accounting_authority_is_migrating()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var companyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var now = new DateTime(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc);
        dbContext.Companies.Add(new Company(companyId, "Migrating company"));
        var configuration = new AccountingConfiguration(
            Guid.NewGuid(), companyId, "USD", 1, 1, "standard", "1", new DateOnly(2026, 1, 1), 2,
            AccountingRoundingModeValues.AwayFromZero, actorId, now);
        configuration.SetSetupState(AccountingSetupStateValues.Ready, actorId, now);
        dbContext.AccountingConfigurations.Add(configuration);
        dbContext.AccountingAuthorityPeriods.Add(new AccountingAuthorityPeriod(
            Guid.NewGuid(), companyId, new DateOnly(2026, 1, 1), null, AccountingAuthorityValues.Migration,
            null, actorId, "Provider cutover", now));
        await dbContext.SaveChangesAsync();

        var result = await CreateService(dbContext).GetAsync(
            new GetFinanceOperatingModeQuery(companyId, new DateOnly(2026, 8, 23)),
            CancellationToken.None);

        Assert.True(result.AccountingSetupReady);
        Assert.True(result.MigrationInProgress);
        Assert.Equal(AccountingAuthorityValues.Migration, result.AccountingAuthority);
        Assert.Equal("none", result.AllowedPostingSource);
        Assert.False(result.IsReadyForOperationalPosting);
        Assert.Contains(result.Issues, issue => issue.Code == "accounting_authority_migration");
    }

    private static FinanceOperatingModeService CreateService(VirtualCompanyDbContext dbContext) =>
        new(dbContext, new DisabledSimulationFeatureGate(), TimeProvider.System);

    private static VirtualCompanyDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options);

    private sealed class DisabledSimulationFeatureGate : ISimulationFeatureGate
    {
        public SimulationFeatureStateDto GetState() => new(false, false, false, "Simulation is disabled.");
        public bool IsUiVisible() => false;
        public bool IsBackendExecutionEnabled() => false;
        public bool AreBackgroundJobsEnabled() => false;
        public bool IsBackgroundExecutionAllowed() => false;
        public bool IsFullyDisabled() => true;
        public void EnsureBackendExecutionEnabled() => throw new SimulationBackendDisabledException("Simulation is disabled.");
        public void EnsureBackgroundExecutionEnabled() => throw new SimulationBackendDisabledException("Simulation is disabled.", true);
    }
}
