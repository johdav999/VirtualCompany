using System.Text.RegularExpressions;
using VirtualCompany.Application.Marketing;

namespace VirtualCompany.Infrastructure.Sales;

public sealed partial class MarketingPolicyService : IMarketingPolicyService
{
    private static readonly IReadOnlySet<string> ApprovalActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        MarketingPolicyActions.StrategyActivation, MarketingPolicyActions.CampaignLaunch,
        MarketingPolicyActions.AudienceActivation, MarketingPolicyActions.OutboundCommunication,
        MarketingPolicyActions.ContentPublication, MarketingPolicyActions.TrackingChange,
        MarketingPolicyActions.RegulatedClaim, MarketingPolicyActions.TargetSelection,
        MarketingPolicyActions.SegmentVersionChange
    };

    [GeneratedRegex("(?:race|ethnicity|religion|health|disability|sexual|political|union|biometric|genetic)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveCriteriaPattern();

    public MarketingPolicyDecision Evaluate(MarketingPolicyRequest request)
    {
        if (request.TargetId == Guid.Empty || request.TargetVersion < 1)
            return Deny("target_version_required", "A concrete immutable target and version are required.");
        var action = request.Action.Trim().ToLowerInvariant();
        var evidence = new[] { $"{request.TargetType}:{request.TargetId}:v{request.TargetVersion}" };
        if (action == MarketingPolicyActions.DestructiveAction)
            return Deny("destructive_action_denied", "Marketing cannot perform this destructive action automatically.", evidence);
        if (!string.IsNullOrWhiteSpace(request.SegmentCriteriaJson) && SensitiveCriteriaPattern().IsMatch(request.SegmentCriteriaJson))
            return Deny("sensitive_segment_criteria", "The segment uses sensitive or proxy criteria that cannot be used for targeting.", evidence);
        if (!request.HasRequiredEvidence)
            return Deny("required_evidence_missing", "Required evidence is missing or no longer current.", evidence);
        if (action is MarketingPolicyActions.OutboundCommunication or MarketingPolicyActions.AudienceActivation)
        {
            if (request.Suppressed) return Deny("recipient_suppressed", "The recipient or audience is currently suppressed.", evidence);
            if (!request.ConsentCurrent) return Deny("consent_not_current", "Current communication permission is required even when approval exists.", evidence);
        }
        if (action == MarketingPolicyActions.PaidSpend && request.Amount is decimal amount)
        {
            if (request.ApprovalThreshold is not decimal threshold)
                return Deny("spend_policy_missing", "A paid-spend approval threshold has not been configured.", evidence);
            if (amount > threshold && !request.ApprovalCompleted)
                return Review("spend_approval_required", "This spend exceeds the configured approval threshold.", evidence);
        }
        if (ApprovalActions.Contains(action) && !request.ApprovalCompleted)
            return Review("approval_required", "A company manager must approve this immutable action version before execution.", evidence);
        if (action == MarketingPolicyActions.BrandSafety && !request.ApprovalCompleted)
            return Review("brand_review_required", "A person must complete brand and safety review before external use.", evidence);
        return new(true, "allowed", "The action satisfies the current Marketing policy checks.", false, null, evidence);
    }

    private static MarketingPolicyDecision Deny(string code, string explanation, IReadOnlyList<string>? evidence = null) =>
        new(false, code, explanation, false, null, evidence ?? []);
    private static MarketingPolicyDecision Review(string code, string explanation, IReadOnlyList<string> evidence) =>
        new(false, code, explanation, true, "company_manager", evidence);
}
