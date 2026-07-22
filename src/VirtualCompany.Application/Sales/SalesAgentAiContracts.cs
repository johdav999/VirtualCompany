using VirtualCompany.Application.Agents;

namespace VirtualCompany.Application.Sales;

public static class SalesAgentAnalysisTypes
{
    public const string LeadIntelligence = "lead_intelligence";
    public const string NextBestAction = "next_best_action";
    public const string DealRisk = "deal_risk";
    public const string ForecastAnalysis = "forecast_analysis";
    public const string CampaignOptimization = "campaign_optimization";
    public const string ProposalAdvice = "proposal_advice";
    public const string OperatingCadence = "operating_cadence";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { LeadIntelligence, NextBestAction, DealRisk, ForecastAnalysis, CampaignOptimization, ProposalAdvice, OperatingCadence };
}

public interface ISalesAgentAnalysisService
{
    Task<RoleAgentAnalysisResult> AnalyzeAsync(Guid companyId, Guid agentId, Guid? actorUserId,
        RoleAgentAnalysisRequest request, CancellationToken cancellationToken);
}
