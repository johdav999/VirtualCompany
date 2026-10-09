namespace VirtualCompany.Application.Finance;
public sealed record CurrencyRevaluationRunListDto(IReadOnlyList<CurrencyRevaluationRunDto> Items, int TotalCount, int Skip, int Take)
{
    public IReadOnlyList<CurrencyRevaluationRunDto> Items { get; set; } = Items;
    public int TotalCount { get; set; } = TotalCount;
    public int Skip { get; set; } = Skip;
    public int Take { get; set; } = Take;

    public CurrencyRevaluationRunListDto() : this([], default !, default !, default !)
    {
    }
}

public sealed record CurrencyRevaluationPopulationItemDto(Guid Id, string PopulationKey, string MonetaryClass, Guid FinanceAccountId, string AccountCode, string AccountName, string NormalBalance, string DocumentCurrency, string FunctionalCurrency, decimal DocumentBalance, decimal CarryingFunctionalAmount, decimal RevaluedFunctionalAmount, decimal AdjustmentAmount, Guid? ExchangeRateConversionId, decimal? PeriodEndRate, DateOnly? RateDate, string SourceChecksum, string Status, string? ReviewReason)
{
    public Guid Id { get; set; } = Id;
    public string PopulationKey { get; set; } = PopulationKey;
    public string MonetaryClass { get; set; } = MonetaryClass;
    public Guid FinanceAccountId { get; set; } = FinanceAccountId;
    public string AccountCode { get; set; } = AccountCode;
    public string AccountName { get; set; } = AccountName;
    public string NormalBalance { get; set; } = NormalBalance;
    public string DocumentCurrency { get; set; } = DocumentCurrency;
    public string FunctionalCurrency { get; set; } = FunctionalCurrency;
    public decimal DocumentBalance { get; set; } = DocumentBalance;
    public decimal CarryingFunctionalAmount { get; set; } = CarryingFunctionalAmount;
    public decimal RevaluedFunctionalAmount { get; set; } = RevaluedFunctionalAmount;
    public decimal AdjustmentAmount { get; set; } = AdjustmentAmount;
    public Guid? ExchangeRateConversionId { get; set; } = ExchangeRateConversionId;
    public decimal? PeriodEndRate { get; set; } = PeriodEndRate;
    public DateOnly? RateDate { get; set; } = RateDate;
    public string SourceChecksum { get; set; } = SourceChecksum;
    public string Status { get; set; } = Status;
    public string? ReviewReason { get; set; } = ReviewReason;

    public CurrencyRevaluationPopulationItemDto() : this(default !, "", "", default !, "", "", default !, "", "", default !, default !, default !, default !, default !, default !, default !, "", "", default !)
    {
    }
}

public sealed record CurrencyRevaluationRunDto(Guid Id, Guid CompanyId, Guid FiscalPeriodId, string FiscalPeriodName, int RunNumber, DateOnly AsOfDate, string FunctionalCurrency, string VoucherSeriesCode, string Status, string? FailureReasonCode, string? FailureSummary, string? PopulationChecksum, string? RateSetChecksum, string? ProposalChecksum, int PopulationCount, int IncludedCount, int ExcludedCount, int ReviewCount, decimal DocumentBalanceTotal, decimal CarryingFunctionalTotal, decimal RevaluedFunctionalTotal, decimal ProposedAdjustmentTotal, Guid? ApprovalRequestId, Guid? LedgerEntryId, Guid? ReversalLedgerEntryId, Guid? SupersededByRunId, bool IsScheduled, long Version, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? SubmittedUtc, DateTime? PostedUtc, DateTime? ReversedUtc, IReadOnlyList<CurrencyRevaluationPopulationItemDto> Population, IReadOnlyList<CurrencyRevaluationRateBindingDto> RateBindings, IReadOnlyList<CurrencyRevaluationProposalLineDto> ProposalLines, IReadOnlyList<CurrencyRevaluationReviewDto> Reviews, IReadOnlyList<CurrencyRevaluationReconciliationDto> Reconciliations, CurrencyRevaluationApprovalDto? Approval)
{
    public Guid Id { get; set; } = Id;
    public Guid CompanyId { get; set; } = CompanyId;
    public Guid FiscalPeriodId { get; set; } = FiscalPeriodId;
    public string FiscalPeriodName { get; set; } = FiscalPeriodName;
    public int RunNumber { get; set; } = RunNumber;
    public DateOnly AsOfDate { get; set; } = AsOfDate;
    public string FunctionalCurrency { get; set; } = FunctionalCurrency;
    public string VoucherSeriesCode { get; set; } = VoucherSeriesCode;
    public string Status { get; set; } = Status;
    public string? FailureReasonCode { get; set; } = FailureReasonCode;
    public string? FailureSummary { get; set; } = FailureSummary;
    public string? PopulationChecksum { get; set; } = PopulationChecksum;
    public string? RateSetChecksum { get; set; } = RateSetChecksum;
    public string? ProposalChecksum { get; set; } = ProposalChecksum;
    public int PopulationCount { get; set; } = PopulationCount;
    public int IncludedCount { get; set; } = IncludedCount;
    public int ExcludedCount { get; set; } = ExcludedCount;
    public int ReviewCount { get; set; } = ReviewCount;
    public decimal DocumentBalanceTotal { get; set; } = DocumentBalanceTotal;
    public decimal CarryingFunctionalTotal { get; set; } = CarryingFunctionalTotal;
    public decimal RevaluedFunctionalTotal { get; set; } = RevaluedFunctionalTotal;
    public decimal ProposedAdjustmentTotal { get; set; } = ProposedAdjustmentTotal;
    public Guid? ApprovalRequestId { get; set; } = ApprovalRequestId;
    public Guid? LedgerEntryId { get; set; } = LedgerEntryId;
    public Guid? ReversalLedgerEntryId { get; set; } = ReversalLedgerEntryId;
    public Guid? SupersededByRunId { get; set; } = SupersededByRunId;
    public bool IsScheduled { get; set; } = IsScheduled;
    public long Version { get; set; } = Version;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;
    public DateTime? SubmittedUtc { get; set; } = SubmittedUtc;
    public DateTime? PostedUtc { get; set; } = PostedUtc;
    public DateTime? ReversedUtc { get; set; } = ReversedUtc;
    public IReadOnlyList<CurrencyRevaluationPopulationItemDto> Population { get; set; } = Population;
    public IReadOnlyList<CurrencyRevaluationRateBindingDto> RateBindings { get; set; } = RateBindings;
    public IReadOnlyList<CurrencyRevaluationProposalLineDto> ProposalLines { get; set; } = ProposalLines;
    public IReadOnlyList<CurrencyRevaluationReviewDto> Reviews { get; set; } = Reviews;
    public IReadOnlyList<CurrencyRevaluationReconciliationDto> Reconciliations { get; set; } = Reconciliations;
    public CurrencyRevaluationApprovalDto? Approval { get; set; } = Approval;

    public CurrencyRevaluationRunDto() : this(default !, default !, default !, "", default !, default !, "", "", "", default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, [], [], [], default !, [], default !)
    {
    }
}

public sealed record CurrencyRevaluationRateBindingDto(Guid Id, Guid PopulationItemId, Guid ExchangeRateConversionId, string DocumentCurrency, string FunctionalCurrency, decimal EffectiveRate, DateOnly RateDate, string RateSetIdentity, string ObservationIdentity, string EvidenceChecksum)
{
    public Guid Id { get; set; } = Id;
    public Guid PopulationItemId { get; set; } = PopulationItemId;
    public Guid ExchangeRateConversionId { get; set; } = ExchangeRateConversionId;
    public string DocumentCurrency { get; set; } = DocumentCurrency;
    public string FunctionalCurrency { get; set; } = FunctionalCurrency;
    public decimal EffectiveRate { get; set; } = EffectiveRate;
    public DateOnly RateDate { get; set; } = RateDate;
    public string RateSetIdentity { get; set; } = RateSetIdentity;
    public string ObservationIdentity { get; set; } = ObservationIdentity;
    public string EvidenceChecksum { get; set; } = EvidenceChecksum;

    public CurrencyRevaluationRateBindingDto() : this(default !, default !, default !, default !, default !, default !, default !, "", default !, "")
    {
    }
}

public sealed record CurrencyRevaluationProposalLineDto(Guid Id, int Sequence, Guid FinanceAccountId, Guid? PopulationItemId, string AccountCode, string AccountName, string LineType, decimal DebitAmount, decimal CreditAmount, string Currency, string Description)
{
    public Guid Id { get; set; } = Id;
    public int Sequence { get; set; } = Sequence;
    public Guid FinanceAccountId { get; set; } = FinanceAccountId;
    public Guid? PopulationItemId { get; set; } = PopulationItemId;
    public string AccountCode { get; set; } = AccountCode;
    public string AccountName { get; set; } = AccountName;
    public string LineType { get; set; } = LineType;
    public decimal DebitAmount { get; set; } = DebitAmount;
    public decimal CreditAmount { get; set; } = CreditAmount;
    public string Currency { get; set; } = Currency;
    public string Description { get; set; } = Description;

    public CurrencyRevaluationProposalLineDto() : this(default !, default !, default !, default !, "", "", default !, default !, default !, "", "")
    {
    }
}

public sealed record CurrencyRevaluationScheduleDto(Guid Id, Guid CompanyId, bool IsEnabled, int DaysBeforePeriodEnd, bool AutomaticReversal, string VoucherSeriesCode, long Version, DateTime UpdatedUtc, DateTime? LastEvaluatedUtc)
{
    public Guid Id { get; set; } = Id;
    public Guid CompanyId { get; set; } = CompanyId;
    public bool IsEnabled { get; set; } = IsEnabled;
    public int DaysBeforePeriodEnd { get; set; } = DaysBeforePeriodEnd;
    public bool AutomaticReversal { get; set; } = AutomaticReversal;
    public string VoucherSeriesCode { get; set; } = VoucherSeriesCode;
    public long Version { get; set; } = Version;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;
    public DateTime? LastEvaluatedUtc { get; set; } = LastEvaluatedUtc;

    public CurrencyRevaluationScheduleDto() : this(default !, default !, default !, default !, default !, "", default !, default !, default !)
    {
    }
}

public sealed record CurrencyRevaluationApprovalDto(Guid Id, string Status, string? DecisionSummary, DateTime CreatedUtc, DateTime? DecidedUtc)
{
    public Guid Id { get; set; } = Id;
    public string Status { get; set; } = Status;
    public string? DecisionSummary { get; set; } = DecisionSummary;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime? DecidedUtc { get; set; } = DecidedUtc;

    public CurrencyRevaluationApprovalDto() : this(default !, "", default !, default !, default !)
    {
    }
}

public sealed record CurrencyRevaluationReconciliationDto(Guid Id, string ReconciliationType, int PopulationCount, decimal CarryingAmount, decimal RevaluedAmount, decimal ProposedAdjustment, decimal ProposalLineAdjustment, decimal Difference, string Currency, string Checksum, bool IsReconciled)
{
    public Guid Id { get; set; } = Id;
    public string ReconciliationType { get; set; } = ReconciliationType;
    public int PopulationCount { get; set; } = PopulationCount;
    public decimal CarryingAmount { get; set; } = CarryingAmount;
    public decimal RevaluedAmount { get; set; } = RevaluedAmount;
    public decimal ProposedAdjustment { get; set; } = ProposedAdjustment;
    public decimal ProposalLineAdjustment { get; set; } = ProposalLineAdjustment;
    public decimal Difference { get; set; } = Difference;
    public string Currency { get; set; } = Currency;
    public string Checksum { get; set; } = Checksum;
    public bool IsReconciled { get; set; } = IsReconciled;

    public CurrencyRevaluationReconciliationDto() : this(default !, "", default !, default !, default !, default !, default !, default !, "", default !, default !)
    {
    }
}

public sealed record CurrencyRevaluationReviewDto(Guid Id, Guid? PopulationItemId, string Action, string Reason, Guid ActorUserId, Guid? ApprovalRequestId, string EvidenceChecksum, DateTime OccurredUtc);
