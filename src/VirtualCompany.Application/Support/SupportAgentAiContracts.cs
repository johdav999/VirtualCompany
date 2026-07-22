using VirtualCompany.Application.Agents;

namespace VirtualCompany.Application.Support;

public static class SupportAgentAnalysisTypes
{
    public const string TriageAnalysis = "triage_analysis";
    public const string GroundedReply = "grounded_reply";
    public const string RiskEscalation = "risk_escalation";
    public const string RootCauseAnalysis = "root_cause_analysis";
    public const string KnowledgeCoverage = "knowledge_coverage";
    public const string OperatingCadence = "operating_cadence";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { TriageAnalysis, GroundedReply, RiskEscalation, RootCauseAnalysis, KnowledgeCoverage, OperatingCadence };
}

public interface ISupportAgentAnalysisService
{
    Task<RoleAgentAnalysisResult> AnalyzeAsync(Guid companyId, Guid agentId, Guid? actorUserId,
        RoleAgentAnalysisRequest request, CancellationToken cancellationToken);
}
