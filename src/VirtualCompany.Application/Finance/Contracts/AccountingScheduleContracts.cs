namespace VirtualCompany.Application.Finance;

public static class AccountingScheduleReasonCodes
{
    public const string NotFound = "accounting_schedule_not_found";
    public const string VersionConflict = "accounting_schedule_version_conflict";
    public const string InvalidTemplate = "accounting_schedule_invalid_template";
    public const string ApprovalRequired = "accounting_schedule_approval_required";
    public const string ApprovalPending = "accounting_schedule_approval_pending";
    public const string ApprovalStale = "accounting_schedule_approval_stale";
    public const string InvalidState = "accounting_schedule_invalid_state";
    public const string PeriodUnavailable = "accounting_schedule_period_unavailable";
    public const string IdempotencyConflict = "accounting_schedule_idempotency_conflict";
    public const string OccurrenceBlocked = "accounting_schedule_occurrence_blocked";
}

public sealed record AccountingScheduleLineInput(Guid FinanceAccountId, decimal DebitAmount,
    decimal CreditAmount, string Description, IReadOnlyList<Guid>? DimensionMemberIds = null);
public sealed record AccountingScheduleInput(string Code, string Name, string ScheduleType, string Cadence,
    string AmountBasis, string ProrationRule, DateOnly StartDate, DateOnly? EndDate, int OccurrenceDay,
    string TimeZoneId, string VoucherSeriesCode, string Currency, string ReversalRule, string Description,
    IReadOnlyList<AccountingScheduleLineInput> Lines, IReadOnlyList<Guid>? EvidenceDocumentIds = null);

public sealed record CreateAccountingScheduleCommand(Guid CompanyId, AccountingScheduleInput Schedule,
    string IdempotencyKey, Guid ActorUserId, string? CorrelationId);
public sealed record UpdateAccountingScheduleCommand(Guid CompanyId, Guid ScheduleId, long ExpectedVersion,
    AccountingScheduleInput Schedule, string IdempotencyKey, Guid ActorUserId, string? CorrelationId);
public sealed record PreviewAccountingScheduleQuery(Guid CompanyId, Guid ScheduleId, long ExpectedVersion, Guid ActorUserId);
public sealed record SubmitAccountingScheduleCommand(Guid CompanyId, Guid ScheduleId, long ExpectedVersion,
    string IdempotencyKey, Guid ActorUserId, string? CorrelationId);
public sealed record DecideAccountingScheduleApprovalCommand(Guid CompanyId, Guid ScheduleId, long ExpectedVersion,
    bool Approve, string? Comment, Guid ClientRequestId, Guid ActorUserId, string? CorrelationId);
public sealed record ActivateAccountingScheduleCommand(Guid CompanyId, Guid ScheduleId, long ExpectedVersion,
    Guid ActorUserId, string? CorrelationId);
public sealed record ChangeAccountingScheduleStateCommand(Guid CompanyId, Guid ScheduleId, long ExpectedVersion,
    string Action, bool GenerateMissed, Guid ActorUserId, string? CorrelationId);
public sealed record RegenerateAccountingScheduleOccurrenceCommand(Guid CompanyId, Guid ScheduleId,
    Guid OccurrenceId, long ExpectedVersion, Guid ActorUserId, string? CorrelationId);
public sealed record ListAccountingSchedulesQuery(Guid CompanyId, string? Status = null, int Skip = 0, int Take = 100);
public sealed record GetAccountingScheduleQuery(Guid CompanyId, Guid ScheduleId);

public interface IAccountingScheduleService
{
    Task<AccountingScheduleDto> CreateAsync(CreateAccountingScheduleCommand command, CancellationToken cancellationToken);
    Task<AccountingScheduleDto> UpdateAsync(UpdateAccountingScheduleCommand command, CancellationToken cancellationToken);
    Task<AccountingSchedulePreviewDto> PreviewAsync(PreviewAccountingScheduleQuery query, CancellationToken cancellationToken);
    Task<AccountingScheduleDto> SubmitAsync(SubmitAccountingScheduleCommand command, CancellationToken cancellationToken);
    Task<AccountingScheduleDto> DecideApprovalAsync(DecideAccountingScheduleApprovalCommand command, CancellationToken cancellationToken);
    Task<AccountingScheduleDto> ActivateAsync(ActivateAccountingScheduleCommand command, CancellationToken cancellationToken);
    Task<AccountingScheduleDto> ChangeStateAsync(ChangeAccountingScheduleStateCommand command, CancellationToken cancellationToken);
    Task<AccountingScheduleDto> RegenerateOccurrenceAsync(RegenerateAccountingScheduleOccurrenceCommand command, CancellationToken cancellationToken);
    Task<AccountingScheduleDto> GetAsync(GetAccountingScheduleQuery query, CancellationToken cancellationToken);
    Task<AccountingScheduleListResult> ListAsync(ListAccountingSchedulesQuery query, CancellationToken cancellationToken);
}

public interface IAccountingScheduleGenerationRunner
{
    Task<int> RunDueAsync(CancellationToken cancellationToken);
}

public sealed class AccountingScheduleException : Exception
{
    public AccountingScheduleException(string reasonCode, string message, bool isConflict = false, long? currentVersion = null)
        : base(message) { ReasonCode = reasonCode; IsConflict = isConflict; CurrentVersion = currentVersion; }
    public string ReasonCode { get; }
    public bool IsConflict { get; }
    public long? CurrentVersion { get; }
}
