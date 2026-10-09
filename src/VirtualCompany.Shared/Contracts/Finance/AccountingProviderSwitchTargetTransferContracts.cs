namespace VirtualCompany.Application.Finance;
public sealed record AccountingProviderSwitchTargetTransferItemDto(Guid Id, Guid StagedRecordId, string Dataset, string SourceIdentity, string SourceVersion, int? MappingVersion, string OperationMode, string Action, string StableIdentity, string Status, Guid? WriteRequestId, Guid? ApprovalRequestId, string? ProviderExternalId, string? FailureCategory, string? SafeSummary, bool ReconciliationNeeded, long Version, IReadOnlyList<AccountingProviderSwitchTargetTransferAttemptDto> Attempts)
{
    public Guid Id { get; set; } = Id;
    public Guid StagedRecordId { get; set; } = StagedRecordId;
    public string Dataset { get; set; } = Dataset;
    public string SourceIdentity { get; set; } = SourceIdentity;
    public string SourceVersion { get; set; } = SourceVersion;
    public int? MappingVersion { get; set; } = MappingVersion;
    public string OperationMode { get; set; } = OperationMode;
    public string Action { get; set; } = Action;
    public string StableIdentity { get; set; } = StableIdentity;
    public string Status { get; set; } = Status;
    public Guid? WriteRequestId { get; set; } = WriteRequestId;
    public Guid? ApprovalRequestId { get; set; } = ApprovalRequestId;
    public string? ProviderExternalId { get; set; } = ProviderExternalId;
    public string? FailureCategory { get; set; } = FailureCategory;
    public string? SafeSummary { get; set; } = SafeSummary;
    public bool ReconciliationNeeded { get; set; } = ReconciliationNeeded;
    public long Version { get; set; } = Version;
    public IReadOnlyList<AccountingProviderSwitchTargetTransferAttemptDto> Attempts { get; set; } = Attempts;

    public AccountingProviderSwitchTargetTransferItemDto() : this(default !, default !, string.Empty, default !, default !, default !, default !, string.Empty, default !, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record AccountingProviderSwitchTargetTransferAttemptDto(Guid Id, int AttemptNumber, string Outcome, string? FailureCategory, string? SafeSummary, bool ProviderAcceptedRequest, DateTime StartedUtc, DateTime? CompletedUtc);
public sealed record AccountingProviderSwitchTargetTransferBatchDto(Guid Id, Guid CompanyId, Guid SwitchId, Guid PlanId, int PlanVersion, string PlanHash, string TargetProviderKey, string PackageHash, string Status, int TotalItemCount, int PreviewItemCount, int PreparatoryItemCount, int FinalItemCount, int CompletedItemCount, int FailedItemCount, int ReconciliationItemCount, string? FailureCode, string? FailureSummary, DateTime RequestedUtc, DateTime? CompletedUtc, long Version, bool IsReadyForCutover, string ReadinessExplanation, IReadOnlyList<AccountingProviderSwitchTargetTransferItemDto> Items)
{
    public Guid Id { get; set; } = Id;
    public Guid CompanyId { get; set; } = CompanyId;
    public Guid SwitchId { get; set; } = SwitchId;
    public Guid PlanId { get; set; } = PlanId;
    public int PlanVersion { get; set; } = PlanVersion;
    public string PlanHash { get; set; } = PlanHash;
    public string TargetProviderKey { get; set; } = TargetProviderKey;
    public string PackageHash { get; set; } = PackageHash;
    public string Status { get; set; } = Status;
    public int TotalItemCount { get; set; } = TotalItemCount;
    public int PreviewItemCount { get; set; } = PreviewItemCount;
    public int PreparatoryItemCount { get; set; } = PreparatoryItemCount;
    public int FinalItemCount { get; set; } = FinalItemCount;
    public int CompletedItemCount { get; set; } = CompletedItemCount;
    public int FailedItemCount { get; set; } = FailedItemCount;
    public int ReconciliationItemCount { get; set; } = ReconciliationItemCount;
    public string? FailureCode { get; set; } = FailureCode;
    public string? FailureSummary { get; set; } = FailureSummary;
    public DateTime RequestedUtc { get; set; } = RequestedUtc;
    public DateTime? CompletedUtc { get; set; } = CompletedUtc;
    public long Version { get; set; } = Version;
    public bool IsReadyForCutover { get; set; } = IsReadyForCutover;
    public string ReadinessExplanation { get; set; } = ReadinessExplanation;
    public IReadOnlyList<AccountingProviderSwitchTargetTransferItemDto> Items { get; set; } = Items;

    public AccountingProviderSwitchTargetTransferBatchDto() : this(default !, default !, default !, default !, default !, default !, default !, default !, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, string.Empty, [])
    {
    }
}
