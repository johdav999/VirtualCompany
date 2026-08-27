namespace VirtualCompany.Application.Finance;

public static class CustomerInvoiceTaxMethodValues
{
    public const string Exclusive = "exclusive";
    public const string Inclusive = "inclusive";
    public const string Exempt = "exempt";

    public static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Tax amount method is required.", nameof(value))
            : value.Trim().Replace('-', '_').ToLowerInvariant() switch
            {
                Exclusive => Exclusive,
                Inclusive => Inclusive,
                Exempt => Exempt,
                _ => throw new ArgumentOutOfRangeException(nameof(value), "Tax amount method is not supported.")
            };
}

public static class CustomerInvoiceAccountingReasonCodes
{
    public const string InvoiceNotFound = "customer_invoice_not_found";
    public const string InvoiceNotApproved = "customer_invoice_not_approved";
    public const string ConfigurationUnavailable = "customer_invoice_accounting_configuration_unavailable";
    public const string AuthorityUnavailable = "customer_invoice_accounting_authority_unavailable";
    public const string CounterpartyInvalid = "customer_invoice_counterparty_invalid";
    public const string DuplicateDocumentNumber = "customer_invoice_duplicate_document_number";
    public const string RequiredFieldMissing = "customer_invoice_required_field_missing";
    public const string PeriodUnavailable = "customer_invoice_period_unavailable";
    public const string VoucherSeriesUnavailable = "customer_invoice_voucher_series_unavailable";
    public const string AccountRoleMissing = "customer_invoice_account_role_missing";
    public const string TaxRuleUnsupported = "customer_invoice_tax_rule_unsupported";
    public const string TaxTreatmentUnsupported = "customer_invoice_tax_treatment_unsupported";
    public const string AmountMismatch = "customer_invoice_amount_mismatch";
    public const string CurrencyConversionMissing = "customer_invoice_currency_conversion_missing";
    public const string EvidenceRequired = "customer_invoice_evidence_required";
    public const string ApprovalRequired = "customer_invoice_accounting_approval_required";
    public const string ApprovalPending = "customer_invoice_accounting_approval_pending";
    public const string ApprovalStale = "customer_invoice_accounting_approval_stale";
    public const string AlreadyPosted = "customer_invoice_already_posted";
    public const string VersionConflict = "customer_invoice_accounting_version_conflict";
    public const string CreditNoteInvalid = "customer_credit_note_invalid";
}

public sealed record CustomerInvoiceAccountingLineInput(
    string Description,
    decimal Amount,
    string TaxRuleKey,
    string? LineClassification = null,
    string? CounterpartyJurisdiction = null,
    string? CounterpartyVatStatus = null,
    IReadOnlyList<AccountingTaxEvidenceInput>? TaxEvidence = null);

public sealed record CustomerInvoiceAccountingInput(
    Guid FiscalPeriodId,
    string VoucherSeriesCode,
    decimal? ExchangeRate,
    IReadOnlyList<CustomerInvoiceAccountingLineInput> Lines);

public sealed record CustomerInvoiceAccountingIssueDto(
    string ReasonCode,
    string Explanation,
    bool IsBlocking = true,
    string? PolicyReasonCode = null);

public sealed record CustomerInvoiceAccountingJournalLineDto(
    Guid FinanceAccountId,
    string AccountRole,
    string AccountCode,
    string AccountName,
    decimal DebitAmount,
    decimal CreditAmount,
    string Currency,
    string Description,
    string? TaxRuleKey = null,
    string? TaxRuleVersion = null,
    IReadOnlyList<string>? VatBoxMappings = null,
    string? EvidenceClassification = null);

public sealed record CustomerInvoiceAccountingPreviewDto(
    Guid InvoiceId,
    bool IsReady,
    string AccountingStatus,
    string DocumentKind,
    decimal NetAmount,
    decimal TaxAmount,
    decimal GrossAmount,
    string DocumentCurrency,
    decimal ExchangeRate,
    decimal NetBaseAmount,
    decimal TaxBaseAmount,
    decimal GrossBaseAmount,
    decimal RoundingBaseAmount,
    string BaseCurrency,
    string PolicyPackKey,
    string PolicyPackVersion,
    long SourceVersion,
    string PayloadHash,
    IReadOnlyList<CustomerInvoiceAccountingJournalLineDto> JournalLines,
    IReadOnlyList<CustomerInvoiceAccountingIssueDto> Issues);

public sealed record CustomerInvoiceAccountingApprovalDto(
    Guid Id, string Status, long SourceVersion, string PayloadHash, DateTime CreatedUtc, DateTime? DecidedUtc);

public sealed record CustomerInvoiceAccountingStateDto(
    Guid InvoiceId,
    Guid? ProfileId,
    string Status,
    string StatusLabel,
    bool CanPreview,
    bool CanSubmit,
    bool CanPost,
    bool CanCreateCreditNote,
    long? SourceVersion,
    decimal? NetAmount,
    decimal? TaxAmount,
    decimal? GrossAmount,
    string? DocumentCurrency,
    decimal? ExchangeRate,
    decimal? GrossBaseAmount,
    string? BaseCurrency,
    string? TaxMethod,
    string? PolicyPackKey,
    string? PolicyPackVersion,
    Guid? LedgerEntryId,
    string? VoucherNumber,
    Guid? OriginalInvoiceId,
    string? BlockingReasonCode,
    string? BlockingReason,
    CustomerInvoiceAccountingApprovalDto? Approval,
    IReadOnlyList<CustomerInvoiceAccountingJournalLineDto> JournalLines,
    IReadOnlyList<CustomerInvoiceAccountingIssueDto> Issues);

public sealed record CustomerInvoiceAccountingTaxRuleOptionDto(string Key, string DisplayName, decimal? Rate, string AmountMethod, DateOnly EffectiveFrom);
public sealed record CustomerInvoiceAccountingPeriodOptionDto(Guid Id, string Name, DateOnly StartDate, DateOnly EndDate);
public sealed record CustomerInvoiceAccountingVoucherSeriesOptionDto(string Code, string DisplayName);
public sealed record CustomerInvoiceAccountingReferenceDataDto(
    Guid InvoiceId, string DocumentCurrency, string BaseCurrency, decimal GrossAmount,
    IReadOnlyList<CustomerInvoiceAccountingTaxRuleOptionDto> TaxRules,
    IReadOnlyList<CustomerInvoiceAccountingPeriodOptionDto> OpenPeriods,
    IReadOnlyList<CustomerInvoiceAccountingVoucherSeriesOptionDto> VoucherSeries,
    string? DefaultTaxRuleKey, Guid? DefaultPeriodId, string? DefaultVoucherSeriesCode);

public sealed record CustomerInvoiceAccountingSubmissionResult(CustomerInvoiceAccountingStateDto State, Guid ApprovalRequestId, bool IsIdempotentReplay);
public sealed record CustomerInvoiceAccountingPostingResult(CustomerInvoiceAccountingStateDto State, AccountingJournalDto Journal, bool IsIdempotentReplay);

public sealed record PreviewCustomerInvoiceAccountingQuery(Guid CompanyId, Guid InvoiceId, CustomerInvoiceAccountingInput Input, Guid ActorUserId);
public sealed record SubmitCustomerInvoiceAccountingCommand(Guid CompanyId, Guid InvoiceId, CustomerInvoiceAccountingInput Input,
    long? ExpectedVersion, string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record PostCustomerInvoiceAccountingCommand(Guid CompanyId, Guid InvoiceId, long ExpectedVersion,
    string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record GetCustomerInvoiceAccountingQuery(Guid CompanyId, Guid InvoiceId);
public sealed record CreateCustomerCreditNoteCommand(Guid CompanyId, Guid OriginalInvoiceId, string CreditNoteNumber,
    DateOnly IssueDate, DateOnly DueDate, string Reason, CustomerInvoiceAccountingInput Accounting,
    Guid ActorUserId, string IdempotencyKey, string? CorrelationId = null);

public sealed record CustomerInvoiceReceivableReconciliationDto(
    Guid CompanyId,
    string BaseCurrency,
    decimal PostedDocumentReceivable,
    decimal PostedJournalReceivable,
    decimal AllocatedAmount,
    decimal OutstandingAmount,
    decimal Difference,
    bool IsReconciled,
    DateTime AsOfUtc);

public sealed record GetCustomerInvoiceReceivableReconciliationQuery(Guid CompanyId, DateOnly? ThroughDate = null);

public interface ICustomerInvoiceAccountingPolicy
{
    Task<CustomerInvoiceAccountingPreviewDto> PreviewAsync(PreviewCustomerInvoiceAccountingQuery query, CancellationToken cancellationToken);
}

public interface ICustomerInvoiceAccountingService
{
    Task<CustomerInvoiceAccountingPreviewDto> PreviewAsync(PreviewCustomerInvoiceAccountingQuery query, CancellationToken cancellationToken);
    Task<CustomerInvoiceAccountingReferenceDataDto> GetReferenceDataAsync(GetCustomerInvoiceAccountingQuery query, CancellationToken cancellationToken);
    Task<CustomerInvoiceAccountingSubmissionResult> SubmitAsync(SubmitCustomerInvoiceAccountingCommand command, CancellationToken cancellationToken);
    Task<CustomerInvoiceAccountingPostingResult> PostAsync(PostCustomerInvoiceAccountingCommand command, CancellationToken cancellationToken);
    Task<CustomerInvoiceAccountingStateDto> GetAsync(GetCustomerInvoiceAccountingQuery query, CancellationToken cancellationToken);
    Task<CustomerInvoiceAccountingStateDto> CreateCreditNoteAsync(CreateCustomerCreditNoteCommand command, CancellationToken cancellationToken);
    Task<CustomerInvoiceReceivableReconciliationDto> ReconcileAsync(GetCustomerInvoiceReceivableReconciliationQuery query, CancellationToken cancellationToken);
}

public sealed class CustomerInvoiceAccountingException : Exception
{
    public CustomerInvoiceAccountingException(string reasonCode, string message, bool isConflict = false) : base(message)
    {
        ReasonCode = string.IsNullOrWhiteSpace(reasonCode) ? throw new ArgumentException("ReasonCode is required.", nameof(reasonCode)) : reasonCode.Trim();
        IsConflict = isConflict;
    }

    public string ReasonCode { get; }
    public bool IsConflict { get; }
}
