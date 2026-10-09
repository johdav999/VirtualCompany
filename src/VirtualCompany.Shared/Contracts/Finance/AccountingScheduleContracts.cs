namespace VirtualCompany.Application.Finance;
public sealed record AccountingScheduleLineDto(Guid Id, int Sequence, Guid FinanceAccountId, string AccountCode, string AccountName, decimal DebitAmount, decimal CreditAmount, string Description, IReadOnlyList<Guid> DimensionMemberIds)
{
    public Guid Id { get; set; } = Id;
    public int Sequence { get; set; } = Sequence;
    public Guid FinanceAccountId { get; set; } = FinanceAccountId;
    public string AccountCode { get; set; } = AccountCode;
    public string AccountName { get; set; } = AccountName;
    public decimal DebitAmount { get; set; } = DebitAmount;
    public decimal CreditAmount { get; set; } = CreditAmount;
    public string Description { get; set; } = Description;
    public IReadOnlyList<Guid> DimensionMemberIds { get; set; } = DimensionMemberIds;

    public AccountingScheduleLineDto() : this(default !, default !, default !, string.Empty, string.Empty, default !, default !, string.Empty, [])
    {
    }
}

public sealed record AccountingScheduleDto(Guid Id, Guid CompanyId, string Code, string Name, string ScheduleType, string Cadence, string AmountBasis, string ProrationRule, DateOnly StartDate, DateOnly? EndDate, int OccurrenceDay, string TimeZoneId, string VoucherSeriesCode, string Currency, string ReversalRule, string Status, DateOnly NextOccurrenceDate, int CurrentVersionNumber, string? CurrentVersionHash, long Version, Guid CreatedByUserId, Guid UpdatedByUserId, DateTime CreatedUtc, DateTime UpdatedUtc, AccountingScheduleVersionDto? CurrentVersion, AccountingScheduleApprovalDto? Approval, IReadOnlyList<AccountingScheduleOccurrenceDto> Occurrences, AccountingScheduleReconciliationDto Reconciliation, IReadOnlyList<string> AllowedActions)
{
    public Guid Id { get; set; } = Id;
    public Guid CompanyId { get; set; } = CompanyId;
    public string Code { get; set; } = Code;
    public string Name { get; set; } = Name;
    public string ScheduleType { get; set; } = ScheduleType;
    public string Cadence { get; set; } = Cadence;
    public string AmountBasis { get; set; } = AmountBasis;
    public string ProrationRule { get; set; } = ProrationRule;
    public DateOnly StartDate { get; set; } = StartDate;
    public DateOnly? EndDate { get; set; } = EndDate;
    public int OccurrenceDay { get; set; } = OccurrenceDay;
    public string TimeZoneId { get; set; } = TimeZoneId;
    public string VoucherSeriesCode { get; set; } = VoucherSeriesCode;
    public string Currency { get; set; } = Currency;
    public string ReversalRule { get; set; } = ReversalRule;
    public string Status { get; set; } = Status;
    public DateOnly NextOccurrenceDate { get; set; } = NextOccurrenceDate;
    public int CurrentVersionNumber { get; set; } = CurrentVersionNumber;
    public string? CurrentVersionHash { get; set; } = CurrentVersionHash;
    public long Version { get; set; } = Version;
    public Guid CreatedByUserId { get; set; } = CreatedByUserId;
    public Guid UpdatedByUserId { get; set; } = UpdatedByUserId;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;
    public AccountingScheduleVersionDto? CurrentVersion { get; set; } = CurrentVersion;
    public AccountingScheduleApprovalDto? Approval { get; set; } = Approval;
    public IReadOnlyList<AccountingScheduleOccurrenceDto> Occurrences { get; set; } = Occurrences;
    public AccountingScheduleReconciliationDto Reconciliation { get; set; } = Reconciliation;
    public IReadOnlyList<string> AllowedActions { get; set; } = AllowedActions;

    public AccountingScheduleDto() : this(default !, default !, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, default !, default !, default !, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, [], new(), [])
    {
    }
}

public sealed record AccountingScheduleApprovalDto(Guid ApprovalRequestId, string Status, int VersionNumber, string PayloadHash, DateTime BoundUtc, string? DecisionSummary)
{
    public Guid ApprovalRequestId { get; set; } = ApprovalRequestId;
    public string Status { get; set; } = Status;
    public int VersionNumber { get; set; } = VersionNumber;
    public string PayloadHash { get; set; } = PayloadHash;
    public DateTime BoundUtc { get; set; } = BoundUtc;
    public string? DecisionSummary { get; set; } = DecisionSummary;

    public AccountingScheduleApprovalDto() : this(default !, string.Empty, default !, string.Empty, default !, default !)
    {
    }
}

public sealed record AccountingScheduleExceptionDto(Guid Id, string ReasonCode, string Explanation, string SafeNextAction, string Status, DateTime CreatedUtc, DateTime? ResolvedUtc)
{
    public Guid Id { get; set; } = Id;
    public string ReasonCode { get; set; } = ReasonCode;
    public string Explanation { get; set; } = Explanation;
    public string SafeNextAction { get; set; } = SafeNextAction;
    public string Status { get; set; } = Status;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime? ResolvedUtc { get; set; } = ResolvedUtc;

    public AccountingScheduleExceptionDto() : this(default !, string.Empty, string.Empty, string.Empty, string.Empty, default !, default !)
    {
    }
}

public sealed record AccountingScheduleVersionDto(Guid Id, int VersionNumber, string PayloadHash, string Description, DateOnly EffectiveFrom, DateTime CreatedUtc, IReadOnlyList<AccountingScheduleLineDto> Lines, IReadOnlyList<AccountingScheduleEvidenceDto> Evidence)
{
    public Guid Id { get; set; } = Id;
    public int VersionNumber { get; set; } = VersionNumber;
    public string PayloadHash { get; set; } = PayloadHash;
    public string Description { get; set; } = Description;
    public DateOnly EffectiveFrom { get; set; } = EffectiveFrom;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public IReadOnlyList<AccountingScheduleLineDto> Lines { get; set; } = Lines;
    public IReadOnlyList<AccountingScheduleEvidenceDto> Evidence { get; set; } = Evidence;

    public AccountingScheduleVersionDto() : this(default !, default !, string.Empty, string.Empty, default !, default !, [], [])
    {
    }
}

public sealed record AccountingScheduleEvidenceDto(Guid DocumentId, string Title, string ContentHash, string OriginalFileName)
{
    public Guid DocumentId { get; set; } = DocumentId;
    public string Title { get; set; } = Title;
    public string ContentHash { get; set; } = ContentHash;
    public string OriginalFileName { get; set; } = OriginalFileName;

    public AccountingScheduleEvidenceDto() : this(default !, string.Empty, string.Empty, string.Empty)
    {
    }
}

public sealed record AccountingScheduleReconciliationDto(decimal OriginalAmount, decimal ReleasedAmount, decimal ReversedAmount, decimal? RemainingAmount, decimal ExceptionAmount, string Currency, int PlannedOccurrences, int PostedOccurrences, int ReversedOccurrences, int ExceptionOccurrences, bool IsReconciled)
{
    public decimal OriginalAmount { get; set; } = OriginalAmount;
    public decimal ReleasedAmount { get; set; } = ReleasedAmount;
    public decimal ReversedAmount { get; set; } = ReversedAmount;
    public decimal? RemainingAmount { get; set; } = RemainingAmount;
    public decimal ExceptionAmount { get; set; } = ExceptionAmount;
    public string Currency { get; set; } = Currency;
    public int PlannedOccurrences { get; set; } = PlannedOccurrences;
    public int PostedOccurrences { get; set; } = PostedOccurrences;
    public int ReversedOccurrences { get; set; } = ReversedOccurrences;
    public int ExceptionOccurrences { get; set; } = ExceptionOccurrences;
    public bool IsReconciled { get; set; } = IsReconciled;

    public AccountingScheduleReconciliationDto() : this(default !, default !, default !, default !, default !, string.Empty, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record AccountingScheduleOccurrenceDto(Guid Id, DateOnly OccurrenceDate, DateOnly PostingDate, decimal ScheduledAmount, decimal ReleasedAmount, decimal ReversedAmount, string Currency, string Status, Guid? LedgerEntryId, Guid? ReversalLedgerEntryId, DateOnly? ReversalDueDate, int AttemptCount, string? FailureCode, string? FailureSummary, long Version, DateTime UpdatedUtc, IReadOnlyList<AccountingScheduleExceptionDto> Exceptions)
{
    public Guid Id { get; set; } = Id;
    public DateOnly OccurrenceDate { get; set; } = OccurrenceDate;
    public DateOnly PostingDate { get; set; } = PostingDate;
    public decimal ScheduledAmount { get; set; } = ScheduledAmount;
    public decimal ReleasedAmount { get; set; } = ReleasedAmount;
    public decimal ReversedAmount { get; set; } = ReversedAmount;
    public string Currency { get; set; } = Currency;
    public string Status { get; set; } = Status;
    public Guid? LedgerEntryId { get; set; } = LedgerEntryId;
    public Guid? ReversalLedgerEntryId { get; set; } = ReversalLedgerEntryId;
    public DateOnly? ReversalDueDate { get; set; } = ReversalDueDate;
    public int AttemptCount { get; set; } = AttemptCount;
    public string? FailureCode { get; set; } = FailureCode;
    public string? FailureSummary { get; set; } = FailureSummary;
    public long Version { get; set; } = Version;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;
    public IReadOnlyList<AccountingScheduleExceptionDto> Exceptions { get; set; } = Exceptions;

    public AccountingScheduleOccurrenceDto() : this(default !, default !, default !, default !, default !, default !, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !, [])
    {
    }
}

public sealed record AccountingSchedulePreviewDto(AccountingScheduleDto Schedule, AccountingPostingPreview PostingPreview, decimal OccurrenceAmount, DateOnly PostingDate, int PlannedOccurrences, IReadOnlyList<AccountingPostingIssue> Issues)
{
    public AccountingScheduleDto Schedule { get; set; } = Schedule;
    public AccountingPostingPreview PostingPreview { get; set; } = PostingPreview;
    public decimal OccurrenceAmount { get; set; } = OccurrenceAmount;
    public DateOnly PostingDate { get; set; } = PostingDate;
    public int PlannedOccurrences { get; set; } = PlannedOccurrences;
    public IReadOnlyList<AccountingPostingIssue> Issues { get; set; } = Issues;

    public AccountingSchedulePreviewDto() : this(new(), new(), default !, default !, default !, [])
    {
    }
}

public sealed record AccountingScheduleListResult(IReadOnlyList<AccountingScheduleDto> Items, int TotalCount, int Skip, int Take, decimal ReleasedAmount, decimal ReversedAmount, decimal RemainingAmount, int ActiveCount, int DueCount, int ExceptionCount, string Currency)
{
    public IReadOnlyList<AccountingScheduleDto> Items { get; set; } = Items;
    public int TotalCount { get; set; } = TotalCount;
    public int Skip { get; set; } = Skip;
    public int Take { get; set; } = Take;
    public decimal ReleasedAmount { get; set; } = ReleasedAmount;
    public decimal ReversedAmount { get; set; } = ReversedAmount;
    public decimal RemainingAmount { get; set; } = RemainingAmount;
    public int ActiveCount { get; set; } = ActiveCount;
    public int DueCount { get; set; } = DueCount;
    public int ExceptionCount { get; set; } = ExceptionCount;
    public string Currency { get; set; } = Currency;

    public AccountingScheduleListResult() : this([], default !, default !, default !, default !, default !, default !, default !, default !, default !, string.Empty)
    {
    }
}
