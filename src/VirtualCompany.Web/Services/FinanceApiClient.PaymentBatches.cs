namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<PaymentBatchListResponse?> ListPaymentBatchesAsync(Guid companyId, string? status = null,
        int limit = 100, CancellationToken cancellationToken = default)
    {
        var query = $"?limit={Math.Clamp(limit, 1, 500)}";
        if (!string.IsNullOrWhiteSpace(status)) query += $"&status={Uri.EscapeDataString(status)}";
        return GetAsync<PaymentBatchListResponse>(companyId,
            $"internal/companies/{companyId}/finance/payment-batches{query}", false, cancellationToken);
    }

    public Task<PaymentBatchDetailResponse?> GetPaymentBatchAsync(Guid companyId, Guid batchId,
        CancellationToken cancellationToken = default) => GetAsync<PaymentBatchDetailResponse>(companyId,
            BatchRoute(companyId, batchId), true, cancellationToken);
    public Task<List<EligiblePaymentObligationResponse>?> ListEligiblePaymentObligationsAsync(Guid companyId,
        int limit = 200, CancellationToken cancellationToken = default) => GetAsync<List<EligiblePaymentObligationResponse>>(
            companyId, $"internal/companies/{companyId}/finance/payment-batches/eligible-obligations?limit={Math.Clamp(limit, 1, 500)}", false, cancellationToken);
    public Task<PaymentBatchPreviewResponse?> PreviewPaymentBatchAsync(Guid companyId, Guid batchId,
        CancellationToken cancellationToken = default) => GetAsync<PaymentBatchPreviewResponse>(companyId,
            $"{BatchRoute(companyId, batchId)}/preview", true, cancellationToken);
    public Task<PaymentBatchSendReadinessResponse?> CheckPaymentBatchSendReadinessAsync(Guid companyId,
        Guid batchId, CancellationToken cancellationToken = default) => GetAsync<PaymentBatchSendReadinessResponse>(
            companyId, $"{BatchRoute(companyId, batchId)}/send-readiness", true, cancellationToken);

    public Task<PaymentBeneficiaryProfileResponse> RegisterPaymentBeneficiaryAsync(Guid companyId,
        RegisterPaymentBeneficiaryApiRequest request, CancellationToken cancellationToken = default) =>
        MutatePaymentBatchAsync<RegisterPaymentBeneficiaryApiRequest, PaymentBeneficiaryProfileResponse>(companyId,
            "beneficiaries", request, cancellationToken);
    public Task<PaymentBatchDetailResponse> CreatePaymentBatchAsync(Guid companyId,
        CreatePaymentBatchApiRequest request, CancellationToken cancellationToken = default) =>
        MutatePaymentBatchAsync<CreatePaymentBatchApiRequest, PaymentBatchDetailResponse>(companyId, string.Empty, request, cancellationToken);
    public Task<PaymentBatchDetailResponse> AddPaymentBatchObligationAsync(Guid companyId, Guid batchId,
        AddPaymentBatchObligationApiRequest request, CancellationToken cancellationToken = default) =>
        MutateBatchAsync(companyId, batchId, "obligations", request, cancellationToken);
    public Task<PaymentBatchDetailResponse> RemovePaymentBatchObligationAsync(Guid companyId, Guid batchId,
        Guid obligationLinkId, PaymentBatchVersionedApiRequest request, CancellationToken cancellationToken = default) =>
        MutateBatchAsync(companyId, batchId, $"obligations/{obligationLinkId:D}/remove", request, cancellationToken);
    public Task<PaymentBatchDetailResponse> ValidatePaymentBatchAsync(Guid companyId, Guid batchId,
        PaymentBatchVersionedApiRequest request, CancellationToken cancellationToken = default) =>
        MutateBatchAsync(companyId, batchId, "validate", request, cancellationToken);
    public Task<PaymentBatchDetailResponse> SubmitPaymentBatchAsync(Guid companyId, Guid batchId,
        PaymentBatchVersionedApiRequest request, CancellationToken cancellationToken = default) =>
        MutateBatchAsync(companyId, batchId, "submit", request, cancellationToken);
    public Task<PaymentBatchDetailResponse> ApprovePaymentBatchAsync(Guid companyId, Guid batchId,
        DecidePaymentBatchApiRequest request, CancellationToken cancellationToken = default) =>
        MutateBatchAsync(companyId, batchId, "approve", request, cancellationToken);
    public Task<PaymentBatchDetailResponse> RejectPaymentBatchAsync(Guid companyId, Guid batchId,
        DecidePaymentBatchApiRequest request, CancellationToken cancellationToken = default) =>
        MutateBatchAsync(companyId, batchId, "reject", request, cancellationToken);
    public Task<PaymentBatchDetailResponse> CancelPaymentBatchAsync(Guid companyId, Guid batchId,
        CancelPaymentBatchApiRequest request, CancellationToken cancellationToken = default) =>
        MutateBatchAsync(companyId, batchId, "cancel", request, cancellationToken);
    public Task<PaymentBatchDetailResponse> RegeneratePaymentBatchAsync(Guid companyId, Guid batchId,
        PaymentBatchVersionedApiRequest request, CancellationToken cancellationToken = default) =>
        MutateBatchAsync(companyId, batchId, "regenerate", request, cancellationToken);

    private Task<TResult> MutatePaymentBatchAsync<TRequest, TResult>(Guid companyId, string segment,
        TRequest request, CancellationToken cancellationToken)
    {
        EnsureOnlineMutation(); var suffix = string.IsNullOrEmpty(segment) ? string.Empty : $"/{segment}";
        return SendCompanyScopedAsync<TRequest, TResult>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/payment-batches{suffix}", request, cancellationToken);
    }
    private Task<PaymentBatchDetailResponse> MutateBatchAsync<TRequest>(Guid companyId, Guid batchId,
        string action, TRequest request, CancellationToken cancellationToken)
    {
        EnsureOnlineMutation(); return SendCompanyScopedAsync<TRequest, PaymentBatchDetailResponse>(companyId,
            HttpMethod.Post, $"{BatchRoute(companyId, batchId)}/{action}", request, cancellationToken);
    }
    private static string BatchRoute(Guid companyId, Guid batchId) =>
        $"internal/companies/{companyId}/finance/payment-batches/{batchId:D}";
}
