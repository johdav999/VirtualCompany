using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Agents;

public sealed record TriggerExecutionWorkItem(
    Guid CompanyId,
    Guid TriggerId,
    string TriggerType,
    Guid? AgentId,
    DateTime OccurrenceUtc,
    string CorrelationId,
    string IdempotencyKey,
    Dictionary<string, JsonNode?> Payload);

public sealed record TriggerExecutionPolicyDecision(
    bool Allowed,
    string? DenialReason = null,
    IReadOnlyDictionary<string, string?>? Metadata = null)
{
    public static TriggerExecutionPolicyDecision Allow(IReadOnlyDictionary<string, string?>? metadata = null) =>
        new(true, null, metadata);

    public static TriggerExecutionPolicyDecision Deny(string denialReason, IReadOnlyDictionary<string, string?>? metadata = null) =>
        new(false, string.IsNullOrWhiteSpace(denialReason) ? "Trigger execution was blocked by policy." : denialReason.Trim(), metadata);
}

public sealed record TriggerExecutionDispatchResult(
    string DispatchReferenceType,
    string DispatchReferenceId,
    IReadOnlyDictionary<string, string?>? Metadata = null);

public sealed record TriggerExecutionRunResult(
    int ScheduledTriggersEvaluated,
    int ConditionChecksEvaluated,
    int Dispatched,
    int Blocked,
    int DuplicateSkipped,
    int Failed,
    int Retried,
    int DeadLettered = 0);

public sealed record TriggerAuditEventContext(
    Guid CompanyId,
    Guid TriggerId,
    string TriggerType,
    Guid? AgentId,
    DateTime OccurrenceUtc,
    string CorrelationId,
    string IdempotencyKey,
    string WorkerName = "trigger_execution_worker");

public sealed record TriggerExecutionAttemptReservation(
    TriggerExecutionAttempt Attempt,
    bool Created,
    bool DuplicateFinalOutcome,
    bool RetryDeferred = false);

public interface ITriggerExecutionAttemptRepository
{
    Task<TriggerExecutionAttemptReservation> ReserveAsync(
        TriggerExecutionWorkItem workItem,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface ITriggerExecutionPolicyChecker
{
    Task<TriggerExecutionPolicyDecision> CheckAsync(
        TriggerExecutionWorkItem workItem,
        CancellationToken cancellationToken);
}

public interface ITriggerOrchestrationDispatcher
{
    Task<TriggerExecutionDispatchResult> DispatchAsync(
        TriggerExecutionWorkItem workItem,
        CancellationToken cancellationToken);
}

public interface ITriggerAuditEventWriter
{
    Task WriteEvaluationStartedAsync(
        TriggerAuditEventContext context,
        CancellationToken cancellationToken);

    Task WriteEvaluationSkippedAsync(
        TriggerAuditEventContext context,
        string reason,
        IReadOnlyDictionary<string, string?>? metadata,
        CancellationToken cancellationToken);

    Task WriteExecutionAttemptCreatedAsync(
        TriggerExecutionAttempt attempt,
        CancellationToken cancellationToken);

    Task WriteRetryDeferredAsync(
        TriggerExecutionAttempt attempt,
        string reason,
        CancellationToken cancellationToken);

    Task WriteDuplicatePreventedAsync(
        TriggerExecutionAttempt attempt,
        CancellationToken cancellationToken);

    Task WritePolicyDeniedAsync(
        TriggerExecutionAttempt attempt,
        IReadOnlyDictionary<string, string?>? metadata,
        CancellationToken cancellationToken);

    Task WriteOrchestrationStartRequestedAsync(
        TriggerExecutionAttempt attempt,
        CancellationToken cancellationToken);

    Task WriteOrchestrationStartedAsync(
        TriggerExecutionAttempt attempt,
        IReadOnlyDictionary<string, string?>? metadata,
        CancellationToken cancellationToken);

    Task WriteRetryScheduledAsync(TriggerExecutionAttempt attempt, CancellationToken cancellationToken);
    Task WriteExecutionFailedAsync(TriggerExecutionAttempt attempt, CancellationToken cancellationToken);
    Task WriteDeadLetteredAsync(TriggerExecutionAttempt attempt, CancellationToken cancellationToken);
}

public interface ITriggerEvaluationWorker
{
    Task<TriggerExecutionRunResult> RunOnceAsync(
        DateTime dueAtUtc,
        int batchSize,
        int maxRetryAttempts,
        CancellationToken cancellationToken,
        int retryBackoffSeconds = 0);
}

public interface ITriggerInitiatedOrchestrationService
{
    Task<TriggerExecutionAttemptStatus> EvaluateAndDispatchAsync(
        TriggerExecutionWorkItem workItem,
        int maxRetryAttempts,
        CancellationToken cancellationToken,
        int retryBackoffSeconds = 0);
}

public interface ITriggerExecutionService : ITriggerInitiatedOrchestrationService
{
    Task<TriggerExecutionAttemptStatus> ProcessScheduledTriggerAsync(
        AgentScheduledTriggerExecutionRequestMessage message,
        int maxRetryAttempts,
        CancellationToken cancellationToken,
        int retryBackoffSeconds = 0);
}

public sealed class TriggerExecutionPermanentException : Exception
{
    public TriggerExecutionPermanentException(string message)
        : base(string.IsNullOrWhiteSpace(message) ? "Trigger execution cannot be retried." : message)
    {
    }
}

public static class TriggerExecutionTypes
{
    public const string AgentScheduled = "agent_scheduled";
    public const string WorkflowCondition = "workflow_condition";
}

public static class TriggerExecutionIdempotency
{
    public static string ForScheduledAgentTrigger(Guid companyId, Guid triggerId, DateTime scheduledAtUtc) =>
        $"trigger-execution:{TriggerExecutionTypes.AgentScheduled}:{companyId:N}:{triggerId:N}:{NormalizeUtc(scheduledAtUtc):yyyyMMddHHmmss}";

    public static string ForWorkflowCondition(Guid companyId, Guid workflowTriggerId, DateTime evaluatedAtUtc) =>
        $"trigger-execution:{TriggerExecutionTypes.WorkflowCondition}:{companyId:N}:{workflowTriggerId:N}:{NormalizeUtc(evaluatedAtUtc):yyyyMMddHHmmss}";

    public static string CorrelationFromIdempotencyKey(string idempotencyKey) =>
        string.IsNullOrWhiteSpace(idempotencyKey) ? Guid.NewGuid().ToString("N") : idempotencyKey.Trim();

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
