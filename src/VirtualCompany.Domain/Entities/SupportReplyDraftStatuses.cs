using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Entities;
public static class SupportReplyDraftStatuses
{
    public const string Draft = "draft";
    public const string NeedsReview = "needs_review";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Superseded = "superseded";
}

public static class SupportReplyDeliveryStatuses
{
    public const string Pending = "pending";
    public const string Sent = "sent";
    public const string Failed = "failed";
    public const string ReconciliationRequired = "reconciliation_required";
}
