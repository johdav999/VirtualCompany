using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class AccountingStatutoryReadinessTests
{
    [Fact]
    public async Task Swedish_company_is_release_blocked_when_exact_reviewer_evidence_and_profile_are_missing()
    {
        var nowUtc = new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc);
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var context = new VirtualCompanyDbContext(
            new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var companyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var pack = new SwedishStatutoryArchiveCandidatePack();
        var configuration = new AccountingConfiguration(
            Guid.NewGuid(), companyId, "SEK", 1, 1,
            pack.Definition.PackKey, pack.Definition.Version,
            new DateOnly(2026, 1, 1), 2, AccountingRoundingModeValues.MidpointToEven,
            actorId, nowUtc);
        configuration.SetSetupState(AccountingSetupStateValues.Ready, actorId, nowUtc);
        context.Companies.Add(new Company(companyId, "Swedish readiness fixture"));
        context.AccountingConfigurations.Add(configuration);
        context.CompanyCurrencyDefinitions.Add(new CompanyCurrencyDefinition(
            Guid.NewGuid(), companyId, "EUR", "Euro", 2, true, nowUtc));
        await context.SaveChangesAsync();

        var service = new AccountingReadinessService(
            context,
            new AccountingPolicyPackResolver([pack]),
            new AccountingPolicyPackValidationRegistry([]),
            new FixedTimeProvider(new DateTimeOffset(nowUtc)));

        var result = await service.EvaluateAsync(companyId, CancellationToken.None);

        Assert.False(result.IsReady);
        Assert.Equal(AccountingReadinessStatuses.Blocked, result.Status);
        var validation = Assert.Single(result.Signals, signal => signal.Key == "policy_pack_validation");
        Assert.Equal(AccountingReadinessStatuses.Blocked, validation.Status);
        Assert.Contains("not registered", validation.Explanation, StringComparison.OrdinalIgnoreCase);
        var profile = Assert.Single(result.Signals, signal => signal.Key == "statutory_profile_completeness");
        Assert.Equal(AccountingReadinessStatuses.Blocked, profile.Status);
        Assert.True(profile.Count > 0);
        Assert.Contains(result.Signals, signal => signal.Key == "stale_vat_returns");
        Assert.Contains(result.Signals, signal => signal.Key == "failed_or_expired_statutory_exports");
        Assert.Contains(result.Signals, signal => signal.Key == "unsupported_configured_capabilities");
        var rates = Assert.Single(result.Signals, signal => signal.Key == "exchange_rate_coverage");
        Assert.Equal(AccountingReadinessStatuses.Blocked, rates.Status);
        Assert.Contains("EUR", rates.Explanation, StringComparison.Ordinal);
        Assert.Contains(result.Signals, signal => signal.Key == "currency_revaluation_operations");
        Assert.Contains(result.Signals, signal => signal.Key == "dimension_governance");
        Assert.Contains(result.Signals, signal => signal.Key == "accounting_schedule_operations");
        Assert.Contains(result.Signals, signal => signal.Key == "fixed_asset_operations");
        Assert.Contains(result.Signals, signal => signal.Key == "accounting_series_governance");
        var inventory = Assert.Single(result.Signals, signal => signal.Key == "inventory_accounting_capability");
        Assert.Equal(AccountingReadinessStatuses.Ready, inventory.Status);
        Assert.Contains("unsupported", inventory.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.Signals, signal => signal.Key == "advanced_rate_coverage");
        Assert.Contains(result.Signals, signal => signal.Key == "advanced_currency_controls");
        Assert.Contains(result.Signals, signal => signal.Key == "advanced_dimension_controls");
        Assert.Contains(result.Signals, signal => signal.Key == "advanced_schedule_controls");
        Assert.Contains(result.Signals, signal => signal.Key == "advanced_asset_controls");
        Assert.Contains(result.Signals, signal => signal.Key == "advanced_series_controls");
        Assert.Contains(result.Signals, signal => signal.Key == "inventory_capability_boundary");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
