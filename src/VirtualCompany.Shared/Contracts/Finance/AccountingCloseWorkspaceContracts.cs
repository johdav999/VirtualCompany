namespace VirtualCompany.Application.Finance;
public sealed record AccountingCloseWorkspaceBlockerDto(string Code, string Title, string Explanation, string SafeNextAction, Guid? OwnerUserId, string Status, int EvidenceCount, DateTime ObservedUtc, string DrilldownUrl, bool IsWaivable, string? EvidenceHash)
{
    public string Code { get; set; } = Code;
    public string Title { get; set; } = Title;
    public string Explanation { get; set; } = Explanation;
    public string SafeNextAction { get; set; } = SafeNextAction;
    public Guid? OwnerUserId { get; set; } = OwnerUserId;
    public string Status { get; set; } = Status;
    public int EvidenceCount { get; set; } = EvidenceCount;
    public DateTime ObservedUtc { get; set; } = ObservedUtc;
    public string DrilldownUrl { get; set; } = DrilldownUrl;
    public bool IsWaivable { get; set; } = IsWaivable;
    public string? EvidenceHash { get; set; } = EvidenceHash;

    public AccountingCloseWorkspaceBlockerDto() : this(string.Empty, string.Empty, string.Empty, string.Empty, default !, string.Empty, default !, default !, string.Empty, default !, default !)
    {
    }
}

public sealed record AccountingCloseWorkspacePanelDto(string Key, string Title, string Status, int TotalCount, int AttentionCount, DateTime? EvidenceUtc, string DrilldownUrl, IReadOnlyList<string> AllowedActions)
{
    public string Key { get; set; } = Key;
    public string Title { get; set; } = Title;
    public string Status { get; set; } = Status;
    public int TotalCount { get; set; } = TotalCount;
    public int AttentionCount { get; set; } = AttentionCount;
    public DateTime? EvidenceUtc { get; set; } = EvidenceUtc;
    public string DrilldownUrl { get; set; } = DrilldownUrl;
    public IReadOnlyList<string> AllowedActions { get; set; } = AllowedActions;

    public AccountingCloseWorkspacePanelDto() : this(string.Empty, string.Empty, string.Empty, default !, default !, default !, string.Empty, [])
    {
    }
}

public sealed record AccountingCloseWorkspaceTaskDto(Guid Id, string Key, string Title, string Status, Guid? OwnerUserId, string? OwnerRole, DateTime DueUtc, int Sequence, long Version, IReadOnlyList<Guid> PredecessorTaskIds, IReadOnlyList<string> BlockingReasonCodes, IReadOnlyList<AccountingCloseWorkspaceEvidenceDto> Evidence, IReadOnlyList<AccountingCloseWorkspaceBlockerDto> Blockers, IReadOnlyList<string> AllowedActions, string DrilldownUrl)
{
    public Guid Id { get; set; } = Id;
    public string Key { get; set; } = Key;
    public string Title { get; set; } = Title;
    public string Status { get; set; } = Status;
    public Guid? OwnerUserId { get; set; } = OwnerUserId;
    public string? OwnerRole { get; set; } = OwnerRole;
    public DateTime DueUtc { get; set; } = DueUtc;
    public int Sequence { get; set; } = Sequence;
    public long Version { get; set; } = Version;
    public IReadOnlyList<Guid> PredecessorTaskIds { get; set; } = PredecessorTaskIds;
    public IReadOnlyList<string> BlockingReasonCodes { get; set; } = BlockingReasonCodes;
    public IReadOnlyList<AccountingCloseWorkspaceEvidenceDto> Evidence { get; set; } = Evidence;
    public IReadOnlyList<AccountingCloseWorkspaceBlockerDto> Blockers { get; set; } = Blockers;
    public IReadOnlyList<string> AllowedActions { get; set; } = AllowedActions;
    public string DrilldownUrl { get; set; } = DrilldownUrl;

    public AccountingCloseWorkspaceTaskDto() : this(default !, string.Empty, string.Empty, string.Empty, default !, default !, default !, default !, default !, [], [], [], [], [], string.Empty)
    {
    }
}

public sealed record AccountingCloseWorkspaceReadinessDto(Guid SnapshotId, int SnapshotNumber, string Status, bool IsReady, string EvidenceHash, DateTime PreparedUtc, long Version, int BlockingCount, int WarningCount, bool IsStale, IReadOnlyList<AccountingCloseWorkspaceBlockerDto> Blockers)
{
    public Guid SnapshotId { get; set; } = SnapshotId;
    public int SnapshotNumber { get; set; } = SnapshotNumber;
    public string Status { get; set; } = Status;
    public bool IsReady { get; set; } = IsReady;
    public string EvidenceHash { get; set; } = EvidenceHash;
    public DateTime PreparedUtc { get; set; } = PreparedUtc;
    public long Version { get; set; } = Version;
    public int BlockingCount { get; set; } = BlockingCount;
    public int WarningCount { get; set; } = WarningCount;
    public bool IsStale { get; set; } = IsStale;
    public IReadOnlyList<AccountingCloseWorkspaceBlockerDto> Blockers { get; set; } = Blockers;

    public AccountingCloseWorkspaceReadinessDto() : this(default !, default !, string.Empty, default !, string.Empty, default !, default !, default !, default !, default !, [])
    {
    }
}

public sealed record AccountingCloseWorkspaceSignOffDto(Guid Id, string Action, string ActorRole, Guid ActorUserId, DateTime OccurredUtc, string? Reason, string EvidenceHash)
{
    public Guid Id { get; set; } = Id;
    public string Action { get; set; } = Action;
    public string ActorRole { get; set; } = ActorRole;
    public Guid ActorUserId { get; set; } = ActorUserId;
    public DateTime OccurredUtc { get; set; } = OccurredUtc;
    public string? Reason { get; set; } = Reason;
    public string EvidenceHash { get; set; } = EvidenceHash;

    public AccountingCloseWorkspaceSignOffDto() : this(default !, string.Empty, string.Empty, default !, default !, default !, string.Empty)
    {
    }
}

public sealed record AccountingCloseWorkspacePeriodDto(Guid FiscalPeriodId, string Name, DateTime StartUtc, DateTime EndUtc, bool IsClosed, Guid? CloseInstanceId, string? CloseStatus, DateTime? UpdatedUtc)
{
    public Guid FiscalPeriodId { get; set; } = FiscalPeriodId;
    public string Name { get; set; } = Name;
    public DateTime StartUtc { get; set; } = StartUtc;
    public DateTime EndUtc { get; set; } = EndUtc;
    public bool IsClosed { get; set; } = IsClosed;
    public Guid? CloseInstanceId { get; set; } = CloseInstanceId;
    public string? CloseStatus { get; set; } = CloseStatus;
    public DateTime? UpdatedUtc { get; set; } = UpdatedUtc;

    public AccountingCloseWorkspacePeriodDto() : this(default !, string.Empty, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record AccountingCloseWorkspaceNotificationDto(Guid Id, string Priority, string Title, string Body, string Status, DateTime CreatedUtc, string? ActionUrl)
{
    public Guid Id { get; set; } = Id;
    public string Priority { get; set; } = Priority;
    public string Title { get; set; } = Title;
    public string Body { get; set; } = Body;
    public string Status { get; set; } = Status;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public string? ActionUrl { get; set; } = ActionUrl;

    public AccountingCloseWorkspaceNotificationDto() : this(default !, string.Empty, string.Empty, string.Empty, string.Empty, default !, default !)
    {
    }
}

public sealed record AccountingCloseWorkspaceDto(Guid CompanyId, string CompanyName, string MembershipRole, DateTime GeneratedUtc, AccountingCloseWorkspacePeriodDto? SelectedPeriod, Guid? CloseInstanceId, string? CloseName, string? CloseStatus, long? CloseVersion, IReadOnlyList<AccountingCloseWorkspacePeriodDto> Periods, AccountingCloseWorkspaceReadinessDto? Readiness, IReadOnlyList<AccountingCloseWorkspaceTaskDto> Tasks, IReadOnlyList<AccountingCloseWorkspacePanelDto> Panels, IReadOnlyList<AccountingCloseWorkspaceSignOffDto> SignOffs, IReadOnlyList<AccountingCloseWorkspaceNotificationDto> Notifications, IReadOnlyList<string> AllowedActions, string EvidenceNotice = "Readiness and allowed actions are authoritative backend decisions for the displayed evidence timestamp.")
{
    public Guid CompanyId { get; set; } = CompanyId;
    public string CompanyName { get; set; } = CompanyName;
    public string MembershipRole { get; set; } = MembershipRole;
    public DateTime GeneratedUtc { get; set; } = GeneratedUtc;
    public AccountingCloseWorkspacePeriodDto? SelectedPeriod { get; set; } = SelectedPeriod;
    public Guid? CloseInstanceId { get; set; } = CloseInstanceId;
    public string? CloseName { get; set; } = CloseName;
    public string? CloseStatus { get; set; } = CloseStatus;
    public long? CloseVersion { get; set; } = CloseVersion;
    public IReadOnlyList<AccountingCloseWorkspacePeriodDto> Periods { get; set; } = Periods;
    public AccountingCloseWorkspaceReadinessDto? Readiness { get; set; } = Readiness;
    public IReadOnlyList<AccountingCloseWorkspaceTaskDto> Tasks { get; set; } = Tasks;
    public IReadOnlyList<AccountingCloseWorkspacePanelDto> Panels { get; set; } = Panels;
    public IReadOnlyList<AccountingCloseWorkspaceSignOffDto> SignOffs { get; set; } = SignOffs;
    public IReadOnlyList<AccountingCloseWorkspaceNotificationDto> Notifications { get; set; } = Notifications;
    public IReadOnlyList<string> AllowedActions { get; set; } = AllowedActions;
    public string EvidenceNotice { get; set; } = EvidenceNotice;

    public AccountingCloseWorkspaceDto() : this(default !, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, [], default !, [], [], [], [], [], string.Empty)
    {
    }
}

public sealed record AccountingCloseWorkspaceEvidenceDto(Guid Id, Guid DocumentId, string EvidenceType, string Title, string? ContentHash, DateTime LinkedUtc, string DrilldownUrl)
{
    public Guid Id { get; set; } = Id;
    public Guid DocumentId { get; set; } = DocumentId;
    public string EvidenceType { get; set; } = EvidenceType;
    public string Title { get; set; } = Title;
    public string? ContentHash { get; set; } = ContentHash;
    public DateTime LinkedUtc { get; set; } = LinkedUtc;
    public string DrilldownUrl { get; set; } = DrilldownUrl;

    public AccountingCloseWorkspaceEvidenceDto() : this(default !, default !, string.Empty, string.Empty, default !, default !, string.Empty)
    {
    }
}
