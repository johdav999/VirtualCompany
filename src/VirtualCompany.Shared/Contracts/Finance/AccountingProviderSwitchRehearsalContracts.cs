namespace VirtualCompany.Application.Finance;
public sealed record AccountingProviderSwitchCutoverPlanDto(Guid Id, Guid CompanyId, Guid SwitchId, Guid RehearsalId, int PlanVersion, string PlanHash, string SourceSnapshotHash, string Strategy, DateTime FreezeStartsUtc, DateTime FreezeEndsUtc, string RecoveryBoundary, string ParticipantsJson, string SnapshotJson, Guid GeneratedByUserId, DateTime GeneratedUtc, Guid? ApprovalRequestId, string? ApprovalStatus, bool IsCurrent, bool IsApprovedAndCurrent)
{
    public Guid Id { get; set; } = Id;
    public Guid CompanyId { get; set; } = CompanyId;
    public Guid SwitchId { get; set; } = SwitchId;
    public Guid RehearsalId { get; set; } = RehearsalId;
    public int PlanVersion { get; set; } = PlanVersion;
    public string PlanHash { get; set; } = PlanHash;
    public string SourceSnapshotHash { get; set; } = SourceSnapshotHash;
    public string Strategy { get; set; } = Strategy;
    public DateTime FreezeStartsUtc { get; set; } = FreezeStartsUtc;
    public DateTime FreezeEndsUtc { get; set; } = FreezeEndsUtc;
    public string RecoveryBoundary { get; set; } = RecoveryBoundary;
    public string ParticipantsJson { get; set; } = ParticipantsJson;
    public string SnapshotJson { get; set; } = SnapshotJson;
    public Guid GeneratedByUserId { get; set; } = GeneratedByUserId;
    public DateTime GeneratedUtc { get; set; } = GeneratedUtc;
    public Guid? ApprovalRequestId { get; set; } = ApprovalRequestId;
    public string? ApprovalStatus { get; set; } = ApprovalStatus;
    public bool IsCurrent { get; set; } = IsCurrent;
    public bool IsApprovedAndCurrent { get; set; } = IsApprovedAndCurrent;

    public AccountingProviderSwitchCutoverPlanDto() : this(default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record AccountingProviderSwitchReconciliationCheckDto(Guid Id, string CheckKey, string ExpectedValue, string ObservedValue, decimal Tolerance, string? Currency, string Result, string ReasonCode, string DataSourcesJson, string CalculationVersion, bool ManualEvidenceAllowed, bool HasCurrentManualEvidence, DateTime CalculatedUtc)
{
    public Guid Id { get; set; } = Id;
    public string CheckKey { get; set; } = CheckKey;
    public string ExpectedValue { get; set; } = ExpectedValue;
    public string ObservedValue { get; set; } = ObservedValue;
    public decimal Tolerance { get; set; } = Tolerance;
    public string? Currency { get; set; } = Currency;
    public string Result { get; set; } = Result;
    public string ReasonCode { get; set; } = ReasonCode;
    public string DataSourcesJson { get; set; } = DataSourcesJson;
    public string CalculationVersion { get; set; } = CalculationVersion;
    public bool ManualEvidenceAllowed { get; set; } = ManualEvidenceAllowed;
    public bool HasCurrentManualEvidence { get; set; } = HasCurrentManualEvidence;
    public DateTime CalculatedUtc { get; set; } = CalculatedUtc;

    public AccountingProviderSwitchReconciliationCheckDto() : this(default !, string.Empty, string.Empty, string.Empty, default !, default !, string.Empty, default !, string.Empty, default !, default !, default !, default !)
    {
    }
}

public sealed record AccountingProviderSwitchManualEvidenceDto(Guid Id, Guid CheckId, string Explanation, string EvidenceReference, Guid RecordedByUserId, DateTime RecordedUtc, DateTime? ExpiresUtc);
public sealed record AccountingProviderSwitchRehearsalDatasetResultDto(Guid Id, string Dataset, long ExpectedCount, long ObservedCount, decimal ExpectedTotal, decimal ObservedTotal, string? Currency, string Result, string ReasonCode, string EvidenceJson, DateTime CalculatedUtc)
{
    public Guid Id { get; set; } = Id;
    public string Dataset { get; set; } = Dataset;
    public long ExpectedCount { get; set; } = ExpectedCount;
    public long ObservedCount { get; set; } = ObservedCount;
    public decimal ExpectedTotal { get; set; } = ExpectedTotal;
    public decimal ObservedTotal { get; set; } = ObservedTotal;
    public string? Currency { get; set; } = Currency;
    public string Result { get; set; } = Result;
    public string ReasonCode { get; set; } = ReasonCode;
    public string EvidenceJson { get; set; } = EvidenceJson;
    public DateTime CalculatedUtc { get; set; } = CalculatedUtc;

    public AccountingProviderSwitchRehearsalDatasetResultDto() : this(default !, string.Empty, default !, default !, default !, default !, default !, string.Empty, default !, default !, default !)
    {
    }
}

public sealed record AccountingProviderSwitchRehearsalInputDto(Guid Id, long SwitchVersion, string Strategy, string SourceSnapshotHash, string StagingHash, string MappingHash, string GapHash, long StagedRecordCount, decimal FinancialTotal, string DatasetSummaryJson, DateTime CreatedUtc);
public sealed record AccountingProviderSwitchPlanReadinessDto(Guid SwitchId, AccountingProviderSwitchCutoverPlanDto? Plan, bool IsReady, string? BlockingReasonCode, string Explanation)
{
    public Guid SwitchId { get; set; } = SwitchId;
    public AccountingProviderSwitchCutoverPlanDto? Plan { get; set; } = Plan;
    public bool IsReady { get; set; } = IsReady;
    public string? BlockingReasonCode { get; set; } = BlockingReasonCode;
    public string Explanation { get; set; } = Explanation;

    public AccountingProviderSwitchPlanReadinessDto() : this(default !, default !, default !, default !, string.Empty)
    {
    }
}

public sealed record AccountingProviderSwitchRehearsalDto(Guid Id, Guid CompanyId, Guid SwitchId, string Status, string? SimulationKind, bool ProviderAcceptanceProven, string? Disclosure, int CompletedWorkItems, int TotalWorkItems, int ProgressPercent, int AttemptCount, DateTime? NextAttemptUtc, string? FailureCode, string? FailureSummary, DateTime RequestedUtc, DateTime? StartedUtc, DateTime? CompletedUtc, long Version, AccountingProviderSwitchRehearsalInputDto? Input, IReadOnlyList<AccountingProviderSwitchRehearsalDatasetResultDto> Datasets, IReadOnlyList<AccountingProviderSwitchReconciliationCheckDto> Checks, IReadOnlyList<AccountingProviderSwitchManualEvidenceDto> ManualEvidence, bool IsReadyForPlan, string ReadinessExplanation)
{
    public Guid Id { get; set; } = Id;
    public Guid CompanyId { get; set; } = CompanyId;
    public Guid SwitchId { get; set; } = SwitchId;
    public string Status { get; set; } = Status;
    public string? SimulationKind { get; set; } = SimulationKind;
    public bool ProviderAcceptanceProven { get; set; } = ProviderAcceptanceProven;
    public string? Disclosure { get; set; } = Disclosure;
    public int CompletedWorkItems { get; set; } = CompletedWorkItems;
    public int TotalWorkItems { get; set; } = TotalWorkItems;
    public int ProgressPercent { get; set; } = ProgressPercent;
    public int AttemptCount { get; set; } = AttemptCount;
    public DateTime? NextAttemptUtc { get; set; } = NextAttemptUtc;
    public string? FailureCode { get; set; } = FailureCode;
    public string? FailureSummary { get; set; } = FailureSummary;
    public DateTime RequestedUtc { get; set; } = RequestedUtc;
    public DateTime? StartedUtc { get; set; } = StartedUtc;
    public DateTime? CompletedUtc { get; set; } = CompletedUtc;
    public long Version { get; set; } = Version;
    public AccountingProviderSwitchRehearsalInputDto? Input { get; set; } = Input;
    public IReadOnlyList<AccountingProviderSwitchRehearsalDatasetResultDto> Datasets { get; set; } = Datasets;
    public IReadOnlyList<AccountingProviderSwitchReconciliationCheckDto> Checks { get; set; } = Checks;
    public IReadOnlyList<AccountingProviderSwitchManualEvidenceDto> ManualEvidence { get; set; } = ManualEvidence;
    public bool IsReadyForPlan { get; set; } = IsReadyForPlan;
    public string ReadinessExplanation { get; set; } = ReadinessExplanation;

    public AccountingProviderSwitchRehearsalDto() : this(default !, default !, default !, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, [], [], default !, default !, string.Empty)
    {
    }
}
