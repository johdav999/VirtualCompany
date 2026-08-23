using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Finance;

namespace VirtualCompany.Finance.Tests;

public sealed class AccountingProviderSwitchCutoverTests
{
    [Fact]
    public void Execution_enforces_freeze_transfer_reconciliation_and_activation_order()
    {
        var now = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var execution = Create(now);
        execution.BeginFreeze(now);
        execution.RecordFrozen(Guid.NewGuid(), Guid.NewGuid(), now);
        execution.BeginReconciliation(now);
        execution.AwaitActivationApproval(now);
        execution.BeginActivation();
        execution.RecordTargetActivity();
        execution.CompleteActivation(now);

        Assert.Equal(AccountingProviderSwitchCutoverStatuses.Activated, execution.Status);
        Assert.True(execution.TargetActivityRecorded);
        Assert.False(execution.RetryIsSafe);
        Assert.NotNull(execution.ActivatedUtc);
    }

    [Fact]
    public void Provider_ambiguity_blocks_blind_retry_and_requires_reconciliation()
    {
        var execution = Create(DateTime.UtcNow);
        execution.Block("unknown_provider_outcome", "The provider outcome is unknown.", false, true,
            "Reconcile with the provider.", DateTime.UtcNow);

        Assert.Equal(AccountingProviderSwitchCutoverStatuses.Blocked, execution.Status);
        Assert.True(execution.ProviderReconciliationRequired);
        Assert.Throws<InvalidOperationException>(() => execution.Resume(DateTime.UtcNow));
    }

    [Fact]
    public void Recovery_policy_requires_corrective_cutover_after_target_activity()
    {
        var execution = Create(DateTime.UtcNow);
        execution.BeginFreeze(DateTime.UtcNow);
        execution.RecordFrozen(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        execution.RecordTargetActivity();
        execution.RecordRecovery(true, "Target activity exists.", DateTime.UtcNow);
        var policy = new AccountingProviderSwitchCutoverPolicy();

        var actions = policy.AllowedActions(execution.Status, execution.TargetActivityRecorded,
            execution.RetryIsSafe, execution.ProviderReconciliationRequired, false);

        Assert.Equal(AccountingProviderSwitchCutoverStatuses.CorrectiveCutoverRequired, execution.Status);
        Assert.True(actions.RequiresCorrectiveCutover);
        Assert.False(actions.CanRecoverSource);
    }

    [Fact]
    public void Only_queued_cutover_can_be_cancelled_before_freeze()
    {
        var execution = Create(DateTime.UtcNow);
        execution.Cancel(DateTime.UtcNow);
        Assert.Equal(AccountingProviderSwitchCutoverStatuses.Cancelled, execution.Status);

        var frozen = Create(DateTime.UtcNow);
        frozen.BeginFreeze(DateTime.UtcNow);
        Assert.Throws<InvalidOperationException>(() => frozen.Cancel(DateTime.UtcNow));
    }

    private static AccountingProviderSwitchCutoverExecution Create(DateTime now) => new(Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, new string('a', 64), null, null,
        Guid.NewGuid(), "cutover-key", "correlation", now, now);
}
