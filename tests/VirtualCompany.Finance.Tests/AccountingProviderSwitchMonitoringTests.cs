using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Finance.Tests;

public sealed class AccountingProviderSwitchMonitoringTests
{
    [Fact]
    public void Monitoring_run_is_leased_idempotently_and_retains_a_bounded_window()
    {
        var now = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var run = CreateRun(now);

        run.Claim("worker-1", now, now.AddMinutes(2));
        var sequence = run.CompletePass(hasBlockingIncident: false, now.AddMinutes(1), now.AddDays(1));

        Assert.Equal(1, sequence);
        Assert.Equal(AccountingProviderSwitchMonitoringStatuses.Active, run.Status);
        Assert.Equal(now.AddDays(14), run.WindowEndsUtc);
        Assert.Null(run.LeaseOwner);
        Assert.Equal(now.AddDays(1), run.NextRunUtc);
    }

    [Fact]
    public void Exhausted_failures_require_an_explicit_operator_retry()
    {
        var now = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var run = CreateRun(now);
        run.Claim("worker-1", now, now.AddMinutes(2));
        run.Fail("provider_timeout", "Provider access timed out.", exhausted: true, now, null);

        Assert.Equal(AccountingProviderSwitchMonitoringStatuses.Failed, run.Status);
        Assert.Null(run.NextRunUtc);

        run.Retry(now.AddMinutes(5));
        Assert.Equal(AccountingProviderSwitchMonitoringStatuses.Active, run.Status);
        Assert.Equal(now.AddMinutes(5), run.NextRunUtc);
    }

    [Fact]
    public void Only_non_blocking_incidents_can_be_accepted_as_documented_exceptions()
    {
        var now = DateTime.UtcNow;
        var blocking = CreateIncident(isBlocking: true, now);
        Assert.Throws<InvalidOperationException>(() => blocking.AcceptException(Guid.NewGuid(), "Reviewed.",
            "One historical record.", 10m, "archive://evidence/1", now));

        var nonBlocking = CreateIncident(isBlocking: false, now);
        nonBlocking.AcceptException(Guid.NewGuid(), "Reviewed and immaterial.", "One historical record.",
            10m, "archive://evidence/2", now);
        Assert.Equal(AccountingProviderSwitchMonitoringIncidentStatuses.AcceptedException, nonBlocking.Status);
        Assert.Equal(10m, nonBlocking.FinancialImpact);
    }

    [Fact]
    public void Closing_into_a_corrective_cutover_retains_the_new_switch_reference()
    {
        var now = DateTime.UtcNow;
        var run = CreateRun(now);
        var correctiveSwitchId = Guid.NewGuid();
        run.Close(Guid.NewGuid(), "corrective_cutover_created", "Blocking variance requires a new cutover.",
            now.AddDays(1), correctiveSwitchId);

        Assert.Equal(AccountingProviderSwitchMonitoringStatuses.Closed, run.Status);
        Assert.Equal(correctiveSwitchId, run.CorrectiveSwitchId);
    }

    [Fact]
    public void Monitoring_continues_while_closure_approval_is_pending()
    {
        var now = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var run = CreateRun(now);
        run.Claim("worker-1", now, now.AddMinutes(2));
        run.CompletePass(hasBlockingIncident: false, now.AddMinutes(1), now.AddDays(1));
        var scheduledRun = run.NextRunUtc;

        run.AwaitClosureApproval(Guid.NewGuid(), new string('b', 64));

        Assert.Equal(AccountingProviderSwitchMonitoringStatuses.ClosureAwaitingApproval, run.Status);
        Assert.Equal(scheduledRun, run.NextRunUtc);
        run.Claim("worker-2", now.AddDays(1), now.AddDays(1).AddMinutes(2));
        run.CompletePass(hasBlockingIncident: false, now.AddDays(1).AddMinutes(1), now.AddDays(2));
        Assert.Equal(2, run.CheckSequence);
        Assert.Equal(AccountingProviderSwitchMonitoringStatuses.Active, run.Status);
    }

    private static AccountingProviderSwitchMonitoringRun CreateRun(DateTime now) => new(Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), 14, Guid.NewGuid(), null, "monitoring-test", now, now);

    private static AccountingProviderSwitchMonitoringIncident CreateIncident(bool isBlocking, DateTime now) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), "projection_integrity",
            isBlocking ? "critical" : "warning", isBlocking, "Observed variance.", Guid.NewGuid(), now);
}
