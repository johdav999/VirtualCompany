using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

namespace VirtualCompany.Application.Orchestration;

public sealed record StartMultiAgentCollaborationCommand(
    Guid CompanyId = default,
    string Objective = "",
    Guid CoordinatorAgentId = default,
    IReadOnlyList<WorkerSubtaskRequest>? Workers = null,
    Guid? InitiatingActorId = null,
    string? InitiatingActorType = null,
    Guid? WorkflowInstanceId = null,
    string? CorrelationId = null,
    CollaborationLimitRequest? Limits = null,
    Dictionary<string, JsonNode?>? InputPayload = null);

public sealed record WorkerSubtaskRequest(
    Guid AgentId = default,
    string Objective = "",
    string? Instructions = null);

public sealed record CollaborationLimitRequest(
    int? MaxWorkers = null,
    int? MaxDepth = null,
    int? MaxRuntimeSeconds = null,
    int? MaxTotalSteps = null);

public sealed record CollaborationPlanDto(
    Guid PlanId,
    Guid CompanyId,
    Guid ParentTaskId,
    Guid CoordinatorAgentId,
    string Objective,
    CollaborationLimitDto Limits,
    IReadOnlyList<CollaborationStepDto> Steps,
    string Status,
    string CorrelationId);

public sealed record CollaborationLimitDto(
    int MaxWorkers,
    int MaxDepth,
    int MaxRuntimeSeconds,
    int MaxTotalSteps);

public sealed record CollaborationStepDto(
    Guid StepId,
    int Sequence,
    Guid ParentTaskId,
    Guid? SubtaskId,
    Guid AssignedAgentId,
    string Objective,
    string? Instructions,
    int DelegationDepth,
    string Status,
    string? RationaleSummary);

public sealed record AgentContributionDto(
    Guid AgentId,
    string AgentName,
    string? AgentRole,
    Guid SubtaskId,
    Guid SourceTaskId,
    int Sequence,
    string Status,
    string Output,
    string? RationaleSummary,
    decimal? ConfidenceScore,
    string CorrelationId);

public sealed record AgentRationaleSummaryDto(
    Guid AgentId,
    string AgentName,
    string? AgentRole,
    Guid SubtaskId,
    Guid SourceTaskId,
    int Sequence,
    string Status,
    string? RationaleSummary,
    decimal? ConfidenceScore,
    string CorrelationId);

public sealed record ConsolidatedMultiAgentResponseDto(
    string FinalResponse,
    IReadOnlyList<AgentContributionDto> Contributions)
{
    public IReadOnlyList<AgentRationaleSummaryDto> ContributorRationaleSummaries { get; init; } = Array.Empty<AgentRationaleSummaryDto>();
}

public sealed record CollaborationExecutionMetricsDto(
    int PlannedStepCount,
    int ExecutedStepCount,
    int ActualDepthReached,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc);

public sealed record MultiAgentCollaborationResultDto(
    Guid PlanId,
    Guid CompanyId,
    Guid ParentTaskId,
    Guid CoordinatorAgentId,
    string Status,
    string FinalResponse,
    IReadOnlyList<AgentContributionDto> Contributions,
    IReadOnlyList<CollaborationStepDto> Steps,
    IReadOnlyList<Guid> FailedSubtaskIds,
    ConsolidatedMultiAgentResponseDto ConsolidatedResponse,
    string TerminationReason,
    bool IsRetryable,
    CollaborationExecutionMetricsDto Metrics,
    Dictionary<string, JsonNode?> StructuredOutput,
    string CorrelationId)
{
    public IReadOnlyList<AgentRationaleSummaryDto> ContributorRationaleSummaries { get; init; } = Array.Empty<AgentRationaleSummaryDto>();
}

public sealed class MultiAgentCollaborationOptions
{
    public const string SectionName = "Orchestration:MultiAgentCollaboration";

    public int MaxWorkers { get; set; } = 3;
    public int MaxDepth { get; set; } = 1;
    public int MaxRuntimeSeconds { get; set; } = 45;
    public int MaxTotalSteps { get; set; } = 6;
}

public interface IMultiAgentCoordinator
{
    Task<MultiAgentCollaborationResultDto> ExecuteAsync(
        StartMultiAgentCollaborationCommand command,
        CancellationToken cancellationToken);
}

public sealed class MultiAgentCollaborationValidationException : Exception
{
    public MultiAgentCollaborationValidationException(IDictionary<string, string[]> errors)
        : base("Multi-agent collaboration validation failed.")
    {
        Errors = new ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

public static class MultiAgentCollaborationStatusValues
{
    public const string Planned = "planned";
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
    public const string Partial = "partial";
    public const string Failed = "failed";
    public const string Blocked = "blocked";
}

public static class MultiAgentCollaborationTaskTypes
{
    public const string Parent = "manager_worker_collaboration";
    public const string WorkerSubtask = "manager_worker_subtask";
}

public static class MultiAgentCollaborationTerminationReasons
{
    public const string Completed = "completed";
    public const string ExplicitPlanMissing = "explicit_plan_missing";
    public const string PlanInvalid = "plan_invalid";
    public const string FanOutExceeded = "fanout_limit_exceeded";
    public const string DepthLimitExceeded = "depth_limit_exceeded";
    public const string StepLimitExceeded = "step_limit_exceeded";
    public const string RuntimeBudgetExceeded = "runtime_budget_exceeded";
    public const string WorkerExecutionFailed = "worker_execution_failed";
}
