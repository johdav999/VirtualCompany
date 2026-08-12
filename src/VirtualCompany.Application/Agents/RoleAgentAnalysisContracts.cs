namespace VirtualCompany.Application.Agents;

public sealed record RoleAgentAnalysisRequest(
    string AnalysisType,
    Guid? SubjectId = null,
    int HorizonDays = 30,
    string? Objective = null,
    DateTime? AsOfUtc = null,
    string Cadence = "on_demand",
    bool IsBootstrap = false);

public sealed record RoleAgentMetric(
    string Key,
    string Label,
    decimal Value,
    string? Unit,
    string SourceId,
    DateTime AsOfUtc);

public sealed record RoleAgentPriority(
    string SubjectType,
    Guid SubjectId,
    string Title,
    int Score,
    string Band,
    IReadOnlyList<string> ReasonCodes,
    string SourceId);

public sealed record RoleAgentAnalysisResult(
    Guid RunId,
    string CapabilityId,
    string Status,
    string Summary,
    decimal Confidence,
    DateTime AsOfUtc,
    IReadOnlyList<RoleAgentMetric> Metrics,
    IReadOnlyList<RoleAgentPriority> Priorities,
    IReadOnlyList<AgentAiClaim> Claims,
    IReadOnlyList<AgentAiSource> Sources,
    IReadOnlyList<string> MissingEvidence,
    IReadOnlyList<AgentAiNextAction> NextActions,
    bool RequiresReview,
    string? FailureCode = null);
