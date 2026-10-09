namespace VirtualCompany.Application.Finance;
public sealed record AccountingProviderSwitchArchiveDependencyDto(Guid Id, Guid CompanyId, Guid SwitchId, Guid PreparedByRunId, Guid? StagedRecordId, string Dataset, string SourceIdentity, string ReasonCode, string Explanation, string EvidenceHash, Guid ApprovedPlanId, string ApprovedPlanHash, DateTime CreatedUtc);
public sealed record AccountingProviderSwitchPreparationDto(Guid Id, Guid CompanyId, Guid SwitchId, Guid PlanId, string PlanHash, string Strategy, string Status, int CompletedWorkItems, int TotalWorkItems, int ProgressPercent, int CandidateCount, int ValidCandidateCount, int RejectedCandidateCount, int ExistingReferenceCount, int ArchiveDependencyCount, int AttemptCount, DateTime? NextAttemptUtc, string? FailureCode, string? FailureSummary, DateTime RequestedUtc, DateTime? StartedUtc, DateTime? CompletedUtc, long Version, bool IsActivationReady, string ActivationReadinessExplanation, AccountingProviderSwitchInternalReadinessDto Readiness, IReadOnlyList<AccountingProviderSwitchNativeCandidateDto> Candidates, IReadOnlyList<AccountingProviderSwitchArchiveDependencyDto> ArchiveDependencies)
{
    public Guid Id { get; set; } = Id;
    public Guid CompanyId { get; set; } = CompanyId;
    public Guid SwitchId { get; set; } = SwitchId;
    public Guid PlanId { get; set; } = PlanId;
    public string PlanHash { get; set; } = PlanHash;
    public string Strategy { get; set; } = Strategy;
    public string Status { get; set; } = Status;
    public int CompletedWorkItems { get; set; } = CompletedWorkItems;
    public int TotalWorkItems { get; set; } = TotalWorkItems;
    public int ProgressPercent { get; set; } = ProgressPercent;
    public int CandidateCount { get; set; } = CandidateCount;
    public int ValidCandidateCount { get; set; } = ValidCandidateCount;
    public int RejectedCandidateCount { get; set; } = RejectedCandidateCount;
    public int ExistingReferenceCount { get; set; } = ExistingReferenceCount;
    public int ArchiveDependencyCount { get; set; } = ArchiveDependencyCount;
    public int AttemptCount { get; set; } = AttemptCount;
    public DateTime? NextAttemptUtc { get; set; } = NextAttemptUtc;
    public string? FailureCode { get; set; } = FailureCode;
    public string? FailureSummary { get; set; } = FailureSummary;
    public DateTime RequestedUtc { get; set; } = RequestedUtc;
    public DateTime? StartedUtc { get; set; } = StartedUtc;
    public DateTime? CompletedUtc { get; set; } = CompletedUtc;
    public long Version { get; set; } = Version;
    public bool IsActivationReady { get; set; } = IsActivationReady;
    public string ActivationReadinessExplanation { get; set; } = ActivationReadinessExplanation;
    public AccountingProviderSwitchInternalReadinessDto Readiness { get; set; } = Readiness;
    public IReadOnlyList<AccountingProviderSwitchNativeCandidateDto> Candidates { get; set; } = Candidates;
    public IReadOnlyList<AccountingProviderSwitchArchiveDependencyDto> ArchiveDependencies { get; set; } = ArchiveDependencies;

    public AccountingProviderSwitchPreparationDto() : this(default !, default !, default !, default !, default !, default !, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, string.Empty, default !, default !, default !)
    {
    }
}

public sealed record AccountingProviderSwitchInternalReadinessDto(Guid CompanyId, Guid SwitchId, Guid? PlanId, string? PlanHash, bool IsReady, bool IsStatutoryComplianceValidated, string ComplianceDisclosure, IReadOnlyList<AccountingProviderSwitchReadinessCheckDto> Checks, IReadOnlyList<AccountingProviderSwitchGapDto> UnresolvedGaps)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public Guid SwitchId { get; set; } = SwitchId;
    public Guid? PlanId { get; set; } = PlanId;
    public string? PlanHash { get; set; } = PlanHash;
    public bool IsReady { get; set; } = IsReady;
    public bool IsStatutoryComplianceValidated { get; set; } = IsStatutoryComplianceValidated;
    public string ComplianceDisclosure { get; set; } = ComplianceDisclosure;
    public IReadOnlyList<AccountingProviderSwitchReadinessCheckDto> Checks { get; set; } = Checks;
    public IReadOnlyList<AccountingProviderSwitchGapDto> UnresolvedGaps { get; set; } = UnresolvedGaps;

    public AccountingProviderSwitchInternalReadinessDto() : this(default !, default !, default !, default !, default !, default !, string.Empty, [], default !)
    {
    }
}

public sealed record AccountingProviderSwitchReadinessCheckDto(string CheckKey, bool IsReady, bool IsBlocking, string? ReasonCode, string Explanation, string EvidenceJson)
{
    public string CheckKey { get; set; } = CheckKey;
    public bool IsReady { get; set; } = IsReady;
    public bool IsBlocking { get; set; } = IsBlocking;
    public string? ReasonCode { get; set; } = ReasonCode;
    public string Explanation { get; set; } = Explanation;
    public string EvidenceJson { get; set; } = EvidenceJson;

    public AccountingProviderSwitchReadinessCheckDto() : this(string.Empty, default !, default !, default !, string.Empty, default !)
    {
    }
}

public sealed record AccountingProviderSwitchCandidateValidationDto(Guid Id, string ReasonCode, bool IsBlocking, string Explanation, string EvidenceJson, DateTime ValidatedUtc);
public sealed record AccountingProviderSwitchNativeCandidateDto(Guid Id, Guid CompanyId, Guid SwitchId, Guid PreparedByRunId, Guid StagedRecordId, string CandidateKind, string SourceDataset, string SourceIdentity, string SourceVersion, string SourceHash, string IdempotencyKey, Guid? FiscalPeriodId, DateOnly? DocumentDate, DateOnly? PostingDate, decimal FinancialAmount, string? Currency, string Status, string PayloadJson, string EvidenceHash, Guid? ExternalReferenceId, DateTime CreatedUtc, DateTime UpdatedUtc, IReadOnlyList<AccountingProviderSwitchCandidateValidationDto> Validations);
