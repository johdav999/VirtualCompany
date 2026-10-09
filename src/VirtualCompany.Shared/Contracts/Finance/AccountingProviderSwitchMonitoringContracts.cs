namespace VirtualCompany.Application.Finance;
public sealed record AccountingProviderSwitchMonitoringIncidentDto(Guid Id, string CheckKey, string Severity, bool IsBlocking, string Explanation, string Status, Guid? TaskId, int OccurrenceCount, DateTime FirstObservedUtc, DateTime LastObservedUtc, Guid? AcceptedByUserId, string? ExceptionExplanation, string? ExceptionScope, decimal? FinancialImpact, string? EvidenceReference, long Version)
{
    public Guid Id { get; set; } = Id;
    public string CheckKey { get; set; } = CheckKey;
    public string Severity { get; set; } = Severity;
    public bool IsBlocking { get; set; } = IsBlocking;
    public string Explanation { get; set; } = Explanation;
    public string Status { get; set; } = Status;
    public Guid? TaskId { get; set; } = TaskId;
    public int OccurrenceCount { get; set; } = OccurrenceCount;
    public DateTime FirstObservedUtc { get; set; } = FirstObservedUtc;
    public DateTime LastObservedUtc { get; set; } = LastObservedUtc;
    public Guid? AcceptedByUserId { get; set; } = AcceptedByUserId;
    public string? ExceptionExplanation { get; set; } = ExceptionExplanation;
    public string? ExceptionScope { get; set; } = ExceptionScope;
    public decimal? FinancialImpact { get; set; } = FinancialImpact;
    public string? EvidenceReference { get; set; } = EvidenceReference;
    public long Version { get; set; } = Version;

    public AccountingProviderSwitchMonitoringIncidentDto() : this(default !, string.Empty, string.Empty, default !, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record AccountingProviderSwitchMonitoringCheckDto(string CheckKey, string Status, string Severity, bool IsBlocking, string ReasonCode, string Explanation, string EvidenceJson, DateTime ObservedUtc)
{
    public string CheckKey { get; set; } = CheckKey;
    public string Status { get; set; } = Status;
    public string Severity { get; set; } = Severity;
    public bool IsBlocking { get; set; } = IsBlocking;
    public string ReasonCode { get; set; } = ReasonCode;
    public string Explanation { get; set; } = Explanation;
    public string EvidenceJson { get; set; } = EvidenceJson;
    public DateTime ObservedUtc { get; set; } = ObservedUtc;

    public AccountingProviderSwitchMonitoringCheckDto() : this(string.Empty, string.Empty, string.Empty, default !, default !, string.Empty, default !, default !)
    {
    }
}

public sealed record AccountingProviderSwitchMonitoringDto(Guid Id, Guid CompanyId, Guid SwitchId, Guid ActivationExecutionId, int WindowDays, Guid AssignedOwnerUserId, Guid? AssignedOwnerAgentId, string Status, int CheckSequence, int AttemptCount, int ConsecutiveFailureCount, DateTime StartedUtc, DateTime WindowEndsUtc, DateTime? LastSuccessfulCheckUtc, DateTime? NextRunUtc, string? FailureCode, string? FailureSummary, Guid? ClosureApprovalRequestId, Guid? CorrectiveSwitchId, DateTime? ClosedUtc, long Version, IReadOnlyList<AccountingProviderSwitchMonitoringCheckDto> Checks, IReadOnlyList<AccountingProviderSwitchMonitoringIncidentDto> Incidents, AccountingProviderSwitchMonitoringAllowedActionsDto AllowedActions)
{
    public Guid Id { get; set; } = Id;
    public Guid CompanyId { get; set; } = CompanyId;
    public Guid SwitchId { get; set; } = SwitchId;
    public Guid ActivationExecutionId { get; set; } = ActivationExecutionId;
    public int WindowDays { get; set; } = WindowDays;
    public Guid AssignedOwnerUserId { get; set; } = AssignedOwnerUserId;
    public Guid? AssignedOwnerAgentId { get; set; } = AssignedOwnerAgentId;
    public string Status { get; set; } = Status;
    public int CheckSequence { get; set; } = CheckSequence;
    public int AttemptCount { get; set; } = AttemptCount;
    public int ConsecutiveFailureCount { get; set; } = ConsecutiveFailureCount;
    public DateTime StartedUtc { get; set; } = StartedUtc;
    public DateTime WindowEndsUtc { get; set; } = WindowEndsUtc;
    public DateTime? LastSuccessfulCheckUtc { get; set; } = LastSuccessfulCheckUtc;
    public DateTime? NextRunUtc { get; set; } = NextRunUtc;
    public string? FailureCode { get; set; } = FailureCode;
    public string? FailureSummary { get; set; } = FailureSummary;
    public Guid? ClosureApprovalRequestId { get; set; } = ClosureApprovalRequestId;
    public Guid? CorrectiveSwitchId { get; set; } = CorrectiveSwitchId;
    public DateTime? ClosedUtc { get; set; } = ClosedUtc;
    public long Version { get; set; } = Version;
    public IReadOnlyList<AccountingProviderSwitchMonitoringCheckDto> Checks { get; set; } = Checks;
    public IReadOnlyList<AccountingProviderSwitchMonitoringIncidentDto> Incidents { get; set; } = Incidents;
    public AccountingProviderSwitchMonitoringAllowedActionsDto AllowedActions { get; set; } = AllowedActions;

    public AccountingProviderSwitchMonitoringDto() : this(default !, default !, default !, default !, default !, default !, default !, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, [], [], new())
    {
    }
}

public sealed record AccountingProviderSwitchMonitoringAllowedActionsDto(bool CanRunNow, bool CanRetry, bool CanReconnectAccess, bool CanReconcileProviderOutcome, bool CanRequestClosure, bool CanClose, bool CanCreateCorrectiveCutover, string Explanation)
{
    public bool CanRunNow { get; set; } = CanRunNow;
    public bool CanRetry { get; set; } = CanRetry;
    public bool CanReconnectAccess { get; set; } = CanReconnectAccess;
    public bool CanReconcileProviderOutcome { get; set; } = CanReconcileProviderOutcome;
    public bool CanRequestClosure { get; set; } = CanRequestClosure;
    public bool CanClose { get; set; } = CanClose;
    public bool CanCreateCorrectiveCutover { get; set; } = CanCreateCorrectiveCutover;
    public string Explanation { get; set; } = Explanation;

    public AccountingProviderSwitchMonitoringAllowedActionsDto() : this(default !, default !, default !, default !, default !, default !, default !, string.Empty)
    {
    }
}

public sealed record AccountingProviderSwitchOperationsDto(Guid CompanyId, DateTime CalculatedUtc, long StuckWorkflows, long ExpiredApprovals, long StaleFreezes, long ExhaustedRetries, long AmbiguousOutcomes, long UnreconciledTotals, IReadOnlyList<AccountingProviderSwitchOperationIssueDto> Issues)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public DateTime CalculatedUtc { get; set; } = CalculatedUtc;
    public long StuckWorkflows { get; set; } = StuckWorkflows;
    public long ExpiredApprovals { get; set; } = ExpiredApprovals;
    public long StaleFreezes { get; set; } = StaleFreezes;
    public long ExhaustedRetries { get; set; } = ExhaustedRetries;
    public long AmbiguousOutcomes { get; set; } = AmbiguousOutcomes;
    public long UnreconciledTotals { get; set; } = UnreconciledTotals;
    public IReadOnlyList<AccountingProviderSwitchOperationIssueDto> Issues { get; set; } = Issues;

    public AccountingProviderSwitchOperationsDto() : this(default !, default !, default !, default !, default !, default !, default !, default !, [])
    {
    }
}

public sealed record AccountingProviderSwitchOperationIssueDto(string Category, string Severity, long Count, string Explanation, string NextAction)
{
    public string Category { get; set; } = Category;
    public string Severity { get; set; } = Severity;
    public long Count { get; set; } = Count;
    public string Explanation { get; set; } = Explanation;
    public string NextAction { get; set; } = NextAction;

    public AccountingProviderSwitchOperationIssueDto() : this(string.Empty, string.Empty, default !, string.Empty, string.Empty)
    {
    }
}
