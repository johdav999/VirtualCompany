using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

namespace VirtualCompany.Application.Workflows;

public sealed record CreateWorkflowDefinitionCommand(
    string Code,
    string Name,
    string? Department,
    string TriggerType,
    Dictionary<string, JsonNode?>? DefinitionJson,
    bool Active = true);

public sealed record CreateWorkflowDefinitionVersionCommand(
    string? Name,
    string? Department,
    string TriggerType,
    Dictionary<string, JsonNode?>? DefinitionJson,
    bool Active = true);

public sealed record CreateWorkflowTriggerCommand(
    string EventName,
    Dictionary<string, JsonNode?>? CriteriaJson,
    bool IsEnabled = true);

public sealed record StartWorkflowInstanceCommand(
    Guid DefinitionId,
    Guid? TriggerId,
    Dictionary<string, JsonNode?>? InputPayload,
    string TriggerSource = "manual",
    string? TriggerRef = null);

public sealed record StartManualWorkflowInstanceCommand(
    Guid DefinitionId,
    string? TriggerRef,
    Dictionary<string, JsonNode?>? InputPayload);

public sealed record StartManualWorkflowByCodeCommand(
    string Code,
    string? TriggerRef,
    Dictionary<string, JsonNode?>? InputPayload);

public sealed record UpdateWorkflowInstanceStateCommand(
    string State,
    string? CurrentStep,
    Dictionary<string, JsonNode?>? OutputPayload = null);

public sealed record ReviewWorkflowExceptionCommand(
    string? ResolutionNotes = null);

public sealed record TriggerScheduledWorkflowsCommand(
    DateTime ScheduledAtUtc,
    string? ScheduleKey = null,
    Dictionary<string, JsonNode?>? ContextJson = null);

public sealed record InternalWorkflowEvent(
    Guid CompanyId,
    string EventName,
    string? EventRef,
    Dictionary<string, JsonNode?>? Payload);

public sealed record InternalWorkflowEventTriggerResult(
    string EventName,
    IReadOnlyList<WorkflowInstanceDto> StartedInstances);

public sealed record WorkflowCatalogItemDto(
    string Code,
    string Name,
    string Description,
    string? Department,
    int Version,
    string TriggerType,
    IReadOnlyList<string> SupportedStepHandlers,
    Dictionary<string, JsonNode?> DefinitionJson);

public sealed record WorkflowTriggerDto(
    Guid Id,
    Guid CompanyId,
    Guid DefinitionId,
    string EventName,
    Dictionary<string, JsonNode?> CriteriaJson,
    bool IsEnabled,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record WorkflowDefinitionDto(
    Guid Id,
    Guid? CompanyId,
    string Code,
    string Name,
    string? Department,
    int Version,
    string TriggerType,
    Dictionary<string, JsonNode?> DefinitionJson,
    bool Active,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<WorkflowTriggerDto> Triggers);

public sealed record WorkflowInstanceDto(
    Guid Id,
    Guid CompanyId,
    Guid DefinitionId,
    Guid? TriggerId,
    string TriggerSource,
    string? TriggerRef,
    string Status,
    string State,
    string? CurrentStep,
    Dictionary<string, JsonNode?> InputPayload,
    Dictionary<string, JsonNode?> ContextJson,
    Dictionary<string, JsonNode?> OutputPayload,
    DateTime StartedAt,
    DateTime UpdatedAt,
    DateTime? CompletedAt,
    string DefinitionCode,
    string DefinitionName,
    int DefinitionVersion);

public sealed record WorkflowExceptionDto(
    Guid Id,
    Guid CompanyId,
    Guid WorkflowInstanceId,
    Guid WorkflowDefinitionId,
    string WorkflowDefinitionCode,
    string WorkflowDefinitionName,
    string StepKey,
    string ExceptionType,
    string Status,
    string Title,
    string Details,
    string? ErrorCode,
    Dictionary<string, JsonNode?> TechnicalDetailsJson,
    DateTime OccurredAt,
    DateTime? ReviewedAt,
    Guid? ReviewedByUserId,
    string? ResolutionNotes,
    string InstanceState,
    string? CurrentStep);

public sealed record WorkflowSchedulerRunResult(
    bool LockAcquired,
    int CompaniesScanned,
    int WorkflowsStarted,
    int Failures);

public interface ICompanyWorkflowService
{
    Task<IReadOnlyList<WorkflowCatalogItemDto>> ListCatalogAsync(Guid companyId, CancellationToken cancellationToken);

    Task<WorkflowDefinitionDto> CreateDefinitionAsync(
        Guid companyId,
        CreateWorkflowDefinitionCommand command,
        CancellationToken cancellationToken);

    Task<WorkflowDefinitionDto> CreateDefinitionVersionAsync(
        Guid companyId,
        Guid definitionId,
        CreateWorkflowDefinitionVersionCommand command,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkflowDefinitionDto>> ListDefinitionsAsync(
        Guid companyId,
        bool activeOnly,
        bool latestOnly,
        bool includeSystem,
        CancellationToken cancellationToken);

    Task<WorkflowDefinitionDto> GetDefinitionAsync(Guid companyId, Guid definitionId, CancellationToken cancellationToken);

    Task<WorkflowTriggerDto> CreateTriggerAsync(
        Guid companyId,
        Guid definitionId,
        CreateWorkflowTriggerCommand command,
        CancellationToken cancellationToken);

    Task<WorkflowInstanceDto> StartManualInstanceAsync(
        Guid companyId,
        StartManualWorkflowInstanceCommand command,
        CancellationToken cancellationToken);

    Task<WorkflowInstanceDto> StartManualInstanceByCodeAsync(
        Guid companyId,
        StartManualWorkflowByCodeCommand command,
        CancellationToken cancellationToken);

    Task<WorkflowInstanceDto> StartInstanceAsync(
        Guid companyId,
        StartWorkflowInstanceCommand command,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkflowInstanceDto>> ListInstancesAsync(Guid companyId, Guid? definitionId, CancellationToken cancellationToken);

    Task<WorkflowInstanceDto> GetInstanceAsync(Guid companyId, Guid instanceId, CancellationToken cancellationToken);

    Task<WorkflowInstanceDto> UpdateInstanceStateAsync(
        Guid companyId,
        Guid instanceId,
        UpdateWorkflowInstanceStateCommand command,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkflowExceptionDto>> ListExceptionsAsync(
        Guid companyId,
        string? status,
        Guid? workflowInstanceId,
        CancellationToken cancellationToken);

    Task<WorkflowExceptionDto> GetExceptionAsync(Guid companyId, Guid exceptionId, CancellationToken cancellationToken);

    Task<WorkflowExceptionDto> ReviewExceptionAsync(
        Guid companyId,
        Guid exceptionId,
        ReviewWorkflowExceptionCommand command,
        CancellationToken cancellationToken);
}

public interface IWorkflowScheduleTriggerService
{
    Task<IReadOnlyList<WorkflowInstanceDto>> StartDueScheduledWorkflowsAsync(
        Guid companyId,
        TriggerScheduledWorkflowsCommand command,
        CancellationToken cancellationToken);
}

public interface IWorkflowSchedulePollingService
{
    Task<WorkflowSchedulerRunResult> RunDueSchedulesAsync(
        DateTime scheduledAtUtc,
        int batchSize,
        CancellationToken cancellationToken);
}

public interface IWorkflowSchedulerCoordinator
{
    Task<WorkflowSchedulerRunResult> RunOnceAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IDistributedLockProvider
{
    Task<IDistributedLockHandle?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken cancellationToken);
}

public interface IDistributedLockHandle : IAsyncDisposable
{
    string Key { get; }
}

public interface IInternalWorkflowEventTriggerService
{
    Task<InternalWorkflowEventTriggerResult> HandleAsync(InternalWorkflowEvent workflowEvent, CancellationToken cancellationToken);
}

public sealed class WorkflowValidationException : Exception
{
    public WorkflowValidationException(IDictionary<string, string[]> errors)
        : base("Workflow validation failed.") =>
        Errors = new ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase));

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
