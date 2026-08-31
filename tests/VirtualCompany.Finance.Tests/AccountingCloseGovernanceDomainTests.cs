using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Finance.Tests;

public sealed class AccountingCloseGovernanceDomainTests
{
    private static readonly string HashA = new('A', 64);
    private static readonly string HashB = new('B', 64);

    [Fact]
    public void Readiness_Requires_Independent_Reviewer()
    {
        var preparer = Guid.NewGuid();
        var snapshot = Snapshot(preparer);
        snapshot.Submit(preparer, DateTime.UtcNow.AddMinutes(1));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            snapshot.Approve(preparer, DateTime.UtcNow.AddMinutes(2)));

        Assert.Contains("cannot approve", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AccountingCloseReadinessStatuses.InReview, snapshot.Status);
    }

    [Fact]
    public void Approved_Readiness_Binds_Lock_To_Exact_Hash()
    {
        var preparer = Guid.NewGuid();
        var reviewer = Guid.NewGuid();
        var snapshot = Snapshot(preparer);
        snapshot.Submit(preparer, DateTime.UtcNow.AddMinutes(1));
        snapshot.Approve(reviewer, DateTime.UtcNow.AddMinutes(2));

        Assert.Throws<InvalidOperationException>(() =>
            snapshot.MarkLocked(Guid.NewGuid(), HashB, DateTime.UtcNow.AddMinutes(3)));

        snapshot.MarkLocked(Guid.NewGuid(), HashA, DateTime.UtcNow.AddMinutes(3));
        Assert.Equal(AccountingCloseReadinessStatuses.Locked, snapshot.Status);
    }

    [Fact]
    public void Readiness_Cancel_Retains_Reason_And_Prevents_Later_Submission()
    {
        var snapshot = Snapshot(Guid.NewGuid());

        snapshot.Cancel("The close scope changed and must be prepared again.", DateTime.UtcNow.AddMinutes(1));

        Assert.Equal(AccountingCloseReadinessStatuses.Cancelled, snapshot.Status);
        Assert.Contains("scope changed", snapshot.ReviewReason, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<InvalidOperationException>(() =>
            snapshot.Submit(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(2)));
    }

    [Fact]
    public void Waiver_Applies_Only_To_Exact_Unexpired_Evidence()
    {
        var proposer = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var waiver = new AccountingCloseWaiver(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "close_document_gap", HashA, "Document will arrive after the operational deadline.", 100m,
            Guid.NewGuid(), HashB, Guid.NewGuid(), proposer, now.AddHours(1), now);
        waiver.Approve(Guid.NewGuid(), now.AddMinutes(1));

        Assert.True(waiver.AppliesTo("close_document_gap", HashA, now.AddMinutes(2)));
        Assert.False(waiver.AppliesTo("close_document_gap", HashB, now.AddMinutes(2)));
        Assert.False(waiver.AppliesTo("close_document_gap", HashA, now.AddHours(2)));
    }

    [Fact]
    public void Reopen_Retains_Scope_Correction_Path_And_Independent_Approval()
    {
        var requester = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var request = new AccountingCloseReopenRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), HashA, "A material supplier invoice arrived after close.", "AP only",
            "Post the invoice, run AP reconciliation, then prepare a replacement close snapshot.",
            requester, now.AddHours(12), now);

        Assert.Throws<InvalidOperationException>(() => request.Review(requester, true, now.AddMinutes(1)));
        request.Review(Guid.NewGuid(), true, now.AddMinutes(1));
        request.MarkExecuted(Guid.NewGuid(), now.AddMinutes(2));

        Assert.Equal(AccountingCloseReopenStatuses.Executed, request.Status);
        Assert.Equal("AP only", request.Scope);
        Assert.Contains("replacement close snapshot", request.CorrectionPath, StringComparison.Ordinal);
    }

    private static AccountingCloseReadinessSnapshot Snapshot(Guid preparer) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, HashA, HashB,
            true, preparer, DateTime.UtcNow);
}
