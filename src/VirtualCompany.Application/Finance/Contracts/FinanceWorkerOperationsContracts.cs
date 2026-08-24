namespace VirtualCompany.Application.Finance;

public static class FinanceWorkerWorkStatuses
{
    public const string Queued = "queued";
    public const string InProgress = "in_progress";
    public const string RetryScheduled = "retry_scheduled";
    public const string NeedsAttention = "needs_attention";
    public const string NeedsReconciliation = "needs_reconciliation";
    public const string Completed = "completed";
    public const string Stopped = "stopped";
}

public static class FinanceWorkerOperationReasonCodes
{
    public const string WorkNotFound = "finance_worker_work_not_found";
    public const string StaleVersion = "finance_worker_stale_version";
    public const string RetryNotAllowed = "finance_worker_retry_not_allowed";
    public const string StopNotAllowed = "finance_worker_stop_not_allowed";
    public const string AcknowledgeNotAllowed = "finance_worker_acknowledge_not_allowed";
}

public sealed record FinanceWorkerCatalogItemDto(
    string Key,
    string DisplayName,
    string Category,
    string DurableUnit,
    string Trigger,
    string ClaimAndLease,
    string BatchBound,
    string IdempotencyIdentity,
    string RetryContract,
    string CancellationContract,
    string ProgressAndTerminalStates,
    string OperatorAction,
    string ConfigurationSection,
    bool IsConfigured,
    bool IsEnabled);

public sealed record FinanceWorkerAllowedActionsDto(
    bool CanRetry,
    bool CanStop,
    bool CanAcknowledge,
    bool CanReconcile,
    string Explanation);

public sealed record FinanceWorkerAttemptDto(
    Guid Id,
    int AttemptNumber,
    string Outcome,
    string? FailureCategory,
    string? FailureCode,
    string? SafeSummary,
    DateTime StartedUtc,
    DateTime? CompletedUtc,
    long? DurationMilliseconds);

public sealed record FinanceWorkerWorkItemDto(
    Guid Id,
    Guid CompanyId,
    string WorkerKey,
    string WorkerName,
    string WorkReference,
    string Status,
    string StatusLabel,
    int AttemptCount,
    int MaxAttempts,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    DateTime? NextRetryUtc,
    DateTime? LeaseExpiresUtc,
    string? FailureCategory,
    string? FailureCode,
    string? SafeFailureSummary,
    DateTime? AcknowledgedUtc,
    long Version,
    FinanceWorkerAllowedActionsDto AllowedActions,
    IReadOnlyList<FinanceWorkerAttemptDto> Attempts);

public sealed record FinanceWorkerHealthDto(
    Guid CompanyId,
    string Status,
    DateTime EvaluatedUtc,
    long QueuedCount,
    long LeasedCount,
    long ExpiredLeaseCount,
    long ExhaustedFailureCount,
    long PoisonWorkCount,
    long ReconciliationRequiredCount,
    DateTime? OldestQueuedUtc,
    IReadOnlyList<string> MissingConfigurationSections,
    IReadOnlyList<string> Issues);

public sealed record FinanceWorkerOperationsReadModel(
    Guid CompanyId,
    FinanceWorkerHealthDto Health,
    IReadOnlyList<FinanceWorkerCatalogItemDto> Workers,
    IReadOnlyList<FinanceWorkerWorkItemDto> WorkItems,
    int TotalCount);

public sealed record GetFinanceWorkerOperationsQuery(
    Guid CompanyId,
    string? Status = null,
    string? WorkerKey = null,
    int Skip = 0,
    int Take = 100);

public sealed record RetryFinanceWorkerExecutionCommand(
    Guid CompanyId,
    Guid ExecutionId,
    long ExpectedVersion,
    Guid ActorUserId,
    string Reason,
    string? CorrelationId = null);

public sealed record StopFinanceWorkerExecutionCommand(
    Guid CompanyId,
    Guid ExecutionId,
    long ExpectedVersion,
    Guid ActorUserId,
    string Reason,
    string? CorrelationId = null);

public sealed record AcknowledgeFinanceWorkerExecutionCommand(
    Guid CompanyId,
    Guid ExecutionId,
    long ExpectedVersion,
    Guid ActorUserId,
    string Acknowledgement,
    string? CorrelationId = null);

public interface IFinanceWorkerOperationsService
{
    Task<FinanceWorkerOperationsReadModel> GetAsync(GetFinanceWorkerOperationsQuery query, CancellationToken cancellationToken);
    Task<FinanceWorkerWorkItemDto> RetryAsync(RetryFinanceWorkerExecutionCommand command, CancellationToken cancellationToken);
    Task<FinanceWorkerWorkItemDto> StopAsync(StopFinanceWorkerExecutionCommand command, CancellationToken cancellationToken);
    Task<FinanceWorkerWorkItemDto> AcknowledgeAsync(AcknowledgeFinanceWorkerExecutionCommand command, CancellationToken cancellationToken);
}

public sealed class FinanceWorkerOperationException : Exception
{
    public FinanceWorkerOperationException(string reasonCode, string message, bool isConflict = true) : base(message)
    {
        ReasonCode = string.IsNullOrWhiteSpace(reasonCode) ? throw new ArgumentException("Reason code is required.", nameof(reasonCode)) : reasonCode.Trim();
        IsConflict = isConflict;
    }

    public string ReasonCode { get; }
    public bool IsConflict { get; }
}

public sealed class FinanceWorkerAmbiguousOutcomeException : Exception
{
    public FinanceWorkerAmbiguousOutcomeException(string safeMessage) : base(safeMessage) { }
}

public sealed class FinanceWorkerPoisonPayloadException : Exception
{
    public FinanceWorkerPoisonPayloadException(string safeMessage) : base(safeMessage) { }
}

public sealed class FinanceWorkerObjectStorageException : Exception
{
    public FinanceWorkerObjectStorageException(string safeMessage, Exception? innerException = null) : base(safeMessage, innerException) { }
}
