namespace VirtualCompany.Application.Finance;

public static class CustomerInvoiceCorrectionReasonCodes
{
    public const string Ready = "customer_invoice_correction_ready";
    public const string InvoiceNotFound = "customer_invoice_correction_invoice_not_found";
    public const string NativeAuthorityRequired = "customer_invoice_correction_native_authority_required";
    public const string ProviderActionRequired = "customer_invoice_correction_provider_action_required";
    public const string OriginalNotPosted = "customer_invoice_correction_original_not_posted";
    public const string AlreadyCancelled = "customer_invoice_correction_already_cancelled";
    public const string CancellationNotAllowed = "customer_invoice_correction_cancellation_not_allowed";
    public const string AmountExceedsBalance = "customer_invoice_correction_amount_exceeds_balance";
    public const string RefundExceedsPaid = "customer_invoice_correction_refund_exceeds_paid";
    public const string WriteOffExceedsOutstanding = "customer_invoice_correction_write_off_exceeds_outstanding";
    public const string SmallBalanceThresholdExceeded = "customer_invoice_correction_small_balance_threshold_exceeded";
    public const string RecoveryExceedsBadDebt = "customer_invoice_correction_recovery_exceeds_bad_debt";
    public const string EvidenceRequired = "customer_invoice_correction_evidence_required";
    public const string PaymentEvidenceRequired = "customer_invoice_correction_payment_evidence_required";
    public const string CreditDraftRequired = "customer_invoice_correction_credit_draft_required";
    public const string ApprovalRequired = "customer_invoice_correction_approval_required";
    public const string ApprovalPending = "customer_invoice_correction_approval_pending";
    public const string ApprovalStale = "customer_invoice_correction_approval_stale";
    public const string SourceChanged = "customer_invoice_correction_source_changed";
    public const string PeriodUnavailable = "customer_invoice_correction_period_unavailable";
    public const string AccountUnavailable = "customer_invoice_correction_account_unavailable";
    public const string VatCorrectionRequired = "customer_invoice_correction_vat_return_required";
    public const string IdempotencyConflict = "customer_invoice_correction_idempotency_conflict";
    public const string VersionConflict = "customer_invoice_correction_version_conflict";
    public const string AlreadyExecuted = "customer_invoice_correction_already_executed";
    public const string RefundReconciliationRequired = "customer_invoice_refund_reconciliation_required";
}

public sealed record CustomerInvoiceCorrectionEvidenceDto(string Key, string Value);

public sealed record EvaluateCustomerInvoiceCorrectionQuery(Guid CompanyId, Guid InvoiceId,
    string CorrectionType, decimal Amount, string Currency, string? ProviderKey = null,
    Guid? ExistingCorrectionId = null);

public sealed record CustomerInvoiceCorrectionPolicyDecisionDto(bool IsAllowed, string ReasonCode,
    string Explanation, bool RequiresApproval, decimal InvoiceAmount, decimal AllocatedPaidAmount,
    decimal PriorCreditAmount, decimal PriorRefundAmount, decimal PriorWriteOffAmount,
    decimal RemainingEconomicBalance, decimal MaximumAllowedAmount, string Currency,
    string SourceVersion, string SourceHash, bool RequiresCurrentPeriodPosting,
    bool RequiresVatCorrectionReturn, Guid? OriginalVatReturnId,
    IReadOnlyList<CustomerInvoiceCorrectionEvidenceDto> Evidence);

public sealed record ProposeCustomerInvoiceCorrectionCommand(Guid CompanyId, Guid InvoiceId,
    string CorrectionType, decimal Amount, string Currency, string Reason, string EvidenceReference,
    string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null,
    string? BeneficiaryReference = null, string? PaymentEvidenceReference = null,
    string? ProviderKey = null, CustomerInvoiceDraftInput? CreditDraft = null);

public sealed record ExecuteCustomerInvoiceCorrectionCommand(Guid CompanyId, Guid CorrectionId,
    long ExpectedVersion, string ExpectedSourceHash, string IdempotencyKey, Guid ActorUserId,
    Guid? SeriesId = null, Guid? FiscalPeriodId = null, DateOnly? AccountingDate = null,
    string? VoucherSeriesCode = null, Guid? ExpenseAccountId = null, string? CorrelationId = null);

public sealed record ReconcileCustomerInvoiceRefundCommand(Guid CompanyId, Guid CorrectionId,
    long ExpectedVersion, bool ProviderConfirmedSucceeded, bool ProviderConfirmedAbsent,
    string EvidenceReference, string? ProviderReference, Guid ActorUserId, string? CorrelationId = null);

public sealed record CustomerInvoiceRefundExecutionDto(Guid Id, string? ProviderKey, string Status,
    int AttemptCount, DateTime AvailableUtc, string? ProviderReference, string? FailureCategory,
    string? SafeFailureSummary, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? CompletedUtc);

public sealed record CustomerInvoiceCorrectionDto(Guid Id, Guid CompanyId, Guid InvoiceId,
    string InvoiceNumber, string CorrectionType, decimal Amount, string Currency, string Reason,
    string Status, long Version, string SourceVersion, string SourceHash, string EvidenceReference,
    Guid? ApprovalRequestId, string? ApprovalStatus, Guid? TaskId, Guid? CreditDraftId,
    Guid? CorrectingInvoiceId, Guid? LedgerEntryId, Guid? OriginalVatReturnId,
    Guid? CorrectionVatReturnId, Guid? ExpenseAccountId, string? ProviderKey,
    string? BeneficiaryReference, string? PaymentEvidenceReference, Guid CreatedByUserId,
    Guid? ExecutedByUserId, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? ExecutedUtc,
    string? FailureReasonCode, string? FailureSummary, CustomerInvoiceRefundExecutionDto? RefundExecution,
    IReadOnlyList<string> AllowedActions, bool IsIdempotentReplay = false);

public sealed record ListCustomerInvoiceCorrectionsQuery(Guid CompanyId, Guid? InvoiceId = null,
    string? Status = null, int Skip = 0, int Take = 100);
public sealed record CustomerInvoiceCorrectionListResult(IReadOnlyList<CustomerInvoiceCorrectionDto> Items,
    int TotalCount, int Skip, int Take);

public interface ICustomerInvoiceCorrectionPolicy
{
    Task<CustomerInvoiceCorrectionPolicyDecisionDto> EvaluateAsync(
        EvaluateCustomerInvoiceCorrectionQuery query, CancellationToken cancellationToken);
}

public interface ICustomerInvoiceCorrectionService
{
    Task<CustomerInvoiceCorrectionPolicyDecisionDto> EvaluateAsync(EvaluateCustomerInvoiceCorrectionQuery query,
        CancellationToken cancellationToken);
    Task<CustomerInvoiceCorrectionDto> ProposeAsync(ProposeCustomerInvoiceCorrectionCommand command,
        CancellationToken cancellationToken);
    Task<CustomerInvoiceCorrectionDto> ExecuteAsync(ExecuteCustomerInvoiceCorrectionCommand command,
        CancellationToken cancellationToken);
    Task<CustomerInvoiceCorrectionDto> ReconcileRefundAsync(ReconcileCustomerInvoiceRefundCommand command,
        CancellationToken cancellationToken);
    Task<CustomerInvoiceCorrectionDto> GetAsync(Guid companyId, Guid correctionId,
        CancellationToken cancellationToken);
    Task<CustomerInvoiceCorrectionListResult> ListAsync(ListCustomerInvoiceCorrectionsQuery query,
        CancellationToken cancellationToken);
}

public sealed record CustomerRefundExecutionRequest(Guid CompanyId, Guid CorrectionId, Guid InvoiceId,
    decimal Amount, string Currency, string BeneficiaryReference, string PaymentEvidenceReference,
    string IdempotencyKey, string? CorrelationId);

public enum CustomerRefundProviderOutcome { Succeeded, RetryableFailure, PermanentFailure, Ambiguous }

public sealed record CustomerRefundExecutionResult(CustomerRefundProviderOutcome Outcome,
    string? ProviderReference, string SafeSummary);

public interface ICustomerRefundExecutionProvider
{
    string ProviderKey { get; }
    Task<CustomerRefundExecutionResult> ExecuteAsync(CustomerRefundExecutionRequest request,
        CancellationToken cancellationToken);
}

public interface ICustomerInvoiceRefundExecutionRunner
{
    Task<int> RunBatchAsync(CancellationToken cancellationToken);
}

public sealed class CustomerInvoiceCorrectionException : Exception
{
    public CustomerInvoiceCorrectionException(string reasonCode, string message, bool isConflict = false,
        long? currentVersion = null) : base(message)
    {
        ReasonCode = reasonCode; IsConflict = isConflict; CurrentVersion = currentVersion;
    }
    public string ReasonCode { get; }
    public bool IsConflict { get; }
    public long? CurrentVersion { get; }
}
