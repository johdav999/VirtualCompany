namespace VirtualCompany.Application.Finance;

public static class SupplierBillTaxTreatmentValues
{
    public const string Recoverable = "recoverable";
    public const string NonRecoverable = "non_recoverable";
    public const string Exempt = "exempt";
}

public static class SupplierBillAccountingReasonCodes
{
    public const string BillNotFound = "supplier_bill_not_found";
    public const string BillNotApproved = "supplier_bill_not_approved";
    public const string ConfigurationUnavailable = "supplier_bill_accounting_configuration_unavailable";
    public const string AuthorityUnavailable = "supplier_bill_accounting_authority_unavailable";
    public const string DuplicateBill = "supplier_bill_duplicate_detected";
    public const string RequiredFieldMissing = "supplier_bill_required_field_missing";
    public const string PeriodUnavailable = "supplier_bill_period_unavailable";
    public const string VoucherSeriesUnavailable = "supplier_bill_voucher_series_unavailable";
    public const string AccountRoleMissing = "supplier_bill_account_role_missing";
    public const string CostAccountMissing = "supplier_bill_cost_account_missing";
    public const string CostAccountInvalid = "supplier_bill_cost_account_invalid";
    public const string TaxRuleUnsupported = "supplier_bill_tax_rule_unsupported";
    public const string TaxTreatmentUnsupported = "supplier_bill_tax_treatment_unsupported";
    public const string AmountMismatch = "supplier_bill_amount_mismatch";
    public const string CurrencyConversionMissing = "supplier_bill_currency_conversion_missing";
    public const string EvidenceRequired = "supplier_bill_evidence_required";
    public const string ApprovalRequired = "supplier_bill_accounting_approval_required";
    public const string ApprovalPending = "supplier_bill_accounting_approval_pending";
    public const string ApprovalStale = "supplier_bill_accounting_approval_stale";
    public const string AlreadyPosted = "supplier_bill_already_posted";
    public const string VersionConflict = "supplier_bill_accounting_version_conflict";
    public const string CreditNoteInvalid = "supplier_credit_note_invalid";
}

public sealed record SupplierBillAccountingLineInput(
    string Description,
    decimal Amount,
    Guid CostAccountId,
    string TaxRuleKey,
    string? LineClassification = null,
    string? CounterpartyJurisdiction = null,
    string? CounterpartyVatStatus = null,
    IReadOnlyList<AccountingTaxEvidenceInput>? TaxEvidence = null);

public sealed record SupplierBillAccountingInput(
    Guid FiscalPeriodId,
    string VoucherSeriesCode,
    decimal? ExchangeRate,
    IReadOnlyList<SupplierBillAccountingLineInput> Lines);

public sealed record SupplierBillAccountingIssueDto(
    string ReasonCode,
    string Explanation,
    bool IsBlocking = true,
    string? PolicyReasonCode = null);

public sealed record SupplierBillDuplicateEvidenceDto(
    Guid MatchedBillId,
    string BillNumber,
    string SupplierName,
    DateOnly BillDate,
    decimal Amount,
    string Currency,
    IReadOnlyList<string> MatchedFields);

public sealed record SupplierBillAccountingJournalLineDto(
    Guid FinanceAccountId,
    string AccountRole,
    string AccountCode,
    string AccountName,
    decimal DebitAmount,
    decimal CreditAmount,
    string Currency,
    string Description,
    string? TaxRuleKey = null,
    string? TaxTreatment = null,
    string? TaxRuleVersion = null,
    IReadOnlyList<string>? VatBoxMappings = null,
    string? EvidenceClassification = null,
    decimal? DocumentDebitAmount = null,
    decimal? DocumentCreditAmount = null,
    string? DocumentCurrency = null);

public sealed record SupplierBillAccountingPreviewDto(
    Guid BillId,
    bool IsReady,
    string AccountingStatus,
    string DocumentKind,
    decimal NetAmount,
    decimal RecoverableTaxAmount,
    decimal NonRecoverableTaxAmount,
    decimal GrossAmount,
    string DocumentCurrency,
    decimal ExchangeRate,
    decimal CostBaseAmount,
    decimal RecoverableTaxBaseAmount,
    decimal GrossBaseAmount,
    decimal RoundingBaseAmount,
    string BaseCurrency,
    string PolicyPackKey,
    string PolicyPackVersion,
    long SourceVersion,
    string PayloadHash,
    string? SourceDocumentHash,
    IReadOnlyList<SupplierBillAccountingJournalLineDto> JournalLines,
    IReadOnlyList<SupplierBillDuplicateEvidenceDto> DuplicateEvidence,
    IReadOnlyList<SupplierBillAccountingIssueDto> Issues,
    DateOnly? ExchangeRateDate = null,
    string? ExchangeRateIdentity = null,
    IReadOnlyList<ExchangeRateLookupLeg>? ExchangeRateLegs = null);

public sealed record SupplierBillAccountingApprovalDto(
    Guid Id, string Status, long SourceVersion, string PayloadHash, DateTime CreatedUtc, DateTime? DecidedUtc);

public sealed record SupplierBillAccountingStateDto(
    Guid BillId,
    Guid? ProfileId,
    string Status,
    string StatusLabel,
    bool CanPreview,
    bool CanSubmit,
    bool CanPost,
    bool CanCreateCreditNote,
    long? SourceVersion,
    decimal? NetAmount,
    decimal? RecoverableTaxAmount,
    decimal? NonRecoverableTaxAmount,
    decimal? GrossAmount,
    string? DocumentCurrency,
    decimal? ExchangeRate,
    decimal? GrossBaseAmount,
    string? BaseCurrency,
    string? TaxTreatment,
    string? PolicyPackKey,
    string? PolicyPackVersion,
    string? SourceDocumentHash,
    Guid? LedgerEntryId,
    string? VoucherNumber,
    Guid? OriginalBillId,
    string? BlockingReasonCode,
    string? BlockingReason,
    SupplierBillAccountingApprovalDto? Approval,
    IReadOnlyList<SupplierBillAccountingJournalLineDto> JournalLines,
    IReadOnlyList<SupplierBillDuplicateEvidenceDto> DuplicateEvidence,
    IReadOnlyList<SupplierBillAccountingIssueDto> Issues,
    DateOnly? ExchangeRateDate = null,
    Guid? ExchangeRateConversionId = null,
    string? ExchangeRateIdentity = null,
    decimal? ConversionRoundingResidual = null,
    string? CurrencyProvenance = null);

public sealed record SupplierBillAccountingTaxRuleOptionDto(
    string Key, string DisplayName, decimal? Rate, string AmountMethod, string TaxTreatment, DateOnly EffectiveFrom);
public sealed record SupplierBillAccountingPeriodOptionDto(Guid Id, string Name, DateOnly StartDate, DateOnly EndDate);
public sealed record SupplierBillAccountingVoucherSeriesOptionDto(string Code, string DisplayName);
public sealed record SupplierBillAccountingAccountOptionDto(Guid Id, string Code, string Name, string AccountClass);
public sealed record SupplierBillAccountingReferenceDataDto(
    Guid BillId,
    string DocumentCurrency,
    string BaseCurrency,
    decimal GrossAmount,
    IReadOnlyList<SupplierBillAccountingTaxRuleOptionDto> TaxRules,
    IReadOnlyList<SupplierBillAccountingPeriodOptionDto> OpenPeriods,
    IReadOnlyList<SupplierBillAccountingVoucherSeriesOptionDto> VoucherSeries,
    IReadOnlyList<SupplierBillAccountingAccountOptionDto> CostAccounts,
    string? DefaultTaxRuleKey,
    Guid? DefaultPeriodId,
    string? DefaultVoucherSeriesCode,
    Guid? SuggestedCostAccountId,
    string? SuggestedCostAccountEvidence);

public sealed record SupplierBillAccountingSubmissionResult(
    SupplierBillAccountingStateDto State, Guid ApprovalRequestId, bool IsIdempotentReplay);
public sealed record SupplierBillAccountingPostingResult(
    SupplierBillAccountingStateDto State, AccountingJournalDto Journal, bool IsIdempotentReplay);

public sealed record PreviewSupplierBillAccountingQuery(
    Guid CompanyId, Guid BillId, SupplierBillAccountingInput Input, Guid ActorUserId);
public sealed record SubmitSupplierBillAccountingCommand(
    Guid CompanyId, Guid BillId, SupplierBillAccountingInput Input, long? ExpectedVersion,
    string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record PostSupplierBillAccountingCommand(
    Guid CompanyId, Guid BillId, long ExpectedVersion, string IdempotencyKey,
    Guid ActorUserId, string? CorrelationId = null);
public sealed record GetSupplierBillAccountingQuery(Guid CompanyId, Guid BillId);
public sealed record CreateNativeSupplierCreditNoteCommand(
    Guid CompanyId, Guid OriginalBillId, string CreditNoteNumber, DateOnly BillDate,
    DateOnly DueDate, string Reason, SupplierBillAccountingInput Accounting,
    Guid ActorUserId, string IdempotencyKey, string? CorrelationId = null);
public sealed record GetSupplierBillPayablesReconciliationQuery(Guid CompanyId, DateOnly? ThroughDate = null);

public sealed record SupplierBillPayablesReconciliationDto(
    Guid CompanyId,
    string BaseCurrency,
    decimal PostedDocumentPayables,
    decimal PostedJournalPayables,
    decimal AllocatedAmount,
    decimal OutstandingAmount,
    decimal Difference,
    bool IsReconciled,
    DateTime AsOfUtc,
    IReadOnlyList<DocumentCurrencyOpenItemControlDto>? DocumentCurrencyBreakdown = null);

public interface ISupplierBillAccountingPolicy
{
    Task<SupplierBillAccountingPreviewDto> PreviewAsync(
        PreviewSupplierBillAccountingQuery query, CancellationToken cancellationToken);
}

public interface ISupplierBillAccountingService
{
    Task<SupplierBillAccountingPreviewDto> PreviewAsync(PreviewSupplierBillAccountingQuery query, CancellationToken cancellationToken);
    Task<SupplierBillAccountingReferenceDataDto> GetReferenceDataAsync(GetSupplierBillAccountingQuery query, CancellationToken cancellationToken);
    Task<SupplierBillAccountingSubmissionResult> SubmitAsync(SubmitSupplierBillAccountingCommand command, CancellationToken cancellationToken);
    Task<SupplierBillAccountingPostingResult> PostAsync(PostSupplierBillAccountingCommand command, CancellationToken cancellationToken);
    Task<SupplierBillAccountingStateDto> GetAsync(GetSupplierBillAccountingQuery query, CancellationToken cancellationToken);
    Task<SupplierBillAccountingStateDto> CreateCreditNoteAsync(CreateNativeSupplierCreditNoteCommand command, CancellationToken cancellationToken);
    Task<SupplierBillPayablesReconciliationDto> ReconcileAsync(GetSupplierBillPayablesReconciliationQuery query, CancellationToken cancellationToken);
}

public sealed class SupplierBillAccountingException : Exception
{
    public SupplierBillAccountingException(string reasonCode, string message, bool isConflict = false) : base(message)
    {
        ReasonCode = string.IsNullOrWhiteSpace(reasonCode)
            ? throw new ArgumentException("ReasonCode is required.", nameof(reasonCode))
            : reasonCode.Trim();
        IsConflict = isConflict;
    }

    public string ReasonCode { get; }
    public bool IsConflict { get; }
}
