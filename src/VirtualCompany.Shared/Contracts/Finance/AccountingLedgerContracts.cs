namespace VirtualCompany.Application.Finance;
public sealed record AccountingJournalEvidenceDto(Guid DocumentId, string Title, string ContentHash, string OriginalFileName)
{
    public Guid DocumentId { get; set; } = DocumentId;
    public string Title { get; set; } = Title;
    public string ContentHash { get; set; } = ContentHash;
    public string OriginalFileName { get; set; } = OriginalFileName;

    public AccountingJournalEvidenceDto() : this(default !, string.Empty, string.Empty, string.Empty)
    {
    }
}

public sealed record AccountingJournalLineDto(Guid Id, Guid FinanceAccountId, string AccountCode, string AccountName, decimal DebitAmount, decimal CreditAmount, string Currency, Guid? CostCenterId, string? Description, IReadOnlyDictionary<string, string> TaxFacts, IReadOnlyDictionary<string, string> DimensionFacts, decimal? DocumentDebitAmount = null, decimal? DocumentCreditAmount = null, string? DocumentCurrency = null, decimal? ExchangeRate = null, DateOnly? ExchangeRateDate = null, Guid? ExchangeRateConversionId = null, string? ExchangeRateIdentity = null, decimal? ConversionRoundingResidual = null, IReadOnlyList<ResolvedAccountingDimensionAssignment>? DimensionAssignments = null)
{
    public Guid Id { get; set; } = Id;
    public Guid FinanceAccountId { get; set; } = FinanceAccountId;
    public string AccountCode { get; set; } = AccountCode;
    public string AccountName { get; set; } = AccountName;
    public decimal DebitAmount { get; set; } = DebitAmount;
    public decimal CreditAmount { get; set; } = CreditAmount;
    public string Currency { get; set; } = Currency;
    public Guid? CostCenterId { get; set; } = CostCenterId;
    public string? Description { get; set; } = Description;
    public IReadOnlyDictionary<string, string> TaxFacts { get; set; } = TaxFacts;
    public IReadOnlyDictionary<string, string> DimensionFacts { get; set; } = DimensionFacts;
    public decimal? DocumentDebitAmount { get; set; } = DocumentDebitAmount;
    public decimal? DocumentCreditAmount { get; set; } = DocumentCreditAmount;
    public string? DocumentCurrency { get; set; } = DocumentCurrency;
    public decimal? ExchangeRate { get; set; } = ExchangeRate;
    public DateOnly? ExchangeRateDate { get; set; } = ExchangeRateDate;
    public Guid? ExchangeRateConversionId { get; set; } = ExchangeRateConversionId;
    public string? ExchangeRateIdentity { get; set; } = ExchangeRateIdentity;
    public decimal? ConversionRoundingResidual { get; set; } = ConversionRoundingResidual;
    public IReadOnlyList<ResolvedAccountingDimensionAssignment>? DimensionAssignments { get; set; } = DimensionAssignments;

    public AccountingJournalLineDto() : this(default !, default !, string.Empty, string.Empty, default !, default !, string.Empty, default !, default !, new Dictionary<string, string>(), new Dictionary<string, string>(), default !, default !, default !, default !, default !, default !, default !, default !, [])
    {
    }
}

public sealed record AccountingPostingPreview(bool IsValid, decimal DebitTotal, decimal CreditTotal, decimal Difference, string BaseCurrency, int RoundingPrecision, IReadOnlyList<AccountingPostingIssue> Issues, string? DocumentCurrency = null, decimal? DocumentDebitTotal = null, decimal? DocumentCreditTotal = null, decimal? DocumentDifference = null)
{
    public bool IsValid { get; set; } = IsValid;
    public decimal DebitTotal { get; set; } = DebitTotal;
    public decimal CreditTotal { get; set; } = CreditTotal;
    public decimal Difference { get; set; } = Difference;
    public string BaseCurrency { get; set; } = BaseCurrency;
    public int RoundingPrecision { get; set; } = RoundingPrecision;
    public IReadOnlyList<AccountingPostingIssue> Issues { get; set; } = Issues;
    public string? DocumentCurrency { get; set; } = DocumentCurrency;
    public decimal? DocumentDebitTotal { get; set; } = DocumentDebitTotal;
    public decimal? DocumentCreditTotal { get; set; } = DocumentCreditTotal;
    public decimal? DocumentDifference { get; set; } = DocumentDifference;

    public AccountingPostingPreview() : this(default !, default !, default !, default !, string.Empty, default !, [], default !, default !, default !, default !)
    {
    }
}

public sealed record AccountingJournalAuditEventDto(Guid Id, string ActorType, Guid? ActorId, string Action, string Outcome, string? Summary, DateTime OccurredUtc)
{
    public Guid Id { get; set; } = Id;
    public string ActorType { get; set; } = ActorType;
    public Guid? ActorId { get; set; } = ActorId;
    public string Action { get; set; } = Action;
    public string Outcome { get; set; } = Outcome;
    public string? Summary { get; set; } = Summary;
    public DateTime OccurredUtc { get; set; } = OccurredUtc;

    public AccountingJournalAuditEventDto() : this(default !, string.Empty, default !, string.Empty, string.Empty, default !, default !)
    {
    }
}

public sealed record DocumentCurrencyOpenItemControlDto(string DocumentCurrency, decimal PostedDocumentAmount, decimal AllocatedDocumentAmount, decimal OutstandingDocumentAmount, decimal PostedFunctionalAmount, decimal AllocatedFunctionalAmount, decimal OutstandingFunctionalAmount, string FunctionalCurrency)
{
    public string DocumentCurrency { get; set; } = DocumentCurrency;
    public decimal PostedDocumentAmount { get; set; } = PostedDocumentAmount;
    public decimal AllocatedDocumentAmount { get; set; } = AllocatedDocumentAmount;
    public decimal OutstandingDocumentAmount { get; set; } = OutstandingDocumentAmount;
    public decimal PostedFunctionalAmount { get; set; } = PostedFunctionalAmount;
    public decimal AllocatedFunctionalAmount { get; set; } = AllocatedFunctionalAmount;
    public decimal OutstandingFunctionalAmount { get; set; } = OutstandingFunctionalAmount;
    public string FunctionalCurrency { get; set; } = FunctionalCurrency;

    public DocumentCurrencyOpenItemControlDto() : this(string.Empty, default !, default !, default !, default !, default !, default !, string.Empty)
    {
    }
}

public sealed record AccountingJournalApprovalDto(Guid Id, string Status, string ApprovalType, string? DecisionSummary, DateTime CreatedUtc, DateTime? DecidedUtc)
{
    public Guid Id { get; set; } = Id;
    public string Status { get; set; } = Status;
    public string ApprovalType { get; set; } = ApprovalType;
    public string? DecisionSummary { get; set; } = DecisionSummary;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime? DecidedUtc { get; set; } = DecidedUtc;

    public AccountingJournalApprovalDto() : this(default !, string.Empty, string.Empty, default !, default !, default !)
    {
    }
}

public sealed record PostedAccountingJournal(AccountingJournalDto Journal, bool IsIdempotentReplay)
{
    public AccountingJournalDto Journal { get; set; } = Journal;
    public bool IsIdempotentReplay { get; set; } = IsIdempotentReplay;

    public PostedAccountingJournal() : this(new(), default !)
    {
    }
}

public sealed record AccountingJournalDto(Guid Id, Guid CompanyId, Guid FiscalPeriodId, string EntryNumber, string Status, string VoucherSeriesCode, long? VoucherSequenceNumber, int? VoucherFiscalYear, DateOnly? DocumentDate, DateOnly? PostingDate, string BaseCurrency, string? PostingType, string? Description, string? SourceType, string? SourceId, string? SourceVersion, string? PolicyPackKey, string? PolicyPackVersion, Guid? PostedByUserId, Guid? ApprovalRequestId, Guid? OriginalLedgerEntryId, string? CorrectionReason, DateTime? PostedAtUtc, decimal DebitTotal, decimal CreditTotal, IReadOnlyList<AccountingJournalLineDto> Lines, IReadOnlyList<AccountingJournalEvidenceDto>? Evidence = null, AccountingJournalApprovalDto? Approval = null, IReadOnlyList<AccountingJournalCorrectionDto>? Corrections = null, IReadOnlyList<AccountingJournalAuditEventDto>? AuditTimeline = null)
{
    public Guid Id { get; set; } = Id;
    public Guid CompanyId { get; set; } = CompanyId;
    public Guid FiscalPeriodId { get; set; } = FiscalPeriodId;
    public string EntryNumber { get; set; } = EntryNumber;
    public string Status { get; set; } = Status;
    public string VoucherSeriesCode { get; set; } = VoucherSeriesCode;
    public long? VoucherSequenceNumber { get; set; } = VoucherSequenceNumber;
    public int? VoucherFiscalYear { get; set; } = VoucherFiscalYear;
    public DateOnly? DocumentDate { get; set; } = DocumentDate;
    public DateOnly? PostingDate { get; set; } = PostingDate;
    public string BaseCurrency { get; set; } = BaseCurrency;
    public string? PostingType { get; set; } = PostingType;
    public string? Description { get; set; } = Description;
    public string? SourceType { get; set; } = SourceType;
    public string? SourceId { get; set; } = SourceId;
    public string? SourceVersion { get; set; } = SourceVersion;
    public string? PolicyPackKey { get; set; } = PolicyPackKey;
    public string? PolicyPackVersion { get; set; } = PolicyPackVersion;
    public Guid? PostedByUserId { get; set; } = PostedByUserId;
    public Guid? ApprovalRequestId { get; set; } = ApprovalRequestId;
    public Guid? OriginalLedgerEntryId { get; set; } = OriginalLedgerEntryId;
    public string? CorrectionReason { get; set; } = CorrectionReason;
    public DateTime? PostedAtUtc { get; set; } = PostedAtUtc;
    public decimal DebitTotal { get; set; } = DebitTotal;
    public decimal CreditTotal { get; set; } = CreditTotal;
    public IReadOnlyList<AccountingJournalLineDto> Lines { get; set; } = Lines;
    public IReadOnlyList<AccountingJournalEvidenceDto>? Evidence { get; set; } = Evidence;
    public AccountingJournalApprovalDto? Approval { get; set; } = Approval;
    public IReadOnlyList<AccountingJournalCorrectionDto>? Corrections { get; set; } = Corrections;
    public IReadOnlyList<AccountingJournalAuditEventDto>? AuditTimeline { get; set; } = AuditTimeline;

    public AccountingJournalDto() : this(default !, default !, default !, string.Empty, string.Empty, string.Empty, default !, default !, default !, default !, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, [], [], default !, [], [])
    {
    }
}

public sealed record AccountingJournalCorrectionDto(Guid Id, string EntryNumber, string PostingType, DateOnly? PostingDate, string? Reason, string Status)
{
    public Guid Id { get; set; } = Id;
    public string EntryNumber { get; set; } = EntryNumber;
    public string PostingType { get; set; } = PostingType;
    public DateOnly? PostingDate { get; set; } = PostingDate;
    public string? Reason { get; set; } = Reason;
    public string Status { get; set; } = Status;

    public AccountingJournalCorrectionDto() : this(default !, string.Empty, string.Empty, default !, default !, string.Empty)
    {
    }
}

public sealed record AccountingPostingIssue(string ReasonCode, string Explanation, Guid? SubjectId = null)
{
    public string ReasonCode { get; set; } = ReasonCode;
    public string Explanation { get; set; } = Explanation;
    public Guid? SubjectId { get; set; } = SubjectId;

    public AccountingPostingIssue() : this(string.Empty, string.Empty, default !)
    {
    }
}

public sealed record AccountingJournalListResult(IReadOnlyList<AccountingJournalDto> Items, int TotalCount, int Skip, int Take)
{
    public IReadOnlyList<AccountingJournalDto> Items { get; set; } = Items;
    public int TotalCount { get; set; } = TotalCount;
    public int Skip { get; set; } = Skip;
    public int Take { get; set; } = Take;

    public AccountingJournalListResult() : this([], default !, default !, default !)
    {
    }
}
