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
