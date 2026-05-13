namespace VirtualCompany.Domain.Entities;

public sealed partial class SalesSequenceExecutionStep
{
    public string? PolicyDecisionOutcome { get; private set; }
    public string? PolicyDecisionReasonCode { get; private set; }
    public string? PolicyDecisionReason { get; private set; }
    public Guid? OutboundMessageReviewId { get; private set; }
    public DateTime? PolicyEvaluatedUtc { get; private set; }

    public void MarkBlockedByPolicy(string reasonCode, string reason)
    {
        Status = SalesStatuses.Blocked;
        DeliveryStatus = SalesStatuses.Blocked;
        PolicyDecisionOutcome = "blocked";
        PolicyDecisionReasonCode = SalesEntityText.NormalizeRequired(reasonCode, nameof(reasonCode), 120);
        PolicyDecisionReason = SalesEntityText.NormalizeRequired(reason, nameof(reason), 1000);
        PolicyEvaluatedUtc = DateTime.UtcNow;
        UpdatedUtc = PolicyEvaluatedUtc.Value;
    }

    public void MarkWaitingForOutboundReview(Guid reviewId, string reasonCode, string reason)
    {
        Status = SalesStatuses.WaitingForApproval;
        OutboundMessageReviewId = SalesEntityText.NormalizeOptionalId(reviewId, nameof(reviewId))!.Value;
        PolicyDecisionOutcome = "requires_approval";
        PolicyDecisionReasonCode = SalesEntityText.NormalizeRequired(reasonCode, nameof(reasonCode), 120);
        PolicyDecisionReason = SalesEntityText.NormalizeRequired(reason, nameof(reason), 1000);
        PolicyEvaluatedUtc = DateTime.UtcNow;
        UpdatedUtc = PolicyEvaluatedUtc.Value;
    }
}