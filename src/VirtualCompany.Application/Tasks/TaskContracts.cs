using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

namespace VirtualCompany.Application.Tasks;

public sealed record CreateTaskCommand(
    string Type,
    string Title,
    string? Description,
    string? Priority,
    DateTime? DueAt,
    Guid? AssignedAgentId,
    Dictionary<string, JsonNode?>? InputPayload,
    Guid? ParentTaskId = null,
    Guid? WorkflowInstanceId = null,
    Dictionary<string, JsonNode?>? OutputPayload = null,
    string? RationaleSummary = null,
    decimal? ConfidenceScore = null,
    string? CorrelationId = null);

public sealed record CreateSubtaskCommand(
    string Type,
    string Title,
    string? Description,
    string? Priority,
    DateTime? DueAt,
    Guid? AssignedAgentId,
    Dictionary<string, JsonNode?>? InputPayload,
    Guid? WorkflowInstanceId = null,
    Dictionary<string, JsonNode?>? OutputPayload = null,
    string? RationaleSummary = null,
    decimal? ConfidenceScore = null,
    string? CorrelationId = null);

public sealed record UpdateTaskStatusCommand(
    string Status,
    Dictionary<string, JsonNode?>? OutputPayload,
    string? RationaleSummary,
    decimal? ConfidenceScore);

public sealed record ReassignTaskCommand(Guid? AssignedAgentId);

public sealed record TaskCommandResultDto(
    Guid Id,
    Guid CompanyId,
    string Status,
    DateTime UpdatedAt);

public sealed record GetTaskByIdQuery(Guid TaskId);

public sealed record ListTasksQuery(
    string? Status,
    Guid? AssignedAgentId,
    Guid? ParentTaskId,
    DateTime? DueBefore,
    DateTime? DueAfter,
    int? Skip,
    int? Take);

public sealed record TaskListFilterDto(
    string? Status,
    Guid? AssignedAgentId,
    Guid? ParentTaskId,
    DateTime? DueBefore,
    DateTime? DueAfter,
    int? Skip,
    int? Take);

public sealed record TaskAgentSummaryDto(
    Guid Id,
    string DisplayName,
    string Status);

public sealed record TaskParentSummaryDto(
    Guid Id,
    string Title,
    string Status);

public sealed record TaskSubtaskSummaryDto(
    Guid Id,
    Guid CompanyId,
    string Type,
    string Title,
    string Priority,
    string Status,
    DateTime? DueAt,
    Guid? AssignedAgentId,
    Guid? ParentTaskId,
    Guid? WorkflowInstanceId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? CompletedAt,
    TaskAgentSummaryDto? AssignedAgent);

public sealed record TaskDetailDto(
    Guid Id,
    Guid CompanyId,
    string Type,
    string Title,
    string? Description,
    string Priority,
    string Status,
    DateTime? DueAt,
    Guid? AssignedAgentId,
    Guid? ParentTaskId,
    Guid? WorkflowInstanceId,
    string CreatedByActorType,
    string SourceType,
    Guid? OriginatingAgentId,
    string? TriggerSource,
    string? CreationReason,
    string? TriggerEventId,
    Guid? CreatedByActorId,
    Dictionary<string, JsonNode?> InputPayload,
    Dictionary<string, JsonNode?> OutputPayload,
    string? RationaleSummary,
    decimal? ConfidenceScore,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? CompletedAt,
    TaskAgentSummaryDto? AssignedAgent,
    TaskParentSummaryDto? ParentTask,
    string? CorrelationId = null,
    IReadOnlyList<TaskSubtaskSummaryDto>? Subtasks = null);

public sealed record TaskListItemDto(
    Guid Id,
    Guid CompanyId,
    string Type,
    string Title,
    string Priority,
    string Status,
    DateTime? DueAt,
    Guid? AssignedAgentId,
    Guid? ParentTaskId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    TaskAgentSummaryDto? AssignedAgent);

public sealed record TaskListResultDto(
    IReadOnlyList<TaskListItemDto> Items,
    int TotalCount,
    int Skip,
    int Take);

public sealed record ProactiveTaskTrigger(
    Guid CompanyId,
    Guid AgentId,
    string TriggerSource,
    string TriggerEventId,
    string CorrelationId,
    string CreationReason,
    Dictionary<string, JsonNode?>? Payload,
    string? TaskType = null,
    string? TaskTitle = null,
    string? TaskDescription = null,
    string? TaskPriority = null,
    DateTime? DueAt = null,
    Guid? AssignedAgentId = null);

public sealed record MappedTaskCreationRequest(
    Guid CompanyId,
    Guid AgentId,
    string TriggerSource,
    string TriggerEventId,
    string CorrelationId,
    string CreationReason,
    string Type,
    string Title,
    string? Description,
    string Priority,
    string Status,
    DateTime? DueAt,
    Guid? AssignedAgentId,
    Dictionary<string, JsonNode?> InputPayload);

public sealed record CreateAgentInitiatedTaskCommand(ProactiveTaskTrigger Trigger);

public sealed record CreateAgentInitiatedTaskResult(
    Guid TaskId,
    Guid CompanyId,
    bool Created,
    bool Duplicate,
    string Status,
    string CorrelationId);

public sealed class ProactiveTaskCreationOptions
{
    public const string SectionName = "ProactiveTaskCreation";

    public int DeduplicationWindowSeconds { get; set; } = 900;
}

public interface ITriggerToTaskMappingService
{
    MappedTaskCreationRequest Map(ProactiveTaskTrigger trigger);
}

public interface IProactiveTaskDuplicateDetector
{
    Task<WorkTaskDuplicateMatch?> FindDuplicateAsync(
        MappedTaskCreationRequest request,
        TimeSpan deduplicationWindow,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}

public sealed record WorkTaskDuplicateMatch(
    Guid TaskId,
    Guid CompanyId,
    string Status,
    string CorrelationId);

public interface IProactiveTaskCreationService
{
    Task<CreateAgentInitiatedTaskResult> CreateAsync(
        CreateAgentInitiatedTaskCommand command,
        CancellationToken cancellationToken);
}

public interface ICompanyTaskCommandService
{
    Task<TaskCommandResultDto> CreateTaskAsync(Guid companyId, CreateTaskCommand command, CancellationToken cancellationToken);
    Task<TaskCommandResultDto> CreateSubtaskAsync(Guid companyId, Guid parentTaskId, CreateSubtaskCommand command, CancellationToken cancellationToken);
    Task<TaskCommandResultDto> UpdateStatusAsync(Guid companyId, Guid taskId, UpdateTaskStatusCommand command, CancellationToken cancellationToken);
    Task<TaskCommandResultDto> ReassignAsync(Guid companyId, Guid taskId, ReassignTaskCommand command, CancellationToken cancellationToken);
}

public interface ICompanyTaskQueryService
{
    Task<TaskDetailDto> GetByIdAsync(Guid companyId, GetTaskByIdQuery query, CancellationToken cancellationToken);
    Task<TaskListResultDto> ListAsync(Guid companyId, ListTasksQuery query, CancellationToken cancellationToken);
}

public interface ICompanyTaskService
{
    Task<TaskDetailDto> CreateTaskAsync(Guid companyId, CreateTaskCommand command, CancellationToken cancellationToken);
    Task<TaskDetailDto> CreateSubtaskAsync(Guid companyId, Guid parentTaskId, CreateSubtaskCommand command, CancellationToken cancellationToken);
    Task<TaskDetailDto> UpdateStatusAsync(Guid companyId, Guid taskId, UpdateTaskStatusCommand command, CancellationToken cancellationToken);
    Task<TaskDetailDto> ReassignAsync(Guid companyId, Guid taskId, ReassignTaskCommand command, CancellationToken cancellationToken);
    Task<TaskDetailDto> GetByIdAsync(Guid companyId, Guid taskId, CancellationToken cancellationToken);
    Task<TaskListResultDto> ListAsync(Guid companyId, TaskListFilterDto filter, CancellationToken cancellationToken);
}

public sealed class TaskValidationException : Exception
{
    public TaskValidationException(IDictionary<string, string[]> errors)
        : base("Task validation failed.")
    {
        Errors = new ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
