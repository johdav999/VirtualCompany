using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Application.Escalations;

public sealed record EscalationEvaluationInput(
    Guid CompanyId,
    Guid SourceEntityId,
    string SourceEntityType,
    string EventType,
    DateTime EventUtc,
    string? CurrentStatus,
    int LifecycleVersion,
    string? CorrelationId,
    IReadOnlyDictionary<string, JsonNode?> Fields,
    IReadOnlyDictionary<string, JsonNode?>? Payload = null)
{
    public static EscalationEvaluationInput ForTaskEvent(
        Guid companyId,
        Guid taskId,
        string eventType,
        DateTime eventUtc,
        string? currentStatus,
        int lifecycleVersion,
        string? correlationId,
        IReadOnlyDictionary<string, JsonNode?> fields,
        IReadOnlyDictionary<string, JsonNode?>? payload = null) =>
        new(companyId, taskId, EscalationSourceEntityTypes.WorkTask, eventType, eventUtc, currentStatus, lifecycleVersion, correlationId, fields, payload);

    public static EscalationEvaluationInput ForAlertEvent(
        Guid companyId,
        Guid alertId,
        string eventType,
        DateTime eventUtc,
        string? currentStatus,
        int lifecycleVersion,
        string? correlationId,
        IReadOnlyDictionary<string, JsonNode?> fields,
        IReadOnlyDictionary<string, JsonNode?>? payload = null) =>
        new(companyId, alertId, EscalationSourceEntityTypes.Alert, eventType, eventUtc, currentStatus, lifecycleVersion, correlationId, fields, payload);
}

public sealed record EvaluateEscalationPoliciesCommand(
    EscalationEvaluationInput Input,
    IReadOnlyList<EscalationPolicyDefinition> Policies);

public sealed record EscalationPolicyEvaluationResult(
    Guid PolicyId,
    int EscalationLevel,
    bool ConditionsMet,
    bool EscalationCreated,
    bool SkippedDueToIdempotency,
    string Reason,
    string? Diagnostic,
    Guid? EscalationId);

public sealed record EscalationPolicyEvaluationSummary(
    Guid CompanyId,
    Guid SourceEntityId,
    string SourceEntityType,
    string? CorrelationId,
    DateTime EvaluatedUtc,
    IReadOnlyList<EscalationPolicyEvaluationResult> Results);

public sealed record EscalationCreationResult(bool Created, Escalation Escalation, bool AlreadyExecuted);

public interface IEscalationPolicyEvaluationService
{
    Task<EscalationPolicyEvaluationSummary> EvaluateAsync(
        EvaluateEscalationPoliciesCommand command,
        CancellationToken cancellationToken);
}

public interface IEscalationRepository
{
    Task<bool> HasExecutedAsync(Guid companyId, Guid policyId, string sourceEntityType, Guid sourceEntityId, int escalationLevel, int lifecycleVersion, CancellationToken cancellationToken);

    Task<EscalationCreationResult> TryCreateAsync(Escalation escalation, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public static class EscalationSourceEntityTypes
{
    public const string WorkTask = "work_task";
    public const string Alert = "alert";
}
