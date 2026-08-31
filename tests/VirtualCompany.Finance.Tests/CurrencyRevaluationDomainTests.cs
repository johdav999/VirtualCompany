using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Finance.Tests;

public sealed class CurrencyRevaluationDomainTests
{
    private static readonly DateTime Now = new(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Exact_proposal_moves_through_approval_posting_and_idempotent_reversal()
    {
        var actor = Guid.NewGuid();
        var run = NewRun(actor);
        run.RecordProposal(Hash('a'), Hash('b'), Hash('c'), 2, 2, 0, 0,
            150m, 1_500m, 1_525m, 25m, Now.AddMinutes(1));
        var approval = Guid.NewGuid();
        var journal = Guid.NewGuid();
        var reversal = Guid.NewGuid();

        run.BindApproval(approval, Now.AddMinutes(2));
        run.MarkPosted(journal, actor, Now.AddMinutes(3));
        run.MarkReversed(reversal, actor, Now.AddDays(1));
        var reversedVersion = run.Version;
        run.MarkReversed(reversal, actor, Now.AddDays(1));

        Assert.Equal(CurrencyRevaluationRunStatuses.Reversed, run.Status);
        Assert.Equal(journal, run.LedgerEntryId);
        Assert.Equal(reversal, run.ReversalLedgerEntryId);
        Assert.Equal(reversedVersion, run.Version);
        Assert.Equal(Hash('a'), run.PopulationChecksum);
        Assert.Equal(Hash('b'), run.RateSetChecksum);
        Assert.Equal(Hash('c'), run.ProposalChecksum);
    }

    [Fact]
    public void Posted_evidence_is_immutable_and_requires_reversal_replacement()
    {
        var actor = Guid.NewGuid();
        var run = NewRun(actor);
        run.RecordProposal(Hash('a'), Hash('b'), Hash('c'), 1, 1, 0, 0,
            100m, 1_000m, 1_010m, 10m, Now.AddMinutes(1));
        run.BindApproval(Guid.NewGuid(), Now.AddMinutes(2));
        run.MarkPosted(Guid.NewGuid(), actor, Now.AddMinutes(3));

        Assert.Throws<InvalidOperationException>(() => run.RecordProposal(Hash('d'), Hash('e'), Hash('f'),
            1, 1, 0, 0, 100m, 1_000m, 1_020m, 20m, Now.AddMinutes(4)));
        Assert.Throws<InvalidOperationException>(() => run.Fail("changed", "Cannot mutate posted evidence.", Now.AddMinutes(4)));
    }

    [Fact]
    public void Regenerated_run_supersedes_mutable_approved_version_without_touching_posted_evidence()
    {
        var actor = Guid.NewGuid();
        var run = NewRun(actor);
        run.RecordProposal(Hash('a'), Hash('b'), Hash('c'), 1, 1, 0, 0,
            100m, 1_000m, 1_010m, 10m, Now.AddMinutes(1));
        run.BindApproval(Guid.NewGuid(), Now.AddMinutes(2));
        var replacement = Guid.NewGuid();

        run.Supersede(replacement, Now.AddMinutes(3));

        Assert.Equal(CurrencyRevaluationRunStatuses.Superseded, run.Status);
        Assert.Equal(replacement, run.SupersededByRunId);
        Assert.Throws<InvalidOperationException>(() => run.BindApproval(Guid.NewGuid(), Now.AddMinutes(4)));
    }

    private static CurrencyRevaluationRun NewRun(Guid actor) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        1, new DateOnly(2026, 8, 31), "SEK", "A", $"test:{Guid.NewGuid():N}", actor, Now, false);
    private static string Hash(char value) => new(value, 64);
}
