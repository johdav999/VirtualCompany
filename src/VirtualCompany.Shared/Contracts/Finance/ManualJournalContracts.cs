namespace VirtualCompany.Application.Finance;
public sealed record ManualJournalReferenceDataDto(IReadOnlyList<ManualJournalVoucherSeriesDto> VoucherSeries, IReadOnlyList<ManualJournalEvidenceOptionDto> EvidenceDocuments, IReadOnlyList<AccountingDimensionTypeDto>? DimensionTypes = null)
{
    public IReadOnlyList<ManualJournalVoucherSeriesDto> VoucherSeries { get; set; } = VoucherSeries;
    public IReadOnlyList<ManualJournalEvidenceOptionDto> EvidenceDocuments { get; set; } = EvidenceDocuments;
    public IReadOnlyList<AccountingDimensionTypeDto>? DimensionTypes { get; set; } = DimensionTypes;

    public ManualJournalReferenceDataDto() : this([], [], default !)
    {
    }
}

public sealed record ManualJournalLineDto(Guid Id, int LineNumber, Guid FinanceAccountId, string AccountCode, string AccountName, decimal DebitAmount, decimal CreditAmount, string Currency, string? Description, Guid? CostCenterId, IReadOnlyDictionary<string, string> TaxFacts, IReadOnlyDictionary<string, string> DimensionFacts, IReadOnlyList<Guid>? DimensionMemberIds = null)
{
    public Guid Id { get; set; } = Id;
    public int LineNumber { get; set; } = LineNumber;
    public Guid FinanceAccountId { get; set; } = FinanceAccountId;
    public string AccountCode { get; set; } = AccountCode;
    public string AccountName { get; set; } = AccountName;
    public decimal DebitAmount { get; set; } = DebitAmount;
    public decimal CreditAmount { get; set; } = CreditAmount;
    public string Currency { get; set; } = Currency;
    public string? Description { get; set; } = Description;
    public Guid? CostCenterId { get; set; } = CostCenterId;
    public IReadOnlyDictionary<string, string> TaxFacts { get; set; } = TaxFacts;
    public IReadOnlyDictionary<string, string> DimensionFacts { get; set; } = DimensionFacts;
    public IReadOnlyList<Guid>? DimensionMemberIds { get; set; } = DimensionMemberIds;

    public ManualJournalLineDto() : this(default !, default !, default !, string.Empty, string.Empty, default !, default !, string.Empty, default !, default !, new Dictionary<string, string>(), new Dictionary<string, string>(), default !)
    {
    }
}

public sealed record ManualJournalSourceReferenceDto(string SourceType, Guid RecordId, string SourceVersion)
{
    public string SourceType { get; set; } = SourceType;
    public Guid RecordId { get; set; } = RecordId;
    public string SourceVersion { get; set; } = SourceVersion;

    public ManualJournalSourceReferenceDto() : this(string.Empty, default !, string.Empty)
    {
    }
}

public sealed record ManualJournalPreviewDto(ManualJournalDraftDto Draft, AccountingPostingPreview PostingPreview, ManualJournalPolicyDecisionDto Policy)
{
    public ManualJournalDraftDto Draft { get; set; } = Draft;
    public AccountingPostingPreview PostingPreview { get; set; } = PostingPreview;
    public ManualJournalPolicyDecisionDto Policy { get; set; } = Policy;

    public ManualJournalPreviewDto() : this(new(), new(), new())
    {
    }
}

public sealed record ManualJournalEvidenceDto(Guid DocumentId, string Title, string ContentHash, string OriginalFileName)
{
    public Guid DocumentId { get; set; } = DocumentId;
    public string Title { get; set; } = Title;
    public string ContentHash { get; set; } = ContentHash;
    public string OriginalFileName { get; set; } = OriginalFileName;

    public ManualJournalEvidenceDto() : this(default !, string.Empty, string.Empty, string.Empty)
    {
    }
}

public sealed record ManualJournalVoucherSeriesDto(string Code, string DisplayName, string NumberPrefix)
{
    public string Code { get; set; } = Code;
    public string DisplayName { get; set; } = DisplayName;
    public string NumberPrefix { get; set; } = NumberPrefix;

    public ManualJournalVoucherSeriesDto() : this(string.Empty, string.Empty, string.Empty)
    {
    }
}

public sealed record ManualJournalDraftDto(Guid Id, Guid CompanyId, Guid FiscalPeriodId, string VoucherSeriesCode, DateOnly DocumentDate, DateOnly PostingDate, string Explanation, string Currency, string Status, long Version, string PayloadHash, Guid CreatedByUserId, Guid UpdatedByUserId, Guid? ApprovalRequestId, Guid? LedgerEntryId, Guid? OriginalLedgerEntryId, string? CorrectionReason, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? PostedUtc, decimal DebitTotal, decimal CreditTotal, decimal Difference, IReadOnlyList<ManualJournalLineDto> Lines, IReadOnlyList<ManualJournalEvidenceDto> Evidence, ManualJournalApprovalDto? Approval, IReadOnlyList<ManualJournalSourceReferenceDto>? SourceRecords = null)
{
    public Guid Id { get; set; } = Id;
    public Guid CompanyId { get; set; } = CompanyId;
    public Guid FiscalPeriodId { get; set; } = FiscalPeriodId;
    public string VoucherSeriesCode { get; set; } = VoucherSeriesCode;
    public DateOnly DocumentDate { get; set; } = DocumentDate;
    public DateOnly PostingDate { get; set; } = PostingDate;
    public string Explanation { get; set; } = Explanation;
    public string Currency { get; set; } = Currency;
    public string Status { get; set; } = Status;
    public long Version { get; set; } = Version;
    public string PayloadHash { get; set; } = PayloadHash;
    public Guid CreatedByUserId { get; set; } = CreatedByUserId;
    public Guid UpdatedByUserId { get; set; } = UpdatedByUserId;
    public Guid? ApprovalRequestId { get; set; } = ApprovalRequestId;
    public Guid? LedgerEntryId { get; set; } = LedgerEntryId;
    public Guid? OriginalLedgerEntryId { get; set; } = OriginalLedgerEntryId;
    public string? CorrectionReason { get; set; } = CorrectionReason;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;
    public DateTime? PostedUtc { get; set; } = PostedUtc;
    public decimal DebitTotal { get; set; } = DebitTotal;
    public decimal CreditTotal { get; set; } = CreditTotal;
    public decimal Difference { get; set; } = Difference;
    public IReadOnlyList<ManualJournalLineDto> Lines { get; set; } = Lines;
    public IReadOnlyList<ManualJournalEvidenceDto> Evidence { get; set; } = Evidence;
    public ManualJournalApprovalDto? Approval { get; set; } = Approval;
    public IReadOnlyList<ManualJournalSourceReferenceDto>? SourceRecords { get; set; } = SourceRecords;

    public ManualJournalDraftDto() : this(default !, default !, default !, string.Empty, default !, default !, string.Empty, string.Empty, string.Empty, default !, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, [], [], default !, [])
    {
    }
}

public sealed record ManualJournalPolicyDecisionDto(bool IsAllowed, bool RequiresApproval, decimal ApprovalThreshold, string ApprovalCurrency, IReadOnlyList<AccountingPostingIssue> Issues, IReadOnlyList<AccountingPostingIssue> Warnings)
{
    public bool IsAllowed { get; set; } = IsAllowed;
    public bool RequiresApproval { get; set; } = RequiresApproval;
    public decimal ApprovalThreshold { get; set; } = ApprovalThreshold;
    public string ApprovalCurrency { get; set; } = ApprovalCurrency;
    public IReadOnlyList<AccountingPostingIssue> Issues { get; set; } = Issues;
    public IReadOnlyList<AccountingPostingIssue> Warnings { get; set; } = Warnings;

    public ManualJournalPolicyDecisionDto() : this(default !, default !, default !, string.Empty, [], [])
    {
    }
}

public sealed record ManualJournalApprovalDto(Guid Id, string Status, string? DecisionSummary, long DraftVersion, string PayloadHash, DateTime CreatedUtc, DateTime? DecidedUtc)
{
    public Guid Id { get; set; } = Id;
    public string Status { get; set; } = Status;
    public string? DecisionSummary { get; set; } = DecisionSummary;
    public long DraftVersion { get; set; } = DraftVersion;
    public string PayloadHash { get; set; } = PayloadHash;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime? DecidedUtc { get; set; } = DecidedUtc;

    public ManualJournalApprovalDto() : this(default !, string.Empty, default !, default !, string.Empty, default !, default !)
    {
    }
}

public sealed record ManualJournalEvidenceOptionDto(Guid DocumentId, string Title, string OriginalFileName, DateTime UploadedUtc)
{
    public Guid DocumentId { get; set; } = DocumentId;
    public string Title { get; set; } = Title;
    public string OriginalFileName { get; set; } = OriginalFileName;
    public DateTime UploadedUtc { get; set; } = UploadedUtc;

    public ManualJournalEvidenceOptionDto() : this(default !, string.Empty, string.Empty, default !)
    {
    }
}

public sealed record ManualJournalPostingResult(ManualJournalDraftDto Draft, AccountingJournalDto Journal, bool IsIdempotentReplay)
{
    public ManualJournalDraftDto Draft { get; set; } = Draft;
    public AccountingJournalDto Journal { get; set; } = Journal;
    public bool IsIdempotentReplay { get; set; } = IsIdempotentReplay;

    public ManualJournalPostingResult() : this(new(), new(), default !)
    {
    }
}

public sealed record ManualJournalSubmissionResult(ManualJournalDraftDto Draft, Guid ApprovalRequestId, bool IsIdempotentReplay)
{
    public ManualJournalDraftDto Draft { get; set; } = Draft;
    public Guid ApprovalRequestId { get; set; } = ApprovalRequestId;
    public bool IsIdempotentReplay { get; set; } = IsIdempotentReplay;

    public ManualJournalSubmissionResult() : this(new(), default !, default !)
    {
    }
}

public sealed record ManualJournalDraftListResult(IReadOnlyList<ManualJournalDraftDto> Items, int TotalCount, int Skip, int Take)
{
    public IReadOnlyList<ManualJournalDraftDto> Items { get; set; } = Items;
    public int TotalCount { get; set; } = TotalCount;
    public int Skip { get; set; } = Skip;
    public int Take { get; set; } = Take;

    public ManualJournalDraftListResult() : this([], default !, default !, default !)
    {
    }
}
