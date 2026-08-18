namespace VirtualCompany.Application.Marketing;

public static class MarketingAgentAccessReasonCodes
{
    public const string InvalidContext = "marketing_agent_context_invalid";
    public const string Unavailable = "marketing_agent_unavailable";
}

public sealed record MarketingAgentAccessContext(
    Guid CompanyId,
    Guid AgentId,
    string DisplayName,
    string RoleName,
    string Department,
    string Status,
    string AutonomyLevel);

public sealed class MarketingAgentAccessException(string reasonCode, string message) : Exception(message)
{
    public string ReasonCode { get; } = reasonCode;
}

public interface IMarketingAgentAccessGuard
{
    Task<MarketingAgentAccessContext> RequireActiveMarketingAgentAsync(
        Guid companyId,
        Guid agentId,
        CancellationToken cancellationToken);
}

public static class MarketingToolIds
{
    public const string ReadWorkspace = "marketing.read_workspace";
    public const string ReadObjectives = "marketing.read_objectives";
    public const string ReadCampaigns = "marketing.read_campaigns";
    public const string ReadContentCalendar = "marketing.read_content_calendar";
    public const string ReadAudienceEvidence = "marketing.read_audience_evidence";
    public const string ReadChannelObservations = "marketing.read_channel_observations";
    public const string ReadAttributionSummary = "marketing.read_attribution_summary";
    public const string SearchApprovedKnowledge = "marketing.search_approved_knowledge";
    public const string ReadSegments = "marketing.read_segments";
    public const string ReadSegmentEvidence = "marketing.read_segment_evidence";
    public const string ReadStrategies = "marketing.read_strategies";
    public const string ReadPlans = "marketing.read_plans";
    public const string ReadPlanCoverage = "marketing.read_plan_coverage";
    public const string ReadCampaignReadiness = "marketing.read_campaign_readiness";

    public const string PreparePlan = "marketing.prepare_plan";
    public const string AnalyzeAudience = "marketing.analyze_audience";
    public const string PrepareContentBrief = "marketing.prepare_content_brief";
    public const string RecommendCampaignChange = "marketing.recommend_campaign_change";
    public const string PreparePerformanceReview = "marketing.prepare_performance_review";
    public const string PrepareExperiment = "marketing.prepare_experiment";
    public const string PrepareOperatingReview = "marketing.prepare_operating_review";
    public const string PrepareSegmentation = "marketing.prepare_segmentation";
    public const string RecommendTargetSegments = "marketing.recommend_target_segments";
    public const string AssessSegmentStrategyImpact = "marketing.assess_segment_strategy_impact";
    public const string PrepareCampaignPortfolio = "marketing.prepare_campaign_portfolio";
    public const string AssessPlanCoverage = "marketing.assess_plan_coverage";

    public const string CreatePlanDraft = "marketing.create_plan_draft";
    public const string CreateCampaignDrafts = "marketing.create_campaign_drafts";
    public const string PopulateCampaignDraft = "marketing.populate_campaign_draft";
    public const string SubmitPlanForReview = "marketing.submit_plan_for_review";
    public const string SubmitCampaignForReadiness = "marketing.submit_campaign_for_readiness";

    public static IReadOnlyList<string> ReadTools { get; } =
    [
        ReadWorkspace,
        ReadObjectives,
        ReadCampaigns,
        ReadContentCalendar,
        ReadAudienceEvidence,
        ReadChannelObservations,
        ReadAttributionSummary,
        SearchApprovedKnowledge,
        ReadSegments,
        ReadSegmentEvidence,
        ReadStrategies,
        ReadPlans,
        ReadPlanCoverage,
        ReadCampaignReadiness
    ];

    public static IReadOnlyList<string> RecommendTools { get; } =
    [
        PreparePlan,
        AnalyzeAudience,
        PrepareContentBrief,
        RecommendCampaignChange,
        PreparePerformanceReview,
        PrepareExperiment,
        PrepareOperatingReview,
        PrepareSegmentation,
        RecommendTargetSegments,
        AssessSegmentStrategyImpact,
        PrepareCampaignPortfolio,
        AssessPlanCoverage
    ];

    public static IReadOnlyList<string> ExecuteTools { get; } =
    [
        CreatePlanDraft,
        CreateCampaignDrafts,
        PopulateCampaignDraft,
        SubmitPlanForReview,
        SubmitCampaignForReadiness
    ];
}
