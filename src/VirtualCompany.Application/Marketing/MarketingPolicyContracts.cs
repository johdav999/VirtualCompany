namespace VirtualCompany.Application.Marketing;

public sealed record MarketingPolicyRequest(
    string Action,
    string TargetType,
    Guid TargetId,
    int TargetVersion,
    bool HasRequiredEvidence,
    bool ApprovalCompleted = false,
    bool ConsentCurrent = true,
    bool Suppressed = false,
    decimal? Amount = null,
    decimal? ApprovalThreshold = null,
    string? SegmentCriteriaJson = null);

public sealed record MarketingPolicyDecision(
    bool Allowed,
    string ReasonCode,
    string Explanation,
    bool RequiresApproval,
    string? RequiredRole,
    IReadOnlyList<string> Evidence);

public interface IMarketingPolicyService
{
    MarketingPolicyDecision Evaluate(MarketingPolicyRequest request);
}

public static class MarketingPolicyActions
{
    public const string InternalDraft = "internal_draft";
    public const string StrategyActivation = "strategy_activation";
    public const string CampaignLaunch = "campaign_launch";
    public const string AudienceActivation = "audience_activation";
    public const string OutboundCommunication = "outbound_communication";
    public const string ContentPublication = "content_publication";
    public const string PaidSpend = "paid_spend";
    public const string TrackingChange = "tracking_change";
    public const string RegulatedClaim = "regulated_claim";
    public const string BrandSafety = "brand_safety";
    public const string TargetSelection = "target_selection";
    public const string SegmentVersionChange = "segment_version_change";
    public const string DestructiveAction = "destructive_action";
}
