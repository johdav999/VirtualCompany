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

public sealed class PaymentBatchExecutionResponse
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public string BatchReference { get; set; } = string.Empty;
    public int InstructionSetVersion { get; set; }
    public long Version { get; set; }
    public string ProviderKey { get; set; } = string.Empty;
    public string ProviderDisplayName { get; set; } = string.Empty;
    public Guid BankConnectionId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public Guid CompanyBankAccountId { get; set; }
    public string BankAccountName { get; set; } = string.Empty;
    public string MaskedBankAccount { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ProviderPaymentId { get; set; }
    public Uri? AuthorizationUri { get; set; }
    public string? ProviderStatus { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public string BusinessIdempotencyKey { get; set; } = string.Empty;
    public bool UpdatesExpected { get; set; }
    public bool CanCancelAtProvider { get; set; }
    public string? ReasonCode { get; set; }
    public string? SafeSummary { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime? ProviderAcceptedUtc { get; set; }
    public DateTime? ProviderCompletedUtc { get; set; }
    public DateTime? SettledUtc { get; set; }
    public List<PaymentExecutionAttemptResponse> Attempts { get; set; } = [];
    public List<PaymentAcknowledgementResponse> Acknowledgements { get; set; } = [];
    public List<PaymentExecutionInstructionResponse> Instructions { get; set; } = [];
    public List<PaymentRemittanceResponse> Remittances { get; set; } = [];
    public PaymentSettlementResponse? Settlement { get; set; }
    public PaymentExecutionAllowedActionsResponse AllowedActions { get; set; } = new();
    public bool IsIdempotentReplay { get; set; }
}

public sealed class PaymentExecutionAttemptResponse
{
    public Guid Id { get; set; }
    public int AttemptNumber { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string? ProviderRequestId { get; set; }
    public string? ReasonCode { get; set; }
    public string? SafeSummary { get; set; }
    public string RetryClassification { get; set; } = string.Empty;
    public DateTime StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
}

public sealed class PaymentAcknowledgementResponse
{
    public Guid Id { get; set; }
    public string Source { get; set; } = string.Empty;
    public string ProviderStatus { get; set; } = string.Empty;
    public string NormalizedStatus { get; set; } = string.Empty;
    public bool IsFinal { get; set; }
    public bool UpdatesExpected { get; set; }
    public string? ReasonCode { get; set; }
    public string? SafeSummary { get; set; }
    public string EvidenceHash { get; set; } = string.Empty;
    public DateTime AcknowledgedUtc { get; set; }
}

public sealed class PaymentExecutionInstructionResponse
{
    public Guid Id { get; set; }
    public Guid PaymentInstructionId { get; set; }
    public int Sequence { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string BeneficiaryName { get; set; } = string.Empty;
    public string MaskedDestination { get; set; } = string.Empty;
    public string? ProviderTransactionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ReasonCode { get; set; }
    public Guid? PaymentId { get; set; }
    public Guid? PaymentAllocationId { get; set; }
}

public sealed class PaymentRemittanceResponse
{
    public Guid Id { get; set; }
    public Guid PaymentInstructionId { get; set; }
    public string BeneficiaryName { get; set; } = string.Empty;
    public string? RecipientEmail { get; set; }
    public string Status { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string? ProviderReference { get; set; }
    public string? ReasonCode { get; set; }
    public string? SafeSummary { get; set; }
    public int AttemptCount { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? AcceptedUtc { get; set; }
}

public sealed class PaymentSettlementResponse
{
    public Guid Id { get; set; }
    public Guid BankTransactionId { get; set; }
    public string BankReference { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int PaymentCount { get; set; }
    public int AllocationCount { get; set; }
    public List<Guid> LedgerEntryIds { get; set; } = [];
    public DateTime SettledUtc { get; set; }
}

public sealed class PaymentExecutionAllowedActionsResponse
{
    public bool CanOpenBankAuthorization { get; set; }
    public bool CanCancel { get; set; }
    public bool CanRefreshStatus { get; set; }
    public bool CanAttachProviderReference { get; set; }
    public bool CanSettle { get; set; }
    public bool CanRetryRemittance { get; set; }
    public string? BlockingReasonCode { get; set; }
    public string Explanation { get; set; } = string.Empty;
}

public sealed class QueuePaymentExecutionApiRequest
{
    public long ExpectedBatchVersion { get; set; }
    public Guid BankConnectionId { get; set; }
    public Guid CompanyBankAccountId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}
public sealed class CancelPaymentExecutionApiRequest
{ public long ExpectedVersion { get; set; } public string Reason { get; set; } = string.Empty; public string IdempotencyKey { get; set; } = string.Empty; }
public sealed class ReconcilePaymentExecutionApiRequest
{ public long ExpectedVersion { get; set; } public string? ProviderPaymentId { get; set; } public string Reason { get; set; } = string.Empty; public string IdempotencyKey { get; set; } = string.Empty; }
public sealed class SettlePaymentExecutionApiRequest
{ public long ExpectedVersion { get; set; } public Guid BankTransactionId { get; set; } public long ExpectedBankTransactionSourceVersion { get; set; } public string IdempotencyKey { get; set; } = string.Empty; }
public sealed class RetryPaymentRemittanceApiRequest
{ public long ExpectedExecutionVersion { get; set; } public string IdempotencyKey { get; set; } = string.Empty; }
