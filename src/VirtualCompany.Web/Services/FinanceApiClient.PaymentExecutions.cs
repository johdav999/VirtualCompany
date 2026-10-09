namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<PaymentBatchExecutionResponse?> GetPaymentExecutionForBatchAsync(Guid companyId,
        Guid batchId, CancellationToken cancellationToken = default) => GetAsync<PaymentBatchExecutionResponse>(
            companyId, $"internal/companies/{companyId}/finance/payment-executions/batch/{batchId:D}",
            true, cancellationToken);

    public Task<PaymentBatchExecutionResponse?> GetPaymentExecutionAsync(Guid companyId,
        Guid executionId, CancellationToken cancellationToken = default) => GetAsync<PaymentBatchExecutionResponse>(
            companyId, $"internal/companies/{companyId}/finance/payment-executions/{executionId:D}",
            true, cancellationToken);

    public Task<PaymentBatchExecutionResponse> QueuePaymentExecutionAsync(Guid companyId, Guid batchId,
        QueuePaymentExecutionApiRequest request, CancellationToken cancellationToken = default) =>
        MutatePaymentExecutionAsync(companyId, $"batch/{batchId:D}/queue", request, cancellationToken);

    public Task<PaymentBatchExecutionResponse> CancelPaymentExecutionAsync(Guid companyId, Guid executionId,
        CancelPaymentExecutionApiRequest request, CancellationToken cancellationToken = default) =>
        MutatePaymentExecutionAsync(companyId, $"{executionId:D}/cancel", request, cancellationToken);

    public Task<PaymentBatchExecutionResponse> ReconcilePaymentExecutionAsync(Guid companyId, Guid executionId,
        ReconcilePaymentExecutionApiRequest request, CancellationToken cancellationToken = default) =>
        MutatePaymentExecutionAsync(companyId, $"{executionId:D}/reconcile", request, cancellationToken);

    public Task<PaymentBatchExecutionResponse> SettlePaymentExecutionAsync(Guid companyId, Guid executionId,
        SettlePaymentExecutionApiRequest request, CancellationToken cancellationToken = default) =>
        MutatePaymentExecutionAsync(companyId, $"{executionId:D}/settle", request, cancellationToken);

    public Task<PaymentBatchExecutionResponse> RetryPaymentRemittanceAsync(Guid companyId, Guid executionId,
        Guid remittanceId, RetryPaymentRemittanceApiRequest request,
        CancellationToken cancellationToken = default) => MutatePaymentExecutionAsync(companyId,
            $"{executionId:D}/remittances/{remittanceId:D}/retry", request, cancellationToken);

    private Task<PaymentBatchExecutionResponse> MutatePaymentExecutionAsync<TRequest>(Guid companyId,
        string segment, TRequest request, CancellationToken cancellationToken)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<TRequest, PaymentBatchExecutionResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/payment-executions/{segment}", request, cancellationToken);
    }
}
