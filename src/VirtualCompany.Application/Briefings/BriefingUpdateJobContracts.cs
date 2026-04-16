using System.Text.Json.Nodes;

namespace VirtualCompany.Application.Briefings;

public sealed record EnqueueBriefingUpdateJobCommand(
    Guid CompanyId,
    string TriggerType,
    string? BriefingType,
    string? EventType,
    string CorrelationId,
    string IdempotencyKey,
    Dictionary<string, JsonNode?>? SourceMetadata = null,
    DateTime? NextAttemptAtUtc = null);

public sealed record BriefingUpdateJobEnqueueResult(
    Guid JobId,
    bool Created,
    string Status,
    string IdempotencyKey);

public sealed record BriefingGenerationJobContext(
    Guid JobId,
    Guid CompanyId,
    string TriggerType,
    string? BriefingType,
    string? EventType,
    string CorrelationId,
    string IdempotencyKey,
    Dictionary<string, JsonNode?> SourceMetadata);

public static class BriefingUpdateEventTypes
{
    public const string TaskStatusChanged = "task_status_changed";
    public const string WorkflowStateChanged = "workflow_state_changed";
    public const string ApprovalRequested = "approval_requested";
    public const string ApprovalDecision = "approval_decision";
    public const string ApprovalDecided = ApprovalDecision;
    public const string Escalation = "escalation";
    public const string EscalationRaised = Escalation;
    public const string AgentGeneratedAlert = "agent_generated_alert";
}

public static class BriefingUpdateJobSources
{
    public const string Schedule = "schedule";
    public const string EventDriven = "event_driven";
}

public interface IBriefingGenerationPipeline
{
    Task<CompanyBriefingGenerationResult> GenerateAsync(BriefingGenerationJobContext job, CancellationToken cancellationToken);
}

public interface IBriefingUpdateJobProducer
{
    Task<BriefingUpdateJobEnqueueResult> EnqueueAsync(
        EnqueueBriefingUpdateJobCommand command,
        CancellationToken cancellationToken);

    Task<BriefingUpdateJobEnqueueResult> EnqueueEventDrivenAsync(
        Guid companyId,
        string eventType,
        string correlationId,
        string idempotencyKey,
        Dictionary<string, JsonNode?>? sourceMetadata,
        CancellationToken cancellationToken);

    Task<BriefingUpdateJobEnqueueResult> EnqueueScheduledAsync(
        Guid companyId,
        string triggerType,
        string briefingType,
        string correlationId,
        string idempotencyKey,
        Dictionary<string, JsonNode?>? sourceMetadata,
        CancellationToken cancellationToken);

    Task ScheduleRetryAsync(
        Guid companyId,
        Guid jobId,
        DateTime nextAttemptAtUtc,
        string? errorCode,
        string error,
        string? errorDetails,
        CancellationToken cancellationToken);

    Task RecordFinalFailureAsync(
        Guid companyId,
        Guid jobId,
        string? errorCode,
        string error,
        string? errorDetails,
        CancellationToken cancellationToken);
}