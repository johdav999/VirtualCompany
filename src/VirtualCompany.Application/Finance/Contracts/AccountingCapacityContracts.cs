namespace VirtualCompany.Application.Finance;

public static class AccountingCapacityProfileKeys
{
    public const string Small = "small";
    public const string Medium = "medium";
}

public static class AccountingCapacityStatuses
{
    public const string WithinObjective = "within_objective";
    public const string Attention = "attention";
    public const string Breached = "breached";
    public const string NotMeasured = "not_measured";
}

public static class AccountingRetentionModes
{
    public const string Preserve = "preserve";
    public const string MetadataOnlyCleanup = "metadata_only_cleanup";
    public const string BoundedDelete = "bounded_delete";
}

public static class AccountingRetentionClassKeys
{
    public const string AccountingTruth = "accounting_truth";
    public const string SourceEvidence = "source_evidence";
    public const string ApprovalAndAudit = "approval_and_audit";
    public const string ProviderReconciliation = "provider_reconciliation";
    public const string GeneratedExports = "generated_exports";
    public const string OperationalAttempts = "operational_attempts";
    public const string SimulationData = "simulation_data";
    public const string EphemeralCaches = "ephemeral_caches";
}

public static class AccountingLifecycleReasonCodes
{
    public const string PreviewStale = "accounting_retention_preview_stale";
    public const string CleanupNotEligible = "accounting_retention_cleanup_not_eligible";
}

public sealed record AccountingSupportedVolumeDto(string Resource, long MaximumCount);

public sealed record AccountingSupportedVolumeProfileDto(
    string Key,
    string DisplayName,
    int ConcurrentUsers,
    int ConcurrentJobs,
    IReadOnlyList<AccountingSupportedVolumeDto> Volumes);

public sealed record AccountingServiceObjectiveDto(
    string Key,
    string DisplayName,
    string Unit,
    decimal Objective,
    decimal WarningThreshold,
    string MeasurementScope,
    string Remediation);

public sealed record AccountingVolumeMeasurementDto(
    string Resource,
    long CurrentCount,
    long SupportedCount,
    string Status);

public sealed record AccountingObjectiveMeasurementDto(
    string ObjectiveKey,
    decimal? CurrentValue,
    string Unit,
    string Status,
    string Explanation,
    string Action);

public sealed record AccountingRetentionClassDto(
    string Key,
    string DisplayName,
    string Mode,
    string Policy,
    bool RequiresPreview,
    bool RequiresAudit,
    bool RegenerationRequired);

public sealed record AccountingCapacityReadModel(
    Guid CompanyId,
    string ProfileKey,
    DateTime MeasuredUtc,
    IReadOnlyList<AccountingSupportedVolumeProfileDto> Profiles,
    IReadOnlyList<AccountingServiceObjectiveDto> Objectives,
    IReadOnlyList<AccountingVolumeMeasurementDto> Volumes,
    IReadOnlyList<AccountingObjectiveMeasurementDto> Measurements,
    IReadOnlyList<AccountingRetentionClassDto> RetentionClasses,
    IReadOnlyList<string> Alerts);

public sealed record AccountingRetentionTargetDto(
    Guid ExportId,
    Guid FiscalPeriodId,
    DateTime ExpiresUtc,
    string FileName,
    string Checksum,
    long ContentLength);

public sealed record AccountingRetentionPreviewDto(
    Guid CompanyId,
    string RetentionClass,
    DateTime PreviewedUtc,
    string PreviewToken,
    int RequestedBatchSize,
    long EligibleCount,
    long EligibleBytes,
    IReadOnlyList<AccountingRetentionTargetDto> Targets,
    IReadOnlyList<string> PreservedEvidence);

public sealed record AccountingRetentionCleanupResultDto(
    Guid CompanyId,
    string RetentionClass,
    DateTime CompletedUtc,
    int ProcessedCount,
    long ReleasedBytes,
    IReadOnlyList<Guid> ExportIds,
    string AuditAction);

public sealed record GetAccountingCapacityQuery(Guid CompanyId, string ProfileKey = AccountingCapacityProfileKeys.Small);
public sealed record PreviewAccountingRetentionCommand(Guid CompanyId, int BatchSize = 100);
public sealed record RunAccountingRetentionCleanupCommand(
    Guid CompanyId,
    string PreviewToken,
    int BatchSize,
    Guid ActorUserId,
    string Reason,
    string? CorrelationId = null);

public interface IAccountingCapacityService
{
    Task<AccountingCapacityReadModel> GetAsync(GetAccountingCapacityQuery query, CancellationToken cancellationToken);
    Task<AccountingRetentionPreviewDto> PreviewRetentionAsync(
        PreviewAccountingRetentionCommand command,
        CancellationToken cancellationToken);
    Task<AccountingRetentionCleanupResultDto> RunRetentionCleanupAsync(
        RunAccountingRetentionCleanupCommand command,
        CancellationToken cancellationToken);
}

public sealed class AccountingLifecycleException : Exception
{
    public AccountingLifecycleException(string reasonCode, string message, bool isConflict = false)
        : base(message)
    {
        ReasonCode = reasonCode;
        IsConflict = isConflict;
    }

    public string ReasonCode { get; }
    public bool IsConflict { get; }
}
