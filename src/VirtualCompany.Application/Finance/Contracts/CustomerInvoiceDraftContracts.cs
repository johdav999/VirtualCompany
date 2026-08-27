using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Application.Finance;

public static class CustomerInvoiceDraftReasonCodes
{
    public const string NotFound = "customer_invoice_draft_not_found";
    public const string NotEditable = "customer_invoice_draft_not_editable";
    public const string VersionConflict = "customer_invoice_draft_version_conflict";
    public const string IdempotencyConflict = "customer_invoice_draft_idempotency_conflict";
    public const string CustomerNotFound = "customer_invoice_draft_customer_not_found";
    public const string CustomerProfileMissing = "customer_invoice_draft_customer_profile_missing";
    public const string CustomerMerged = "customer_invoice_draft_customer_merged";
    public const string CustomerCreditHold = "customer_invoice_draft_customer_credit_hold";
    public const string CustomerCreditLimit = "customer_invoice_draft_credit_limit_exceeded";
    public const string EvidenceNotFound = "customer_invoice_draft_evidence_not_found";
    public const string InvalidEvidence = "customer_invoice_draft_evidence_invalid";
    public const string LinesRequired = "customer_invoice_draft_lines_required";
    public const string InvalidDates = "customer_invoice_draft_dates_invalid";
    public const string UnsupportedCurrency = "customer_invoice_draft_currency_unsupported";
    public const string UnsupportedTax = "customer_invoice_draft_tax_unsupported";
    public const string AccountingConfigurationMissing = "customer_invoice_draft_accounting_configuration_missing";
    public const string StatutoryProfileMissing = "customer_invoice_draft_statutory_profile_missing";
    public const string StatutoryProfileIncomplete = "customer_invoice_draft_statutory_profile_incomplete";
    public const string CalculationBlocked = "customer_invoice_draft_calculation_blocked";
    public const string CalculationStale = "customer_invoice_draft_calculation_stale";
    public const string CustomerConflict = "customer_invoice_draft_customer_conflict";
    public const string ApprovalRequired = "customer_invoice_draft_approval_required";
    public const string ApprovalPending = "customer_invoice_draft_approval_pending";
    public const string ApprovalRejected = "customer_invoice_draft_approval_rejected";
    public const string ApprovalStale = "customer_invoice_draft_approval_stale";
    public const string Ready = "customer_invoice_draft_ready";
    public const string AlreadyIssued = "customer_invoice_draft_already_issued";
    public const string IssueHashConflict = "customer_invoice_draft_issue_hash_conflict";
    public const string SeriesUnavailable = "customer_invoice_draft_series_unavailable";
    public const string AccountingPeriodUnavailable = "customer_invoice_draft_accounting_period_unavailable";
    public const string PostingBlocked = "customer_invoice_draft_posting_blocked";
}

public sealed record CustomerInvoiceDraftTaxEvidenceInput(string Classification, string? SourceReference = null);

public sealed record CustomerInvoiceDraftLineInput(
    int Sequence,
    string Description,
    decimal Quantity,
    string Unit,
    decimal UnitPrice,
    decimal DiscountPercent,
    string TaxRuleKey,
    string TaxClassification,
    IReadOnlyList<CustomerInvoiceDraftTaxEvidenceInput> TaxEvidence,
    IReadOnlyDictionary<string, string>? DimensionFacts = null,
    string? RevenueAccountRoleKey = null,
    string? SourceReference = null,
    string? OrderReference = null);

public sealed record CustomerInvoiceDraftInput(
    Guid CustomerId,
    string DocumentType,
    DateOnly IssueDate,
    DateOnly SupplyDate,
    DateOnly DueDate,
    string Currency,
    string PaymentTermKind,
    int PaymentTermDays,
    string? BuyerReference,
    string? SellerReference,
    string? Notes,
    string DeliveryIntent,
    string SourceKind,
    string? SourceReference,
    IReadOnlyList<CustomerInvoiceDraftLineInput> Lines,
    IReadOnlyList<Guid> EvidenceDocumentIds,
    Guid? OriginalInvoiceId = null);

public sealed record CreateCustomerInvoiceDraftCommand(Guid CompanyId, CustomerInvoiceDraftInput Draft,
    string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record UpdateCustomerInvoiceDraftCommand(Guid CompanyId, Guid DraftId, long ExpectedVersion,
    CustomerInvoiceDraftInput Draft, string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record CopyCustomerInvoiceDraftCommand(Guid CompanyId, Guid DraftId, long ExpectedVersion,
    DateOnly IssueDate, string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record DiscardCustomerInvoiceDraftCommand(Guid CompanyId, Guid DraftId, long ExpectedVersion,
    string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record PreviewCustomerInvoiceDraftQuery(Guid CompanyId, Guid DraftId, long ExpectedVersion);
public sealed record GetCustomerInvoiceDraftReadinessQuery(Guid CompanyId, Guid DraftId, long ExpectedVersion);
public sealed record SubmitCustomerInvoiceDraftForApprovalCommand(Guid CompanyId, Guid DraftId, long ExpectedVersion,
    string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record IssueCustomerInvoiceDraftCommand(Guid CompanyId, Guid DraftId, long ExpectedVersion,
    string ExpectedResultHash, Guid SeriesId, Guid FiscalPeriodId, DateOnly AccountingDate,
    string VoucherSeriesCode, string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record GetCustomerInvoiceDraftQuery(Guid CompanyId, Guid DraftId);
public sealed record ListCustomerInvoiceDraftsQuery(Guid CompanyId, string? Status = null, Guid? CustomerId = null,
    int Skip = 0, int Take = 100);

public sealed record CustomerInvoiceDraftIssue(string ReasonCode, string Explanation, Guid? RelatedEntityId = null);
public sealed record CustomerInvoiceDraftEvidenceDto(Guid DocumentId, string Title, string ContentHash, string OriginalFileName);
public sealed record CustomerInvoiceDraftApprovalDto(Guid Id, string Status, string? DecisionSummary,
    long DraftVersion, string ResultHash, DateTime CreatedUtc, DateTime? DecidedUtc, bool IsCurrent);
public sealed record CustomerInvoiceDraftLineDto(Guid Id, int Sequence, string Description, decimal Quantity,
    string Unit, decimal UnitPrice, decimal DiscountPercent, decimal DiscountAmount, decimal NetAmount,
    string TaxRuleKey, string TaxRuleVersion, string TaxClassification, decimal TaxRate, decimal TaxAmount,
    decimal GrossAmount, string? RevenueAccountRoleKey, string? TaxAccountRoleKey,
    IReadOnlyList<string> VatBoxMappings, IReadOnlyList<CustomerInvoiceDraftTaxEvidenceInput> TaxEvidence,
    IReadOnlyDictionary<string, string> DimensionFacts, string? SourceReference, string? OrderReference);
public sealed record CustomerInvoiceDraftTotalsDto(decimal NetTotal, decimal DiscountTotal, decimal TaxTotal,
    decimal GrossTotal, decimal RoundingAmount, int RoundingPrecision, string RoundingMode);
public sealed record CustomerInvoiceDraftDto(Guid Id, Guid CompanyId, Guid CustomerId, string CustomerName,
    string Status, string DocumentType, DateOnly IssueDate, DateOnly SupplyDate, DateOnly DueDate,
    string Currency, string PaymentTermKind, int PaymentTermDays, string? BuyerReference,
    string? SellerReference, string? Notes, string DeliveryIntent, string SourceKind, string? SourceReference,
    long Version, string InputHash, string ResultHash, string PolicyPackKey, string PolicyPackVersion,
    string PolicyDefinitionHash, Guid CreatedByUserId, Guid UpdatedByUserId, DateTime CreatedUtc,
    DateTime UpdatedUtc, DateTime? DiscardedUtc, CustomerInvoiceDraftTotalsDto Totals,
    IReadOnlyList<CustomerInvoiceDraftLineDto> Lines, IReadOnlyList<CustomerInvoiceDraftEvidenceDto> Evidence,
    IReadOnlyList<CustomerInvoiceDraftIssue> Warnings, IReadOnlyList<CustomerInvoiceDraftIssue> Blockers,
    CustomerInvoiceDraftApprovalDto? Approval, Guid? OriginalInvoiceId = null);
public sealed record CustomerInvoiceDraftPreviewDto(CustomerInvoiceDraftDto Draft, bool IsDeterministicReplay);
public sealed record CustomerInvoiceDraftReadinessDto(bool IsAllowed, string ReasonCode, string Explanation,
    bool RequiresApproval, decimal ApprovalThreshold, string ApprovalCurrency,
    IReadOnlyList<CustomerInvoiceDraftIssue> Blockers, IReadOnlyList<CustomerInvoiceDraftIssue> Warnings,
    IReadOnlyDictionary<string, string> Evidence);
public sealed record CustomerInvoiceDraftListResult(IReadOnlyList<CustomerInvoiceDraftDto> Items,
    int TotalCount, int Skip, int Take);
public sealed record CustomerInvoiceDraftSubmissionResult(CustomerInvoiceDraftDto Draft,
    CustomerInvoiceDraftReadinessDto Readiness, Guid ApprovalRequestId, bool IsIdempotentReplay);
public sealed record CustomerInvoiceDraftIssueResult(Guid InvoiceId, Guid IssuedDocumentId,
    Guid LedgerEntryId, string DocumentNumber, string DeliveryState, string SnapshotHash,
    decimal NetTotal, decimal TaxTotal, decimal GrossTotal, string Currency,
    IReadOnlyList<string> AllowedNextActions, bool IsIdempotentReplay);

public interface ICustomerInvoiceDraftCalculationPolicy
{
    Task<CustomerInvoiceDraftCalculation> CalculateAsync(Guid companyId, CustomerInvoiceDraftInput input,
        CancellationToken cancellationToken);
}

public sealed record CustomerInvoiceDraftCalculatedLine(int Sequence, decimal DiscountAmount, decimal NetAmount,
    string TaxRuleVersion, decimal TaxRate, decimal TaxAmount, decimal GrossAmount, string? TaxAccountRoleKey,
    IReadOnlyList<string> VatBoxMappings);
public sealed record CustomerInvoiceDraftCalculation(string InputHash, string ResultHash, string PolicyPackKey,
    string PolicyPackVersion, string PolicyDefinitionHash, int RoundingPrecision, string RoundingMode,
    decimal NetTotal, decimal DiscountTotal, decimal TaxTotal, decimal GrossTotal, decimal RoundingAmount,
    IReadOnlyList<CustomerInvoiceDraftCalculatedLine> Lines, IReadOnlyList<CustomerInvoiceDraftIssue> Warnings,
    IReadOnlyList<CustomerInvoiceDraftIssue> Blockers);

public interface ICustomerInvoiceDraftReadinessPolicy
{
    Task<CustomerInvoiceDraftReadinessDto> EvaluateAsync(Guid companyId, CustomerInvoiceDraft draft,
        CancellationToken cancellationToken);
}

public interface ICustomerInvoiceDraftService
{
    Task<CustomerInvoiceDraftDto> CreateAsync(CreateCustomerInvoiceDraftCommand command, CancellationToken cancellationToken);
    Task<CustomerInvoiceDraftDto> UpdateAsync(UpdateCustomerInvoiceDraftCommand command, CancellationToken cancellationToken);
    Task<CustomerInvoiceDraftDto> CopyAsync(CopyCustomerInvoiceDraftCommand command, CancellationToken cancellationToken);
    Task<CustomerInvoiceDraftDto> DiscardAsync(DiscardCustomerInvoiceDraftCommand command, CancellationToken cancellationToken);
    Task<CustomerInvoiceDraftPreviewDto> PreviewAsync(PreviewCustomerInvoiceDraftQuery query, CancellationToken cancellationToken);
    Task<CustomerInvoiceDraftReadinessDto> GetReadinessAsync(GetCustomerInvoiceDraftReadinessQuery query, CancellationToken cancellationToken);
    Task<CustomerInvoiceDraftSubmissionResult> SubmitAsync(SubmitCustomerInvoiceDraftForApprovalCommand command, CancellationToken cancellationToken);
    Task<CustomerInvoiceDraftIssueResult> IssueAsync(IssueCustomerInvoiceDraftCommand command, CancellationToken cancellationToken);
    Task<CustomerInvoiceDraftDto> GetAsync(GetCustomerInvoiceDraftQuery query, CancellationToken cancellationToken);
    Task<CustomerInvoiceDraftListResult> ListAsync(ListCustomerInvoiceDraftsQuery query, CancellationToken cancellationToken);
}

public sealed class CustomerInvoiceDraftException : Exception
{
    public CustomerInvoiceDraftException(string reasonCode, string message, bool isConflict = false,
        long? currentVersion = null) : base(message)
    {
        ReasonCode = reasonCode;
        IsConflict = isConflict;
        CurrentVersion = currentVersion;
    }

    public string ReasonCode { get; }
    public bool IsConflict { get; }
    public long? CurrentVersion { get; }
}
