using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class TriggerExecutionAttempt : ICompanyOwnedEntity
{
    private const int TriggerTypeMaxLength = 64;
    private const int CorrelationIdMaxLength = 128;
    private const int IdempotencyKeyMaxLength = 200;
    private const int DenialReasonMaxLength = 2000;
    private const int FailureDetailsMaxLength = 4000;
    private const int DispatchReferenceTypeMaxLength = 100;
    private const int DispatchReferenceIdMaxLength = 128;

    private TriggerExecutionAttempt()
    {
    }

    public TriggerExecutionAttempt(
        Guid id,
        Guid companyId,
        Guid triggerId,
        string triggerType,
        Guid? agentId,
        DateTime occurrenceUtc,
        string correlationId,
        string idempotencyKey,
        int retryAttempt)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (triggerId == Guid.Empty)
        {
            throw new ArgumentException("TriggerId is required.", nameof(triggerId));
        }

        if (agentId == Guid.Empty)
        {
            throw new ArgumentException("AgentId cannot be empty.", nameof(agentId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        TriggerId = triggerId;
        TriggerType = NormalizeRequired(triggerType, nameof(triggerType), TriggerTypeMaxLength);
        AgentId = agentId;
        OccurrenceUtc = NormalizeUtc(occurrenceUtc, nameof(occurrenceUtc));
        CorrelationId = NormalizeRequired(correlationId, nameof(correlationId), CorrelationIdMaxLength);
        IdempotencyKey = NormalizeRequired(idempotencyKey, nameof(idempotencyKey), IdempotencyKeyMaxLength);
        RetryAttemptCount = Math.Max(1, retryAttempt);
        Status = TriggerExecutionAttemptStatus.Pending;
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid TriggerId { get; private set; }
    public string TriggerType { get; private set; } = null!;
    public Guid? AgentId { get; private set; }
    public DateTime OccurrenceUtc { get; private set; }
    public string CorrelationId { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public TriggerExecutionAttemptStatus Status { get; private set; }
    public string? DenialReason { get; private set; }
    public int RetryAttemptCount { get; private set; }
    public string? FailureDetails { get; private set; }
    public string? DispatchReferenceType { get; private set; }
    public string? DispatchReferenceId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? NextRetryUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public Agent? Agent { get; private set; }

    public bool HasFinalOutcome =>
        Status is TriggerExecutionAttemptStatus.Dispatched
            or TriggerExecutionAttemptStatus.Blocked
            or TriggerExecutionAttemptStatus.DuplicateSkipped
            or TriggerExecutionAttemptStatus.Failed
            or TriggerExecutionAttemptStatus.DeadLettered;

    public void MarkRetried(int retryAttempt)
    {
        RetryAttemptCount = Math.Max(RetryAttemptCount + 1, retryAttempt);
        Status = TriggerExecutionAttemptStatus.Retried;
        UpdatedUtc = DateTime.UtcNow;
        CompletedUtc = null;
        NextRetryUtc = null;
    }

    public void MarkDispatched(string? dispatchReferenceType, string? dispatchReferenceId)
    {
        Status = TriggerExecutionAttemptStatus.Dispatched;
        DispatchReferenceType = NormalizeOptional(dispatchReferenceType, nameof(dispatchReferenceType), DispatchReferenceTypeMaxLength);
        DispatchReferenceId = NormalizeOptional(dispatchReferenceId, nameof(dispatchReferenceId), DispatchReferenceIdMaxLength);
        FailureDetails = null;
        DenialReason = null;
        NextRetryUtc = null;
        CompletedUtc = DateTime.UtcNow;
        UpdatedUtc = CompletedUtc.Value;
    }

    public void MarkBlocked(string denialReason)
    {
        Status = TriggerExecutionAttemptStatus.Blocked;
        DenialReason = NormalizeRequired(denialReason, nameof(denialReason), DenialReasonMaxLength);
        FailureDetails = null;
        NextRetryUtc = null;
        CompletedUtc = DateTime.UtcNow;
        UpdatedUtc = CompletedUtc.Value;
    }

    public void MarkDuplicateSkipped(string? reason = null)
    {
        Status = TriggerExecutionAttemptStatus.DuplicateSkipped;
        DenialReason = NormalizeOptional(reason, nameof(reason), DenialReasonMaxLength);
        NextRetryUtc = null;
        CompletedUtc = DateTime.UtcNow;
        UpdatedUtc = CompletedUtc.Value;
    }

    public void MarkFailed(string failureDetails)
    {
        Status = TriggerExecutionAttemptStatus.Failed;
        FailureDetails = NormalizeRequired(failureDetails, nameof(failureDetails), FailureDetailsMaxLength);
        NextRetryUtc = null;
        CompletedUtc = DateTime.UtcNow;
        UpdatedUtc = CompletedUtc.Value;
    }

    public void MarkRetryScheduled(string failureDetails, DateTime nextRetryUtc)
    {
        Status = TriggerExecutionAttemptStatus.RetryScheduled;
        FailureDetails = NormalizeRequired(failureDetails, nameof(failureDetails), FailureDetailsMaxLength);
        NextRetryUtc = nextRetryUtc.Kind == DateTimeKind.Utc ? nextRetryUtc : nextRetryUtc.ToUniversalTime();
        CompletedUtc = null;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkDeadLettered(string failureDetails)
    {
        Status = TriggerExecutionAttemptStatus.DeadLettered;
        FailureDetails = NormalizeRequired(failureDetails, nameof(failureDetails), FailureDetailsMaxLength);
        NextRetryUtc = null;
        CompletedUtc = DateTime.UtcNow;
        UpdatedUtc = CompletedUtc.Value;
    }

    private static DateTime NormalizeUtc(DateTime value, string name)
    {
        if (value == default)
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        return NormalizeOptional(value, name, maxLength)!;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}
