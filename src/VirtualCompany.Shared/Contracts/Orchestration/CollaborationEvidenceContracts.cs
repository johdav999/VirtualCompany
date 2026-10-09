using VirtualCompany.Application.Cockpit;

namespace VirtualCompany.Application.Orchestration;

public sealed record CollaborationHandoffDto(Guid Id, Guid InputId, Guid RecipientId, bool Passed,
    string? Reason, DateTime CreatedUtc);

public sealed record CollaborationEvidenceDto(Guid CompanyId, string Kind, Guid WorkId,
    string Objective, string OutcomeState, string AccountableHuman, string NextDependency,
    DateTime ObservedUtc, IReadOnlyList<CollaborationArtifactDto> Artifacts,
    IReadOnlyList<CollaborationHandoffDto> Handoffs, IReadOnlyList<AgentWorkLinkDto> RelatedRecords,
    bool IsPartial, bool HasRestrictedEvidence, IReadOnlyList<string> Diagnostics);


public sealed record CollaborationArtifactDto(Guid Id, Guid ParentTaskId, Guid SourceTaskId,
    int Sequence, int Version, AgentWorkPersonDto Agent, string Role, string Pattern,
    string Objective, string State, string Output, string? Rationale, string? ReviewOutcome,
    DateTime CreatedUtc, IReadOnlyList<Guid> InputIds, IReadOnlyList<AgentWorkLinkDto> Sources);
