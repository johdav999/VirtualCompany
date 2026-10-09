namespace VirtualCompany.Application.Finance;
public sealed record AccountingRetentionCleanupResultDto(Guid CompanyId, string RetentionClass, DateTime CompletedUtc, int ProcessedCount, long ReleasedBytes, IReadOnlyList<Guid> ExportIds, string AuditAction)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public string RetentionClass { get; set; } = RetentionClass;
    public DateTime CompletedUtc { get; set; } = CompletedUtc;
    public int ProcessedCount { get; set; } = ProcessedCount;
    public long ReleasedBytes { get; set; } = ReleasedBytes;
    public IReadOnlyList<Guid> ExportIds { get; set; } = ExportIds;
    public string AuditAction { get; set; } = AuditAction;

    public AccountingRetentionCleanupResultDto() : this(default !, "", default !, default !, default !, [], "")
    {
    }
}

public sealed record AccountingRetentionTargetDto(Guid ExportId, Guid FiscalPeriodId, DateTime ExpiresUtc, string FileName, string Checksum, long ContentLength)
{
    public Guid ExportId { get; set; } = ExportId;
    public Guid FiscalPeriodId { get; set; } = FiscalPeriodId;
    public DateTime ExpiresUtc { get; set; } = ExpiresUtc;
    public string FileName { get; set; } = FileName;
    public string Checksum { get; set; } = Checksum;
    public long ContentLength { get; set; } = ContentLength;

    public AccountingRetentionTargetDto() : this(default !, default !, default !, "", "", default !)
    {
    }
}

public sealed record AccountingRetentionPreviewDto(Guid CompanyId, string RetentionClass, DateTime PreviewedUtc, string PreviewToken, int RequestedBatchSize, long EligibleCount, long EligibleBytes, IReadOnlyList<AccountingRetentionTargetDto> Targets, IReadOnlyList<string> PreservedEvidence)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public string RetentionClass { get; set; } = RetentionClass;
    public DateTime PreviewedUtc { get; set; } = PreviewedUtc;
    public string PreviewToken { get; set; } = PreviewToken;
    public int RequestedBatchSize { get; set; } = RequestedBatchSize;
    public long EligibleCount { get; set; } = EligibleCount;
    public long EligibleBytes { get; set; } = EligibleBytes;
    public IReadOnlyList<AccountingRetentionTargetDto> Targets { get; set; } = Targets;
    public IReadOnlyList<string> PreservedEvidence { get; set; } = PreservedEvidence;

    public AccountingRetentionPreviewDto() : this(default !, "", default !, "", default !, default !, default !, [], [])
    {
    }
}

public sealed record AccountingRetentionClassDto(string Key, string DisplayName, string Mode, string Policy, bool RequiresPreview, bool RequiresAudit, bool RegenerationRequired)
{
    public string Key { get; set; } = Key;
    public string DisplayName { get; set; } = DisplayName;
    public string Mode { get; set; } = Mode;
    public string Policy { get; set; } = Policy;
    public bool RequiresPreview { get; set; } = RequiresPreview;
    public bool RequiresAudit { get; set; } = RequiresAudit;
    public bool RegenerationRequired { get; set; } = RegenerationRequired;

    public AccountingRetentionClassDto() : this("", "", "", "", default !, default !, default !)
    {
    }
}

public sealed record AccountingObjectiveMeasurementDto(string ObjectiveKey, decimal? CurrentValue, string Unit, string Status, string Explanation, string Action)
{
    public string ObjectiveKey { get; set; } = ObjectiveKey;
    public decimal? CurrentValue { get; set; } = CurrentValue;
    public string Unit { get; set; } = Unit;
    public string Status { get; set; } = Status;
    public string Explanation { get; set; } = Explanation;
    public string Action { get; set; } = Action;

    public AccountingObjectiveMeasurementDto() : this("", default !, "", "", "", "")
    {
    }
}

public sealed record AccountingVolumeMeasurementDto(string Resource, long CurrentCount, long SupportedCount, string Status)
{
    public string Resource { get; set; } = Resource;
    public long CurrentCount { get; set; } = CurrentCount;
    public long SupportedCount { get; set; } = SupportedCount;
    public string Status { get; set; } = Status;

    public AccountingVolumeMeasurementDto() : this("", default !, default !, "")
    {
    }
}

public sealed record AccountingServiceObjectiveDto(string Key, string DisplayName, string Unit, decimal Objective, decimal WarningThreshold, string MeasurementScope, string Remediation)
{
    public string Key { get; set; } = Key;
    public string DisplayName { get; set; } = DisplayName;
    public string Unit { get; set; } = Unit;
    public decimal Objective { get; set; } = Objective;
    public decimal WarningThreshold { get; set; } = WarningThreshold;
    public string MeasurementScope { get; set; } = MeasurementScope;
    public string Remediation { get; set; } = Remediation;

    public AccountingServiceObjectiveDto() : this("", "", "", default !, default !, "", "")
    {
    }
}

public sealed record AccountingSupportedVolumeProfileDto(string Key, string DisplayName, int ConcurrentUsers, int ConcurrentJobs, IReadOnlyList<AccountingSupportedVolumeDto> Volumes)
{
    public string Key { get; set; } = Key;
    public string DisplayName { get; set; } = DisplayName;
    public int ConcurrentUsers { get; set; } = ConcurrentUsers;
    public int ConcurrentJobs { get; set; } = ConcurrentJobs;
    public IReadOnlyList<AccountingSupportedVolumeDto> Volumes { get; set; } = Volumes;

    public AccountingSupportedVolumeProfileDto() : this("", "", default !, default !, [])
    {
    }
}

public sealed record AccountingSupportedVolumeDto(string Resource, long MaximumCount)
{
    public string Resource { get; set; } = Resource;
    public long MaximumCount { get; set; } = MaximumCount;

    public AccountingSupportedVolumeDto() : this("", default !)
    {
    }
}

public sealed record AccountingCapacityReadModel(Guid CompanyId, string ProfileKey, DateTime MeasuredUtc, IReadOnlyList<AccountingSupportedVolumeProfileDto> Profiles, IReadOnlyList<AccountingServiceObjectiveDto> Objectives, IReadOnlyList<AccountingVolumeMeasurementDto> Volumes, IReadOnlyList<AccountingObjectiveMeasurementDto> Measurements, IReadOnlyList<AccountingRetentionClassDto> RetentionClasses, IReadOnlyList<string> Alerts)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public string ProfileKey { get; set; } = ProfileKey;
    public DateTime MeasuredUtc { get; set; } = MeasuredUtc;
    public IReadOnlyList<AccountingSupportedVolumeProfileDto> Profiles { get; set; } = Profiles;
    public IReadOnlyList<AccountingServiceObjectiveDto> Objectives { get; set; } = Objectives;
    public IReadOnlyList<AccountingVolumeMeasurementDto> Volumes { get; set; } = Volumes;
    public IReadOnlyList<AccountingObjectiveMeasurementDto> Measurements { get; set; } = Measurements;
    public IReadOnlyList<AccountingRetentionClassDto> RetentionClasses { get; set; } = RetentionClasses;
    public IReadOnlyList<string> Alerts { get; set; } = Alerts;

    public AccountingCapacityReadModel() : this(default !, "small", default !, [], [], [], [], [], [])
    {
    }
}
