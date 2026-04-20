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
    public BackgroundExecutionFailureCategory? FailureCategory { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }
    public Guid? EscalationId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;

    public bool IsTerminal => Status is BackgroundExecutionStatus.Succeeded or BackgroundExecutionStatus.Failed or BackgroundExecutionStatus.Escalated or BackgroundExecutionStatus.Blocked;

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
        UpdatedUtc = StartedUtc.Value;
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
        UpdatedUtc = normalizedUtcNow;
    }

    public void RecordHeartbeat(DateTime utcNow)
    {
        HeartbeatUtc = utcNow.Kind == DateTimeKind.Utc ? utcNow : utcNow.ToUniversalTime();
        UpdatedUtc = HeartbeatUtc.Value;
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
        UpdatedUtc = utcNow;
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
        UpdatedUtc = utcNow;
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
        UpdatedUtc = utcNow;
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
        UpdatedUtc = utcNow;
    }

    public void RecoverStale(DateTime nextRetryUtc, string failureMessage)
    {
        ScheduleRetry(
            nextRetryUtc,
            BackgroundExecutionFailureCategory.TransientInfrastructure,
            "stale_execution",
            failureMessage);
    }

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
    public const string FinanceSeed = "finance_seed";
}