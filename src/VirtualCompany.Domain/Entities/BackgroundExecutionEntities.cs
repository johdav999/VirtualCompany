using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class BackgroundExecution : ICompanyOwnedEntity
{
    private const int RelatedEntityTypeMaxLength = 100;
    private const int RelatedEntityIdMaxLength = 128;
    private const int CorrelationIdMaxLength = 128;
    private const int IdempotencyKeyMaxLength = 200;
    private const int FailureCodeMaxLength = 100;
    private const int FailureMessageMaxLength = 4000;
    private const int LeaseOwnerMaxLength = 128;
    private const int OperatorReasonMaxLength = 1000;

    private BackgroundExecution()
    {
    }

    public BackgroundExecution(
        Guid id,
        Guid companyId,
        BackgroundExecutionType executionType,
        string relatedEntityType,
        string relatedEntityId,
        string correlationId,
        string idempotencyKey,
        int maxAttempts)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (maxAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "MaxAttempts must be greater than zero.");
        }

        _ = executionType.ToStorageValue();

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        ExecutionType = executionType;
        RelatedEntityType = NormalizeRequired(relatedEntityType, nameof(relatedEntityType), RelatedEntityTypeMaxLength);
        RelatedEntityId = NormalizeRequired(relatedEntityId, nameof(relatedEntityId), RelatedEntityIdMaxLength);
        CorrelationId = NormalizeRequired(correlationId, nameof(correlationId), CorrelationIdMaxLength);
        IdempotencyKey = NormalizeRequired(idempotencyKey, nameof(idempotencyKey), IdempotencyKeyMaxLength);
        Status = BackgroundExecutionStatus.Pending;
        MaxAttempts = maxAttempts;
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public BackgroundExecutionType ExecutionType { get; private set; }
    public string RelatedEntityType { get; private set; } = null!;
    public string RelatedEntityId { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public BackgroundExecutionStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public int MaxAttempts { get; private set; }
    public DateTime? NextRetryUtc { get; private set; }
    public DateTime? StartedUtc { get; private set; }
    public DateTime? HeartbeatUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTime? LeaseExpiresUtc { get; private set; }
    public BackgroundExecutionFailureCategory? FailureCategory { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }
    public Guid? EscalationId { get; private set; }
    public DateTime? CancelledUtc { get; private set; }
    public Guid? CancelledByUserId { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTime? AcknowledgedUtc { get; private set; }
    public Guid? AcknowledgedByUserId { get; private set; }
    public string? Acknowledgement { get; private set; }
    public long Version { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;

    public bool IsTerminal => Status is BackgroundExecutionStatus.Succeeded or BackgroundExecutionStatus.Failed or BackgroundExecutionStatus.Escalated or BackgroundExecutionStatus.Blocked or BackgroundExecutionStatus.Cancelled;

    public void StartAttempt(string correlationId, int attempt, int maxAttempts)
    {
        if (attempt <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt), "Attempt must be greater than zero.");
        }

        if (maxAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "MaxAttempts must be greater than zero.");
        }

        CorrelationId = NormalizeRequired(correlationId, nameof(correlationId), CorrelationIdMaxLength);
        AttemptCount = attempt;
        MaxAttempts = maxAttempts;
        Status = BackgroundExecutionStatus.InProgress;
        StartedUtc = DateTime.UtcNow;
        HeartbeatUtc = StartedUtc;
        CompletedUtc = null;
        NextRetryUtc = null;
        FailureCategory = null;
        FailureCode = null;
        FailureMessage = null;
        AcknowledgedUtc = null;
        AcknowledgedByUserId = null;
        Acknowledgement = null;
        CancelledUtc = null;
        CancelledByUserId = null;
        CancellationReason = null;
        UpdatedUtc = StartedUtc.Value;
        Version++;
    }

    public void RecordLease(string owner, DateTime expiresUtc, DateTime utcNow)
    {
        LeaseOwner = NormalizeRequired(owner, nameof(owner), LeaseOwnerMaxLength);
        LeaseExpiresUtc = NormalizeUtc(expiresUtc);
        HeartbeatUtc = NormalizeUtc(utcNow);
        UpdatedUtc = HeartbeatUtc.Value;
        Version++;
    }

    public void Queue(DateTime utcNow, string? correlationId = null, bool resetAttempts = false)
    {
        var normalizedUtcNow = utcNow.Kind == DateTimeKind.Utc ? utcNow : utcNow.ToUniversalTime();
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            CorrelationId = NormalizeRequired(correlationId, nameof(correlationId), CorrelationIdMaxLength);
        }

        Status = BackgroundExecutionStatus.Pending;
        if (resetAttempts)
        {
            AttemptCount = 0;
        }

        NextRetryUtc = null;
        StartedUtc = null;
        HeartbeatUtc = null;
        CompletedUtc = null;
        FailureCategory = null;
        FailureCode = null;
        FailureMessage = null;
        EscalationId = null;
        ClearLease();
        CancelledUtc = null;
        CancelledByUserId = null;
        CancellationReason = null;
        AcknowledgedUtc = null;
        AcknowledgedByUserId = null;
        Acknowledgement = null;
        UpdatedUtc = normalizedUtcNow;
        Version++;
    }

    public void RecordHeartbeat(DateTime utcNow)
    {
        HeartbeatUtc = utcNow.Kind == DateTimeKind.Utc ? utcNow : utcNow.ToUniversalTime();
        UpdatedUtc = HeartbeatUtc.Value;
        Version++;
    }

    public void MarkSucceeded()
    {
        var utcNow = DateTime.UtcNow;
        Status = BackgroundExecutionStatus.Succeeded;
        CompletedUtc = utcNow;
        HeartbeatUtc = utcNow;
        NextRetryUtc = null;
        FailureCategory = null;
        FailureCode = null;
        FailureMessage = null;
        ClearLease();
        UpdatedUtc = utcNow;
        Version++;
    }

    public void ScheduleRetry(
        DateTime nextRetryUtc,
        BackgroundExecutionFailureCategory failureCategory,
        string failureCode,
        string failureMessage)
    {
        var utcNow = DateTime.UtcNow;
        Status = BackgroundExecutionStatus.RetryScheduled;
        NextRetryUtc = nextRetryUtc.Kind == DateTimeKind.Utc ? nextRetryUtc : nextRetryUtc.ToUniversalTime();
        HeartbeatUtc = utcNow;
        CompletedUtc = null;
        FailureCategory = failureCategory;
        FailureCode = NormalizeOptional(failureCode, nameof(failureCode), FailureCodeMaxLength);
        FailureMessage = NormalizeRequired(failureMessage, nameof(failureMessage), FailureMessageMaxLength);
        ClearLease();
        UpdatedUtc = utcNow;
        Version++;
    }

    public void MarkFailed(
        BackgroundExecutionFailureCategory failureCategory,
        string failureCode,
        string failureMessage,
        Guid? escalationId = null)
    {
        var utcNow = DateTime.UtcNow;
        Status = escalationId.HasValue ? BackgroundExecutionStatus.Escalated : BackgroundExecutionStatus.Failed;
        CompletedUtc = utcNow;
        HeartbeatUtc = utcNow;
        NextRetryUtc = null;
        FailureCategory = failureCategory;
        FailureCode = NormalizeOptional(failureCode, nameof(failureCode), FailureCodeMaxLength);
        FailureMessage = NormalizeRequired(failureMessage, nameof(failureMessage), FailureMessageMaxLength);
        EscalationId = escalationId;
        ClearLease();
        UpdatedUtc = utcNow;
        Version++;
    }

    public void MarkBlocked(
        BackgroundExecutionFailureCategory failureCategory,
        string failureCode,
        string failureMessage,
        Guid? escalationId = null)
    {
        var utcNow = DateTime.UtcNow;
        Status = BackgroundExecutionStatus.Blocked;
        CompletedUtc = utcNow;
        HeartbeatUtc = utcNow;
        NextRetryUtc = null;
        FailureCategory = failureCategory;
        FailureCode = NormalizeOptional(failureCode, nameof(failureCode), FailureCodeMaxLength);
        FailureMessage = NormalizeRequired(failureMessage, nameof(failureMessage), FailureMessageMaxLength);
        EscalationId = escalationId;
        ClearLease();
        UpdatedUtc = utcNow;
        Version++;
    }

    public void Cancel(Guid actorUserId, string reason, DateTime utcNow)
    {
        if (actorUserId == Guid.Empty) throw new ArgumentException("Actor user id is required.", nameof(actorUserId));
        if (Status is not (BackgroundExecutionStatus.Pending or BackgroundExecutionStatus.RetryScheduled))
        {
            throw new InvalidOperationException("Only queued Finance work can be stopped safely.");
        }

        Status = BackgroundExecutionStatus.Cancelled;
        CancelledUtc = NormalizeUtc(utcNow);
        CancelledByUserId = actorUserId;
        CancellationReason = NormalizeRequired(reason, nameof(reason), OperatorReasonMaxLength);
        CompletedUtc = CancelledUtc;
        NextRetryUtc = null;
        ClearLease();
        UpdatedUtc = CancelledUtc.Value;
        Version++;
    }

    public void Acknowledge(Guid actorUserId, string acknowledgement, DateTime utcNow)
    {
        if (actorUserId == Guid.Empty) throw new ArgumentException("Actor user id is required.", nameof(actorUserId));
        if (Status is not (BackgroundExecutionStatus.Failed or BackgroundExecutionStatus.Blocked or BackgroundExecutionStatus.Escalated))
        {
            throw new InvalidOperationException("Only terminal failed Finance work can be acknowledged.");
        }

        AcknowledgedUtc = NormalizeUtc(utcNow);
        AcknowledgedByUserId = actorUserId;
        Acknowledgement = NormalizeRequired(acknowledgement, nameof(acknowledgement), OperatorReasonMaxLength);
        UpdatedUtc = AcknowledgedUtc.Value;
        Version++;
    }

    public void RecoverStale(DateTime nextRetryUtc, string failureMessage)
    {
        ScheduleRetry(
            nextRetryUtc,
            BackgroundExecutionFailureCategory.TransientInfrastructure,
            "stale_execution",
            failureMessage);
    }

    private void ClearLease()
    {
        LeaseOwner = null;
        LeaseExpiresUtc = null;
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
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

public static class BackgroundExecutionRelatedEntityTypes
{
    public const string WorkflowInstance = "workflow_instance";
    public const string WorkTask = "task";
    public const string OutboxMessage = "outbox_message";
    public const string Schedule = "schedule";
    public const string FiscalPeriod = "fiscal_period";
    public const string FinanceSeed = "finance_seed";
    public const string FinanceInsightSnapshot = "finance_insight_snapshot";
}

public static class BackgroundExecutionAttemptOutcomes
{
    public const string InProgress = "in_progress";
    public const string Succeeded = "succeeded";
    public const string RetryScheduled = "retry_scheduled";
    public const string Failed = "failed";
    public const string Blocked = "blocked";
    public const string LeaseExpired = "lease_expired";
    public const string Cancelled = "cancelled";
}

public sealed class BackgroundExecutionAttempt : ICompanyOwnedEntity
{
    private BackgroundExecutionAttempt() { }

    public BackgroundExecutionAttempt(
        Guid id,
        Guid companyId,
        Guid backgroundExecutionId,
        string workerName,
        int attemptNumber,
        string leaseOwner,
        DateTime leaseExpiresUtc,
        DateTime startedUtc)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (backgroundExecutionId == Guid.Empty) throw new ArgumentException("Background execution id is required.", nameof(backgroundExecutionId));
        if (attemptNumber <= 0) throw new ArgumentOutOfRangeException(nameof(attemptNumber));
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        BackgroundExecutionId = backgroundExecutionId;
        WorkerName = Normalize(workerName, nameof(workerName), 100);
        AttemptNumber = attemptNumber;
        LeaseOwner = Normalize(leaseOwner, nameof(leaseOwner), 128);
        LeaseExpiresUtc = Utc(leaseExpiresUtc);
        Outcome = BackgroundExecutionAttemptOutcomes.InProgress;
        StartedUtc = Utc(startedUtc);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BackgroundExecutionId { get; private set; }
    public string WorkerName { get; private set; } = null!;
    public int AttemptNumber { get; private set; }
    public string LeaseOwner { get; private set; } = null!;
    public DateTime LeaseExpiresUtc { get; private set; }
    public string Outcome { get; private set; } = null!;
    public BackgroundExecutionFailureCategory? FailureCategory { get; private set; }
    public string? FailureCode { get; private set; }
    public string? SafeSummary { get; private set; }
    public DateTime StartedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public long? DurationMilliseconds { get; private set; }
    public BackgroundExecution BackgroundExecution { get; private set; } = null!;
    public Company Company { get; private set; } = null!;

    public void Complete(string outcome, DateTime completedUtc, BackgroundExecutionFailureCategory? failureCategory = null,
        string? failureCode = null, string? safeSummary = null)
    {
        if (CompletedUtc.HasValue) return;
        Outcome = Normalize(outcome, nameof(outcome), 32);
        FailureCategory = failureCategory;
        FailureCode = Optional(failureCode, 100);
        SafeSummary = Optional(safeSummary, 2000);
        CompletedUtc = Utc(completedUtc);
        DurationMilliseconds = Math.Max(0, (long)(CompletedUtc.Value - StartedUtc).TotalMilliseconds);
    }

    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static string Normalize(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        if (normalized.Length > maxLength) throw new ArgumentOutOfRangeException(name);
        return normalized;
    }
    private static string? Optional(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, maxLength)];
}
