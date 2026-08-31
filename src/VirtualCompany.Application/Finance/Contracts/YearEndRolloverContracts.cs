namespace VirtualCompany.Application.Finance;

public static class YearEndReasonCodes
{
    public const string NotFound = "year_end_not_found";
    public const string InvalidState = "year_end_invalid_state";
    public const string NotReady = "year_end_not_ready";
    public const string EvidenceStale = "year_end_evidence_stale";
    public const string SelfReview = "year_end_self_review_forbidden";
    public const string ApprovalRequired = "year_end_approval_required";
    public const string ConcurrencyConflict = "year_end_concurrency_conflict";
    public const string IdempotencyConflict = "year_end_idempotency_conflict";
    public const string CrossCompanyReference = "year_end_cross_company_reference";
    public const string PostingFailed = "year_end_posting_failed";
    public const string ReconciliationFailed = "year_end_reconciliation_failed";
    public const string DocumentAccessDenied = "year_end_document_access_denied";
}

public sealed record PrepareYearEndRunCommand(Guid CompanyId, DateOnly FiscalYearStart,
    Guid TargetFiscalPeriodId, Guid RetainedEarningsAccountId, Guid OpeningBalanceClearingAccountId,
    string VoucherSeriesCode, string IdempotencyKey, Guid ActorUserId, string? CorrelationId);
public sealed record RefreshYearEndReadinessCommand(Guid CompanyId, Guid RunId, long ExpectedVersion,
    string IdempotencyKey, Guid ActorUserId, string? CorrelationId);
public sealed record SubmitYearEndRunCommand(Guid CompanyId, Guid RunId, long ExpectedVersion,
    string ExpectedEvidenceHash, string IdempotencyKey, Guid ActorUserId, string? CorrelationId);
public sealed record ReviewYearEndRunCommand(Guid CompanyId, Guid RunId, long ExpectedVersion,
    string ExpectedEvidenceHash, bool Approve, string? Reason, string IdempotencyKey,
    Guid ActorUserId, string? CorrelationId);
public sealed record ExecuteYearEndRunCommand(Guid CompanyId, Guid RunId, long ExpectedVersion,
    string ExpectedEvidenceHash, string IdempotencyKey, Guid ActorUserId, string? CorrelationId);
public sealed record ReconcileYearEndRunCommand(Guid CompanyId, Guid RunId, long ExpectedVersion,
    string ExpectedEvidenceHash, string IdempotencyKey, Guid ActorUserId, string? CorrelationId);
public sealed record FinalizeYearEndRunCommand(Guid CompanyId, Guid RunId, long ExpectedVersion,
    string IdempotencyKey, Guid ActorUserId, string? CorrelationId);
public sealed record RecordYearEndSubsequentEventCommand(Guid CompanyId, Guid RunId, DateOnly EventDate,
    string Title, string Description, decimal? EstimatedAmount, string Currency, string Decision,
    Guid OwnerUserId, Guid? EvidenceDocumentId, string IdempotencyKey, Guid ActorUserId, string? CorrelationId);
public sealed record SubmitYearEndSubsequentEventCommand(Guid CompanyId, Guid RunId, Guid EventId,
    long ExpectedVersion, string IdempotencyKey, Guid ActorUserId, string? CorrelationId);
public sealed record ReviewYearEndSubsequentEventCommand(Guid CompanyId, Guid RunId, Guid EventId,
    long ExpectedVersion, bool Approve, string? Reason, string IdempotencyKey, Guid ActorUserId, string? CorrelationId);
public sealed record LinkYearEndCorrectionCommand(Guid CompanyId, Guid RunId, Guid EventId,
    long ExpectedVersion, Guid? CorrectionLedgerEntryId, Guid? ReopenRequestId, string Reason,
    string IdempotencyKey, Guid ActorUserId, string? CorrelationId);
public sealed record GetYearEndRunQuery(Guid CompanyId, Guid RunId);
public sealed record ListYearEndRunsQuery(Guid CompanyId, int Take = 20);

public sealed record YearEndReadinessCheckDto(string Code, string Label, bool Passed, bool Blocking,
    int Count, string Explanation, string? TargetType, Guid? TargetId, DateTime ObservedUtc);
public sealed record YearEndReadinessSnapshotDto(Guid Id, int SnapshotNumber, string Status,
    string EvidenceHash, string JournalCutoffHash, int BlockerCount, int ClosedPeriodCount,
    Guid PreparedByUserId, DateTime PreparedUtc, long Version, IReadOnlyList<YearEndReadinessCheckDto> Checks);
public sealed record YearEndRetainedEarningsProposalDto(Guid Id, Guid RetainedEarningsAccountId,
    string RetainedEarningsAccountCode, Guid OpeningBalanceClearingAccountId, string OpeningBalanceClearingAccountCode,
    decimal NetIncome, string Currency, string EvidenceHash, string Status, Guid PreparedByUserId,
    Guid? ReviewedByUserId, DateTime PreparedUtc, DateTime? ReviewedUtc, long Version);
public sealed record YearEndOpeningBalanceCandidateDto(Guid Id, Guid FinanceAccountId, string AccountCode,
    string AccountName, string AccountClass, string SourceCurrency, string DimensionKey,
    decimal ClosingFunctionalBalance, decimal ClosingDocumentBalance, decimal OpeningFunctionalBalance,
    decimal OpeningDocumentBalance, decimal Difference, string Status, Guid? OpeningLedgerEntryId);
public sealed record YearEndSignOffDto(Guid Id, string Action, string Decision, string EvidenceHash,
    Guid ActorUserId, string ActorRole, string? Reason, DateTime OccurredUtc);
public sealed record YearEndSubsequentEventDto(Guid Id, DateOnly EventDate, string Title, string Description,
    decimal? EstimatedAmount, string Currency, string Decision, Guid OwnerUserId, Guid? EvidenceDocumentId,
    string Status, Guid RecordedByUserId, Guid? ReviewedByUserId, Guid? CorrectionLedgerEntryId,
    Guid? ReopenRequestId, DateTime RecordedUtc, DateTime UpdatedUtc, DateTime? ResolvedUtc, long Version);
public sealed record YearEndHistoryDto(Guid Id, string Action, string FromStatus, string ToStatus,
    Guid ActorUserId, string EvidenceHash, string Summary, DateTime OccurredUtc);
public sealed record YearEndRunSummaryDto(Guid Id, DateOnly FiscalYearStart, DateOnly FiscalYearEnd,
    string Status, int BlockerCount, decimal NetIncome, string Currency, DateTime UpdatedUtc, long Version);
public sealed record YearEndRunDto(Guid Id, Guid CompanyId, string CompanyName, DateOnly FiscalYearStart,
    DateOnly FiscalYearEnd, Guid TargetFiscalPeriodId, string TargetFiscalPeriodName, string VoucherSeriesCode,
    string Status, Guid PreparedByUserId, Guid? ApprovedByUserId, Guid? ExecutedByUserId,
    Guid? ReconciledByUserId, Guid? CompletedByUserId, string? ApprovedEvidenceHash,
    Guid? RetainedEarningsLedgerEntryId, Guid? OpeningBalanceLedgerEntryId, string? OpeningBalanceChecksum,
    string? FailureCode, string? FailureSummary, DateTime CreatedUtc, DateTime UpdatedUtc,
    DateTime? ApprovedUtc, DateTime? ExecutedUtc, DateTime? ReconciledUtc, DateTime? CompletedUtc,
    long Version, YearEndReadinessSnapshotDto? CurrentReadiness,
    YearEndRetainedEarningsProposalDto? RetainedEarningsProposal,
    IReadOnlyList<YearEndOpeningBalanceCandidateDto> OpeningBalances,
    IReadOnlyList<YearEndSignOffDto> SignOffs, IReadOnlyList<YearEndSubsequentEventDto> SubsequentEvents,
    IReadOnlyList<YearEndHistoryDto> History, IReadOnlyList<string> AllowedActions);

public interface IYearEndRolloverService
{
    Task<IReadOnlyList<YearEndRunSummaryDto>> ListAsync(ListYearEndRunsQuery query, CancellationToken cancellationToken);
    Task<YearEndRunDto> GetAsync(GetYearEndRunQuery query, CancellationToken cancellationToken);
    Task<YearEndRunDto> PrepareAsync(PrepareYearEndRunCommand command, CancellationToken cancellationToken);
    Task<YearEndRunDto> RefreshReadinessAsync(RefreshYearEndReadinessCommand command, CancellationToken cancellationToken);
    Task<YearEndRunDto> SubmitAsync(SubmitYearEndRunCommand command, CancellationToken cancellationToken);
    Task<YearEndRunDto> ReviewAsync(ReviewYearEndRunCommand command, CancellationToken cancellationToken);
    Task<YearEndRunDto> ExecuteAsync(ExecuteYearEndRunCommand command, CancellationToken cancellationToken);
    Task<YearEndRunDto> ReconcileAsync(ReconcileYearEndRunCommand command, CancellationToken cancellationToken);
    Task<YearEndRunDto> FinalizeAsync(FinalizeYearEndRunCommand command, CancellationToken cancellationToken);
    Task<YearEndRunDto> RecordSubsequentEventAsync(RecordYearEndSubsequentEventCommand command, CancellationToken cancellationToken);
    Task<YearEndRunDto> SubmitSubsequentEventAsync(SubmitYearEndSubsequentEventCommand command, CancellationToken cancellationToken);
    Task<YearEndRunDto> ReviewSubsequentEventAsync(ReviewYearEndSubsequentEventCommand command, CancellationToken cancellationToken);
    Task<YearEndRunDto> LinkCorrectionAsync(LinkYearEndCorrectionCommand command, CancellationToken cancellationToken);
}

public sealed class YearEndRolloverException(string reasonCode, string message, bool isConflict = false,
    long? currentVersion = null) : Exception(message)
{
    public string ReasonCode { get; } = reasonCode;
    public bool IsConflict { get; } = isConflict;
    public long? CurrentVersion { get; } = currentVersion;
}
