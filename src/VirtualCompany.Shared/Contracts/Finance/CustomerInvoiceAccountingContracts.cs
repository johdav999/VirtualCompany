namespace VirtualCompany.Application.Finance;
public sealed record CustomerInvoiceAccountingReferenceDataDto(Guid InvoiceId, string DocumentCurrency, string BaseCurrency, decimal GrossAmount, IReadOnlyList<CustomerInvoiceAccountingTaxRuleOptionDto> TaxRules, IReadOnlyList<CustomerInvoiceAccountingPeriodOptionDto> OpenPeriods, IReadOnlyList<CustomerInvoiceAccountingVoucherSeriesOptionDto> VoucherSeries, string? DefaultTaxRuleKey, Guid? DefaultPeriodId, string? DefaultVoucherSeriesCode)
{
    public Guid InvoiceId { get; set; } = InvoiceId;
    public string DocumentCurrency { get; set; } = DocumentCurrency;
    public string BaseCurrency { get; set; } = BaseCurrency;
    public decimal GrossAmount { get; set; } = GrossAmount;
    public IReadOnlyList<CustomerInvoiceAccountingTaxRuleOptionDto> TaxRules { get; set; } = TaxRules;
    public IReadOnlyList<CustomerInvoiceAccountingPeriodOptionDto> OpenPeriods { get; set; } = OpenPeriods;
    public IReadOnlyList<CustomerInvoiceAccountingVoucherSeriesOptionDto> VoucherSeries { get; set; } = VoucherSeries;
    public string? DefaultTaxRuleKey { get; set; } = DefaultTaxRuleKey;
    public Guid? DefaultPeriodId { get; set; } = DefaultPeriodId;
    public string? DefaultVoucherSeriesCode { get; set; } = DefaultVoucherSeriesCode;

    public CustomerInvoiceAccountingReferenceDataDto() : this(default !, string.Empty, string.Empty, default !, [], [], [], default !, default !, default !)
    {
    }
}

public sealed record CustomerInvoiceAccountingTaxRuleOptionDto(string Key, string DisplayName, decimal? Rate, string AmountMethod, DateOnly EffectiveFrom)
{
    public string Key { get; set; } = Key;
    public string DisplayName { get; set; } = DisplayName;
    public decimal? Rate { get; set; } = Rate;
    public string AmountMethod { get; set; } = AmountMethod;
    public DateOnly EffectiveFrom { get; set; } = EffectiveFrom;

    public CustomerInvoiceAccountingTaxRuleOptionDto() : this(string.Empty, string.Empty, default !, string.Empty, default !)
    {
    }
}

public sealed record CustomerInvoiceAccountingPreviewDto(Guid InvoiceId, bool IsReady, string AccountingStatus, string DocumentKind, decimal NetAmount, decimal TaxAmount, decimal GrossAmount, string DocumentCurrency, decimal ExchangeRate, decimal NetBaseAmount, decimal TaxBaseAmount, decimal GrossBaseAmount, decimal RoundingBaseAmount, string BaseCurrency, string PolicyPackKey, string PolicyPackVersion, long SourceVersion, string PayloadHash, IReadOnlyList<CustomerInvoiceAccountingJournalLineDto> JournalLines, IReadOnlyList<CustomerInvoiceAccountingIssueDto> Issues, DateOnly? ExchangeRateDate = null, string? ExchangeRateIdentity = null, IReadOnlyList<ExchangeRateLookupLeg>? ExchangeRateLegs = null)
{
    public Guid InvoiceId { get; set; } = InvoiceId;
    public bool IsReady { get; set; } = IsReady;
    public string AccountingStatus { get; set; } = AccountingStatus;
    public string DocumentKind { get; set; } = DocumentKind;
    public decimal NetAmount { get; set; } = NetAmount;
    public decimal TaxAmount { get; set; } = TaxAmount;
    public decimal GrossAmount { get; set; } = GrossAmount;
    public string DocumentCurrency { get; set; } = DocumentCurrency;
    public decimal ExchangeRate { get; set; } = ExchangeRate;
    public decimal NetBaseAmount { get; set; } = NetBaseAmount;
    public decimal TaxBaseAmount { get; set; } = TaxBaseAmount;
    public decimal GrossBaseAmount { get; set; } = GrossBaseAmount;
    public decimal RoundingBaseAmount { get; set; } = RoundingBaseAmount;
    public string BaseCurrency { get; set; } = BaseCurrency;
    public string PolicyPackKey { get; set; } = PolicyPackKey;
    public string PolicyPackVersion { get; set; } = PolicyPackVersion;
    public long SourceVersion { get; set; } = SourceVersion;
    public string PayloadHash { get; set; } = PayloadHash;
    public IReadOnlyList<CustomerInvoiceAccountingJournalLineDto> JournalLines { get; set; } = JournalLines;
    public IReadOnlyList<CustomerInvoiceAccountingIssueDto> Issues { get; set; } = Issues;
    public DateOnly? ExchangeRateDate { get; set; } = ExchangeRateDate;
    public string? ExchangeRateIdentity { get; set; } = ExchangeRateIdentity;
    public IReadOnlyList<ExchangeRateLookupLeg>? ExchangeRateLegs { get; set; } = ExchangeRateLegs;

    public CustomerInvoiceAccountingPreviewDto() : this(default !, default !, string.Empty, string.Empty, default !, default !, default !, string.Empty, default !, default !, default !, default !, default !, string.Empty, string.Empty, string.Empty, default !, string.Empty, [], [], default !, default !, [])
    {
    }
}

public sealed record CustomerInvoiceAccountingStateDto(Guid InvoiceId, Guid? ProfileId, string Status, string StatusLabel, bool CanPreview, bool CanSubmit, bool CanPost, bool CanCreateCreditNote, long? SourceVersion, decimal? NetAmount, decimal? TaxAmount, decimal? GrossAmount, string? DocumentCurrency, decimal? ExchangeRate, decimal? GrossBaseAmount, string? BaseCurrency, string? TaxMethod, string? PolicyPackKey, string? PolicyPackVersion, Guid? LedgerEntryId, string? VoucherNumber, Guid? OriginalInvoiceId, string? BlockingReasonCode, string? BlockingReason, CustomerInvoiceAccountingApprovalDto? Approval, IReadOnlyList<CustomerInvoiceAccountingJournalLineDto> JournalLines, IReadOnlyList<CustomerInvoiceAccountingIssueDto> Issues, DateOnly? ExchangeRateDate = null, Guid? ExchangeRateConversionId = null, string? ExchangeRateIdentity = null, decimal? ConversionRoundingResidual = null, string? CurrencyProvenance = null)
{
    public Guid InvoiceId { get; set; } = InvoiceId;
    public Guid? ProfileId { get; set; } = ProfileId;
    public string Status { get; set; } = Status;
    public string StatusLabel { get; set; } = StatusLabel;
    public bool CanPreview { get; set; } = CanPreview;
    public bool CanSubmit { get; set; } = CanSubmit;
    public bool CanPost { get; set; } = CanPost;
    public bool CanCreateCreditNote { get; set; } = CanCreateCreditNote;
    public long? SourceVersion { get; set; } = SourceVersion;
    public decimal? NetAmount { get; set; } = NetAmount;
    public decimal? TaxAmount { get; set; } = TaxAmount;
    public decimal? GrossAmount { get; set; } = GrossAmount;
    public string? DocumentCurrency { get; set; } = DocumentCurrency;
    public decimal? ExchangeRate { get; set; } = ExchangeRate;
    public decimal? GrossBaseAmount { get; set; } = GrossBaseAmount;
    public string? BaseCurrency { get; set; } = BaseCurrency;
    public string? TaxMethod { get; set; } = TaxMethod;
    public string? PolicyPackKey { get; set; } = PolicyPackKey;
    public string? PolicyPackVersion { get; set; } = PolicyPackVersion;
    public Guid? LedgerEntryId { get; set; } = LedgerEntryId;
    public string? VoucherNumber { get; set; } = VoucherNumber;
    public Guid? OriginalInvoiceId { get; set; } = OriginalInvoiceId;
    public string? BlockingReasonCode { get; set; } = BlockingReasonCode;
    public string? BlockingReason { get; set; } = BlockingReason;
    public CustomerInvoiceAccountingApprovalDto? Approval { get; set; } = Approval;
    public IReadOnlyList<CustomerInvoiceAccountingJournalLineDto> JournalLines { get; set; } = JournalLines;
    public IReadOnlyList<CustomerInvoiceAccountingIssueDto> Issues { get; set; } = Issues;
    public DateOnly? ExchangeRateDate { get; set; } = ExchangeRateDate;
    public Guid? ExchangeRateConversionId { get; set; } = ExchangeRateConversionId;
    public string? ExchangeRateIdentity { get; set; } = ExchangeRateIdentity;
    public decimal? ConversionRoundingResidual { get; set; } = ConversionRoundingResidual;
    public string? CurrencyProvenance { get; set; } = CurrencyProvenance;

    public CustomerInvoiceAccountingStateDto() : this(default !, default !, "not_ready", "Not ready", default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, [], [], default !, default !, default !, default !, default !)
    {
    }
}

public sealed record CustomerInvoiceAccountingVoucherSeriesOptionDto(string Code, string DisplayName)
{
    public string Code { get; set; } = Code;
    public string DisplayName { get; set; } = DisplayName;

    public CustomerInvoiceAccountingVoucherSeriesOptionDto() : this(string.Empty, string.Empty)
    {
    }
}

public sealed record CustomerInvoiceAccountingPeriodOptionDto(Guid Id, string Name, DateOnly StartDate, DateOnly EndDate)
{
    public Guid Id { get; set; } = Id;
    public string Name { get; set; } = Name;
    public DateOnly StartDate { get; set; } = StartDate;
    public DateOnly EndDate { get; set; } = EndDate;

    public CustomerInvoiceAccountingPeriodOptionDto() : this(default !, string.Empty, default !, default !)
    {
    }
}

public sealed record CustomerInvoiceAccountingApprovalDto(Guid Id, string Status, long SourceVersion, string PayloadHash, DateTime CreatedUtc, DateTime? DecidedUtc)
{
    public Guid Id { get; set; } = Id;
    public string Status { get; set; } = Status;
    public long SourceVersion { get; set; } = SourceVersion;
    public string PayloadHash { get; set; } = PayloadHash;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime? DecidedUtc { get; set; } = DecidedUtc;

    public CustomerInvoiceAccountingApprovalDto() : this(default !, string.Empty, default !, string.Empty, default !, default !)
    {
    }
}

public sealed record CustomerInvoiceAccountingJournalLineDto(Guid FinanceAccountId, string AccountRole, string AccountCode, string AccountName, decimal DebitAmount, decimal CreditAmount, string Currency, string Description, string? TaxRuleKey = null, string? TaxRuleVersion = null, IReadOnlyList<string>? VatBoxMappings = null, string? EvidenceClassification = null, decimal? DocumentDebitAmount = null, decimal? DocumentCreditAmount = null, string? DocumentCurrency = null)
{
    public Guid FinanceAccountId { get; set; } = FinanceAccountId;
    public string AccountRole { get; set; } = AccountRole;
    public string AccountCode { get; set; } = AccountCode;
    public string AccountName { get; set; } = AccountName;
    public decimal DebitAmount { get; set; } = DebitAmount;
    public decimal CreditAmount { get; set; } = CreditAmount;
    public string Currency { get; set; } = Currency;
    public string Description { get; set; } = Description;
    public string? TaxRuleKey { get; set; } = TaxRuleKey;
    public string? TaxRuleVersion { get; set; } = TaxRuleVersion;
    public IReadOnlyList<string>? VatBoxMappings { get; set; } = VatBoxMappings;
    public string? EvidenceClassification { get; set; } = EvidenceClassification;
    public decimal? DocumentDebitAmount { get; set; } = DocumentDebitAmount;
    public decimal? DocumentCreditAmount { get; set; } = DocumentCreditAmount;
    public string? DocumentCurrency { get; set; } = DocumentCurrency;

    public CustomerInvoiceAccountingJournalLineDto() : this(default !, string.Empty, string.Empty, string.Empty, default !, default !, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record CustomerInvoiceReceivableReconciliationDto(Guid CompanyId, string BaseCurrency, decimal PostedDocumentReceivable, decimal PostedJournalReceivable, decimal AllocatedAmount, decimal OutstandingAmount, decimal Difference, bool IsReconciled, DateTime AsOfUtc, IReadOnlyList<DocumentCurrencyOpenItemControlDto>? DocumentCurrencyBreakdown = null)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public string BaseCurrency { get; set; } = BaseCurrency;
    public decimal PostedDocumentReceivable { get; set; } = PostedDocumentReceivable;
    public decimal PostedJournalReceivable { get; set; } = PostedJournalReceivable;
    public decimal AllocatedAmount { get; set; } = AllocatedAmount;
    public decimal OutstandingAmount { get; set; } = OutstandingAmount;
    public decimal Difference { get; set; } = Difference;
    public bool IsReconciled { get; set; } = IsReconciled;
    public DateTime AsOfUtc { get; set; } = AsOfUtc;
    public IReadOnlyList<DocumentCurrencyOpenItemControlDto>? DocumentCurrencyBreakdown { get; set; } = DocumentCurrencyBreakdown;

    public CustomerInvoiceReceivableReconciliationDto() : this(default !, string.Empty, default !, default !, default !, default !, default !, default !, default !, [])
    {
    }
}

public sealed record CustomerInvoiceAccountingIssueDto(string ReasonCode, string Explanation, bool IsBlocking = true, string? PolicyReasonCode = null)
{
    public string ReasonCode { get; set; } = ReasonCode;
    public string Explanation { get; set; } = Explanation;
    public bool IsBlocking { get; set; } = IsBlocking;
    public string? PolicyReasonCode { get; set; } = PolicyReasonCode;

    public CustomerInvoiceAccountingIssueDto() : this(string.Empty, string.Empty, default !, default !)
    {
    }
}

public sealed record CustomerInvoiceAccountingPostingResult(CustomerInvoiceAccountingStateDto State, AccountingJournalDto Journal, bool IsIdempotentReplay)
{
    public CustomerInvoiceAccountingStateDto State { get; set; } = State;
    public AccountingJournalDto Journal { get; set; } = Journal;
    public bool IsIdempotentReplay { get; set; } = IsIdempotentReplay;

    public CustomerInvoiceAccountingPostingResult() : this(new(), new(), default !)
    {
    }
}

public sealed record CustomerInvoiceAccountingSubmissionResult(CustomerInvoiceAccountingStateDto State, Guid ApprovalRequestId, bool IsIdempotentReplay)
{
    public CustomerInvoiceAccountingStateDto State { get; set; } = State;
    public Guid ApprovalRequestId { get; set; } = ApprovalRequestId;
    public bool IsIdempotentReplay { get; set; } = IsIdempotentReplay;

    public CustomerInvoiceAccountingSubmissionResult() : this(new(), default !, default !)
    {
    }
}
