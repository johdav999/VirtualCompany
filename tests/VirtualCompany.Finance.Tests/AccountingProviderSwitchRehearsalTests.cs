using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Finance.Tests;

public sealed class AccountingProviderSwitchRehearsalTests
{
    private static readonly DateTime Now = new(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Lease_expiry_can_be_reclaimed_and_terminal_failure_releases_the_lease()
    {
        var run = new AccountingProviderSwitchRehearsal(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), "stable-key", "correlation", Now);
        run.Claim("worker-1", Now.AddMinutes(1), Now);
        run.SetProgress(2, 10);
        run.Claim("worker-2", Now.AddMinutes(2), Now.AddMinutes(1));
        run.Fail("permanent_validation_failure", "The staged source data is invalid.", Now.AddMinutes(1));

        Assert.Equal(2, run.AttemptCount);
        Assert.Equal(AccountingProviderSwitchRehearsalStatuses.Failed, run.Status);
        Assert.Null(run.LeaseOwner);
        Assert.Null(run.NextAttemptUtc);
    }

    [Fact]
    public void Manual_evidence_requires_reference_and_future_expiry()
    {
        var companyId = Guid.NewGuid(); var switchId = Guid.NewGuid(); var rehearsalId = Guid.NewGuid();
        var checkId = Guid.NewGuid(); var actor = Guid.NewGuid(); var hash = new string('a', 64);
        Assert.Throws<ArgumentException>(() => new AccountingProviderSwitchManualEvidence(companyId, switchId,
            rehearsalId, checkId, hash, "External accountant confirmed archive access.", "", actor, Now, null));
        Assert.Throws<ArgumentException>(() => new AccountingProviderSwitchManualEvidence(companyId, switchId,
            rehearsalId, checkId, hash, "External accountant confirmed archive access.", "document:123", actor,
            Now, Now));
        var evidence = new AccountingProviderSwitchManualEvidence(companyId, switchId, rehearsalId, checkId,
            hash, "External accountant confirmed archive access.", "document:123", actor, Now, Now.AddDays(30));
        Assert.Equal("document:123", evidence.EvidenceReference);
    }

    [Fact]
    public void Cutover_plan_validates_hash_and_freeze_window_at_creation()
    {
        var hash = new string('b', 64);
        Assert.Throws<ArgumentException>(() => new AccountingProviderSwitchCutoverPlan(Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), 1, hash, hash,
            AccountingProviderSwitchStrategies.OpeningBalancesAndOpenItems, Now, Now,
            "Recover before activation.", "[]", "{}", Guid.NewGuid(), Now));
    }
}
