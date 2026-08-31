namespace VirtualCompany.Application.Finance;

public static class AccountingCloseWorkspaceActions
{
    public const string CompleteTask = "complete_task";
    public const string RefreshReadiness = "refresh_readiness";
    public const string ProposeWaiver = "propose_waiver";
    public const string SignOff = "sign_off";
    public const string Lock = "lock";
    public const string RequestReopen = "request_reopen";
    public const string ExecuteReopen = "execute_reopen";
    public const string RequestPackage = "request_package";
    public const string ApprovePackage = "approve_package";
    public const string CancelPackage = "cancel_package";
    public const string OpenYearEnd = "open_year_end";
    public const string RunYearEndAction = "run_year_end_action";
}

public sealed record GetAccountingCloseWorkspaceQuery(Guid CompanyId, Guid? FiscalPeriodId = null,
    Guid? CloseInstanceId = null);

public sealed record AccountingCloseWorkspacePeriodDto(Guid FiscalPeriodId, string Name,
    DateTime StartUtc, DateTime EndUtc, bool IsClosed, Guid? CloseInstanceId, string? CloseStatus,
    DateTime? UpdatedUtc);

public sealed record AccountingCloseWorkspaceEvidenceDto(Guid Id, Guid DocumentId, string EvidenceType, string Title,
    string? ContentHash, DateTime LinkedUtc, string DrilldownUrl);

public sealed record AccountingCloseWorkspaceBlockerDto(string Code, string Title, string Explanation,
    string SafeNextAction, Guid? OwnerUserId, string Status, int EvidenceCount, DateTime ObservedUtc,
    string DrilldownUrl, bool IsWaivable, string? EvidenceHash);

public sealed record AccountingCloseWorkspaceTaskDto(Guid Id, string Key, string Title, string Status,
    Guid? OwnerUserId, string? OwnerRole, DateTime DueUtc, int Sequence, long Version,
    IReadOnlyList<Guid> PredecessorTaskIds, IReadOnlyList<string> BlockingReasonCodes,
    IReadOnlyList<AccountingCloseWorkspaceEvidenceDto> Evidence,
    IReadOnlyList<AccountingCloseWorkspaceBlockerDto> Blockers,
    IReadOnlyList<string> AllowedActions, string DrilldownUrl);

public sealed record AccountingCloseWorkspaceReadinessDto(Guid SnapshotId, int SnapshotNumber,
    string Status, bool IsReady, string EvidenceHash, DateTime PreparedUtc, long Version,
    int BlockingCount, int WarningCount, bool IsStale,
    IReadOnlyList<AccountingCloseWorkspaceBlockerDto> Blockers);

public sealed record AccountingCloseWorkspacePanelDto(string Key, string Title, string Status,
    int TotalCount, int AttentionCount, DateTime? EvidenceUtc, string DrilldownUrl,
    IReadOnlyList<string> AllowedActions);

public sealed record AccountingCloseWorkspaceSignOffDto(Guid Id, string Action, string ActorRole,
    Guid ActorUserId, DateTime OccurredUtc, string? Reason, string EvidenceHash);

public sealed record AccountingCloseWorkspaceNotificationDto(Guid Id, string Priority, string Title,
    string Body, string Status, DateTime CreatedUtc, string? ActionUrl);

public sealed record AccountingCloseWorkspaceDto(Guid CompanyId, string CompanyName, string MembershipRole,
    DateTime GeneratedUtc, AccountingCloseWorkspacePeriodDto? SelectedPeriod, Guid? CloseInstanceId,
    string? CloseName, string? CloseStatus, long? CloseVersion,
    IReadOnlyList<AccountingCloseWorkspacePeriodDto> Periods,
    AccountingCloseWorkspaceReadinessDto? Readiness,
    IReadOnlyList<AccountingCloseWorkspaceTaskDto> Tasks,
    IReadOnlyList<AccountingCloseWorkspacePanelDto> Panels,
    IReadOnlyList<AccountingCloseWorkspaceSignOffDto> SignOffs,
    IReadOnlyList<AccountingCloseWorkspaceNotificationDto> Notifications,
    IReadOnlyList<string> AllowedActions,
    string EvidenceNotice = "Readiness and allowed actions are authoritative backend decisions for the displayed evidence timestamp.");

public interface IAccountingCloseWorkspaceService
{
    Task<AccountingCloseWorkspaceDto> GetAsync(GetAccountingCloseWorkspaceQuery query,
        CancellationToken cancellationToken);
}
