using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class AccountingProviderSwitchTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
    private static readonly string[] AllStatuses =
    [
        AccountingProviderSwitchStatuses.Draft,
        AccountingProviderSwitchStatuses.Assessing,
        AccountingProviderSwitchStatuses.ReadyForPlanning,
        AccountingProviderSwitchStatuses.PlanAwaitingApproval,
        AccountingProviderSwitchStatuses.PreparingTarget,
        AccountingProviderSwitchStatuses.RehearsalPassed,
        AccountingProviderSwitchStatuses.Scheduled,
        AccountingProviderSwitchStatuses.SourceFrozen,
        AccountingProviderSwitchStatuses.Reconciling,
        AccountingProviderSwitchStatuses.ActivationAwaitingApproval,
        AccountingProviderSwitchStatuses.TargetAuthoritative,
        AccountingProviderSwitchStatuses.Monitoring,
        AccountingProviderSwitchStatuses.Completed,
        AccountingProviderSwitchStatuses.Blocked,
        AccountingProviderSwitchStatuses.Cancelled,
        AccountingProviderSwitchStatuses.Recovery
    ];

    [Fact]
    public void Transition_policy_enforces_every_allowed_and_forbidden_state_pair()
    {
        foreach (var sourceStatus in AllStatuses)
        {
            foreach (var targetStatus in AllStatuses)
            {
                var providerSwitch = BuildAtStatus(sourceStatus);
                var allowed = AccountingProviderSwitchStatuses
                    .AllowedTransitions(providerSwitch.Status, providerSwitch.BlockedFromStatus)
                    .Contains(targetStatus, StringComparer.Ordinal);

                var exception = Record.Exception(() => ApplyTransition(providerSwitch, targetStatus));

                Assert.Equal(allowed, exception is null);
                if (allowed) Assert.Equal(targetStatus, providerSwitch.Status);
            }
        }
    }

    [Theory]
    [InlineData("INTERNAL", null, "internal", null)]
    [InlineData("external", " FORTNOX ", "external", "fortnox")]
    public void Endpoint_values_are_normalized(
        string kind,
        string? providerKey,
        string expectedKind,
        string? expectedProviderKey)
    {
        var endpoint = new AccountingProviderEndpoint(kind, providerKey);

        Assert.Equal(expectedKind, endpoint.Kind);
        Assert.Equal(expectedProviderKey, endpoint.ProviderKey);
    }

    [Fact]
    public void Endpoint_values_reject_missing_or_incompatible_provider_keys()
    {
        Assert.Throws<ArgumentException>(() => new AccountingProviderEndpoint("internal", "fortnox"));
        Assert.Throws<ArgumentException>(() => new AccountingProviderEndpoint("external", null));
        Assert.Throws<ArgumentException>(() => new AccountingProviderEndpoint("external", "unsafe/provider"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AccountingProviderEndpoint("unsupported", null));
    }

    [Theory]
    [InlineData("opening-balances-and-open-items", AccountingProviderSwitchStrategies.OpeningBalancesAndOpenItems)]
    [InlineData("CURRENT_FISCAL_YEAR", AccountingProviderSwitchStrategies.CurrentFiscalYear)]
    [InlineData(" full_history ", AccountingProviderSwitchStrategies.FullHistory)]
    public void Strategy_values_are_normalized(string input, string expected) =>
        Assert.Equal(expected, AccountingProviderSwitchStrategies.Normalize(input));

    [Fact]
    public void Aggregate_rejects_same_endpoint_and_versions_every_accepted_change()
    {
        var actorId = Guid.NewGuid();
        var internalEndpoint = new AccountingProviderEndpoint("internal", null);
        Assert.Throws<ArgumentException>(() => new AccountingProviderSwitch(
            Guid.NewGuid(), Guid.NewGuid(), internalEndpoint, internalEndpoint, Guid.NewGuid(),
            AccountingProviderSwitchStrategies.FullHistory, "Replace the accounting system.", actorId, null,
            actorId, "same-endpoint", NowUtc));

        var providerSwitch = NewSwitch();
        Assert.Equal(1, providerSwitch.Version);
        providerSwitch.UpdatePlan(
            providerSwitch.Source,
            new AccountingProviderEndpoint("external", "another-provider"),
            Guid.NewGuid(),
            AccountingProviderSwitchStrategies.CurrentFiscalYear,
            "Use current-year detail.",
            actorId,
            null,
            actorId,
            "updated-plan",
            NowUtc.AddMinutes(1));
        Assert.Equal(2, providerSwitch.Version);
        providerSwitch.Cancel("Plans changed.", actorId, "cancelled", NowUtc.AddMinutes(2));
        Assert.Equal(3, providerSwitch.Version);
        Assert.True(providerSwitch.IsTerminal);
    }

    [Fact]
    public void Persistence_model_enforces_one_non_terminal_switch_per_company()
    {
        using var dbContext = new VirtualCompanyDbContext(
            new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite("Data Source=:memory:").Options);
        var entity = dbContext.Model.FindEntityType(typeof(AccountingProviderSwitch))!;
        var activeIndex = Assert.Single(entity.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(AccountingProviderSwitch.CompanyId)]));

        Assert.Contains("completed", activeIndex.GetFilter(), StringComparison.Ordinal);
        Assert.Contains("cancelled", activeIndex.GetFilter(), StringComparison.Ordinal);
        Assert.True(entity.FindProperty(nameof(AccountingProviderSwitch.Version))!.IsConcurrencyToken);
    }

    private static AccountingProviderSwitch BuildAtStatus(string status)
    {
        var providerSwitch = NewSwitch();
        if (status == AccountingProviderSwitchStatuses.Draft) return providerSwitch;
        if (status == AccountingProviderSwitchStatuses.Cancelled)
        {
            providerSwitch.Cancel("Test cancellation.", Guid.NewGuid(), "cancelled", NowUtc.AddMinutes(1));
            return providerSwitch;
        }
        if (status == AccountingProviderSwitchStatuses.Blocked)
        {
            providerSwitch.Block("test_block", "Test blocking reason.", Guid.NewGuid(), "blocked", NowUtc.AddMinutes(1));
            return providerSwitch;
        }
        if (status == AccountingProviderSwitchStatuses.Recovery)
        {
            providerSwitch.Block("test_block", "Test blocking reason.", Guid.NewGuid(), "blocked", NowUtc.AddMinutes(1));
            providerSwitch.TransitionTo(AccountingProviderSwitchStatuses.Recovery, Guid.NewGuid(), "recovery", NowUtc.AddMinutes(2));
            return providerSwitch;
        }

        var path = new[]
        {
            AccountingProviderSwitchStatuses.Assessing,
            AccountingProviderSwitchStatuses.ReadyForPlanning,
            AccountingProviderSwitchStatuses.PlanAwaitingApproval,
            AccountingProviderSwitchStatuses.PreparingTarget,
            AccountingProviderSwitchStatuses.RehearsalPassed,
            AccountingProviderSwitchStatuses.Scheduled,
            AccountingProviderSwitchStatuses.SourceFrozen,
            AccountingProviderSwitchStatuses.Reconciling,
            AccountingProviderSwitchStatuses.ActivationAwaitingApproval,
            AccountingProviderSwitchStatuses.TargetAuthoritative,
            AccountingProviderSwitchStatuses.Monitoring,
            AccountingProviderSwitchStatuses.Completed
        };
        for (var index = 0; index < path.Length; index++)
        {
            providerSwitch.TransitionTo(path[index], Guid.NewGuid(), $"transition-{index}", NowUtc.AddMinutes(index + 1));
            if (path[index] == status) return providerSwitch;
        }

        throw new InvalidOperationException($"Test status '{status}' is not reachable.");
    }

    private static void ApplyTransition(AccountingProviderSwitch providerSwitch, string targetStatus)
    {
        if (targetStatus == AccountingProviderSwitchStatuses.Blocked)
        {
            providerSwitch.Block("test_block", "Test blocking reason.", Guid.NewGuid(), "blocked", NowUtc.AddHours(1));
            return;
        }
        if (targetStatus == AccountingProviderSwitchStatuses.Cancelled)
        {
            providerSwitch.Cancel("Test cancellation.", Guid.NewGuid(), "cancelled", NowUtc.AddHours(1));
            return;
        }
        providerSwitch.TransitionTo(targetStatus, Guid.NewGuid(), "transition", NowUtc.AddHours(1));
    }

    private static AccountingProviderSwitch NewSwitch()
    {
        var actorId = Guid.NewGuid();
        return new AccountingProviderSwitch(
            Guid.NewGuid(), Guid.NewGuid(),
            new AccountingProviderEndpoint("internal", null),
            new AccountingProviderEndpoint("external", "fortnox"),
            Guid.NewGuid(), AccountingProviderSwitchStrategies.OpeningBalancesAndOpenItems,
            "Move accounting at a future monthly boundary.", actorId, null, actorId,
            "provider-switch-test", NowUtc);
    }
}
