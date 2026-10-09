namespace VirtualCompany.Application.Finance;
public sealed record AccountingProviderSwitchCompletenessDto(Guid SwitchId, bool IsComplete, long ExpectedCount, long StagedCount, long ValidDispositionCount, long BlockingCount, IReadOnlyList<AccountingProviderSwitchDispositionCountDto> Dispositions, IReadOnlyList<AccountingProviderSwitchDatasetCompletenessDto> Datasets, string Explanation)
{
    public Guid SwitchId { get; set; } = SwitchId;
    public bool IsComplete { get; set; } = IsComplete;
    public long ExpectedCount { get; set; } = ExpectedCount;
    public long StagedCount { get; set; } = StagedCount;
    public long ValidDispositionCount { get; set; } = ValidDispositionCount;
    public long BlockingCount { get; set; } = BlockingCount;
    public IReadOnlyList<AccountingProviderSwitchDispositionCountDto> Dispositions { get; set; } = Dispositions;
    public IReadOnlyList<AccountingProviderSwitchDatasetCompletenessDto> Datasets { get; set; } = Datasets;
    public string Explanation { get; set; } = Explanation;

    public AccountingProviderSwitchCompletenessDto() : this(default !, default !, default !, default !, default !, default !, default !, [], string.Empty)
    {
    }
}

public sealed record AccountingProviderSwitchDatasetCompletenessDto(string Dataset, long ExpectedCount, long StagedCount, long ValidDispositionCount, bool IsComplete, string Explanation)
{
    public string Dataset { get; set; } = Dataset;
    public long ExpectedCount { get; set; } = ExpectedCount;
    public long StagedCount { get; set; } = StagedCount;
    public long ValidDispositionCount { get; set; } = ValidDispositionCount;
    public bool IsComplete { get; set; } = IsComplete;
    public string Explanation { get; set; } = Explanation;

    public AccountingProviderSwitchDatasetCompletenessDto() : this(string.Empty, default !, default !, default !, default !, string.Empty)
    {
    }
}

public sealed record AccountingProviderSwitchDispositionCountDto(string Disposition, long Count, decimal FinancialTotal);
public sealed record AccountingProviderSwitchMappingDecisionDto(Guid Id, Guid MappingSetId, int MappingVersion, string MappingType, string SourceKey, string? TargetKey, string SuggestionMethod, decimal Confidence, string EvidenceJson, bool IsMaterial, long AffectedRecordCount, decimal AffectedFinancialTotal, string Status, Guid? ApprovalRequestId, bool IsApprovalCurrent, DateTime CreatedUtc, DateTime UpdatedUtc, long Version)
{
    public Guid Id { get; set; } = Id;
    public Guid MappingSetId { get; set; } = MappingSetId;
    public int MappingVersion { get; set; } = MappingVersion;
    public string MappingType { get; set; } = MappingType;
    public string SourceKey { get; set; } = SourceKey;
    public string? TargetKey { get; set; } = TargetKey;
    public string SuggestionMethod { get; set; } = SuggestionMethod;
    public decimal Confidence { get; set; } = Confidence;
    public string EvidenceJson { get; set; } = EvidenceJson;
    public bool IsMaterial { get; set; } = IsMaterial;
    public long AffectedRecordCount { get; set; } = AffectedRecordCount;
    public decimal AffectedFinancialTotal { get; set; } = AffectedFinancialTotal;
    public string Status { get; set; } = Status;
    public Guid? ApprovalRequestId { get; set; } = ApprovalRequestId;
    public bool IsApprovalCurrent { get; set; } = IsApprovalCurrent;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;
    public long Version { get; set; } = Version;

    public AccountingProviderSwitchMappingDecisionDto() : this(default !, default !, default !, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, default !, string.Empty, default !, default !, default !, default !, default !)
    {
    }
}
