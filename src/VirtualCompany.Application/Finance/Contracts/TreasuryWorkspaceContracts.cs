namespace VirtualCompany.Application.Finance;

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

public static class TreasuryWorkspaceEvidenceStates
{
    public const string Current = "current";
    public const string Stale = "stale";
    public const string Missing = "missing";
}

public static class TreasuryWorkspaceSeverity
{
    public const string Critical = "critical";
    public const string High = "high";
    public const string Medium = "medium";
    public const string Low = "low";
    public const string Info = "info";
}

public sealed record GetTreasuryWorkspaceQuery(
    Guid CompanyId,
    DateTime? AsOfUtc = null,
    int HorizonDays = 14,
    int ExceptionLimit = 12,
    int TaskLimit = 8,
    bool CanEdit = false,
    bool CanApprove = false);

public sealed record TreasuryWorkspacePolicyInput(
    bool CanEdit,
    bool CanApprove,
    string? ConnectionStatus = null,
    string? ConnectionReasonCode = null,
    bool HasOpenGap = false,
    string? ReconciliationStatus = null,
    string? PaymentStatus = null,
    bool PaymentCanCancel = false,
    string? LiquidityRisk = null);

public sealed record TreasuryWorkspaceActionDecisionDto(
    string Action,
    bool IsAllowed,
    string ReasonCode,
    string Explanation,
    bool RequiresApproval,
    string? NavigationTarget = null);

public interface ITreasuryWorkspacePolicy
{
    IReadOnlyList<TreasuryWorkspaceActionDecisionDto> Evaluate(TreasuryWorkspacePolicyInput input);
}

public sealed record TreasuryLiquiditySummaryDto(
    decimal AvailableCash,
    decimal ProjectedCash,
    decimal ExpectedInflows,
    decimal ExpectedOutflows,
    string Currency,
    int HorizonDays,
    DateTime ProjectionThroughUtc,
    string RiskLevel,
    int? EstimatedRunwayDays,
    decimal? WarningCashAmount,
    decimal? CriticalCashAmount,
    int WarningRunwayDays,
    int CriticalRunwayDays,
    IReadOnlyList<TreasuryProjectionPointDto> Projection);

public sealed record TreasuryProjectionPointDto(
    DateOnly Date,
    decimal ProjectedCash,
    string EvidenceBasis);

public sealed record TreasuryAccountCoverageDto(
    Guid ConnectionId,
    Guid CompanyBankAccountId,
    Guid? CheckpointId,
    string InstitutionName,
    string AccountName,
    string MaskedAccountNumber,
    decimal? Balance,
    string Currency,
    string EvidenceState,
    string EvidenceSource,
    DateTime? EvidenceUtc,
    DateOnly? CoverageFrom,
    DateOnly? CoverageThrough,
    int? LagMinutes,
    string ConnectionStatus,
    string FeedStatus,
    string? ReasonCode,
    string Explanation,
    IReadOnlyList<TreasuryWorkspaceActionDecisionDto> AllowedActions);

public sealed record TreasuryReconciliationSummaryDto(
    int TotalUnreconciled,
    int AgedUnreconciled,
    int? OldestAgeDays,
    IReadOnlyList<TreasuryUnreconciledItemDto> Items);

public sealed record TreasuryUnreconciledItemDto(
    Guid BankTransactionId,
    Guid CompanyBankAccountId,
    string AccountName,
    DateTime BookingDateUtc,
    int AgeDays,
    decimal Amount,
    decimal RemainingAmount,
    string Currency,
    string Counterparty,
    string ReferenceText,
    string Status,
    TreasuryWorkspaceActionDecisionDto Action);

public sealed record TreasuryPaymentWorkSummaryDto(
    int Approved,
    int Queued,
    int AwaitingAuthorization,
    int Processing,
    int Rejected,
    int ReconciliationRequired,
    int Settled,
    IReadOnlyList<TreasuryPaymentWorkItemDto> Items);

public sealed record TreasuryPaymentWorkItemDto(
    Guid BatchId,
    Guid? ExecutionId,
    string Reference,
    string Status,
    string Severity,
    decimal Amount,
    string Currency,
    string Explanation,
    DateTime UpdatedUtc,
    TreasuryWorkspaceActionDecisionDto ReviewAction,
    TreasuryWorkspaceActionDecisionDto CancelAction);

public sealed record TreasuryWorkspaceTaskDto(
    Guid TaskId,
    string Title,
    string? Description,
    string Priority,
    string Status,
    DateTime? DueUtc,
    string Owner,
    string NavigationTarget);

public sealed record TreasuryWorkspaceExceptionDto(
    string Id,
    string Kind,
    string Severity,
    string Title,
    string Explanation,
    decimal? Amount,
    string? Currency,
    DateTime ObservedUtc,
    int PriorityScore,
    TreasuryWorkspaceActionDecisionDto Action);

public sealed record TreasuryEvidenceReferenceDto(
    string SourceId,
    string SourceType,
    string Label,
    DateTime? ObservedUtc,
    string NavigationTarget);

public sealed record TreasuryLauraRecommendationDto(
    Guid? AgentId,
    string AgentName,
    string RoleName,
    string? AvatarUrl,
    string Mode,
    string Summary,
    IReadOnlyList<TreasuryEvidenceReferenceDto> Citations,
    IReadOnlyList<string> MissingEvidence,
    bool RequiresReview,
    string NavigationTarget);

public sealed record TreasuryWorkspaceDto(
    Guid CompanyId,
    DateTime AsOfUtc,
    DateTime? FreshestEvidenceUtc,
    DateTime? StalestEvidenceUtc,
    bool HasStaleEvidence,
    bool HasMissingEvidence,
    TreasuryLiquiditySummaryDto Liquidity,
    IReadOnlyList<TreasuryAccountCoverageDto> Accounts,
    TreasuryReconciliationSummaryDto Reconciliation,
    TreasuryPaymentWorkSummaryDto PaymentWork,
    IReadOnlyList<TreasuryWorkspaceExceptionDto> Exceptions,
    IReadOnlyList<TreasuryWorkspaceTaskDto> Tasks,
    TreasuryLauraRecommendationDto Laura,
    IReadOnlyList<TreasuryWorkspaceActionDecisionDto> AllowedActions,
    bool IsTruncated);

public interface ITreasuryWorkspaceQueryService
{
    Task<TreasuryWorkspaceDto> GetAsync(
        GetTreasuryWorkspaceQuery query,
        CancellationToken cancellationToken);
}
