namespace VirtualCompany.Application.Finance;
public static class TreasuryWorkspaceEvidenceStates
{
    public const string Current = "current";
    public const string Stale = "stale";
    public const string Missing = "missing";
}

public sealed record TreasuryPaymentWorkItemDto(Guid BatchId, Guid? ExecutionId, string Reference, string Status, string Severity, decimal Amount, string Currency, string Explanation, DateTime UpdatedUtc, TreasuryWorkspaceActionDecisionDto ReviewAction, TreasuryWorkspaceActionDecisionDto CancelAction)
{
    public Guid BatchId { get; set; } = BatchId;
    public Guid? ExecutionId { get; set; } = ExecutionId;
    public string Reference { get; set; } = Reference;
    public string Status { get; set; } = Status;
    public string Severity { get; set; } = Severity;
    public decimal Amount { get; set; } = Amount;
    public string Currency { get; set; } = Currency;
    public string Explanation { get; set; } = Explanation;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;
    public TreasuryWorkspaceActionDecisionDto ReviewAction { get; set; } = ReviewAction;
    public TreasuryWorkspaceActionDecisionDto CancelAction { get; set; } = CancelAction;

    public TreasuryPaymentWorkItemDto() : this(default !, default !, string.Empty, string.Empty, string.Empty, default !, "SEK", string.Empty, default !, new(), new())
    {
    }
}

public sealed record TreasuryAccountCoverageDto(Guid ConnectionId, Guid CompanyBankAccountId, Guid? CheckpointId, string InstitutionName, string AccountName, string MaskedAccountNumber, decimal? Balance, string Currency, string EvidenceState, string EvidenceSource, DateTime? EvidenceUtc, DateOnly? CoverageFrom, DateOnly? CoverageThrough, int? LagMinutes, string ConnectionStatus, string FeedStatus, string? ReasonCode, string Explanation, IReadOnlyList<TreasuryWorkspaceActionDecisionDto> AllowedActions)
{
    public Guid ConnectionId { get; set; } = ConnectionId;
    public Guid CompanyBankAccountId { get; set; } = CompanyBankAccountId;
    public Guid? CheckpointId { get; set; } = CheckpointId;
    public string InstitutionName { get; set; } = InstitutionName;
    public string AccountName { get; set; } = AccountName;
    public string MaskedAccountNumber { get; set; } = MaskedAccountNumber;
    public decimal? Balance { get; set; } = Balance;
    public string Currency { get; set; } = Currency;
    public string EvidenceState { get; set; } = EvidenceState;
    public string EvidenceSource { get; set; } = EvidenceSource;
    public DateTime? EvidenceUtc { get; set; } = EvidenceUtc;
    public DateOnly? CoverageFrom { get; set; } = CoverageFrom;
    public DateOnly? CoverageThrough { get; set; } = CoverageThrough;
    public int? LagMinutes { get; set; } = LagMinutes;
    public string ConnectionStatus { get; set; } = ConnectionStatus;
    public string FeedStatus { get; set; } = FeedStatus;
    public string? ReasonCode { get; set; } = ReasonCode;
    public string Explanation { get; set; } = Explanation;
    public IReadOnlyList<TreasuryWorkspaceActionDecisionDto> AllowedActions { get; set; } = AllowedActions;

    public TreasuryAccountCoverageDto() : this(default !, default !, default !, string.Empty, string.Empty, string.Empty, default !, "SEK", VirtualCompany.Application.Finance.TreasuryWorkspaceEvidenceStates.Missing, string.Empty, default !, default !, default !, default !, string.Empty, string.Empty, default !, string.Empty, [])
    {
    }
}

public sealed record TreasuryWorkspaceExceptionDto(string Id, string Kind, string Severity, string Title, string Explanation, decimal? Amount, string? Currency, DateTime ObservedUtc, int PriorityScore, TreasuryWorkspaceActionDecisionDto Action)
{
    public string Id { get; set; } = Id;
    public string Kind { get; set; } = Kind;
    public string Severity { get; set; } = Severity;
    public string Title { get; set; } = Title;
    public string Explanation { get; set; } = Explanation;
    public decimal? Amount { get; set; } = Amount;
    public string? Currency { get; set; } = Currency;
    public DateTime ObservedUtc { get; set; } = ObservedUtc;
    public int PriorityScore { get; set; } = PriorityScore;
    public TreasuryWorkspaceActionDecisionDto Action { get; set; } = Action;

    public TreasuryWorkspaceExceptionDto() : this(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, default !, default !, default !, default !, new())
    {
    }
}

public sealed record TreasuryProjectionPointDto(DateOnly Date, decimal ProjectedCash, string EvidenceBasis)
{
    public DateOnly Date { get; set; } = Date;
    public decimal ProjectedCash { get; set; } = ProjectedCash;
    public string EvidenceBasis { get; set; } = EvidenceBasis;

    public TreasuryProjectionPointDto() : this(default !, default !, string.Empty)
    {
    }
}

public sealed record TreasuryUnreconciledItemDto(Guid BankTransactionId, Guid CompanyBankAccountId, string AccountName, DateTime BookingDateUtc, int AgeDays, decimal Amount, decimal RemainingAmount, string Currency, string Counterparty, string ReferenceText, string Status, TreasuryWorkspaceActionDecisionDto Action)
{
    public Guid BankTransactionId { get; set; } = BankTransactionId;
    public Guid CompanyBankAccountId { get; set; } = CompanyBankAccountId;
    public string AccountName { get; set; } = AccountName;
    public DateTime BookingDateUtc { get; set; } = BookingDateUtc;
    public int AgeDays { get; set; } = AgeDays;
    public decimal Amount { get; set; } = Amount;
    public decimal RemainingAmount { get; set; } = RemainingAmount;
    public string Currency { get; set; } = Currency;
    public string Counterparty { get; set; } = Counterparty;
    public string ReferenceText { get; set; } = ReferenceText;
    public string Status { get; set; } = Status;
    public TreasuryWorkspaceActionDecisionDto Action { get; set; } = Action;

    public TreasuryUnreconciledItemDto() : this(default !, default !, string.Empty, default !, default !, default !, default !, "SEK", string.Empty, string.Empty, string.Empty, new())
    {
    }
}

public sealed record TreasuryEvidenceReferenceDto(string SourceId, string SourceType, string Label, DateTime? ObservedUtc, string NavigationTarget)
{
    public string SourceId { get; set; } = SourceId;
    public string SourceType { get; set; } = SourceType;
    public string Label { get; set; } = Label;
    public DateTime? ObservedUtc { get; set; } = ObservedUtc;
    public string NavigationTarget { get; set; } = NavigationTarget;

    public TreasuryEvidenceReferenceDto() : this(string.Empty, string.Empty, string.Empty, default !, string.Empty)
    {
    }
}

public sealed record TreasuryWorkspaceTaskDto(Guid TaskId, string Title, string? Description, string Priority, string Status, DateTime? DueUtc, string Owner, string NavigationTarget)
{
    public Guid TaskId { get; set; } = TaskId;
    public string Title { get; set; } = Title;
    public string? Description { get; set; } = Description;
    public string Priority { get; set; } = Priority;
    public string Status { get; set; } = Status;
    public DateTime? DueUtc { get; set; } = DueUtc;
    public string Owner { get; set; } = Owner;
    public string NavigationTarget { get; set; } = NavigationTarget;

    public TreasuryWorkspaceTaskDto() : this(default !, string.Empty, default !, string.Empty, string.Empty, default !, string.Empty, string.Empty)
    {
    }
}

public sealed record TreasuryWorkspaceDto(Guid CompanyId, DateTime AsOfUtc, DateTime? FreshestEvidenceUtc, DateTime? StalestEvidenceUtc, bool HasStaleEvidence, bool HasMissingEvidence, TreasuryLiquiditySummaryDto Liquidity, IReadOnlyList<TreasuryAccountCoverageDto> Accounts, TreasuryReconciliationSummaryDto Reconciliation, TreasuryPaymentWorkSummaryDto PaymentWork, IReadOnlyList<TreasuryWorkspaceExceptionDto> Exceptions, IReadOnlyList<TreasuryWorkspaceTaskDto> Tasks, TreasuryLauraRecommendationDto Laura, IReadOnlyList<TreasuryWorkspaceActionDecisionDto> AllowedActions, bool IsTruncated)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public DateTime AsOfUtc { get; set; } = AsOfUtc;
    public DateTime? FreshestEvidenceUtc { get; set; } = FreshestEvidenceUtc;
    public DateTime? StalestEvidenceUtc { get; set; } = StalestEvidenceUtc;
    public bool HasStaleEvidence { get; set; } = HasStaleEvidence;
    public bool HasMissingEvidence { get; set; } = HasMissingEvidence;
    public TreasuryLiquiditySummaryDto Liquidity { get; set; } = Liquidity;
    public IReadOnlyList<TreasuryAccountCoverageDto> Accounts { get; set; } = Accounts;
    public TreasuryReconciliationSummaryDto Reconciliation { get; set; } = Reconciliation;
    public TreasuryPaymentWorkSummaryDto PaymentWork { get; set; } = PaymentWork;
    public IReadOnlyList<TreasuryWorkspaceExceptionDto> Exceptions { get; set; } = Exceptions;
    public IReadOnlyList<TreasuryWorkspaceTaskDto> Tasks { get; set; } = Tasks;
    public TreasuryLauraRecommendationDto Laura { get; set; } = Laura;
    public IReadOnlyList<TreasuryWorkspaceActionDecisionDto> AllowedActions { get; set; } = AllowedActions;
    public bool IsTruncated { get; set; } = IsTruncated;

    public TreasuryWorkspaceDto() : this(default !, default !, default !, default !, default !, default !, new(), [], new(), new(), [], [], new(), [], default !)
    {
    }
}

public static class TreasuryWorkspaceReasonCodes
{
    public const string Allowed = "treasury_action_allowed";
    public const string FinanceEditRequired = "treasury_finance_edit_required";
    public const string FinanceApprovalRequired = "treasury_finance_approval_required";
    public const string ConnectionCurrent = "treasury_connection_current";
    public const string ConnectionRecoveryRequired = "treasury_connection_recovery_required";
    public const string FeedGapOpen = "treasury_feed_gap_open";
    public const string FeedGapUnavailable = "treasury_feed_gap_unavailable";
    public const string ReconciliationRequired = "treasury_reconciliation_required";
    public const string ReconciliationComplete = "treasury_reconciliation_complete";
    public const string PaymentReviewAvailable = "treasury_payment_review_available";
    public const string PaymentUnavailable = "treasury_payment_unavailable";
    public const string PaymentCancellationAllowed = "treasury_payment_cancellation_allowed";
    public const string PaymentCancellationUnsafe = "treasury_payment_cancellation_unsafe";
    public const string LiquidityInvestigationRequired = "treasury_liquidity_investigation_required";
    public const string LiquidityHealthy = "treasury_liquidity_healthy";
}

public sealed record TreasuryPaymentWorkSummaryDto(int Approved, int Queued, int AwaitingAuthorization, int Processing, int Rejected, int ReconciliationRequired, int Settled, IReadOnlyList<TreasuryPaymentWorkItemDto> Items)
{
    public int Approved { get; set; } = Approved;
    public int Queued { get; set; } = Queued;
    public int AwaitingAuthorization { get; set; } = AwaitingAuthorization;
    public int Processing { get; set; } = Processing;
    public int Rejected { get; set; } = Rejected;
    public int ReconciliationRequired { get; set; } = ReconciliationRequired;
    public int Settled { get; set; } = Settled;
    public IReadOnlyList<TreasuryPaymentWorkItemDto> Items { get; set; } = Items;

    public TreasuryPaymentWorkSummaryDto() : this(default !, default !, default !, default !, default !, default !, default !, [])
    {
    }
}

public static class TreasuryWorkspaceActionTypes
{
    public const string Reconnect = "reconnect";
    public const string RecoverGap = "recover_gap";
    public const string Reconcile = "reconcile";
    public const string ReviewPayment = "review_payment";
    public const string CancelPayment = "cancel_payment";
    public const string InvestigateLiquidity = "investigate_liquidity";
}

public sealed record TreasuryReconciliationSummaryDto(int TotalUnreconciled, int AgedUnreconciled, int? OldestAgeDays, IReadOnlyList<TreasuryUnreconciledItemDto> Items)
{
    public int TotalUnreconciled { get; set; } = TotalUnreconciled;
    public int AgedUnreconciled { get; set; } = AgedUnreconciled;
    public int? OldestAgeDays { get; set; } = OldestAgeDays;
    public IReadOnlyList<TreasuryUnreconciledItemDto> Items { get; set; } = Items;

    public TreasuryReconciliationSummaryDto() : this(default !, default !, default !, [])
    {
    }
}

public sealed record TreasuryLiquiditySummaryDto(decimal AvailableCash, decimal ProjectedCash, decimal ExpectedInflows, decimal ExpectedOutflows, string Currency, int HorizonDays, DateTime ProjectionThroughUtc, string RiskLevel, int? EstimatedRunwayDays, decimal? WarningCashAmount, decimal? CriticalCashAmount, int WarningRunwayDays, int CriticalRunwayDays, IReadOnlyList<TreasuryProjectionPointDto> Projection)
{
    public decimal AvailableCash { get; set; } = AvailableCash;
    public decimal ProjectedCash { get; set; } = ProjectedCash;
    public decimal ExpectedInflows { get; set; } = ExpectedInflows;
    public decimal ExpectedOutflows { get; set; } = ExpectedOutflows;
    public string Currency { get; set; } = Currency;
    public int HorizonDays { get; set; } = HorizonDays;
    public DateTime ProjectionThroughUtc { get; set; } = ProjectionThroughUtc;
    public string RiskLevel { get; set; } = RiskLevel;
    public int? EstimatedRunwayDays { get; set; } = EstimatedRunwayDays;
    public decimal? WarningCashAmount { get; set; } = WarningCashAmount;
    public decimal? CriticalCashAmount { get; set; } = CriticalCashAmount;
    public int WarningRunwayDays { get; set; } = WarningRunwayDays;
    public int CriticalRunwayDays { get; set; } = CriticalRunwayDays;
    public IReadOnlyList<TreasuryProjectionPointDto> Projection { get; set; } = Projection;

    public TreasuryLiquiditySummaryDto() : this(default !, default !, default !, default !, "SEK", default !, default !, "missing", default !, default !, default !, default !, default !, [])
    {
    }
}

public sealed record TreasuryLauraRecommendationDto(Guid? AgentId, string AgentName, string RoleName, string? AvatarUrl, string Mode, string Summary, IReadOnlyList<TreasuryEvidenceReferenceDto> Citations, IReadOnlyList<string> MissingEvidence, bool RequiresReview, string NavigationTarget)
{
    public Guid? AgentId { get; set; } = AgentId;
    public string AgentName { get; set; } = AgentName;
    public string RoleName { get; set; } = RoleName;
    public string? AvatarUrl { get; set; } = AvatarUrl;
    public string Mode { get; set; } = Mode;
    public string Summary { get; set; } = Summary;
    public IReadOnlyList<TreasuryEvidenceReferenceDto> Citations { get; set; } = Citations;
    public IReadOnlyList<string> MissingEvidence { get; set; } = MissingEvidence;
    public bool RequiresReview { get; set; } = RequiresReview;
    public string NavigationTarget { get; set; } = NavigationTarget;

    public TreasuryLauraRecommendationDto() : this(default !, "Laura", string.Empty, default !, "recommend_only", string.Empty, [], [], default !, string.Empty)
    {
    }
}

public sealed record TreasuryWorkspaceActionDecisionDto(string Action, bool IsAllowed, string ReasonCode, string Explanation, bool RequiresApproval, string? NavigationTarget = null)
{
    public string Action { get; set; } = Action;
    public bool IsAllowed { get; set; } = IsAllowed;
    public string ReasonCode { get; set; } = ReasonCode;
    public string Explanation { get; set; } = Explanation;
    public bool RequiresApproval { get; set; } = RequiresApproval;
    public string? NavigationTarget { get; set; } = NavigationTarget;

    public TreasuryWorkspaceActionDecisionDto() : this(string.Empty, default !, string.Empty, string.Empty, default !, default !)
    {
    }
}
