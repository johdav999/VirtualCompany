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
