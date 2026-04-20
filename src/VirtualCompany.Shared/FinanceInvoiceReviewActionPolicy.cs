namespace VirtualCompany.Shared;

public static class FinanceInvoiceReviewActionPolicy
{
    public static bool IsActionableReviewState(string? reviewTaskStatus) =>
        string.Equals(NormalizeReviewToken(reviewTaskStatus), "awaiting_approval", StringComparison.Ordinal);

    public static bool CanPerformReviewAction(bool hasInvoiceApprovalPermission, string? reviewTaskStatus) =>
        hasInvoiceApprovalPermission && IsActionableReviewState(reviewTaskStatus);

    public static FinanceInvoiceReviewActionAvailabilityResponse ApplyUserPermission(
        FinanceInvoiceReviewActionAvailabilityResponse? actions,
        string? membershipRole)
    {
        var serverActions = actions ?? new FinanceInvoiceReviewActionAvailabilityResponse();
        var hasApprovalPermission = FinanceAccess.CanApproveInvoices(membershipRole);
        var isActionable = hasApprovalPermission && serverActions.IsActionable;

        return new FinanceInvoiceReviewActionAvailabilityResponse
        {
            IsActionable = isActionable,
            CanApprove = isActionable && serverActions.CanApprove,
            CanReject = isActionable && serverActions.CanReject,
            CanSendForFollowUp = isActionable && serverActions.CanSendForFollowUp
        };
    }

    private static string? NormalizeReviewToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().Replace(" ", "_", StringComparison.Ordinal).Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();
    }
}