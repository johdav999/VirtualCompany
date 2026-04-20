using VirtualCompany.Shared;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceInvoiceReviewActionPolicyTests
{
    [Fact]
    public void Is_actionable_review_state_only_allows_awaiting_approval()
    {
        Assert.True(FinanceInvoiceReviewActionPolicy.IsActionableReviewState("awaiting_approval"));
        Assert.True(FinanceInvoiceReviewActionPolicy.IsActionableReviewState("awaiting-approval"));
        Assert.False(FinanceInvoiceReviewActionPolicy.IsActionableReviewState("completed"));
        Assert.False(FinanceInvoiceReviewActionPolicy.IsActionableReviewState("not_started"));
        Assert.False(FinanceInvoiceReviewActionPolicy.IsActionableReviewState(null));
    }

    [Fact]
    public void Can_perform_review_action_requires_permission_and_actionable_state()
    {
        Assert.True(FinanceInvoiceReviewActionPolicy.CanPerformReviewAction(true, "awaiting_approval"));
        Assert.False(FinanceInvoiceReviewActionPolicy.CanPerformReviewAction(true, "completed"));
        Assert.False(FinanceInvoiceReviewActionPolicy.CanPerformReviewAction(false, "awaiting_approval"));
    }

    [Fact]
    public void Apply_user_permission_preserves_backend_actions_for_approvers()
    {
        var availability = FinanceInvoiceReviewActionPolicy.ApplyUserPermission(
            new FinanceInvoiceReviewActionAvailabilityResponse
            {
                IsActionable = true,
                CanApprove = true,
                CanReject = true,
                CanSendForFollowUp = true
            },
            "owner");

        Assert.True(availability.IsActionable);
        Assert.True(availability.CanApprove);
        Assert.True(availability.CanReject);
        Assert.True(availability.CanSendForFollowUp);
    }

    [Fact]
    public void Apply_user_permission_hides_actions_for_non_approvers()
    {
        var availability = FinanceInvoiceReviewActionPolicy.ApplyUserPermission(
            new FinanceInvoiceReviewActionAvailabilityResponse
            {
                IsActionable = true,
                CanApprove = true,
                CanReject = true,
                CanSendForFollowUp = true
            },
            "employee");

        Assert.False(availability.IsActionable);
        Assert.False(availability.CanApprove);
        Assert.False(availability.CanReject);
        Assert.False(availability.CanSendForFollowUp);
    }
}