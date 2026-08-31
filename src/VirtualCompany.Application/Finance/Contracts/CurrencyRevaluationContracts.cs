namespace VirtualCompany.Application.Finance;

public static class CurrencyRevaluationReasonCodes
{
    public const string NotFound = "currency_revaluation_not_found";
    public const string PeriodNotOpen = "currency_revaluation_period_not_open";
    public const string PeriodDateMismatch = "currency_revaluation_period_date_mismatch";
    public const string MissingMonetaryAccounts = "currency_revaluation_monetary_accounts_missing";
    public const string MissingGainLossAccounts = "currency_revaluation_gain_loss_accounts_missing";
    public const string MissingRate = "currency_revaluation_rate_missing";
    public const string ReviewRequired = "currency_revaluation_review_required";
    public const string ApprovalRequired = "currency_revaluation_approval_required";
    public const string ApprovalPending = "currency_revaluation_approval_pending";
    public const string ApprovalRejected = "currency_revaluation_approval_rejected";
    public const string ApprovalStale = "currency_revaluation_approval_stale";
    public const string ProposalStale = "currency_revaluation_proposal_stale";
    public const string AlreadyPosted = "currency_revaluation_already_posted";
    public const string AlreadyReversed = "currency_revaluation_already_reversed";
    public const string NextPeriodMissing = "currency_revaluation_next_period_missing";
    public const string IdempotencyConflict = "currency_revaluation_idempotency_conflict";
    public const string VersionConflict = "currency_revaluation_version_conflict";
    public const string ReconciliationFailed = "currency_revaluation_reconciliation_failed";
}

public sealed record ListCurrencyRevaluationRunsQuery(Guid CompanyId, Guid? FiscalPeriodId = null, int Skip = 0, int Take = 50);
public sealed record GetCurrencyRevaluationRunQuery(Guid CompanyId, Guid RunId);
public sealed record PreviewCurrencyRevaluationCommand(Guid CompanyId, Guid FiscalPeriodId, string VoucherSeriesCode,
    string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null, bool Scheduled = false);
public sealed record ReviewCurrencyRevaluationItemCommand(Guid CompanyId, Guid RunId, Guid PopulationItemId,
    string Action, string Reason, long ExpectedVersion, Guid ActorUserId, string? CorrelationId = null);
public sealed record SubmitCurrencyRevaluationCommand(Guid CompanyId, Guid RunId, long ExpectedVersion,
    Guid ActorUserId, string? CorrelationId = null);
public sealed record PostCurrencyRevaluationCommand(Guid CompanyId, Guid RunId, long ExpectedVersion,
    string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record ReverseCurrencyRevaluationCommand(Guid CompanyId, Guid RunId, long ExpectedVersion,
    string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record ConfigureCurrencyRevaluationAccountCommand(Guid CompanyId, Guid FinanceAccountId,
    string MonetaryClass, bool IsEnabled, long? ExpectedVersion, Guid ActorUserId, string? CorrelationId = null);
public sealed record ConfigureCurrencyRevaluationScheduleCommand(Guid CompanyId, bool IsEnabled,
    int DaysBeforePeriodEnd, bool AutomaticReversal, string VoucherSeriesCode, long? ExpectedVersion,
    Guid ActorUserId, string? CorrelationId = null);

public sealed record CurrencyRevaluationPopulationItemDto(Guid Id, string PopulationKey, string MonetaryClass,
    Guid FinanceAccountId, string AccountCode, string AccountName, string NormalBalance, string DocumentCurrency,
    string FunctionalCurrency, decimal DocumentBalance, decimal CarryingFunctionalAmount,
    decimal RevaluedFunctionalAmount, decimal AdjustmentAmount, Guid? ExchangeRateConversionId,
    decimal? PeriodEndRate, DateOnly? RateDate, string SourceChecksum, string Status, string? ReviewReason);
public sealed record CurrencyRevaluationRateBindingDto(Guid Id, Guid PopulationItemId, Guid ExchangeRateConversionId,
    string DocumentCurrency, string FunctionalCurrency, decimal EffectiveRate, DateOnly RateDate,
    string RateSetIdentity, string ObservationIdentity, string EvidenceChecksum);
public sealed record CurrencyRevaluationProposalLineDto(Guid Id, int Sequence, Guid FinanceAccountId,
    Guid? PopulationItemId, string AccountCode, string AccountName, string LineType, decimal DebitAmount,
    decimal CreditAmount, string Currency, string Description);
public sealed record CurrencyRevaluationReviewDto(Guid Id, Guid? PopulationItemId, string Action, string Reason,
    Guid ActorUserId, Guid? ApprovalRequestId, string EvidenceChecksum, DateTime OccurredUtc);
public sealed record CurrencyRevaluationReconciliationDto(Guid Id, string ReconciliationType, int PopulationCount,
    decimal CarryingAmount, decimal RevaluedAmount, decimal ProposedAdjustment, decimal ProposalLineAdjustment,
    decimal Difference, string Currency, string Checksum, bool IsReconciled);
public sealed record CurrencyRevaluationApprovalDto(Guid Id, string Status, string? DecisionSummary,
    DateTime CreatedUtc, DateTime? DecidedUtc);
public sealed record CurrencyRevaluationRunDto(Guid Id, Guid CompanyId, Guid FiscalPeriodId, string FiscalPeriodName,
    int RunNumber, DateOnly AsOfDate, string FunctionalCurrency, string VoucherSeriesCode, string Status,
    string? FailureReasonCode, string? FailureSummary, string? PopulationChecksum, string? RateSetChecksum,
    string? ProposalChecksum, int PopulationCount, int IncludedCount, int ExcludedCount, int ReviewCount,
    decimal DocumentBalanceTotal, decimal CarryingFunctionalTotal, decimal RevaluedFunctionalTotal,
    decimal ProposedAdjustmentTotal, Guid? ApprovalRequestId, Guid? LedgerEntryId, Guid? ReversalLedgerEntryId,
    Guid? SupersededByRunId, bool IsScheduled, long Version, DateTime CreatedUtc, DateTime UpdatedUtc,
    DateTime? SubmittedUtc, DateTime? PostedUtc, DateTime? ReversedUtc,
    IReadOnlyList<CurrencyRevaluationPopulationItemDto> Population,
    IReadOnlyList<CurrencyRevaluationRateBindingDto> RateBindings,
    IReadOnlyList<CurrencyRevaluationProposalLineDto> ProposalLines,
    IReadOnlyList<CurrencyRevaluationReviewDto> Reviews,
    IReadOnlyList<CurrencyRevaluationReconciliationDto> Reconciliations,
    CurrencyRevaluationApprovalDto? Approval);
public sealed record CurrencyRevaluationRunListDto(IReadOnlyList<CurrencyRevaluationRunDto> Items, int TotalCount, int Skip, int Take);
public sealed record CurrencyRevaluationAccountPolicyDto(Guid Id, Guid FinanceAccountId, string AccountCode,
    string AccountName, string MonetaryClass, bool IsEnabled, long Version, DateTime UpdatedUtc);
public sealed record CurrencyRevaluationScheduleDto(Guid Id, Guid CompanyId, bool IsEnabled, int DaysBeforePeriodEnd,
    bool AutomaticReversal, string VoucherSeriesCode, long Version, DateTime UpdatedUtc, DateTime? LastEvaluatedUtc);

public interface ICurrencyRevaluationService
{
    Task<CurrencyRevaluationRunListDto> ListAsync(ListCurrencyRevaluationRunsQuery query, CancellationToken cancellationToken);
    Task<CurrencyRevaluationRunDto> GetAsync(GetCurrencyRevaluationRunQuery query, CancellationToken cancellationToken);
    Task<CurrencyRevaluationRunDto> PreviewAsync(PreviewCurrencyRevaluationCommand command, CancellationToken cancellationToken);
    Task<CurrencyRevaluationRunDto> ReviewItemAsync(ReviewCurrencyRevaluationItemCommand command, CancellationToken cancellationToken);
    Task<CurrencyRevaluationRunDto> SubmitAsync(SubmitCurrencyRevaluationCommand command, CancellationToken cancellationToken);
    Task<CurrencyRevaluationRunDto> PostAsync(PostCurrencyRevaluationCommand command, CancellationToken cancellationToken);
    Task<CurrencyRevaluationRunDto> ReverseAsync(ReverseCurrencyRevaluationCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyList<CurrencyRevaluationAccountPolicyDto>> ListAccountPoliciesAsync(Guid companyId, CancellationToken cancellationToken);
    Task<CurrencyRevaluationAccountPolicyDto> ConfigureAccountAsync(ConfigureCurrencyRevaluationAccountCommand command, CancellationToken cancellationToken);
    Task<CurrencyRevaluationScheduleDto?> GetScheduleAsync(Guid companyId, CancellationToken cancellationToken);
    Task<CurrencyRevaluationScheduleDto> ConfigureScheduleAsync(ConfigureCurrencyRevaluationScheduleCommand command, CancellationToken cancellationToken);
    Task<int> RunScheduledAsync(CancellationToken cancellationToken);
}

public sealed class CurrencyRevaluationException : Exception
{
    public CurrencyRevaluationException(string reasonCode, string message, bool isConflict = false, long? currentVersion = null)
        : base(message) { ReasonCode = reasonCode; IsConflict = isConflict; CurrentVersion = currentVersion; }
    public string ReasonCode { get; }
    public bool IsConflict { get; }
    public long? CurrentVersion { get; }
}
