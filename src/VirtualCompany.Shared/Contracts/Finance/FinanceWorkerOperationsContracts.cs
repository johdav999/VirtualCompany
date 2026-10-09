

namespace VirtualCompany.Application.Finance;


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
