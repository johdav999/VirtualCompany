using VirtualCompany.Domain.Entities;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceBillInboxReviewStateTests
{
    [Fact]
    public void Approve_records_auditable_transition_metadata()
    {
        var companyId = Guid.NewGuid();
        var billId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var occurredUtc = new DateTime(2026, 4, 26, 12, 0, 0, DateTimeKind.Utc);
        var state = CreateState(companyId, billId, FinanceBillInboxStatuses.ProposedForApproval);

        var action = state.Approve(actorId, "Finance Approver", "Matches purchase order.", occurredUtc, hasUnresolvedValidationFailures: false);

        Assert.Equal(FinanceBillInboxStatuses.Approved, state.Status);
        Assert.Equal("approve", action.Action);
        Assert.Equal(companyId, action.CompanyId);
        Assert.Equal(billId, action.DetectedBillId);
        Assert.Equal(actorId, action.ActorUserId);
        Assert.Equal("Finance Approver", action.ActorDisplayName);
        Assert.Equal(occurredUtc, action.OccurredUtc);
        Assert.Equal(FinanceBillInboxStatuses.ProposedForApproval, action.PriorStatus);
        Assert.Equal(FinanceBillInboxStatuses.Approved, action.NewStatus);
        Assert.Equal("Matches purchase order.", action.Rationale);
        Assert.Same(action, Assert.Single(state.Actions));
    }

    [Fact]
    public void Approve_blocks_unresolved_validation_failures()
    {
        var state = CreateState(Guid.NewGuid(), Guid.NewGuid(), FinanceBillInboxStatuses.ProposedForApproval);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            state.Approve(Guid.NewGuid(), "Finance Approver", "Looks fine.", DateTime.UtcNow, hasUnresolvedValidationFailures: true));

        Assert.Equal("Finance bill approval is blocked while validation failures are unresolved.", ex.Message);
        Assert.Equal(FinanceBillInboxStatuses.ProposedForApproval, state.Status);
        Assert.Empty(state.Actions);
    }

    [Theory]
    [InlineData(FinanceBillInboxStatuses.Detected)]
    [InlineData(FinanceBillInboxStatuses.Extracted)]
    [InlineData(FinanceBillInboxStatuses.NeedsReview)]
    [InlineData(FinanceBillInboxStatuses.ProposedForApproval)]
    public void Approve_allows_active_inbox_statuses(string status)
    {
        var state = CreateState(Guid.NewGuid(), Guid.NewGuid(), status);

        var action = state.Approve(Guid.NewGuid(), "Finance Approver", "Approved after review.", DateTime.UtcNow, hasUnresolvedValidationFailures: false);

        Assert.Equal(status, action.PriorStatus);
        Assert.Equal(FinanceBillInboxStatuses.Approved, action.NewStatus);
    }

    [Theory]
    [InlineData(FinanceBillInboxStatuses.Approved)]
    [InlineData(FinanceBillInboxStatuses.Rejected)]
    [InlineData(FinanceBillInboxStatuses.SentToPaymentExported)]
    public void Approve_rejects_terminal_statuses(string status)
    {
        var state = CreateState(Guid.NewGuid(), Guid.NewGuid(), status);

        Assert.Throws<InvalidOperationException>(() =>
            state.Approve(Guid.NewGuid(), "Finance Approver", "Trying again.", DateTime.UtcNow, hasUnresolvedValidationFailures: false));

        Assert.Equal(status, state.Status);
        Assert.Empty(state.Actions);
    }

    [Fact]
    public void Reject_records_history_and_terminal_status()
    {
        var occurredUtc = new DateTime(2026, 4, 26, 13, 0, 0, DateTimeKind.Utc);
        var state = CreateState(Guid.NewGuid(), Guid.NewGuid(), FinanceBillInboxStatuses.NeedsReview);

        var action = state.Reject(Guid.NewGuid(), "Finance Reviewer", "Supplier confirmed it is void.", occurredUtc);

        Assert.Equal("reject", action.Action);
        Assert.Equal(FinanceBillInboxStatuses.NeedsReview, action.PriorStatus);
        Assert.Equal(FinanceBillInboxStatuses.Rejected, action.NewStatus);
        Assert.Equal(FinanceBillInboxStatuses.Rejected, state.Status);
        Assert.Equal(occurredUtc, action.OccurredUtc);
    }

    [Fact]
    public void RequestClarification_records_history_without_leaving_inbox_lifecycle()
    {
        var state = CreateState(Guid.NewGuid(), Guid.NewGuid(), FinanceBillInboxStatuses.ProposedForApproval);

        var action = state.RequestClarification(Guid.NewGuid(), "Finance Reviewer", "Need corrected VAT evidence.", DateTime.UtcNow);

        Assert.Equal("clarification_requested", action.Action);
        Assert.Equal(FinanceBillInboxStatuses.ProposedForApproval, action.PriorStatus);
        Assert.Equal(FinanceBillInboxStatuses.NeedsReview, action.NewStatus);
        Assert.Equal(FinanceBillInboxStatuses.NeedsReview, state.Status);
    }

    [Theory]
    [InlineData(FinanceBillInboxStatuses.Approved)]
    [InlineData(FinanceBillInboxStatuses.Rejected)]
    [InlineData(FinanceBillInboxStatuses.SentToPaymentExported)]
    public void RequestClarification_rejects_terminal_statuses(string status)
    {
        var state = CreateState(Guid.NewGuid(), Guid.NewGuid(), status);

        Assert.Throws<InvalidOperationException>(() =>
            state.RequestClarification(Guid.NewGuid(), "Finance Reviewer", "Need more detail.", DateTime.UtcNow));

        Assert.Equal(status, state.Status);
        Assert.Empty(state.Actions);
    }

    [Fact]
    public void BillApprovalProposal_never_requests_payment_execution()
    {
        var proposal = new BillApprovalProposal(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Please approve this bill proposal. Approval records the decision only and does not initiate payment.",
            Guid.NewGuid(),
            DateTime.UtcNow);

        Assert.False(proposal.PaymentExecutionRequested);
    }

    [Fact]
    public void BillApprovalProposal_rejects_auto_payment_language()
    {
        Assert.Throws<InvalidOperationException>(() => new BillApprovalProposal(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Payment has been initiated for this bill.", Guid.NewGuid(), DateTime.UtcNow));
    }

    private static FinanceBillReviewState CreateState(Guid companyId, Guid billId, string status) =>
        new(Guid.NewGuid(), companyId, billId, status, "Approval records the decision only and does not initiate payment.", DateTime.UtcNow);
}
