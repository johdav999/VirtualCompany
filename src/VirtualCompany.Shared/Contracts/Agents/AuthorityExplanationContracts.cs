

namespace VirtualCompany.Application.Agents;

public sealed record AuthorityExplanationDto(Guid CompanyId, string? WorkKind, Guid? WorkId, string TaskType,
    string CompanyIntent, int ConfigurationVersion, IReadOnlyList<AuthorityLimitDto> CompanyLimits,
    AuthorityCheckDto TaskPolicy, IReadOnlyList<AuthorityAgentDto> Agents, IReadOnlyList<AuthorityAgentChoiceDto> AvailableAgents,
    IReadOnlyList<string> Diagnostics, string ProjectionVersion, string ProjectionHash, DateTime EvaluatedUtc);


// Read projections only. These receipts never authorize execution or replace an owning policy.
public sealed record AuthorityLimitDto(string Label, string Value);

public sealed record AuthorityGrantDto(string CapabilityId, string Level, int Version, DateTime EffectiveFromUtc,
    DateTime? ExpiresUtc, IReadOnlyList<AuthorityLimitDto> Limits, AuthorityCheckDto Check);

public sealed record AuthorityCheckDto(string Source, string State, string ReasonCode, string Explanation, bool ReviewRequired);

public sealed record AuthorityAgentChoiceDto(Guid Id, string Name);

public sealed record AuthorityAgentDto(Guid Id, string Name, string ProfileLevel, string AuthorityVersion,
    string AuthorityHash, IReadOnlyList<AuthorityActionDto> Actions, IReadOnlyList<AuthorityGrantDto> Grants);

public sealed record AuthorityActionDto(string ToolName, string ActionType, string Scope, string CapabilityState,
    string IntegrationState, string ActorPermission, AuthorityCheckDto Check);
