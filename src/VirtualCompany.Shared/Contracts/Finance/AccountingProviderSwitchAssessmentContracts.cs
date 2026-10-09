using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Application.Finance;
public sealed record AccountingProviderSwitchDatasetDto(string EndpointRole, string DatasetKey, string Availability, string CapabilityLevel, long RecordCount, decimal FinancialTotal, string? Currency, string? SourceCursor, string? SourceVersion, string IntegrityHash, string EvidenceJson, string? FailureCode, string? FailureSummary, DateTime ExtractedUtc)
{
    public string EndpointRole { get; set; } = EndpointRole;
    public string DatasetKey { get; set; } = DatasetKey;
    public string Availability { get; set; } = Availability;
    public string CapabilityLevel { get; set; } = CapabilityLevel;
    public long RecordCount { get; set; } = RecordCount;
    public decimal FinancialTotal { get; set; } = FinancialTotal;
    public string? Currency { get; set; } = Currency;
    public string? SourceCursor { get; set; } = SourceCursor;
    public string? SourceVersion { get; set; } = SourceVersion;
    public string IntegrityHash { get; set; } = IntegrityHash;
    public string EvidenceJson { get; set; } = EvidenceJson;
    public string? FailureCode { get; set; } = FailureCode;
    public string? FailureSummary { get; set; } = FailureSummary;
    public DateTime ExtractedUtc { get; set; } = ExtractedUtc;

    public AccountingProviderSwitchDatasetDto() : this(string.Empty, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record AccountingProviderSwitchCapabilityDto(string EndpointRole, string CapabilityKey, string Level, string Explanation, string? RequiredScope, DateTime ObservedUtc)
{
    public string EndpointRole { get; set; } = EndpointRole;
    public string CapabilityKey { get; set; } = CapabilityKey;
    public string Level { get; set; } = Level;
    public string Explanation { get; set; } = Explanation;
    public string? RequiredScope { get; set; } = RequiredScope;
    public DateTime ObservedUtc { get; set; } = ObservedUtc;

    public AccountingProviderSwitchCapabilityDto() : this(string.Empty, string.Empty, string.Empty, string.Empty, default !, default !)
    {
    }
}

public sealed record AccountingProviderSwitchAssessmentDto(Guid Id, Guid CompanyId, Guid SwitchId, string Status, int CompletedWorkItems, int TotalWorkItems, int ProgressPercent, int AttemptCount, DateTime? NextAttemptUtc, string? FailureCode, string? FailureSummary, DateTime RequestedUtc, DateTime? StartedUtc, DateTime? CompletedUtc, IReadOnlyList<AccountingProviderSwitchCapabilityDto> Capabilities, IReadOnlyList<AccountingProviderSwitchDatasetDto> Datasets, IReadOnlyList<AccountingProviderSwitchGapDto> Gaps, bool HasBlockingGaps, string AllowedNextAction, string AllowedNextActionExplanation)
{
    public Guid Id { get; set; } = Id;
    public Guid CompanyId { get; set; } = CompanyId;
    public Guid SwitchId { get; set; } = SwitchId;
    public string Status { get; set; } = Status;
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
    public IReadOnlyList<AccountingProviderSwitchCapabilityDto> Capabilities { get; set; } = Capabilities;
    public IReadOnlyList<AccountingProviderSwitchDatasetDto> Datasets { get; set; } = Datasets;
    public IReadOnlyList<AccountingProviderSwitchGapDto> Gaps { get; set; } = Gaps;
    public bool HasBlockingGaps { get; set; } = HasBlockingGaps;
    public string AllowedNextAction { get; set; } = AllowedNextAction;
    public string AllowedNextActionExplanation { get; set; } = AllowedNextActionExplanation;

    public AccountingProviderSwitchAssessmentDto() : this(default !, default !, default !, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, [], [], [], default !, string.Empty, string.Empty)
    {
    }
}

public sealed record AccountingProviderSwitchGapDto(Guid Id, string Category, string? DatasetKey, string Severity, bool IsBlocking, string ReasonCode, string Explanation, string EvidenceJson, string OperatorAction, DateTime CreatedUtc)
{
    public Guid Id { get; set; } = Id;
    public string Category { get; set; } = Category;
    public string? DatasetKey { get; set; } = DatasetKey;
    public string Severity { get; set; } = Severity;
    public bool IsBlocking { get; set; } = IsBlocking;
    public string ReasonCode { get; set; } = ReasonCode;
    public string Explanation { get; set; } = Explanation;
    public string EvidenceJson { get; set; } = EvidenceJson;
    public string OperatorAction { get; set; } = OperatorAction;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;

    public AccountingProviderSwitchGapDto() : this(default !, string.Empty, default !, string.Empty, default !, default !, string.Empty, default !, string.Empty, default !)
    {
    }
}
