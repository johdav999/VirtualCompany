namespace VirtualCompany.Application.Finance;

public static class AccountingCloseGovernanceReasonCodes
{
    public const string NotFound = "accounting_close_governance_not_found";
    public const string InvalidState = "accounting_close_governance_invalid_state";
    public const string NotReady = "accounting_close_not_ready";
    public const string EvidenceStale = "accounting_close_evidence_stale";
    public const string SelfReview = "accounting_close_self_review_forbidden";
    public const string WaiverNotAllowed = "accounting_close_waiver_not_allowed";
    public const string WaiverExpired = "accounting_close_waiver_expired";
    public const string ApprovalRequired = "accounting_close_approval_required";
    public const string PeriodStateChanged = "accounting_close_period_state_changed";
}

public sealed record ConfigureAccountingClosePolicyCommand(Guid CompanyId, long? ExpectedVersion,
    decimal MaterialityThreshold, string Currency, int WaiverValidityHours, Guid ActorUserId, string? CorrelationId);
public sealed record GetAccountingClosePolicyQuery(Guid CompanyId);
public sealed record PrepareAccountingCloseReadinessCommand(Guid CompanyId, Guid CloseInstanceId,
    long ExpectedInstanceVersion, bool Refresh, string IdempotencyKey, Guid ActorUserId, string? CorrelationId);
public sealed record SubmitAccountingCloseReadinessCommand(Guid CompanyId, Guid CloseInstanceId,
    Guid SnapshotId, long ExpectedSnapshotVersion, string ExpectedEvidenceHash, string IdempotencyKey,
    Guid ActorUserId, string? CorrelationId);
public sealed record ReviewAccountingCloseReadinessCommand(Guid CompanyId, Guid CloseInstanceId,
    Guid SnapshotId, long ExpectedSnapshotVersion, string ExpectedEvidenceHash, bool Approve,
    string? Reason, string IdempotencyKey, Guid ActorUserId, string? CorrelationId);
public sealed record CancelAccountingCloseReadinessCommand(Guid CompanyId, Guid CloseInstanceId,
    Guid SnapshotId, long ExpectedSnapshotVersion, string ExpectedEvidenceHash, string Reason,
    string IdempotencyKey, Guid ActorUserId, string? CorrelationId);
public sealed record LockAccountingCloseCommand(Guid CompanyId, Guid CloseInstanceId, Guid SnapshotId,
    long ExpectedSnapshotVersion, string ExpectedEvidenceHash, string Reason, string IdempotencyKey,
    Guid ActorUserId, string? CorrelationId);
public sealed record ProposeAccountingCloseWaiverCommand(Guid CompanyId, Guid CloseInstanceId,
    Guid SnapshotId, string CheckCode, string ExpectedCheckEvidenceHash, string Reason, decimal? Amount,
    Guid EvidenceDocumentId, DateTime? ExpiresUtc, string IdempotencyKey, Guid ActorUserId, string? CorrelationId);
public sealed record ReviewAccountingCloseWaiverCommand(Guid CompanyId, Guid CloseInstanceId,
    Guid WaiverId, bool Approve, string? Comment, string IdempotencyKey, Guid ActorUserId, string? CorrelationId);
public sealed record RequestAccountingCloseReopenCommand(Guid CompanyId, Guid CloseInstanceId,
    Guid PriorSnapshotId, string ExpectedSnapshotHash, string Reason, string Scope, string CorrectionPath,
    DateTime? ExpiresUtc, string IdempotencyKey, Guid ActorUserId, string? CorrelationId);
public sealed record ReviewAccountingCloseReopenCommand(Guid CompanyId, Guid CloseInstanceId,
    Guid ReopenRequestId, long ExpectedVersion, bool Approve, string? Comment, string IdempotencyKey,
    Guid ActorUserId, string? CorrelationId);
public sealed record ExecuteAccountingCloseReopenCommand(Guid CompanyId, Guid CloseInstanceId,
    Guid ReopenRequestId, long ExpectedVersion, string ExpectedSnapshotHash, string IdempotencyKey,
    Guid ActorUserId, string? CorrelationId);
public sealed record GetAccountingCloseGovernanceQuery(Guid CompanyId, Guid CloseInstanceId);

public sealed record AccountingClosePolicyDto(Guid Id, Guid CompanyId, decimal MaterialityThreshold,
    string Currency, int WaiverValidityHours, long Version, Guid UpdatedByUserId, DateTime UpdatedUtc);
public sealed record AccountingCloseReadinessCheckDto(Guid Id, string Category, string Code, string Message,
    bool IsBlocking, bool IsWaivable, decimal? Amount, string? Currency, int ItemCount,
    string EvidenceHash, DateTime ObservedUtc);
public sealed record AccountingCloseWaiverDto(Guid Id, Guid SnapshotId, string CheckCode,
    string CheckEvidenceHash, string Reason, decimal? Amount, Guid EvidenceDocumentId,
    string EvidenceDocumentHash, Guid ApprovalRequestId, string Status, Guid ProposedByUserId,
    Guid? ReviewedByUserId, DateTime CreatedUtc, DateTime ExpiresUtc, DateTime? ReviewedUtc);
public sealed record AccountingCloseReadinessSnapshotDto(Guid Id, int SnapshotNumber, string Status,
    string EvidenceHash, string TrialBalanceChecksum, bool IsReady, Guid PreparedByUserId, DateTime PreparedUtc,
    Guid? SubmittedByUserId, DateTime? SubmittedUtc, Guid? ReviewedByUserId, DateTime? ReviewedUtc,
    string? ReviewReason, Guid? LockedByUserId, DateTime? LockedUtc, string? FailureCode,
    string? FailureSummary, long Version, IReadOnlyList<AccountingCloseReadinessCheckDto> Checks);
public sealed record AccountingCloseReopenRequestDto(Guid Id, Guid PriorSnapshotId, string PriorSnapshotHash,
    string Reason, string Scope, string CorrectionPath, string Status, Guid RequestedByUserId,
    DateTime RequestedUtc, DateTime ExpiresUtc, Guid? ReviewedByUserId, DateTime? ReviewedUtc,
    Guid? ExecutedByUserId, DateTime? ExecutedUtc, long Version);
public sealed record AccountingCloseSignOffDto(Guid Id, Guid? SnapshotId, Guid? ReopenRequestId,
    string Action, string EvidenceHash, Guid ActorUserId, string ActorRole, string? Reason, DateTime OccurredUtc);
public sealed record AccountingCloseGovernanceDto(Guid CloseInstanceId, Guid FiscalPeriodId,
    string CloseStatus, long CloseVersion, AccountingClosePolicyDto Policy,
    AccountingCloseReadinessSnapshotDto? CurrentSnapshot,
    IReadOnlyList<AccountingCloseReadinessSnapshotDto> Snapshots,
    IReadOnlyList<AccountingCloseWaiverDto> Waivers,
    IReadOnlyList<AccountingCloseReopenRequestDto> ReopenRequests,
    IReadOnlyList<AccountingCloseSignOffDto> SignOffs,
    IReadOnlyList<string> AllowedActions);

public interface IAccountingCloseGovernanceService
{
    Task<AccountingClosePolicyDto> ConfigurePolicyAsync(ConfigureAccountingClosePolicyCommand command, CancellationToken cancellationToken);
    Task<AccountingClosePolicyDto> GetPolicyAsync(GetAccountingClosePolicyQuery query, CancellationToken cancellationToken);
    Task<AccountingCloseGovernanceDto> PrepareAsync(PrepareAccountingCloseReadinessCommand command, CancellationToken cancellationToken);
    Task<AccountingCloseGovernanceDto> SubmitAsync(SubmitAccountingCloseReadinessCommand command, CancellationToken cancellationToken);
    Task<AccountingCloseGovernanceDto> ReviewAsync(ReviewAccountingCloseReadinessCommand command, CancellationToken cancellationToken);
    Task<AccountingCloseGovernanceDto> CancelAsync(CancelAccountingCloseReadinessCommand command, CancellationToken cancellationToken);
    Task<AccountingCloseGovernanceDto> LockAsync(LockAccountingCloseCommand command, CancellationToken cancellationToken);
    Task<AccountingCloseGovernanceDto> ProposeWaiverAsync(ProposeAccountingCloseWaiverCommand command, CancellationToken cancellationToken);
    Task<AccountingCloseGovernanceDto> ReviewWaiverAsync(ReviewAccountingCloseWaiverCommand command, CancellationToken cancellationToken);
    Task<AccountingCloseGovernanceDto> RequestReopenAsync(RequestAccountingCloseReopenCommand command, CancellationToken cancellationToken);
    Task<AccountingCloseGovernanceDto> ReviewReopenAsync(ReviewAccountingCloseReopenCommand command, CancellationToken cancellationToken);
    Task<AccountingCloseGovernanceDto> ExecuteReopenAsync(ExecuteAccountingCloseReopenCommand command, CancellationToken cancellationToken);
    Task<AccountingCloseGovernanceDto> GetAsync(GetAccountingCloseGovernanceQuery query, CancellationToken cancellationToken);
}

public sealed class AccountingCloseGovernanceException : Exception
{
    public AccountingCloseGovernanceException(string reasonCode, string message, bool isConflict = false,
        long? currentVersion = null) : base(message)
    { ReasonCode = reasonCode; IsConflict = isConflict; CurrentVersion = currentVersion; }
    public string ReasonCode { get; }
    public bool IsConflict { get; }
    public long? CurrentVersion { get; }
}
