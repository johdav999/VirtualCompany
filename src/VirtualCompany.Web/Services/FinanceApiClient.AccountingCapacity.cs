namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<AccountingCapacityApiResponse?> GetAccountingCapacityAsync(Guid companyId,
        string profile = "small", CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<AccountingCapacityApiResponse?>(null)
            : GetAsync<AccountingCapacityApiResponse>(companyId,
                $"api/companies/{companyId:D}/finance/accounting-capacity?profile={Uri.EscapeDataString(profile)}",
                allowNotFound: false, cancellationToken);

    public Task<AccountingRetentionPreviewApiResponse> PreviewAccountingRetentionAsync(Guid companyId,
        int batchSize = 100, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<AccountingRetentionPreviewApiRequest, AccountingRetentionPreviewApiResponse>(companyId, HttpMethod.Post,
            $"api/companies/{companyId:D}/finance/accounting-capacity/retention/preview",
            new AccountingRetentionPreviewApiRequest(batchSize), cancellationToken);
    }

    public Task<AccountingRetentionCleanupApiResponse> RunAccountingRetentionCleanupAsync(Guid companyId,
        AccountingRetentionCleanupApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<AccountingRetentionCleanupApiRequest, AccountingRetentionCleanupApiResponse>(companyId, HttpMethod.Post,
            $"api/companies/{companyId:D}/finance/accounting-capacity/retention/run",
            request, cancellationToken);
    }
}

public sealed class AccountingCapacityApiResponse
{
    public Guid CompanyId { get; set; }
    public string ProfileKey { get; set; } = "small";
    public DateTime MeasuredUtc { get; set; }
    public List<AccountingSupportedVolumeProfileApiResponse> Profiles { get; set; } = [];
    public List<AccountingServiceObjectiveApiResponse> Objectives { get; set; } = [];
    public List<AccountingVolumeMeasurementApiResponse> Volumes { get; set; } = [];
    public List<AccountingObjectiveMeasurementApiResponse> Measurements { get; set; } = [];
    public List<AccountingRetentionClassApiResponse> RetentionClasses { get; set; } = [];
    public List<string> Alerts { get; set; } = [];
}

public sealed class AccountingSupportedVolumeProfileApiResponse
{
    public string Key { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int ConcurrentUsers { get; set; }
    public int ConcurrentJobs { get; set; }
    public List<AccountingSupportedVolumeApiResponse> Volumes { get; set; } = [];
}

public sealed class AccountingSupportedVolumeApiResponse
{
    public string Resource { get; set; } = "";
    public long MaximumCount { get; set; }
}

public sealed class AccountingServiceObjectiveApiResponse
{
    public string Key { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal Objective { get; set; }
    public decimal WarningThreshold { get; set; }
    public string MeasurementScope { get; set; } = "";
    public string Remediation { get; set; } = "";
}

public sealed class AccountingVolumeMeasurementApiResponse
{
    public string Resource { get; set; } = "";
    public long CurrentCount { get; set; }
    public long SupportedCount { get; set; }
    public string Status { get; set; } = "";
}

public sealed class AccountingObjectiveMeasurementApiResponse
{
    public string ObjectiveKey { get; set; } = "";
    public decimal? CurrentValue { get; set; }
    public string Unit { get; set; } = "";
    public string Status { get; set; } = "";
    public string Explanation { get; set; } = "";
    public string Action { get; set; } = "";
}

public sealed class AccountingRetentionClassApiResponse
{
    public string Key { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Mode { get; set; } = "";
    public string Policy { get; set; } = "";
    public bool RequiresPreview { get; set; }
    public bool RequiresAudit { get; set; }
    public bool RegenerationRequired { get; set; }
}

public sealed record AccountingRetentionPreviewApiRequest(int BatchSize = 100);

public sealed class AccountingRetentionPreviewApiResponse
{
    public Guid CompanyId { get; set; }
    public string RetentionClass { get; set; } = "";
    public DateTime PreviewedUtc { get; set; }
    public string PreviewToken { get; set; } = "";
    public int RequestedBatchSize { get; set; }
    public long EligibleCount { get; set; }
    public long EligibleBytes { get; set; }
    public List<AccountingRetentionTargetApiResponse> Targets { get; set; } = [];
    public List<string> PreservedEvidence { get; set; } = [];
}

public sealed class AccountingRetentionTargetApiResponse
{
    public Guid ExportId { get; set; }
    public Guid FiscalPeriodId { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public string FileName { get; set; } = "";
    public string Checksum { get; set; } = "";
    public long ContentLength { get; set; }
}

public sealed record AccountingRetentionCleanupApiRequest(
    string PreviewToken,
    int BatchSize,
    string Reason,
    string? CorrelationId = null);

public sealed class AccountingRetentionCleanupApiResponse
{
    public Guid CompanyId { get; set; }
    public string RetentionClass { get; set; } = "";
    public DateTime CompletedUtc { get; set; }
    public int ProcessedCount { get; set; }
    public long ReleasedBytes { get; set; }
    public List<Guid> ExportIds { get; set; } = [];
    public string AuditAction { get; set; } = "";
}
