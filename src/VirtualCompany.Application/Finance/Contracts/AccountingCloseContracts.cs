namespace VirtualCompany.Application.Finance;

public static class AccountingCloseReasonCodes
{
    public const string NotFound = "accounting_close_not_found";
    public const string VersionConflict = "accounting_close_version_conflict";
    public const string IdempotencyConflict = "accounting_close_idempotency_conflict";
    public const string InvalidTemplate = "accounting_close_invalid_template";
    public const string DependencyCycle = "accounting_close_dependency_cycle";
    public const string PredecessorIncomplete = "accounting_close_predecessor_incomplete";
    public const string EvidenceRequired = "accounting_close_evidence_required";
    public const string EvidenceAccessDenied = "accounting_close_evidence_access_denied";
    public const string OwnerOutsideCompany = "accounting_close_owner_outside_company";
    public const string CompletionForbidden = "accounting_close_completion_forbidden";
    public const string ReportedAmountRequired = "accounting_close_reported_amount_required";
    public const string SignOffRequired = "accounting_close_sign_off_required";
    public const string InvalidState = "accounting_close_invalid_state";
}

public sealed record AccountingCloseEvidenceRequirementInput(string EvidenceType, string Description,
    int MinimumCount = 1);
public sealed record AccountingCloseTaskDefinitionInput(string Key, string Title, string? Description,
    int Sequence, int DueOffsetDays, Guid? DefaultOwnerUserId, string? DefaultOwnerRole,
    bool RequiresSignOff, string? SignOffRole, decimal? MaterialityAmount,
    IReadOnlyList<AccountingCloseEvidenceRequirementInput>? EvidenceRequirements = null,
    IReadOnlyList<string>? PredecessorKeys = null);
public sealed record AccountingCloseSectionInput(string Key, string Name, int Sequence,
    IReadOnlyList<AccountingCloseTaskDefinitionInput> Tasks);
public sealed record AccountingCloseTemplateInput(string Code, string Name, string? Description,
    decimal MaterialityAmount, decimal? MaterialityPercentage,
    IReadOnlyList<AccountingCloseSectionInput> Sections);

public sealed record CreateAccountingCloseTemplateCommand(Guid CompanyId, AccountingCloseTemplateInput Template,
    string IdempotencyKey, Guid ActorUserId, string? CorrelationId);
public sealed record CreateAccountingCloseTemplateVersionCommand(Guid CompanyId, Guid TemplateId,
    long ExpectedVersion, AccountingCloseTemplateInput Template, string IdempotencyKey,
    Guid ActorUserId, string? CorrelationId);
public sealed record CopyAccountingCloseTemplateCommand(Guid CompanyId, Guid SourceTemplateId,
    Guid SourceVersionId, string NewCode, string NewName, string IdempotencyKey,
    Guid ActorUserId, string? CorrelationId);
public sealed record ActivateAccountingCloseTemplateCommand(Guid CompanyId, Guid TemplateId,
    Guid TemplateVersionId, long ExpectedVersion, string IdempotencyKey,
    Guid ActorUserId, string? CorrelationId);
public sealed record RetireAccountingCloseTemplateCommand(Guid CompanyId, Guid TemplateId,
    Guid? TemplateVersionId, long ExpectedVersion, string Reason, string IdempotencyKey,
    Guid ActorUserId, string? CorrelationId);
public sealed record GetAccountingCloseTemplateQuery(Guid CompanyId, Guid TemplateId);
public sealed record PreviewAccountingCloseTemplateQuery(Guid CompanyId, Guid TemplateId, Guid TemplateVersionId);
public sealed record ListAccountingCloseTemplatesQuery(Guid CompanyId, string? Status = null, int Skip = 0, int Take = 100);

public sealed record StartAccountingCloseCommand(Guid CompanyId, Guid FiscalPeriodId, Guid TemplateId,
    Guid? TemplateVersionId, string IdempotencyKey, Guid ActorUserId, string? CorrelationId);
public sealed record AssignAccountingCloseTaskCommand(Guid CompanyId, Guid CloseInstanceId, Guid CloseTaskId,
    long ExpectedVersion, Guid OwnerUserId, string IdempotencyKey, Guid ActorUserId, string? CorrelationId);
public sealed record AccountingCloseEvidenceInput(Guid DocumentId, string EvidenceType);
public sealed record CompleteAccountingCloseTaskCommand(Guid CompanyId, Guid CloseInstanceId, Guid CloseTaskId,
    long ExpectedVersion, decimal? ReportedAmount, IReadOnlyList<AccountingCloseEvidenceInput>? Evidence,
    string? Note, string IdempotencyKey, Guid ActorUserId, string? CorrelationId);
public sealed record ReopenAccountingCloseTaskCommand(Guid CompanyId, Guid CloseInstanceId, Guid CloseTaskId,
    long ExpectedVersion, string Reason, string IdempotencyKey, Guid ActorUserId, string? CorrelationId);
public sealed record CancelAccountingCloseTaskCommand(Guid CompanyId, Guid CloseInstanceId, Guid CloseTaskId,
    long ExpectedVersion, string Reason, string IdempotencyKey, Guid ActorUserId, string? CorrelationId);
public sealed record CancelAccountingCloseCommand(Guid CompanyId, Guid CloseInstanceId, long ExpectedVersion,
    string Reason, string IdempotencyKey, Guid ActorUserId, string? CorrelationId);
public sealed record AddAccountingCloseTaskBlockerCommand(Guid CompanyId, Guid CloseInstanceId, Guid CloseTaskId,
    long ExpectedVersion, string ReasonCode, string Explanation, string SafeNextAction,
    string IdempotencyKey, Guid ActorUserId, string? CorrelationId);
public sealed record ResolveAccountingCloseTaskBlockerCommand(Guid CompanyId, Guid CloseInstanceId, Guid CloseTaskId,
    Guid BlockerId, long ExpectedVersion, string IdempotencyKey, Guid ActorUserId, string? CorrelationId);
public sealed record GetAccountingCloseQuery(Guid CompanyId, Guid CloseInstanceId);
public sealed record ListAccountingClosesQuery(Guid CompanyId, Guid? FiscalPeriodId = null,
    string? Status = null, int Skip = 0, int Take = 100);

public sealed record AccountingCloseEvidenceRequirementDto(Guid Id, string EvidenceType, string Description,
    int MinimumCount);
public sealed record AccountingCloseTaskDefinitionDto(Guid Id, Guid SectionId, string Key, string Title,
    string? Description, int Sequence, int DueOffsetDays, Guid? DefaultOwnerUserId, string? DefaultOwnerRole,
    bool RequiresSignOff, string? SignOffRole, decimal? MaterialityAmount,
    IReadOnlyList<string> PredecessorKeys, IReadOnlyList<AccountingCloseEvidenceRequirementDto> EvidenceRequirements);
public sealed record AccountingCloseSectionDto(Guid Id, string Key, string Name, int Sequence,
    IReadOnlyList<AccountingCloseTaskDefinitionDto> Tasks);
public sealed record AccountingCloseTemplateHistoryDto(Guid Id, Guid TemplateVersionId, string Action,
    Guid ActorUserId, string? Reason, DateTime OccurredUtc);
public sealed record AccountingCloseTemplateVersionDto(Guid Id, int VersionNumber, string Name,
    string? Description, decimal MaterialityAmount, decimal? MaterialityPercentage, string Status,
    DateTime CreatedUtc, DateTime? ActivatedUtc, IReadOnlyList<AccountingCloseSectionDto> Sections);
public sealed record AccountingCloseTemplateDto(Guid Id, Guid CompanyId, string Code, string Name,
    string? Description, string Status, Guid? ActiveVersionId, int LatestVersionNumber, long Version,
    DateTime CreatedUtc, DateTime UpdatedUtc, AccountingCloseTemplateVersionDto? ActiveVersion,
    IReadOnlyList<AccountingCloseTemplateVersionDto> Versions,
    IReadOnlyList<AccountingCloseTemplateHistoryDto> History, IReadOnlyList<string> AllowedActions);
public sealed record AccountingCloseTemplatePreviewDto(AccountingCloseTemplateDto Template,
    Guid TemplateVersionId, int TaskCount, int DependencyCount, int EvidenceRequirementCount,
    IReadOnlyList<string> TopologicalTaskKeys, IReadOnlyList<string> Issues);
public sealed record AccountingCloseTemplateListResult(IReadOnlyList<AccountingCloseTemplateDto> Items,
    int TotalCount, int Skip, int Take);

public sealed record AccountingCloseTaskEvidenceDto(Guid Id, Guid DocumentId, string EvidenceType,
    string DocumentTitle, string? ContentHash, Guid LinkedByUserId, DateTime LinkedUtc);
public sealed record AccountingCloseTaskNoteDto(Guid Id, Guid AuthorUserId, string Note, DateTime CreatedUtc);
public sealed record AccountingCloseTaskBlockerDto(Guid Id, string ReasonCode, string Explanation,
    string SafeNextAction, string Status, Guid CreatedByUserId, DateTime CreatedUtc,
    Guid? ResolvedByUserId, DateTime? ResolvedUtc);
public sealed record AccountingCloseTaskDto(Guid Id, Guid TaskDefinitionId, Guid SectionId, string Key,
    string Title, string? Description, int Sequence, string Status, Guid? OwnerUserId, string? OwnerRole,
    DateTime DueUtc, bool RequiresSignOff, string? SignOffRole, decimal MaterialityAmount,
    Guid WorkTaskId, Guid? ApprovalRequestId, string? ApprovalStatus, DateTime CreatedUtc,
    DateTime UpdatedUtc, DateTime? CompletedUtc, Guid? CompletedByUserId, decimal? ReportedAmount,
    long Version, IReadOnlyList<Guid> PredecessorTaskIds, IReadOnlyList<string> BlockingReasonCodes,
    IReadOnlyList<AccountingCloseTaskEvidenceDto> Evidence, IReadOnlyList<AccountingCloseTaskNoteDto> Notes,
    IReadOnlyList<AccountingCloseTaskBlockerDto> Blockers, IReadOnlyList<string> AllowedActions);
public sealed record AccountingCloseHistoryDto(Guid Id, Guid? CloseTaskId, string Action,
    string? FromStatus, string ToStatus, Guid ActorUserId, string? Reason, DateTime OccurredUtc);
public sealed record AccountingCloseDto(Guid Id, Guid CompanyId, Guid FiscalPeriodId, string FiscalPeriodName,
    Guid TemplateId, Guid TemplateVersionId, int TemplateVersionNumber, string Name, string Status,
    Guid StartedByUserId, DateTime StartedUtc, DateTime UpdatedUtc, DateTime? CompletedUtc,
    DateTime? CancelledUtc, long Version, int CompletedTaskCount, int TaskCount,
    IReadOnlyList<AccountingCloseTaskDto> Tasks, IReadOnlyList<AccountingCloseHistoryDto> History,
    IReadOnlyList<string> AllowedActions);
public sealed record AccountingCloseListResult(IReadOnlyList<AccountingCloseDto> Items,
    int TotalCount, int Skip, int Take);

public interface IAccountingCloseService
{
    Task<AccountingCloseTemplateDto> CreateTemplateAsync(CreateAccountingCloseTemplateCommand command, CancellationToken cancellationToken);
    Task<AccountingCloseTemplateDto> CreateTemplateVersionAsync(CreateAccountingCloseTemplateVersionCommand command, CancellationToken cancellationToken);
    Task<AccountingCloseTemplateDto> CopyTemplateAsync(CopyAccountingCloseTemplateCommand command, CancellationToken cancellationToken);
    Task<AccountingCloseTemplateDto> ActivateTemplateAsync(ActivateAccountingCloseTemplateCommand command, CancellationToken cancellationToken);
    Task<AccountingCloseTemplateDto> RetireTemplateAsync(RetireAccountingCloseTemplateCommand command, CancellationToken cancellationToken);
    Task<AccountingCloseTemplateDto> GetTemplateAsync(GetAccountingCloseTemplateQuery query, CancellationToken cancellationToken);
    Task<AccountingCloseTemplatePreviewDto> PreviewTemplateAsync(PreviewAccountingCloseTemplateQuery query, CancellationToken cancellationToken);
    Task<AccountingCloseTemplateListResult> ListTemplatesAsync(ListAccountingCloseTemplatesQuery query, CancellationToken cancellationToken);
    Task<AccountingCloseDto> StartAsync(StartAccountingCloseCommand command, CancellationToken cancellationToken);
    Task<AccountingCloseDto> AssignTaskAsync(AssignAccountingCloseTaskCommand command, CancellationToken cancellationToken);
    Task<AccountingCloseDto> CompleteTaskAsync(CompleteAccountingCloseTaskCommand command, CancellationToken cancellationToken);
    Task<AccountingCloseDto> ReopenTaskAsync(ReopenAccountingCloseTaskCommand command, CancellationToken cancellationToken);
    Task<AccountingCloseDto> CancelTaskAsync(CancelAccountingCloseTaskCommand command, CancellationToken cancellationToken);
    Task<AccountingCloseDto> CancelAsync(CancelAccountingCloseCommand command, CancellationToken cancellationToken);
    Task<AccountingCloseDto> AddBlockerAsync(AddAccountingCloseTaskBlockerCommand command, CancellationToken cancellationToken);
    Task<AccountingCloseDto> ResolveBlockerAsync(ResolveAccountingCloseTaskBlockerCommand command, CancellationToken cancellationToken);
    Task<AccountingCloseDto> GetAsync(GetAccountingCloseQuery query, CancellationToken cancellationToken);
    Task<AccountingCloseListResult> ListAsync(ListAccountingClosesQuery query, CancellationToken cancellationToken);
}

public sealed class AccountingCloseException : Exception
{
    public AccountingCloseException(string reasonCode, string message, bool isConflict = false,
        long? currentVersion = null) : base(message)
    {
        ReasonCode = reasonCode;
        IsConflict = isConflict;
        CurrentVersion = currentVersion;
    }
    public string ReasonCode { get; }
    public bool IsConflict { get; }
    public long? CurrentVersion { get; }
}
