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

public sealed class PaymentBatchListResponse
{
    public List<PaymentBatchSummaryResponse> Items { get; set; } = [];
    public int DraftCount { get; set; } public int NeedsValidationCount { get; set; }
    public int AwaitingApprovalCount { get; set; } public List<PaymentBatchTotalResponse> PlannedTotals { get; set; } = [];
}
public sealed class PaymentBatchSummaryResponse
{
    public Guid Id { get; set; } public string Reference { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty; public DateOnly PlannedExecutionDate { get; set; }
    public string Status { get; set; } = string.Empty; public long Version { get; set; }
    public int InstructionSetVersion { get; set; } public int ObligationCount { get; set; }
    public List<PaymentBatchTotalResponse> Totals { get; set; } = []; public Guid CreatedByUserId { get; set; }
    public Guid? SubmittedByUserId { get; set; } public Guid? ApprovedByUserId { get; set; }
    public DateTime CreatedUtc { get; set; } public DateTime UpdatedUtc { get; set; }
    public bool IsIdempotentReplay { get; set; }
}
public sealed class PaymentBatchTotalResponse
{ public string Currency { get; set; } = string.Empty; public decimal Amount { get; set; } public decimal? AvailableCash { get; set; } public bool HasSufficientCash { get; set; } }
public sealed class PaymentBatchDetailResponse
{
    public PaymentBatchSummaryResponse Summary { get; set; } = new();
    public List<PaymentBatchObligationResponse> Obligations { get; set; } = [];
    public List<PaymentInstructionResponse> Instructions { get; set; } = [];
    public PaymentBatchValidationResultResponse? Validation { get; set; }
    public PaymentBatchApprovalResponse? Approval { get; set; }
    public PaymentBatchAllowedActionsResponse AllowedActions { get; set; } = new();
    public string InternalApprovalNotice { get; set; } = string.Empty; public string? ExportArtifactHash { get; set; }
}
public sealed class PaymentBatchObligationResponse
{
    public Guid Id { get; set; } public string ObligationType { get; set; } = string.Empty; public Guid SourceId { get; set; }
    public string SourceReference { get; set; } = string.Empty; public string SourceVersion { get; set; } = string.Empty;
    public string SourceHash { get; set; } = string.Empty; public decimal Amount { get; set; } public string Currency { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; } public string PaymentReference { get; set; } = string.Empty; public string BeneficiaryName { get; set; } = string.Empty;
    public string Rail { get; set; } = string.Empty; public string MaskedDestination { get; set; } = string.Empty; public int BeneficiaryVersion { get; set; }
    public string VerificationEvidenceReference { get; set; } = string.Empty; public DateTime VerifiedUtc { get; set; } public DateTime CreatedUtc { get; set; }
}
public sealed class PaymentInstructionResponse
{
    public Guid Id { get; set; } public int InstructionSetVersion { get; set; } public int Sequence { get; set; }
    public DateOnly ExecutionDate { get; set; } public decimal Amount { get; set; } public string Currency { get; set; } = string.Empty;
    public string PaymentReference { get; set; } = string.Empty; public string BeneficiaryName { get; set; } = string.Empty;
    public string Rail { get; set; } = string.Empty; public string MaskedDestination { get; set; } = string.Empty;
    public string SourceVersion { get; set; } = string.Empty; public string ContentHash { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; public bool IsCurrent { get; set; } public DateTime CreatedUtc { get; set; }
}
public sealed class PaymentBatchValidationResultResponse
{
    public Guid Id { get; set; } public long EvaluatedBatchVersion { get; set; } public int InstructionSetVersion { get; set; }
    public bool IsValid { get; set; } public string SourceSetHash { get; set; } = string.Empty;
    public List<PaymentBatchTotalResponse> Totals { get; set; } = []; public List<PaymentBatchValidationIssueResponse> Issues { get; set; } = [];
    public DateTime CreatedUtc { get; set; }
}
public sealed class PaymentBatchValidationIssueResponse
{ public Guid Id { get; set; } public Guid? ObligationLinkId { get; set; } public string Severity { get; set; } = string.Empty; public string ReasonCode { get; set; } = string.Empty; public string Explanation { get; set; } = string.Empty; }
public sealed class PaymentBatchApprovalResponse
{
    public Guid BindingId { get; set; } public Guid ApprovalRequestId { get; set; } public string Status { get; set; } = string.Empty;
    public int InstructionSetVersion { get; set; } public string SourceSetHash { get; set; } = string.Empty; public Guid RequestedByUserId { get; set; }
    public Guid? DecidedByUserId { get; set; } public DateTime CreatedUtc { get; set; } public DateTime? DecidedUtc { get; set; }
}
public sealed class PaymentBatchAllowedActionsResponse
{
    public bool CanAddOrRemove { get; set; } public bool CanValidate { get; set; } public bool CanSubmit { get; set; }
    public bool CanApprove { get; set; } public bool CanReject { get; set; } public bool CanCancel { get; set; }
    public bool CanRegenerate { get; set; } public bool CanCheckSendReadiness { get; set; }
    public string? BlockingReasonCode { get; set; } public string Explanation { get; set; } = string.Empty;
}
public sealed class EligiblePaymentObligationResponse
{
    public string ObligationType { get; set; } = string.Empty; public Guid SourceId { get; set; }
    public string SourceReference { get; set; } = string.Empty; public string BeneficiaryName { get; set; } = string.Empty;
    public string? Rail { get; set; } public string? MaskedDestination { get; set; } public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty; public DateOnly DueDate { get; set; }
    public string PaymentReference { get; set; } = string.Empty; public bool IsEligible { get; set; }
    public string ReasonCode { get; set; } = string.Empty; public string Explanation { get; set; } = string.Empty;
    public DateOnly RecommendedExecutionDate { get; set; }
}
public sealed class PaymentBatchPreviewResponse
{
    public Guid BatchId { get; set; } public long Version { get; set; } public bool CanValidate { get; set; }
    public DateOnly RecommendedExecutionDate { get; set; } public List<PaymentBatchTotalResponse> Totals { get; set; } = [];
    public List<PaymentBatchValidationIssueResponse> Issues { get; set; } = []; public string InternalApprovalNotice { get; set; } = string.Empty;
}
public sealed class PaymentBatchSendReadinessResponse
{
    public Guid BatchId { get; set; } public bool IsReady { get; set; } public string ReasonCode { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty; public int InstructionSetVersion { get; set; }
    public string? ApprovedSourceSetHash { get; set; } public string CurrentSourceSetHash { get; set; } = string.Empty;
    public List<PaymentBatchValidationIssueResponse> Issues { get; set; } = [];
}
public sealed class PaymentBeneficiaryProfileResponse
{
    public Guid Id { get; set; } public string PartyType { get; set; } = string.Empty; public Guid PartyId { get; set; }
    public string DisplayName { get; set; } = string.Empty; public string Rail { get; set; } = string.Empty;
    public string MaskedDestination { get; set; } = string.Empty; public string Currency { get; set; } = string.Empty;
    public int Version { get; set; } public string Status { get; set; } = string.Empty;
    public string VerificationEvidenceReference { get; set; } = string.Empty; public DateTime VerifiedUtc { get; set; }
}

public sealed class RegisterPaymentBeneficiaryApiRequest
{ public string PartyType { get; set; } = string.Empty; public Guid PartyId { get; set; } public string DisplayName { get; set; } = string.Empty; public string Rail { get; set; } = string.Empty; public string Destination { get; set; } = string.Empty; public string MaskedDestination { get; set; } = string.Empty; public string Currency { get; set; } = string.Empty; public string VerificationEvidenceReference { get; set; } = string.Empty; public string VerificationEvidenceHash { get; set; } = string.Empty; }
public sealed class CreatePaymentBatchApiRequest
{ public string Name { get; set; } = string.Empty; public DateOnly PlannedExecutionDate { get; set; } public string IdempotencyKey { get; set; } = string.Empty; }
public sealed class AddPaymentBatchObligationApiRequest
{ public string ObligationType { get; set; } = string.Empty; public Guid SourceId { get; set; } public long ExpectedVersion { get; set; } public string IdempotencyKey { get; set; } = string.Empty; }
public class PaymentBatchVersionedApiRequest
{ public long ExpectedVersion { get; set; } public string IdempotencyKey { get; set; } = string.Empty; }
public sealed class DecidePaymentBatchApiRequest : PaymentBatchVersionedApiRequest
{ public string Comment { get; set; } = string.Empty; }
public sealed class CancelPaymentBatchApiRequest : PaymentBatchVersionedApiRequest
{ public string Reason { get; set; } = string.Empty; }
