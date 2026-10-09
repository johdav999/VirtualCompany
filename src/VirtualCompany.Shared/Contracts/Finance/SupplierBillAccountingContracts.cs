namespace VirtualCompany.Application.Finance;
public sealed record SupplierBillDuplicateEvidenceDto(Guid MatchedBillId, string BillNumber, string SupplierName, DateOnly BillDate, decimal Amount, string Currency, IReadOnlyList<string> MatchedFields)
{
    public Guid MatchedBillId { get; set; } = MatchedBillId;
    public string BillNumber { get; set; } = BillNumber;
    public string SupplierName { get; set; } = SupplierName;
    public DateOnly BillDate { get; set; } = BillDate;
    public decimal Amount { get; set; } = Amount;
    public string Currency { get; set; } = Currency;
    public IReadOnlyList<string> MatchedFields { get; set; } = MatchedFields;

    public SupplierBillDuplicateEvidenceDto() : this(default !, string.Empty, string.Empty, default !, default !, string.Empty, [])
    {
    }
}

public sealed record SupplierBillAccountingIssueDto(string ReasonCode, string Explanation, bool IsBlocking = true, string? PolicyReasonCode = null)
{
    public string ReasonCode { get; set; } = ReasonCode;
    public string Explanation { get; set; } = Explanation;
    public bool IsBlocking { get; set; } = IsBlocking;
    public string? PolicyReasonCode { get; set; } = PolicyReasonCode;

    public SupplierBillAccountingIssueDto() : this(string.Empty, string.Empty, default !, default !)
    {
    }
}

public sealed record SupplierBillAccountingReferenceDataDto(Guid BillId, string DocumentCurrency, string BaseCurrency, decimal GrossAmount, IReadOnlyList<SupplierBillAccountingTaxRuleOptionDto> TaxRules, IReadOnlyList<SupplierBillAccountingPeriodOptionDto> OpenPeriods, IReadOnlyList<SupplierBillAccountingVoucherSeriesOptionDto> VoucherSeries, IReadOnlyList<SupplierBillAccountingAccountOptionDto> CostAccounts, string? DefaultTaxRuleKey, Guid? DefaultPeriodId, string? DefaultVoucherSeriesCode, Guid? SuggestedCostAccountId, string? SuggestedCostAccountEvidence)
{
    public Guid BillId { get; set; } = BillId;
    public string DocumentCurrency { get; set; } = DocumentCurrency;
    public string BaseCurrency { get; set; } = BaseCurrency;
    public decimal GrossAmount { get; set; } = GrossAmount;
    public IReadOnlyList<SupplierBillAccountingTaxRuleOptionDto> TaxRules { get; set; } = TaxRules;
    public IReadOnlyList<SupplierBillAccountingPeriodOptionDto> OpenPeriods { get; set; } = OpenPeriods;
    public IReadOnlyList<SupplierBillAccountingVoucherSeriesOptionDto> VoucherSeries { get; set; } = VoucherSeries;
    public IReadOnlyList<SupplierBillAccountingAccountOptionDto> CostAccounts { get; set; } = CostAccounts;
    public string? DefaultTaxRuleKey { get; set; } = DefaultTaxRuleKey;
    public Guid? DefaultPeriodId { get; set; } = DefaultPeriodId;
    public string? DefaultVoucherSeriesCode { get; set; } = DefaultVoucherSeriesCode;
    public Guid? SuggestedCostAccountId { get; set; } = SuggestedCostAccountId;
    public string? SuggestedCostAccountEvidence { get; set; } = SuggestedCostAccountEvidence;

    public SupplierBillAccountingReferenceDataDto() : this(default !, string.Empty, string.Empty, default !, [], [], [], [], default !, default !, default !, default !, default !)
    {
    }
}

public sealed record SupplierBillAccountingTaxRuleOptionDto(string Key, string DisplayName, decimal? Rate, string AmountMethod, string TaxTreatment, DateOnly EffectiveFrom)
{
    public string Key { get; set; } = Key;
    public string DisplayName { get; set; } = DisplayName;
    public decimal? Rate { get; set; } = Rate;
    public string AmountMethod { get; set; } = AmountMethod;
    public string TaxTreatment { get; set; } = TaxTreatment;
    public DateOnly EffectiveFrom { get; set; } = EffectiveFrom;

    public SupplierBillAccountingTaxRuleOptionDto() : this(string.Empty, string.Empty, default !, string.Empty, string.Empty, default !)
    {
    }
}

public sealed record SupplierBillAccountingVoucherSeriesOptionDto(string Code, string DisplayName)
{
    public string Code { get; set; } = Code;
    public string DisplayName { get; set; } = DisplayName;

    public SupplierBillAccountingVoucherSeriesOptionDto() : this(string.Empty, string.Empty)
    {
    }
}

public sealed record SupplierBillAccountingPeriodOptionDto(Guid Id, string Name, DateOnly StartDate, DateOnly EndDate)
{
    public Guid Id { get; set; } = Id;
    public string Name { get; set; } = Name;
    public DateOnly StartDate { get; set; } = StartDate;
    public DateOnly EndDate { get; set; } = EndDate;

    public SupplierBillAccountingPeriodOptionDto() : this(default !, string.Empty, default !, default !)
    {
    }
}

public sealed record SupplierBillPayablesReconciliationDto(Guid CompanyId, string BaseCurrency, decimal PostedDocumentPayables, decimal PostedJournalPayables, decimal AllocatedAmount, decimal OutstandingAmount, decimal Difference, bool IsReconciled, DateTime AsOfUtc, IReadOnlyList<DocumentCurrencyOpenItemControlDto>? DocumentCurrencyBreakdown = null)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public string BaseCurrency { get; set; } = BaseCurrency;
    public decimal PostedDocumentPayables { get; set; } = PostedDocumentPayables;
    public decimal PostedJournalPayables { get; set; } = PostedJournalPayables;
    public decimal AllocatedAmount { get; set; } = AllocatedAmount;
    public decimal OutstandingAmount { get; set; } = OutstandingAmount;
    public decimal Difference { get; set; } = Difference;
    public bool IsReconciled { get; set; } = IsReconciled;
    public DateTime AsOfUtc { get; set; } = AsOfUtc;
    public IReadOnlyList<DocumentCurrencyOpenItemControlDto>? DocumentCurrencyBreakdown { get; set; } = DocumentCurrencyBreakdown;

    public SupplierBillPayablesReconciliationDto() : this(default !, string.Empty, default !, default !, default !, default !, default !, default !, default !, [])
    {
    }
}

public sealed record SupplierBillAccountingStateDto(Guid BillId, Guid? ProfileId, string Status, string StatusLabel, bool CanPreview, bool CanSubmit, bool CanPost, bool CanCreateCreditNote, long? SourceVersion, decimal? NetAmount, decimal? RecoverableTaxAmount, decimal? NonRecoverableTaxAmount, decimal? GrossAmount, string? DocumentCurrency, decimal? ExchangeRate, decimal? GrossBaseAmount, string? BaseCurrency, string? TaxTreatment, string? PolicyPackKey, string? PolicyPackVersion, string? SourceDocumentHash, Guid? LedgerEntryId, string? VoucherNumber, Guid? OriginalBillId, string? BlockingReasonCode, string? BlockingReason, SupplierBillAccountingApprovalDto? Approval, IReadOnlyList<SupplierBillAccountingJournalLineDto> JournalLines, IReadOnlyList<SupplierBillDuplicateEvidenceDto> DuplicateEvidence, IReadOnlyList<SupplierBillAccountingIssueDto> Issues, DateOnly? ExchangeRateDate = null, Guid? ExchangeRateConversionId = null, string? ExchangeRateIdentity = null, decimal? ConversionRoundingResidual = null, string? CurrencyProvenance = null)
{
    public Guid BillId { get; set; } = BillId;
    public Guid? ProfileId { get; set; } = ProfileId;
    public string Status { get; set; } = Status;
    public string StatusLabel { get; set; } = StatusLabel;
    public bool CanPreview { get; set; } = CanPreview;
    public bool CanSubmit { get; set; } = CanSubmit;
    public bool CanPost { get; set; } = CanPost;
    public bool CanCreateCreditNote { get; set; } = CanCreateCreditNote;
    public long? SourceVersion { get; set; } = SourceVersion;
    public decimal? NetAmount { get; set; } = NetAmount;
    public decimal? RecoverableTaxAmount { get; set; } = RecoverableTaxAmount;
    public decimal? NonRecoverableTaxAmount { get; set; } = NonRecoverableTaxAmount;
    public decimal? GrossAmount { get; set; } = GrossAmount;
    public string? DocumentCurrency { get; set; } = DocumentCurrency;
    public decimal? ExchangeRate { get; set; } = ExchangeRate;
    public decimal? GrossBaseAmount { get; set; } = GrossBaseAmount;
    public string? BaseCurrency { get; set; } = BaseCurrency;
    public string? TaxTreatment { get; set; } = TaxTreatment;
    public string? PolicyPackKey { get; set; } = PolicyPackKey;
    public string? PolicyPackVersion { get; set; } = PolicyPackVersion;
    public string? SourceDocumentHash { get; set; } = SourceDocumentHash;
    public Guid? LedgerEntryId { get; set; } = LedgerEntryId;
    public string? VoucherNumber { get; set; } = VoucherNumber;
    public Guid? OriginalBillId { get; set; } = OriginalBillId;
    public string? BlockingReasonCode { get; set; } = BlockingReasonCode;
    public string? BlockingReason { get; set; } = BlockingReason;
    public SupplierBillAccountingApprovalDto? Approval { get; set; } = Approval;
    public IReadOnlyList<SupplierBillAccountingJournalLineDto> JournalLines { get; set; } = JournalLines;
    public IReadOnlyList<SupplierBillDuplicateEvidenceDto> DuplicateEvidence { get; set; } = DuplicateEvidence;
    public IReadOnlyList<SupplierBillAccountingIssueDto> Issues { get; set; } = Issues;
    public DateOnly? ExchangeRateDate { get; set; } = ExchangeRateDate;
    public Guid? ExchangeRateConversionId { get; set; } = ExchangeRateConversionId;
    public string? ExchangeRateIdentity { get; set; } = ExchangeRateIdentity;
    public decimal? ConversionRoundingResidual { get; set; } = ConversionRoundingResidual;
    public string? CurrencyProvenance { get; set; } = CurrencyProvenance;

    public SupplierBillAccountingStateDto() : this(default !, default !, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, [], [], [], default !, default !, default !, default !, default !)
    {
    }
}

public sealed record SupplierBillAccountingAccountOptionDto(Guid Id, string Code, string Name, string AccountClass)
{
    public Guid Id { get; set; } = Id;
    public string Code { get; set; } = Code;
    public string Name { get; set; } = Name;
    public string AccountClass { get; set; } = AccountClass;

    public SupplierBillAccountingAccountOptionDto() : this(default !, string.Empty, string.Empty, string.Empty)
    {
    }
}

public sealed record SupplierBillAccountingApprovalDto(Guid Id, string Status, long SourceVersion, string PayloadHash, DateTime CreatedUtc, DateTime? DecidedUtc)
{
    public Guid Id { get; set; } = Id;
    public string Status { get; set; } = Status;
    public long SourceVersion { get; set; } = SourceVersion;
    public string PayloadHash { get; set; } = PayloadHash;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime? DecidedUtc { get; set; } = DecidedUtc;

    public SupplierBillAccountingApprovalDto() : this(default !, string.Empty, default !, string.Empty, default !, default !)
    {
    }
}

public sealed record SupplierBillAccountingJournalLineDto(Guid FinanceAccountId, string AccountRole, string AccountCode, string AccountName, decimal DebitAmount, decimal CreditAmount, string Currency, string Description, string? TaxRuleKey = null, string? TaxTreatment = null, string? TaxRuleVersion = null, IReadOnlyList<string>? VatBoxMappings = null, string? EvidenceClassification = null, decimal? DocumentDebitAmount = null, decimal? DocumentCreditAmount = null, string? DocumentCurrency = null)
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
    public string? TaxTreatment { get; set; } = TaxTreatment;
    public string? TaxRuleVersion { get; set; } = TaxRuleVersion;
    public IReadOnlyList<string>? VatBoxMappings { get; set; } = VatBoxMappings;
    public string? EvidenceClassification { get; set; } = EvidenceClassification;
    public decimal? DocumentDebitAmount { get; set; } = DocumentDebitAmount;
    public decimal? DocumentCreditAmount { get; set; } = DocumentCreditAmount;
    public string? DocumentCurrency { get; set; } = DocumentCurrency;

    public SupplierBillAccountingJournalLineDto() : this(default !, string.Empty, string.Empty, string.Empty, default !, default !, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record SupplierBillAccountingPreviewDto(Guid BillId, bool IsReady, string AccountingStatus, string DocumentKind, decimal NetAmount, decimal RecoverableTaxAmount, decimal NonRecoverableTaxAmount, decimal GrossAmount, string DocumentCurrency, decimal ExchangeRate, decimal CostBaseAmount, decimal RecoverableTaxBaseAmount, decimal GrossBaseAmount, decimal RoundingBaseAmount, string BaseCurrency, string PolicyPackKey, string PolicyPackVersion, long SourceVersion, string PayloadHash, string? SourceDocumentHash, IReadOnlyList<SupplierBillAccountingJournalLineDto> JournalLines, IReadOnlyList<SupplierBillDuplicateEvidenceDto> DuplicateEvidence, IReadOnlyList<SupplierBillAccountingIssueDto> Issues, DateOnly? ExchangeRateDate = null, string? ExchangeRateIdentity = null, IReadOnlyList<ExchangeRateLookupLeg>? ExchangeRateLegs = null)
{
    public Guid BillId { get; set; } = BillId;
    public bool IsReady { get; set; } = IsReady;
    public string AccountingStatus { get; set; } = AccountingStatus;
    public string DocumentKind { get; set; } = DocumentKind;
    public decimal NetAmount { get; set; } = NetAmount;
    public decimal RecoverableTaxAmount { get; set; } = RecoverableTaxAmount;
    public decimal NonRecoverableTaxAmount { get; set; } = NonRecoverableTaxAmount;
    public decimal GrossAmount { get; set; } = GrossAmount;
    public string DocumentCurrency { get; set; } = DocumentCurrency;
    public decimal ExchangeRate { get; set; } = ExchangeRate;
    public decimal CostBaseAmount { get; set; } = CostBaseAmount;
    public decimal RecoverableTaxBaseAmount { get; set; } = RecoverableTaxBaseAmount;
    public decimal GrossBaseAmount { get; set; } = GrossBaseAmount;
    public decimal RoundingBaseAmount { get; set; } = RoundingBaseAmount;
    public string BaseCurrency { get; set; } = BaseCurrency;
    public string PolicyPackKey { get; set; } = PolicyPackKey;
    public string PolicyPackVersion { get; set; } = PolicyPackVersion;
    public long SourceVersion { get; set; } = SourceVersion;
    public string PayloadHash { get; set; } = PayloadHash;
    public string? SourceDocumentHash { get; set; } = SourceDocumentHash;
    public IReadOnlyList<SupplierBillAccountingJournalLineDto> JournalLines { get; set; } = JournalLines;
    public IReadOnlyList<SupplierBillDuplicateEvidenceDto> DuplicateEvidence { get; set; } = DuplicateEvidence;
    public IReadOnlyList<SupplierBillAccountingIssueDto> Issues { get; set; } = Issues;
    public DateOnly? ExchangeRateDate { get; set; } = ExchangeRateDate;
    public string? ExchangeRateIdentity { get; set; } = ExchangeRateIdentity;
    public IReadOnlyList<ExchangeRateLookupLeg>? ExchangeRateLegs { get; set; } = ExchangeRateLegs;

    public SupplierBillAccountingPreviewDto() : this(default !, default !, string.Empty, string.Empty, default !, default !, default !, default !, string.Empty, default !, default !, default !, default !, default !, string.Empty, string.Empty, string.Empty, default !, string.Empty, default !, [], [], [], default !, default !, [])
    {
    }
}

public sealed record SupplierBillAccountingPostingResult(SupplierBillAccountingStateDto State, AccountingJournalDto Journal, bool IsIdempotentReplay)
{
    public SupplierBillAccountingStateDto State { get; set; } = State;
    public AccountingJournalDto Journal { get; set; } = Journal;
    public bool IsIdempotentReplay { get; set; } = IsIdempotentReplay;

    public SupplierBillAccountingPostingResult() : this(new(), new(), default !)
    {
    }
}

public sealed record SupplierBillAccountingSubmissionResult(SupplierBillAccountingStateDto State, Guid ApprovalRequestId, bool IsIdempotentReplay)
{
    public SupplierBillAccountingStateDto State { get; set; } = State;
    public Guid ApprovalRequestId { get; set; } = ApprovalRequestId;
    public bool IsIdempotentReplay { get; set; } = IsIdempotentReplay;

    public SupplierBillAccountingSubmissionResult() : this(new(), default !, default !)
    {
    }
}
