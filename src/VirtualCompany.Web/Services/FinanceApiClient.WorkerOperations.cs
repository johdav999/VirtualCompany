namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<FinanceWorkerOperationsResponse?> GetWorkerOperationsAsync(Guid companyId, string? status = null,
        string? workerKey = null, int skip = 0, int take = 100, CancellationToken cancellationToken = default) =>
        _useOfflineMode ? Task.FromResult<FinanceWorkerOperationsResponse?>(null) :
        GetAsync<FinanceWorkerOperationsResponse>(companyId,
            $"api/companies/{companyId}/finance/worker-operations{BuildQuery(("status", status), ("workerKey", workerKey), ("skip", skip.ToString()), ("take", take.ToString()))}",
            allowNotFound: false, cancellationToken);

    public Task<FinanceWorkerWorkItemResponse> RetryWorkerExecutionAsync(Guid companyId, Guid executionId,
        FinanceWorkerOperatorActionApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<FinanceWorkerOperatorActionApiRequest, FinanceWorkerWorkItemResponse>(companyId,
            HttpMethod.Post, $"api/companies/{companyId}/finance/worker-operations/background-executions/{executionId:D}/retry", request, cancellationToken);
    }

    public Task<FinanceWorkerWorkItemResponse> StopWorkerExecutionAsync(Guid companyId, Guid executionId,
        FinanceWorkerOperatorActionApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<FinanceWorkerOperatorActionApiRequest, FinanceWorkerWorkItemResponse>(companyId,
            HttpMethod.Post, $"api/companies/{companyId}/finance/worker-operations/background-executions/{executionId:D}/stop", request, cancellationToken);
    }

    public Task<FinanceWorkerWorkItemResponse> AcknowledgeWorkerExecutionAsync(Guid companyId, Guid executionId,
        FinanceWorkerOperatorActionApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<FinanceWorkerOperatorActionApiRequest, FinanceWorkerWorkItemResponse>(companyId,
            HttpMethod.Post, $"api/companies/{companyId}/finance/worker-operations/background-executions/{executionId:D}/acknowledge", request, cancellationToken);
    }
}

public sealed record FinanceWorkerOperatorActionApiRequest(long ExpectedVersion, string Reason, string? CorrelationId = null);
public sealed record FinanceWorkerAllowedActionsResponse(bool CanRetry, bool CanStop, bool CanAcknowledge, bool CanReconcile, string Explanation);
public sealed record FinanceWorkerAttemptResponse(Guid Id, int AttemptNumber, string Outcome, string? FailureCategory,
    string? FailureCode, string? SafeSummary, DateTime StartedUtc, DateTime? CompletedUtc, long? DurationMilliseconds);
public sealed record FinanceWorkerWorkItemResponse(Guid Id, Guid CompanyId, string WorkerKey, string WorkerName,
    string WorkReference, string Status, string StatusLabel, int AttemptCount, int MaxAttempts, DateTime CreatedUtc,
    DateTime UpdatedUtc, DateTime? NextRetryUtc, DateTime? LeaseExpiresUtc, string? FailureCategory, string? FailureCode,
    string? SafeFailureSummary, DateTime? AcknowledgedUtc, long Version, FinanceWorkerAllowedActionsResponse AllowedActions,
    IReadOnlyList<FinanceWorkerAttemptResponse> Attempts);
public sealed record FinanceWorkerCatalogItemResponse(string Key, string DisplayName, string Category, string DurableUnit,
    string Trigger, string ClaimAndLease, string BatchBound, string IdempotencyIdentity, string RetryContract,
    string CancellationContract, string ProgressAndTerminalStates, string OperatorAction, string ConfigurationSection,
    bool IsConfigured, bool IsEnabled);
public sealed record FinanceWorkerHealthResponse(Guid CompanyId, string Status, DateTime EvaluatedUtc, long QueuedCount,
    long LeasedCount, long ExpiredLeaseCount, long ExhaustedFailureCount, long PoisonWorkCount,
    long ReconciliationRequiredCount, DateTime? OldestQueuedUtc, IReadOnlyList<string> MissingConfigurationSections,
    IReadOnlyList<string> Issues);
public sealed record FinanceWorkerOperationsResponse(Guid CompanyId, FinanceWorkerHealthResponse Health,
    IReadOnlyList<FinanceWorkerCatalogItemResponse> Workers, IReadOnlyList<FinanceWorkerWorkItemResponse> WorkItems,
    int TotalCount);
