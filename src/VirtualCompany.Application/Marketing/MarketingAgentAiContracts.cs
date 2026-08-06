using VirtualCompany.Application.Agents;

namespace VirtualCompany.Application.Marketing;

public static class MarketingAgentAnalysisTypes
{
    public const string Planning = "planning";
    public const string AudienceIntelligence = "audience_intelligence";
    public const string ContentAdvice = "content_advice";
    public const string CampaignCoordination = "campaign_coordination";
    public const string PerformanceAnalysis = "performance_analysis";
    public const string ExperimentAdvice = "experiment_advice";
    public const string OperatingCadence = "operating_cadence";
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { Planning, AudienceIntelligence, ContentAdvice, CampaignCoordination, PerformanceAnalysis, ExperimentAdvice, OperatingCadence };
}

public interface IMarketingAgentAnalysisService
{
    Task<RoleAgentAnalysisResult> AnalyzeAsync(Guid companyId, Guid agentId, Guid? actorUserId,
        RoleAgentAnalysisRequest request, CancellationToken cancellationToken);
}
