namespace VirtualCompany.Application.Finance;
public sealed record TreasuryEvidenceDto(Guid Id, string EvidenceType, string Reference, string ContentHash, string Description, DateTime CreatedUtc)
{
    public Guid Id { get; set; } = Id;
    public string EvidenceType { get; set; } = EvidenceType;
    public string Reference { get; set; } = Reference;
    public string ContentHash { get; set; } = ContentHash;
    public string Description { get; set; } = Description;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;

    public TreasuryEvidenceDto() : this(default !, string.Empty, string.Empty, string.Empty, string.Empty, default !)
    {
    }
}

public sealed record TreasuryPostingPreviewDto(bool CanPost, string? BlockingReasonCode, string? BlockingReason, AccountingPostingPreview? Accounting, IReadOnlyList<TreasuryPostingLineDto> Lines)
{
    public bool CanPost { get; set; } = CanPost;
    public string? BlockingReasonCode { get; set; } = BlockingReasonCode;
    public string? BlockingReason { get; set; } = BlockingReason;
    public AccountingPostingPreview? Accounting { get; set; } = Accounting;
    public IReadOnlyList<TreasuryPostingLineDto> Lines { get; set; } = Lines;

    public TreasuryPostingPreviewDto() : this(default !, default !, default !, default !, [])
    {
    }
}

public sealed record TreasurySourceListDto(IReadOnlyList<TreasurySourceSummaryDto> Items, int AttentionCount, int InTransitCount, int ReadyCount, int PostedCount)
{
    public IReadOnlyList<TreasurySourceSummaryDto> Items { get; set; } = Items;
    public int AttentionCount { get; set; } = AttentionCount;
    public int InTransitCount { get; set; } = InTransitCount;
    public int ReadyCount { get; set; } = ReadyCount;
    public int PostedCount { get; set; } = PostedCount;

    public TreasurySourceListDto() : this([], default !, default !, default !, default !)
    {
    }
}

public sealed record TreasuryAllowedActionsDto(bool CanLinkBankEvidence, bool CanBindApproval, bool CanPreview, bool CanPost, bool CanReverse, string? BlockingReasonCode, string? Explanation)
{
    public bool CanLinkBankEvidence { get; set; } = CanLinkBankEvidence;
    public bool CanBindApproval { get; set; } = CanBindApproval;
    public bool CanPreview { get; set; } = CanPreview;
    public bool CanPost { get; set; } = CanPost;
    public bool CanReverse { get; set; } = CanReverse;
    public string? BlockingReasonCode { get; set; } = BlockingReasonCode;
    public string? Explanation { get; set; } = Explanation;

    public TreasuryAllowedActionsDto() : this(default !, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record TreasurySourceDetailDto(TreasurySourceSummaryDto Summary, Guid? FromBankAccountId, Guid? ToBankAccountId, Guid? BankAccountId, Guid? CounterpartFinanceAccountId, Guid? CorrectionOfSourceId, IReadOnlyList<TreasuryBankEvidenceDto> BankEvidence, IReadOnlyList<TreasuryEvidenceDto> Evidence, IReadOnlyList<TreasuryLedgerLinkDto> Journals, IReadOnlyList<TreasurySourceEventDto> History, TreasuryAllowedActionsDto AllowedActions, TreasuryPostingPreviewDto? PostingPreview = null)
{
    public TreasurySourceSummaryDto Summary { get; set; } = Summary;
    public Guid? FromBankAccountId { get; set; } = FromBankAccountId;
    public Guid? ToBankAccountId { get; set; } = ToBankAccountId;
    public Guid? BankAccountId { get; set; } = BankAccountId;
    public Guid? CounterpartFinanceAccountId { get; set; } = CounterpartFinanceAccountId;
    public Guid? CorrectionOfSourceId { get; set; } = CorrectionOfSourceId;
    public IReadOnlyList<TreasuryBankEvidenceDto> BankEvidence { get; set; } = BankEvidence;
    public IReadOnlyList<TreasuryEvidenceDto> Evidence { get; set; } = Evidence;
    public IReadOnlyList<TreasuryLedgerLinkDto> Journals { get; set; } = Journals;
    public IReadOnlyList<TreasurySourceEventDto> History { get; set; } = History;
    public TreasuryAllowedActionsDto AllowedActions { get; set; } = AllowedActions;
    public TreasuryPostingPreviewDto? PostingPreview { get; set; } = PostingPreview;

    public TreasurySourceDetailDto() : this(new(), default !, default !, default !, default !, default !, [], [], [], [], new(), default !)
    {
    }
}

public sealed record TreasuryBankEvidenceDto(Guid BankTransactionId, string LegRole, DateTime BookingDate, decimal Amount, string Currency, string Reference, string Counterparty)
{
    public Guid BankTransactionId { get; set; } = BankTransactionId;
    public string LegRole { get; set; } = LegRole;
    public DateTime BookingDate { get; set; } = BookingDate;
    public decimal Amount { get; set; } = Amount;
    public string Currency { get; set; } = Currency;
    public string Reference { get; set; } = Reference;
    public string Counterparty { get; set; } = Counterparty;

    public TreasuryBankEvidenceDto() : this(default !, string.Empty, default !, default !, string.Empty, string.Empty, string.Empty)
    {
    }
}

public sealed record TreasurySourceSummaryDto(Guid Id, string SourceType, string SourceIdentity, string DisplayName, string Status, string? ReasonCode, string Currency, decimal GrossAmount, decimal FeeAmount, decimal NetAmount, bool RequiresApproval, Guid? ApprovalRequestId, long Version, DateTime UpdatedUtc)
{
    public Guid Id { get; set; } = Id;
    public string SourceType { get; set; } = SourceType;
    public string SourceIdentity { get; set; } = SourceIdentity;
    public string DisplayName { get; set; } = DisplayName;
    public string Status { get; set; } = Status;
    public string? ReasonCode { get; set; } = ReasonCode;
    public string Currency { get; set; } = Currency;
    public decimal GrossAmount { get; set; } = GrossAmount;
    public decimal FeeAmount { get; set; } = FeeAmount;
    public decimal NetAmount { get; set; } = NetAmount;
    public bool RequiresApproval { get; set; } = RequiresApproval;
    public Guid? ApprovalRequestId { get; set; } = ApprovalRequestId;
    public long Version { get; set; } = Version;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;

    public TreasurySourceSummaryDto() : this(default !, string.Empty, string.Empty, string.Empty, string.Empty, default !, string.Empty, default !, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record TreasurySourceEventDto(Guid Id, string Action, Guid ActorUserId, string? ReasonCode, string BeforeJson, string AfterJson, DateTime CreatedUtc)
{
    public Guid Id { get; set; } = Id;
    public string Action { get; set; } = Action;
    public Guid ActorUserId { get; set; } = ActorUserId;
    public string? ReasonCode { get; set; } = ReasonCode;
    public string BeforeJson { get; set; } = BeforeJson;
    public string AfterJson { get; set; } = AfterJson;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;

    public TreasurySourceEventDto() : this(default !, string.Empty, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record TreasuryLedgerLinkDto(Guid LedgerEntryId, string EntryNumber, string LinkRole, DateTime CreatedUtc)
{
    public Guid LedgerEntryId { get; set; } = LedgerEntryId;
    public string EntryNumber { get; set; } = EntryNumber;
    public string LinkRole { get; set; } = LinkRole;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;

    public TreasuryLedgerLinkDto() : this(default !, string.Empty, string.Empty, default !)
    {
    }
}

public sealed record TreasuryPostingLineDto(Guid FinanceAccountId, string AccountCode, string AccountName, decimal DebitAmount, decimal CreditAmount, string Currency, string Description)
{
    public Guid FinanceAccountId { get; set; } = FinanceAccountId;
    public string AccountCode { get; set; } = AccountCode;
    public string AccountName { get; set; } = AccountName;
    public decimal DebitAmount { get; set; } = DebitAmount;
    public decimal CreditAmount { get; set; } = CreditAmount;
    public string Currency { get; set; } = Currency;
    public string Description { get; set; } = Description;

    public TreasuryPostingLineDto() : this(default !, string.Empty, string.Empty, default !, default !, string.Empty, string.Empty)
    {
    }
}

public sealed record TreasuryEvidenceInputDto(string EvidenceType, string Reference, string ContentHash, string Description)
{
    public string EvidenceType { get; set; } = EvidenceType;
    public string Reference { get; set; } = Reference;
    public string ContentHash { get; set; } = ContentHash;
    public string Description { get; set; } = Description;

    public TreasuryEvidenceInputDto() : this(string.Empty, string.Empty, string.Empty, string.Empty)
    {
    }
}
