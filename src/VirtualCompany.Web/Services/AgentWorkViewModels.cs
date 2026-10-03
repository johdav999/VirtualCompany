namespace VirtualCompany.Web.Services;

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
public static class AgentWorkStates
{
    public const string Planned = "planned", Active = "in_progress", AwaitingApproval = "awaiting_approval",
        Completed = "completed", Blocked = "blocked", Failed = "failed", Paused = "paused";
    public static IReadOnlyList<string> All { get; } = [Planned, Active, AwaitingApproval, Completed, Blocked, Failed, Paused];
}

