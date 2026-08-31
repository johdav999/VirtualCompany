namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<AuditPackageWorkspaceResponse?> GetAuditPackagesAsync(Guid companyId, Guid? fiscalPeriodId = null,
        CancellationToken cancellationToken = default) => GetAsync<AuditPackageWorkspaceResponse>(companyId,
        $"internal/companies/{companyId:D}/finance/accounting/audit-packages{(fiscalPeriodId.HasValue ? $"?fiscalPeriodId={fiscalPeriodId:D}" : string.Empty)}",
        false, cancellationToken);

    public Task<AuditPackageResponse> RequestAuditPackageAsync(Guid companyId, Guid fiscalPeriodId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, AuditPackageResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId:D}/finance/accounting/audit-packages",
            new { fiscalPeriodId, idempotencyKey, scopeKey = "period_close", scopeVersion = "audit-package-v1" }, cancellationToken);
    }

    public Task<AuditPackageResponse> ApproveAuditPackageAsync(Guid companyId, Guid packageId,
        long expectedVersion, string? reason = null, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, AuditPackageResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId:D}/finance/accounting/audit-packages/{packageId:D}/approve",
            new { expectedVersion, reason }, cancellationToken);
    }

    public Task<AuditPackageResponse> CancelAuditPackageAsync(Guid companyId, Guid packageId,
        long expectedVersion, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, AuditPackageResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId:D}/finance/accounting/audit-packages/{packageId:D}/cancel",
            new { expectedVersion }, cancellationToken);
    }

    public Task<AuditPackageVerificationResponse> VerifyAuditPackageAsync(Guid companyId, Guid packageId,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, AuditPackageVerificationResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId:D}/finance/accounting/audit-packages/{packageId:D}/verify",
            new { }, cancellationToken);
    }

    public Task<AuditPackageDownloadAuthorizationResponse> AuthorizeAuditPackageDownloadAsync(Guid companyId,
        Guid packageId, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, AuditPackageDownloadAuthorizationResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId:D}/finance/accounting/audit-packages/{packageId:D}/download-authorizations",
            new { }, cancellationToken);
    }
}

public sealed class AuditPackageWorkspaceResponse
{
    public Guid CompanyId { get; set; }
    public int TotalCount { get; set; }
    public int FinalCount { get; set; }
    public int IncompleteCount { get; set; }
    public int PendingCount { get; set; }
    public List<AuditPackageResponse> Packages { get; set; } = [];
}

public sealed class AuditPackageResponse
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid FiscalPeriodId { get; set; }
    public string FiscalPeriodName { get; set; } = string.Empty;
    public string ScopeKey { get; set; } = string.Empty;
    public string ScopeVersion { get; set; } = string.Empty;
    public string ScopeHash { get; set; } = string.Empty;
    public string SnapshotVersionsJson { get; set; } = "{}";
    public string Status { get; set; } = string.Empty;
    public bool IsFinal { get; set; }
    public string? ManifestChecksum { get; set; }
    public string? PackageChecksum { get; set; }
    public string? FileName { get; set; }
    public string? MediaType { get; set; }
    public long? ContentLength { get; set; }
    public Guid RequestedByUserId { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime RequestedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime RetainUntilUtc { get; set; }
    public DateTime? FinalizedUtc { get; set; }
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; }
    public bool CancellationRequested { get; set; }
    public string? FailureCode { get; set; }
    public string? SafeFailureSummary { get; set; }
    public long Version { get; set; }
    public List<AuditPackageArtifactResponse> Artifacts { get; set; } = [];
    public List<AuditPackageAttemptResponse> Attempts { get; set; } = [];
    public List<AuditPackageApprovalResponse> Approvals { get; set; } = [];
    public List<AuditPackageVerificationResponse> Verifications { get; set; } = [];
    public string IntegrityNotice { get; set; } = string.Empty;
}

public sealed class AuditPackageArtifactResponse
{
    public Guid Id { get; set; }
    public int Sequence { get; set; }
    public string ArtifactType { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string SourceReference { get; set; } = string.Empty;
    public string? SourceVersion { get; set; }
    public string? DefinitionVersion { get; set; }
    public string? Checksum { get; set; }
    public long? ContentLength { get; set; }
    public string? SafeDetail { get; set; }
}

public sealed class AuditPackageAttemptResponse
{
    public Guid Id { get; set; } public int AttemptNumber { get; set; } public string Outcome { get; set; } = string.Empty;
    public string? FailureCode { get; set; } public string? SafeSummary { get; set; }
    public DateTime StartedUtc { get; set; } public DateTime CompletedUtc { get; set; }
}

public sealed class AuditPackageApprovalResponse
{
    public Guid Id { get; set; } public Guid DecidedByUserId { get; set; } public string Decision { get; set; } = string.Empty;
    public string? Reason { get; set; } public DateTime DecidedUtc { get; set; }
}

public sealed class AuditPackageVerificationResponse
{
    public Guid Id { get; set; } public Guid VerifiedByUserId { get; set; } public bool IsValid { get; set; }
    public string PackageChecksum { get; set; } = string.Empty; public string ManifestChecksum { get; set; } = string.Empty;
    public int CheckedItemCount { get; set; } public int MissingItemCount { get; set; } public int CorruptItemCount { get; set; }
    public string ResultCode { get; set; } = string.Empty; public string SafeSummary { get; set; } = string.Empty;
    public DateTime VerifiedUtc { get; set; }
}

public sealed class AuditPackageDownloadAuthorizationResponse
{
    public Guid AuthorizationId { get; set; } public Guid PackageId { get; set; }
    public string Token { get; set; } = string.Empty; public DateTime ExpiresUtc { get; set; }
    public string DownloadPath { get; set; } = string.Empty;
}
