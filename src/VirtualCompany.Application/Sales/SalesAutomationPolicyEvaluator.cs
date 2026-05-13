namespace VirtualCompany.Application.Sales;

public static class SalesAutomationPolicyModes
{
    public const string ManualOnly = "manual_only";
    public const string DraftOnly = "draft_only";
    public const string AutoSendLowRiskFollowUps = "auto_send_low_risk_follow_ups";
}

public static class SalesRecommendationActions
{
    public const string CreateDraftReply = "create_draft_reply";
    public const string SendEmail = "send_email";
    public const string CreateFinanceDocument = "create_finance_document";
}

public static class SalesRecommendationRiskLevels
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
}

public sealed record SalesAutomationPolicyDecision(
    string PolicyMode,
    string ActionType,
    string ExecutionMode,
    bool RequiresApproval,
    bool CanAutoExecute,
    string Reason);

public interface ISalesAutomationPolicyEvaluator
{
    SalesAutomationPolicyDecision Evaluate(string policyMode, string actionType, string riskLevel);
}

public sealed class SalesAutomationPolicyEvaluator : ISalesAutomationPolicyEvaluator
{
    public SalesAutomationPolicyDecision Evaluate(string policyMode, string actionType, string riskLevel)
    {
        var normalizedMode = Normalize(policyMode, SalesAutomationPolicyModes.ManualOnly);
        var normalizedAction = Normalize(actionType, SalesRecommendationActions.CreateDraftReply);
        var normalizedRisk = Normalize(riskLevel, SalesRecommendationRiskLevels.Medium);

        if (normalizedAction == SalesRecommendationActions.CreateFinanceDocument)
        {
            return new(normalizedMode, normalizedAction, "approval_required", true, false, "Finance documents always need approval before Alex creates a quote or invoice.");
        }

        return normalizedMode switch
        {
            SalesAutomationPolicyModes.AutoSendLowRiskFollowUps when normalizedAction == SalesRecommendationActions.SendEmail && normalizedRisk == SalesRecommendationRiskLevels.Low => new(normalizedMode, normalizedAction, "auto_send", false, true, "Low-risk follow-ups can be sent automatically under the current sales policy."),
            SalesAutomationPolicyModes.DraftOnly when normalizedAction == SalesRecommendationActions.CreateDraftReply => new(normalizedMode, normalizedAction, "draft", false, true, "Alex can prepare a draft reply, but sending still needs review."),
            _ => new(normalizedMode, normalizedAction, "approval_required", true, false, "The current sales policy requires review before this action runs.")
        };
    }

    private static string Normalize(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();
}