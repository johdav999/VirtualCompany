namespace VirtualCompany.Application.Finance;
public sealed record YearEndSignOffDto(Guid Id, string Action, string Decision, string EvidenceHash, Guid ActorUserId, string ActorRole, string? Reason, DateTime OccurredUtc)
{
    public Guid Id { get; set; } = Id;
    public string Action { get; set; } = Action;
    public string Decision { get; set; } = Decision;
    public string EvidenceHash { get; set; } = EvidenceHash;
    public Guid ActorUserId { get; set; } = ActorUserId;
    public string ActorRole { get; set; } = ActorRole;
    public string? Reason { get; set; } = Reason;
    public DateTime OccurredUtc { get; set; } = OccurredUtc;

    public YearEndSignOffDto() : this(default !, string.Empty, string.Empty, default !, default !, string.Empty, default !, default !)
    {
    }
}

public sealed record YearEndReadinessCheckDto(string Code, string Label, bool Passed, bool Blocking, int Count, string Explanation, string? TargetType, Guid? TargetId, DateTime ObservedUtc)
{
    public string Code { get; set; } = Code;
    public string Label { get; set; } = Label;
    public bool Passed { get; set; } = Passed;
    public bool Blocking { get; set; } = Blocking;
    public int Count { get; set; } = Count;
    public string Explanation { get; set; } = Explanation;
    public string? TargetType { get; set; } = TargetType;
    public Guid? TargetId { get; set; } = TargetId;
    public DateTime ObservedUtc { get; set; } = ObservedUtc;

    public YearEndReadinessCheckDto() : this(string.Empty, string.Empty, default !, default !, default !, string.Empty, default !, default !, default !)
    {
    }
}

public sealed record YearEndRunDto(Guid Id, Guid CompanyId, string CompanyName, DateOnly FiscalYearStart, DateOnly FiscalYearEnd, Guid TargetFiscalPeriodId, string TargetFiscalPeriodName, string VoucherSeriesCode, string Status, Guid PreparedByUserId, Guid? ApprovedByUserId, Guid? ExecutedByUserId, Guid? ReconciledByUserId, Guid? CompletedByUserId, string? ApprovedEvidenceHash, Guid? RetainedEarningsLedgerEntryId, Guid? OpeningBalanceLedgerEntryId, string? OpeningBalanceChecksum, string? FailureCode, string? FailureSummary, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? ApprovedUtc, DateTime? ExecutedUtc, DateTime? ReconciledUtc, DateTime? CompletedUtc, long Version, YearEndReadinessSnapshotDto? CurrentReadiness, YearEndRetainedEarningsProposalDto? RetainedEarningsProposal, IReadOnlyList<YearEndOpeningBalanceCandidateDto> OpeningBalances, IReadOnlyList<YearEndSignOffDto> SignOffs, IReadOnlyList<YearEndSubsequentEventDto> SubsequentEvents, IReadOnlyList<YearEndHistoryDto> History, IReadOnlyList<string> AllowedActions)
{
    public Guid Id { get; set; } = Id;
    public Guid CompanyId { get; set; } = CompanyId;
    public string CompanyName { get; set; } = CompanyName;
    public DateOnly FiscalYearStart { get; set; } = FiscalYearStart;
    public DateOnly FiscalYearEnd { get; set; } = FiscalYearEnd;
    public Guid TargetFiscalPeriodId { get; set; } = TargetFiscalPeriodId;
    public string TargetFiscalPeriodName { get; set; } = TargetFiscalPeriodName;
    public string VoucherSeriesCode { get; set; } = VoucherSeriesCode;
    public string Status { get; set; } = Status;
    public Guid PreparedByUserId { get; set; } = PreparedByUserId;
    public Guid? ApprovedByUserId { get; set; } = ApprovedByUserId;
    public Guid? ExecutedByUserId { get; set; } = ExecutedByUserId;
    public Guid? ReconciledByUserId { get; set; } = ReconciledByUserId;
    public Guid? CompletedByUserId { get; set; } = CompletedByUserId;
    public string? ApprovedEvidenceHash { get; set; } = ApprovedEvidenceHash;
    public Guid? RetainedEarningsLedgerEntryId { get; set; } = RetainedEarningsLedgerEntryId;
    public Guid? OpeningBalanceLedgerEntryId { get; set; } = OpeningBalanceLedgerEntryId;
    public string? OpeningBalanceChecksum { get; set; } = OpeningBalanceChecksum;
    public string? FailureCode { get; set; } = FailureCode;
    public string? FailureSummary { get; set; } = FailureSummary;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;
    public DateTime? ApprovedUtc { get; set; } = ApprovedUtc;
    public DateTime? ExecutedUtc { get; set; } = ExecutedUtc;
    public DateTime? ReconciledUtc { get; set; } = ReconciledUtc;
    public DateTime? CompletedUtc { get; set; } = CompletedUtc;
    public long Version { get; set; } = Version;
    public YearEndReadinessSnapshotDto? CurrentReadiness { get; set; } = CurrentReadiness;
    public YearEndRetainedEarningsProposalDto? RetainedEarningsProposal { get; set; } = RetainedEarningsProposal;
    public IReadOnlyList<YearEndOpeningBalanceCandidateDto> OpeningBalances { get; set; } = OpeningBalances;
    public IReadOnlyList<YearEndSignOffDto> SignOffs { get; set; } = SignOffs;
    public IReadOnlyList<YearEndSubsequentEventDto> SubsequentEvents { get; set; } = SubsequentEvents;
    public IReadOnlyList<YearEndHistoryDto> History { get; set; } = History;
    public IReadOnlyList<string> AllowedActions { get; set; } = AllowedActions;

    public YearEndRunDto() : this(default !, default !, string.Empty, default !, default !, default !, string.Empty, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, [], [], [], [], [])
    {
    }
}

public sealed record YearEndReadinessSnapshotDto(Guid Id, int SnapshotNumber, string Status, string EvidenceHash, string JournalCutoffHash, int BlockerCount, int ClosedPeriodCount, Guid PreparedByUserId, DateTime PreparedUtc, long Version, IReadOnlyList<YearEndReadinessCheckDto> Checks)
{
    public Guid Id { get; set; } = Id;
    public int SnapshotNumber { get; set; } = SnapshotNumber;
    public string Status { get; set; } = Status;
    public string EvidenceHash { get; set; } = EvidenceHash;
    public string JournalCutoffHash { get; set; } = JournalCutoffHash;
    public int BlockerCount { get; set; } = BlockerCount;
    public int ClosedPeriodCount { get; set; } = ClosedPeriodCount;
    public Guid PreparedByUserId { get; set; } = PreparedByUserId;
    public DateTime PreparedUtc { get; set; } = PreparedUtc;
    public long Version { get; set; } = Version;
    public IReadOnlyList<YearEndReadinessCheckDto> Checks { get; set; } = Checks;

    public YearEndReadinessSnapshotDto() : this(default !, default !, string.Empty, string.Empty, string.Empty, default !, default !, default !, default !, default !, [])
    {
    }
}

public sealed record YearEndHistoryDto(Guid Id, string Action, string FromStatus, string ToStatus, Guid ActorUserId, string EvidenceHash, string Summary, DateTime OccurredUtc)
{
    public Guid Id { get; set; } = Id;
    public string Action { get; set; } = Action;
    public string FromStatus { get; set; } = FromStatus;
    public string ToStatus { get; set; } = ToStatus;
    public Guid ActorUserId { get; set; } = ActorUserId;
    public string EvidenceHash { get; set; } = EvidenceHash;
    public string Summary { get; set; } = Summary;
    public DateTime OccurredUtc { get; set; } = OccurredUtc;

    public YearEndHistoryDto() : this(default !, string.Empty, string.Empty, string.Empty, default !, string.Empty, string.Empty, default !)
    {
    }
}

public sealed record YearEndOpeningBalanceCandidateDto(Guid Id, Guid FinanceAccountId, string AccountCode, string AccountName, string AccountClass, string SourceCurrency, string DimensionKey, decimal ClosingFunctionalBalance, decimal ClosingDocumentBalance, decimal OpeningFunctionalBalance, decimal OpeningDocumentBalance, decimal Difference, string Status, Guid? OpeningLedgerEntryId)
{
    public Guid Id { get; set; } = Id;
    public Guid FinanceAccountId { get; set; } = FinanceAccountId;
    public string AccountCode { get; set; } = AccountCode;
    public string AccountName { get; set; } = AccountName;
    public string AccountClass { get; set; } = AccountClass;
    public string SourceCurrency { get; set; } = SourceCurrency;
    public string DimensionKey { get; set; } = DimensionKey;
    public decimal ClosingFunctionalBalance { get; set; } = ClosingFunctionalBalance;
    public decimal ClosingDocumentBalance { get; set; } = ClosingDocumentBalance;
    public decimal OpeningFunctionalBalance { get; set; } = OpeningFunctionalBalance;
    public decimal OpeningDocumentBalance { get; set; } = OpeningDocumentBalance;
    public decimal Difference { get; set; } = Difference;
    public string Status { get; set; } = Status;
    public Guid? OpeningLedgerEntryId { get; set; } = OpeningLedgerEntryId;

    public YearEndOpeningBalanceCandidateDto() : this(default !, default !, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, default !, default !, default !, default !, default !, string.Empty, default !)
    {
    }
}

public sealed record YearEndRunSummaryDto(Guid Id, DateOnly FiscalYearStart, DateOnly FiscalYearEnd, string Status, int BlockerCount, decimal NetIncome, string Currency, DateTime UpdatedUtc, long Version)
{
    public Guid Id { get; set; } = Id;
    public DateOnly FiscalYearStart { get; set; } = FiscalYearStart;
    public DateOnly FiscalYearEnd { get; set; } = FiscalYearEnd;
    public string Status { get; set; } = Status;
    public int BlockerCount { get; set; } = BlockerCount;
    public decimal NetIncome { get; set; } = NetIncome;
    public string Currency { get; set; } = Currency;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;
    public long Version { get; set; } = Version;

    public YearEndRunSummaryDto() : this(default !, default !, default !, string.Empty, default !, default !, string.Empty, default !, default !)
    {
    }
}

public sealed record YearEndSubsequentEventDto(Guid Id, DateOnly EventDate, string Title, string Description, decimal? EstimatedAmount, string Currency, string Decision, Guid OwnerUserId, Guid? EvidenceDocumentId, string Status, Guid RecordedByUserId, Guid? ReviewedByUserId, Guid? CorrectionLedgerEntryId, Guid? ReopenRequestId, DateTime RecordedUtc, DateTime UpdatedUtc, DateTime? ResolvedUtc, long Version)
{
    public Guid Id { get; set; } = Id;
    public DateOnly EventDate { get; set; } = EventDate;
    public string Title { get; set; } = Title;
    public string Description { get; set; } = Description;
    public decimal? EstimatedAmount { get; set; } = EstimatedAmount;
    public string Currency { get; set; } = Currency;
    public string Decision { get; set; } = Decision;
    public Guid OwnerUserId { get; set; } = OwnerUserId;
    public Guid? EvidenceDocumentId { get; set; } = EvidenceDocumentId;
    public string Status { get; set; } = Status;
    public Guid RecordedByUserId { get; set; } = RecordedByUserId;
    public Guid? ReviewedByUserId { get; set; } = ReviewedByUserId;
    public Guid? CorrectionLedgerEntryId { get; set; } = CorrectionLedgerEntryId;
    public Guid? ReopenRequestId { get; set; } = ReopenRequestId;
    public DateTime RecordedUtc { get; set; } = RecordedUtc;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;
    public DateTime? ResolvedUtc { get; set; } = ResolvedUtc;
    public long Version { get; set; } = Version;

    public YearEndSubsequentEventDto() : this(default !, default !, string.Empty, string.Empty, default !, string.Empty, string.Empty, default !, default !, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record YearEndRetainedEarningsProposalDto(Guid Id, Guid RetainedEarningsAccountId, string RetainedEarningsAccountCode, Guid OpeningBalanceClearingAccountId, string OpeningBalanceClearingAccountCode, decimal NetIncome, string Currency, string EvidenceHash, string Status, Guid PreparedByUserId, Guid? ReviewedByUserId, DateTime PreparedUtc, DateTime? ReviewedUtc, long Version)
{
    public Guid Id { get; set; } = Id;
    public Guid RetainedEarningsAccountId { get; set; } = RetainedEarningsAccountId;
    public string RetainedEarningsAccountCode { get; set; } = RetainedEarningsAccountCode;
    public Guid OpeningBalanceClearingAccountId { get; set; } = OpeningBalanceClearingAccountId;
    public string OpeningBalanceClearingAccountCode { get; set; } = OpeningBalanceClearingAccountCode;
    public decimal NetIncome { get; set; } = NetIncome;
    public string Currency { get; set; } = Currency;
    public string EvidenceHash { get; set; } = EvidenceHash;
    public string Status { get; set; } = Status;
    public Guid PreparedByUserId { get; set; } = PreparedByUserId;
    public Guid? ReviewedByUserId { get; set; } = ReviewedByUserId;
    public DateTime PreparedUtc { get; set; } = PreparedUtc;
    public DateTime? ReviewedUtc { get; set; } = ReviewedUtc;
    public long Version { get; set; } = Version;

    public YearEndRetainedEarningsProposalDto() : this(default !, default !, string.Empty, default !, string.Empty, default !, string.Empty, string.Empty, string.Empty, default !, default !, default !, default !, default !)
    {
    }
}
