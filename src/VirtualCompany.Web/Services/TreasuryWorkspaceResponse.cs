namespace VirtualCompany.Web.Services;

public static class TreasuryWorkspaceActionTypes
{
    public const string Reconnect = "reconnect";
    public const string RecoverGap = "recover_gap";
    public const string Reconcile = "reconcile";
    public const string ReviewPayment = "review_payment";
    public const string CancelPayment = "cancel_payment";
    public const string InvestigateLiquidity = "investigate_liquidity";
}

public static class TreasuryWorkspaceReasonCodes
{
    public const string FinanceEditRequired = "treasury_finance_edit_required";
    public const string ConnectionRecoveryRequired = "treasury_connection_recovery_required";
    public const string FeedGapOpen = "treasury_feed_gap_open";
}

public static class TreasuryWorkspaceEvidenceStates
{
    public const string Current = "current";
    public const string Stale = "stale";
    public const string Missing = "missing";
}

public sealed class TreasuryWorkspaceResponse
{
    public Guid CompanyId { get; set; }
    public DateTime AsOfUtc { get; set; }
    public DateTime? FreshestEvidenceUtc { get; set; }
    public DateTime? StalestEvidenceUtc { get; set; }
    public bool HasStaleEvidence { get; set; }
    public bool HasMissingEvidence { get; set; }
    public TreasuryLiquiditySummaryResponse Liquidity { get; set; } = new();
    public List<TreasuryAccountCoverageResponse> Accounts { get; set; } = [];
    public TreasuryReconciliationSummaryResponse Reconciliation { get; set; } = new();
    public TreasuryPaymentWorkSummaryResponse PaymentWork { get; set; } = new();
    public List<TreasuryWorkspaceExceptionResponse> Exceptions { get; set; } = [];
    public List<TreasuryWorkspaceTaskResponse> Tasks { get; set; } = [];
    public TreasuryLauraRecommendationResponse Laura { get; set; } = new();
    public List<TreasuryWorkspaceActionDecisionResponse> AllowedActions { get; set; } = [];
    public bool IsTruncated { get; set; }
}

public sealed class TreasuryLiquiditySummaryResponse
{
    public decimal AvailableCash { get; set; }
    public decimal ProjectedCash { get; set; }
    public decimal ExpectedInflows { get; set; }
    public decimal ExpectedOutflows { get; set; }
    public string Currency { get; set; } = "SEK";
    public int HorizonDays { get; set; }
    public DateTime ProjectionThroughUtc { get; set; }
    public string RiskLevel { get; set; } = "missing";
    public int? EstimatedRunwayDays { get; set; }
    public decimal? WarningCashAmount { get; set; }
    public decimal? CriticalCashAmount { get; set; }
    public int WarningRunwayDays { get; set; }
    public int CriticalRunwayDays { get; set; }
    public List<TreasuryProjectionPointResponse> Projection { get; set; } = [];
}

public sealed class TreasuryProjectionPointResponse
{
    public DateOnly Date { get; set; }
    public decimal ProjectedCash { get; set; }
    public string EvidenceBasis { get; set; } = string.Empty;
}

public sealed class TreasuryAccountCoverageResponse
{
    public Guid ConnectionId { get; set; }
    public Guid CompanyBankAccountId { get; set; }
    public Guid? CheckpointId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string MaskedAccountNumber { get; set; } = string.Empty;
    public decimal? Balance { get; set; }
    public string Currency { get; set; } = "SEK";
    public string EvidenceState { get; set; } = TreasuryWorkspaceEvidenceStates.Missing;
    public string EvidenceSource { get; set; } = string.Empty;
    public DateTime? EvidenceUtc { get; set; }
    public DateOnly? CoverageFrom { get; set; }
    public DateOnly? CoverageThrough { get; set; }
    public int? LagMinutes { get; set; }
    public string ConnectionStatus { get; set; } = string.Empty;
    public string FeedStatus { get; set; } = string.Empty;
    public string? ReasonCode { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public List<TreasuryWorkspaceActionDecisionResponse> AllowedActions { get; set; } = [];
}

public sealed class TreasuryReconciliationSummaryResponse
{
    public int TotalUnreconciled { get; set; }
    public int AgedUnreconciled { get; set; }
    public int? OldestAgeDays { get; set; }
    public List<TreasuryUnreconciledItemResponse> Items { get; set; } = [];
}

public sealed class TreasuryUnreconciledItemResponse
{
    public Guid BankTransactionId { get; set; }
    public Guid CompanyBankAccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public DateTime BookingDateUtc { get; set; }
    public int AgeDays { get; set; }
    public decimal Amount { get; set; }
    public decimal RemainingAmount { get; set; }
    public string Currency { get; set; } = "SEK";
    public string Counterparty { get; set; } = string.Empty;
    public string ReferenceText { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public TreasuryWorkspaceActionDecisionResponse Action { get; set; } = new();
}

public sealed class TreasuryPaymentWorkSummaryResponse
{
    public int Approved { get; set; }
    public int Queued { get; set; }
    public int AwaitingAuthorization { get; set; }
    public int Processing { get; set; }
    public int Rejected { get; set; }
    public int ReconciliationRequired { get; set; }
    public int Settled { get; set; }
    public List<TreasuryPaymentWorkItemResponse> Items { get; set; } = [];
}

public sealed class TreasuryPaymentWorkItemResponse
{
    public Guid BatchId { get; set; }
    public Guid? ExecutionId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "SEK";
    public string Explanation { get; set; } = string.Empty;
    public DateTime UpdatedUtc { get; set; }
    public TreasuryWorkspaceActionDecisionResponse ReviewAction { get; set; } = new();
    public TreasuryWorkspaceActionDecisionResponse CancelAction { get; set; } = new();
}

public sealed class TreasuryWorkspaceExceptionResponse
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTime ObservedUtc { get; set; }
    public int PriorityScore { get; set; }
    public TreasuryWorkspaceActionDecisionResponse Action { get; set; } = new();
}

public sealed class TreasuryWorkspaceTaskResponse
{
    public Guid TaskId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? DueUtc { get; set; }
    public string Owner { get; set; } = string.Empty;
    public string NavigationTarget { get; set; } = string.Empty;
}

public sealed class TreasuryLauraRecommendationResponse
{
    public Guid? AgentId { get; set; }
    public string AgentName { get; set; } = "Laura";
    public string RoleName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string Mode { get; set; } = "recommend_only";
    public string Summary { get; set; } = string.Empty;
    public List<TreasuryEvidenceReferenceResponse> Citations { get; set; } = [];
    public List<string> MissingEvidence { get; set; } = [];
    public bool RequiresReview { get; set; }
    public string NavigationTarget { get; set; } = string.Empty;
}

public sealed class TreasuryEvidenceReferenceResponse
{
    public string SourceId { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public DateTime? ObservedUtc { get; set; }
    public string NavigationTarget { get; set; } = string.Empty;
}

public sealed class TreasuryWorkspaceActionDecisionResponse
{
    public string Action { get; set; } = string.Empty;
    public bool IsAllowed { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public bool RequiresApproval { get; set; }
    public string? NavigationTarget { get; set; }
}
