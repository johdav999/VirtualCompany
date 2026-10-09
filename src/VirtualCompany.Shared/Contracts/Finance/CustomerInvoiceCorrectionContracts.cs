

namespace VirtualCompany.Application.Finance;


public sealed record CustomerInvoiceRefundExecutionDto(Guid Id, string? ProviderKey, string Status,
    int AttemptCount, DateTime AvailableUtc, string? ProviderReference, string? FailureCategory,
    string? SafeFailureSummary, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? CompletedUtc);


public sealed record CustomerInvoiceCorrectionEvidenceDto(string Key, string Value);


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

public sealed record CustomerInvoiceCorrectionListResult(IReadOnlyList<CustomerInvoiceCorrectionDto> Items,
    int TotalCount, int Skip, int Take);


public sealed record CustomerInvoiceCorrectionPolicyDecisionDto(bool IsAllowed, string ReasonCode,
    string Explanation, bool RequiresApproval, decimal InvoiceAmount, decimal AllocatedPaidAmount,
    decimal PriorCreditAmount, decimal PriorRefundAmount, decimal PriorWriteOffAmount,
    decimal RemainingEconomicBalance, decimal MaximumAllowedAmount, string Currency,
    string SourceVersion, string SourceHash, bool RequiresCurrentPeriodPosting,
    bool RequiresVatCorrectionReturn, Guid? OriginalVatReturnId,
    IReadOnlyList<CustomerInvoiceCorrectionEvidenceDto> Evidence);
