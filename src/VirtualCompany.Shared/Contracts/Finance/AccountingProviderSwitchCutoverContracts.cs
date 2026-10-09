namespace VirtualCompany.Application.Finance;
public sealed record AccountingProviderSwitchFinalSnapshotDto(Guid Id, string ApprovedSourceSnapshotHash, string FinalSourceSnapshotHash, long RecordCount, decimal FinancialTotal, long DeltaRecordCount, decimal DeltaFinancialTotal, DateTime ExtractionStartedUtc, DateTime ExtractionCompletedUtc);
public sealed record AccountingProviderSwitchCutoverAllowedActionsDto(bool CanStartFreeze, bool CanRequestActivationApproval, bool CanActivate, bool CanCancel, bool CanRetry, bool CanRecoverSource, bool RequiresProviderReconciliation, bool RequiresCorrectiveCutover)
{
    public bool CanStartFreeze { get; set; } = CanStartFreeze;
    public bool CanRequestActivationApproval { get; set; } = CanRequestActivationApproval;
    public bool CanActivate { get; set; } = CanActivate;
    public bool CanCancel { get; set; } = CanCancel;
    public bool CanRetry { get; set; } = CanRetry;
    public bool CanRecoverSource { get; set; } = CanRecoverSource;
    public bool RequiresProviderReconciliation { get; set; } = RequiresProviderReconciliation;
    public bool RequiresCorrectiveCutover { get; set; } = RequiresCorrectiveCutover;

    public AccountingProviderSwitchCutoverAllowedActionsDto() : this(default !, default !, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record AccountingProviderSwitchFinalCheckDto(Guid Id, string CheckKey, string Result, string ReasonCode, string Explanation, string EvidenceJson, DateTime CalculatedUtc)
{
    public Guid Id { get; set; } = Id;
    public string CheckKey { get; set; } = CheckKey;
    public string Result { get; set; } = Result;
    public string ReasonCode { get; set; } = ReasonCode;
    public string Explanation { get; set; } = Explanation;
    public string EvidenceJson { get; set; } = EvidenceJson;
    public DateTime CalculatedUtc { get; set; } = CalculatedUtc;

    public AccountingProviderSwitchFinalCheckDto() : this(default !, string.Empty, string.Empty, default !, string.Empty, default !, default !)
    {
    }
}

public sealed record AccountingProviderSwitchCutoverDto(Guid Id, Guid CompanyId, Guid SwitchId, Guid PlanId, int PlanVersion, string PlanHash, Guid? PreparationId, Guid? TargetTransferBatchId, Guid? AuthorityPeriodId, string Status, string CurrentStep, bool TargetActivityRecorded, bool RetryIsSafe, bool ProviderReconciliationRequired, string? FailureCode, string? FailureSummary, string? NextAction, int AttemptCount, DateTime? NextAttemptUtc, DateTime ScheduledUtc, DateTime RequestedUtc, DateTime? FreezeStartedUtc, DateTime? ReconciledUtc, DateTime? ActivatedUtc, DateTime? CompletedUtc, long Version, AccountingProviderSwitchFinalSnapshotDto? FinalSnapshot, IReadOnlyList<AccountingProviderSwitchFinalCheckDto> Checks, AccountingProviderSwitchActivationApprovalDto? ActivationApproval, AccountingProviderSwitchCutoverAllowedActionsDto AllowedActions)
{
    public Guid Id { get; set; } = Id;
    public Guid CompanyId { get; set; } = CompanyId;
    public Guid SwitchId { get; set; } = SwitchId;
    public Guid PlanId { get; set; } = PlanId;
    public int PlanVersion { get; set; } = PlanVersion;
    public string PlanHash { get; set; } = PlanHash;
    public Guid? PreparationId { get; set; } = PreparationId;
    public Guid? TargetTransferBatchId { get; set; } = TargetTransferBatchId;
    public Guid? AuthorityPeriodId { get; set; } = AuthorityPeriodId;
    public string Status { get; set; } = Status;
    public string CurrentStep { get; set; } = CurrentStep;
    public bool TargetActivityRecorded { get; set; } = TargetActivityRecorded;
    public bool RetryIsSafe { get; set; } = RetryIsSafe;
    public bool ProviderReconciliationRequired { get; set; } = ProviderReconciliationRequired;
    public string? FailureCode { get; set; } = FailureCode;
    public string? FailureSummary { get; set; } = FailureSummary;
    public string? NextAction { get; set; } = NextAction;
    public int AttemptCount { get; set; } = AttemptCount;
    public DateTime? NextAttemptUtc { get; set; } = NextAttemptUtc;
    public DateTime ScheduledUtc { get; set; } = ScheduledUtc;
    public DateTime RequestedUtc { get; set; } = RequestedUtc;
    public DateTime? FreezeStartedUtc { get; set; } = FreezeStartedUtc;
    public DateTime? ReconciledUtc { get; set; } = ReconciledUtc;
    public DateTime? ActivatedUtc { get; set; } = ActivatedUtc;
    public DateTime? CompletedUtc { get; set; } = CompletedUtc;
    public long Version { get; set; } = Version;
    public AccountingProviderSwitchFinalSnapshotDto? FinalSnapshot { get; set; } = FinalSnapshot;
    public IReadOnlyList<AccountingProviderSwitchFinalCheckDto> Checks { get; set; } = Checks;
    public AccountingProviderSwitchActivationApprovalDto? ActivationApproval { get; set; } = ActivationApproval;
    public AccountingProviderSwitchCutoverAllowedActionsDto AllowedActions { get; set; } = AllowedActions;

    public AccountingProviderSwitchCutoverDto() : this(default !, default !, default !, default !, default !, default !, default !, default !, default !, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, [], default !, new())
    {
    }
}

public sealed record AccountingProviderSwitchActivationApprovalDto(Guid ApprovalRequestId, string Status, string FinalSnapshotHash, string ReconciliationHash, long SwitchVersion, DateTime RequestedUtc)
{
    public Guid ApprovalRequestId { get; set; } = ApprovalRequestId;
    public string Status { get; set; } = Status;
    public string FinalSnapshotHash { get; set; } = FinalSnapshotHash;
    public string ReconciliationHash { get; set; } = ReconciliationHash;
    public long SwitchVersion { get; set; } = SwitchVersion;
    public DateTime RequestedUtc { get; set; } = RequestedUtc;

    public AccountingProviderSwitchActivationApprovalDto() : this(default !, string.Empty, default !, default !, default !, default !)
    {
    }
}
