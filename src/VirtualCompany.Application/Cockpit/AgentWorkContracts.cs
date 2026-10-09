namespace VirtualCompany.Application.Cockpit;

// PerState applies Skip/Take to each filtered lifecycle lane; the default retains global list paging.
public sealed record AgentWorkQuery(Guid CompanyId, string? Responsibility = null, Guid? AgentId = null,
    string? Objective = null, string? State = null, int Skip = 0, int Take = 24, bool PerState = false);
public sealed record AgentWorkPersonDto(Guid Id, string Name, string Role, string? AvatarUrl = null);
public sealed record AgentWorkLinkDto(string Label, string Route, DateTime? ObservedUtc = null);
public sealed record AgentWorkOutputDto(string Name, string Summary, DateTime ObservedUtc, string? Route = null);
public sealed record AgentWorkItemDto(string Kind, Guid Id, string Title, string Objective, string Responsibility,
    string State, string SourceState, string AccountableHuman, IReadOnlyList<AgentWorkPersonDto> Agents,
    string CurrentStep, string Dependency, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? CompletedUtc,
    string DetailRoute, string? WorkRoute, IReadOnlyList<AgentWorkLinkDto> RelatedRecords,
    IReadOnlyList<AgentWorkOutputDto> Outputs, IReadOnlyList<AgentWorkLinkDto> Evidence,
    IReadOnlyList<string> Diagnostics);
public sealed record AgentWorkBoardDto(Guid CompanyId, string CompanyName, DateTime ObservedUtc,
    IReadOnlyList<AgentWorkItemDto> Items, IReadOnlyDictionary<string, int> StateCounts,
    IReadOnlyList<AgentWorkPersonDto> Agents, IReadOnlyList<string> Responsibilities, int Total,
    int Skip, int Take, bool HasNext, bool IsPartial, IReadOnlyList<string> Diagnostics);
public interface IAgentWorkQueryService
{
    Task<AgentWorkBoardDto> ListAsync(AgentWorkQuery query, CancellationToken cancellationToken);
    Task<AgentWorkItemDto> GetAsync(Guid companyId, string kind, Guid id, CancellationToken cancellationToken);
}

public static class AgentWorkStates
{
    public const string Planned = "planned", Active = "in_progress", AwaitingApproval = "awaiting_approval",
        Completed = "completed", Blocked = "blocked", Failed = "failed", Paused = "paused";
    public static IReadOnlyList<string> All { get; } = [Planned, Active, AwaitingApproval, Completed, Blocked, Failed, Paused];
}

public sealed record BusinessWorkArtifactDto(Guid Id, string Kind, string Name, string Summary,
    string RecordedState, string ApprovalState, string ExecutionState, string ReviewReason,
    AgentWorkPersonDto? Agent, DateTime UpdatedUtc, long? Version, string RecordRoute,
    string? DecisionRoute, IReadOnlyList<string> Diagnostics);
public sealed record BusinessWorkEvidenceDto(Guid CompanyId, string RecordKind, Guid RecordId,
    DateTime ObservedUtc, string WorkflowMeaning, string NextAction,
    IReadOnlyList<BusinessWorkArtifactDto> Artifacts, IReadOnlyList<AgentWorkItemDto> Work,
    IReadOnlyList<VirtualCompany.Application.Orchestration.CollaborationEvidenceDto> Collaboration,
    IReadOnlyList<string> Diagnostics, bool IsPartial, IReadOnlyList<BusinessWorkDecisionDto>? Decisions = null);

public interface IBusinessWorkEvidenceQueryService
{
    Task<BusinessWorkEvidenceDto> GetAsync(Guid companyId, string kind, Guid id, CancellationToken token);
}

public sealed record BusinessWorkDecisionDto(Guid TaskId, Guid ApprovalId, string Status, string Reason,
    string? Limits, string Reviewer, DateTime? ExpiresUtc, bool ProposalChanged, string Route);
