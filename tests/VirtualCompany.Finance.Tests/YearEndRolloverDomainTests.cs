using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Finance.Tests;

public sealed class YearEndRolloverDomainTests
{
    private static readonly string Evidence = new('a', 64);
    private static readonly string Checksum = new('b', 64);
    private static readonly DateTime Now = new(2026, 8, 30, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Run_requires_one_exact_fiscal_year_and_distinct_equity_roles()
    {
        var account = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => new YearEndRun(Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2025, 1, 1), new DateOnly(2025, 11, 30), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), "YE", Guid.NewGuid(), Now));
        Assert.Throws<ArgumentException>(() => new YearEndRun(Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), Guid.NewGuid(),
            account, account, "YE", Guid.NewGuid(), Now));
    }

    [Fact]
    public void Approval_requires_independent_reviewer_and_exact_evidence()
    {
        var preparer = Guid.NewGuid();
        var run = Run(preparer);
        run.ApplyReadiness(Guid.NewGuid(), true, Now.AddMinutes(1));
        run.Submit(preparer, Evidence, Now.AddMinutes(2));

        Assert.Throws<InvalidOperationException>(() => run.Review(preparer, true, Evidence, Now.AddMinutes(3)));
        Assert.Throws<InvalidOperationException>(() => run.Review(Guid.NewGuid(), true, Checksum, Now.AddMinutes(3)));
        run.Review(Guid.NewGuid(), true, Evidence, Now.AddMinutes(3));

        Assert.Equal(YearEndRunStatuses.Approved, run.Status);
        Assert.NotEqual(run.PreparedByUserId, run.ApprovedByUserId);
    }

    [Fact]
    public void Golden_lifecycle_requires_posting_then_zero_difference_reconciliation()
    {
        var preparer = Guid.NewGuid();
        var run = Run(preparer);
        run.ApplyReadiness(Guid.NewGuid(), true, Now.AddMinutes(1));
        run.Submit(preparer, Evidence, Now.AddMinutes(2));
        run.Review(Guid.NewGuid(), true, Evidence, Now.AddMinutes(3));
        run.BeginExecution(Guid.NewGuid(), Evidence, Now.AddMinutes(4));
        run.MarkExecuted(Guid.NewGuid(), Guid.NewGuid(), Now.AddMinutes(5));
        run.Reconcile(Guid.NewGuid(), Checksum, true, Now.AddMinutes(6));
        run.Complete(Guid.NewGuid(), Now.AddMinutes(7));

        Assert.Equal(YearEndRunStatuses.Completed, run.Status);
        Assert.Equal(Checksum, run.OpeningBalanceChecksum);
        Assert.NotNull(run.RetainedEarningsLedgerEntryId);
        Assert.NotNull(run.OpeningBalanceLedgerEntryId);
    }

    [Fact]
    public void Reconciliation_mismatch_blocks_finalization_and_retains_failure()
    {
        var preparer = Guid.NewGuid();
        var run = Run(preparer);
        run.ApplyReadiness(Guid.NewGuid(), true, Now.AddMinutes(1));
        run.Submit(preparer, Evidence, Now.AddMinutes(2));
        run.Review(Guid.NewGuid(), true, Evidence, Now.AddMinutes(3));
        run.BeginExecution(Guid.NewGuid(), Evidence, Now.AddMinutes(4));
        run.MarkExecuted(null, Guid.NewGuid(), Now.AddMinutes(5));
        run.Reconcile(Guid.NewGuid(), Checksum, false, Now.AddMinutes(6));

        Assert.Equal(YearEndRunStatuses.Failed, run.Status);
        Assert.Equal("opening_balance_mismatch", run.FailureCode);
        Assert.Throws<InvalidOperationException>(() => run.Complete(Guid.NewGuid(), Now.AddMinutes(7)));
    }

    [Fact]
    public void Opening_candidate_preserves_currency_and_dimension_key_and_detects_difference()
    {
        var candidate = new YearEndOpeningBalanceCandidate(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), "1510", "Customer receivables", "asset", "eur",
            "cost_center=STO|project=P-42", "{\"cost_center\":\"STO\",\"project\":\"P-42\"}",
            12500m, 1100m, Now);

        candidate.MarkPosted(Guid.NewGuid());
        candidate.Reconcile(12499.99m);

        Assert.Equal("EUR", candidate.SourceCurrency);
        Assert.Equal("cost_center=STO|project=P-42", candidate.DimensionKey);
        Assert.Equal(-0.01m, candidate.Difference);
        Assert.Equal(YearEndCandidateStatuses.Mismatch, candidate.Status);
    }

    [Theory]
    [InlineData(SubsequentEventDecisions.PostForward, true, false)]
    [InlineData(SubsequentEventDecisions.RequestReopen, false, true)]
    [InlineData(SubsequentEventDecisions.DiscloseOnly, false, false)]
    public void Subsequent_event_resolution_enforces_selected_correction_path(string decision,
        bool needsJournal, bool needsReopen)
    {
        var recorder = Guid.NewGuid();
        var item = new YearEndSubsequentEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 2, 1), "Material subsequent event", "Retained evidence and assessment.",
            25000m, "SEK", decision, Guid.NewGuid(), null, recorder, Now);
        item.Submit(recorder, Now.AddMinutes(1));
        Assert.Throws<InvalidOperationException>(() => item.Review(recorder, true, Now.AddMinutes(2)));
        item.Review(Guid.NewGuid(), true, Now.AddMinutes(2));

        if (needsJournal || needsReopen)
            Assert.Throws<InvalidOperationException>(() => item.LinkResolution(null, null, Now.AddMinutes(3)));
        item.LinkResolution(needsJournal ? Guid.NewGuid() : null, needsReopen ? Guid.NewGuid() : null, Now.AddMinutes(3));

        Assert.Equal(SubsequentEventStatuses.Resolved, item.Status);
    }

    private static YearEndRun Run(Guid preparer) => new(Guid.NewGuid(), Guid.NewGuid(),
        new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), "ye", preparer, Now);
}
