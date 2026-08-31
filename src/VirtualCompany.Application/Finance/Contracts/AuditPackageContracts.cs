namespace VirtualCompany.Application.Finance;

public static class AuditPackageScopeValues
{
    public const string PeriodClose = "period_close";
    public const string CurrentVersion = "audit-package-v1";
}

public sealed record AuditPackageArtifactDto(
    Guid Id, int Sequence, string ArtifactType, string Path, string Status, bool IsRequired,
    string SourceType, string SourceReference, string? SourceVersion, string? DefinitionVersion,
    string? Checksum, long? ContentLength, string? SafeDetail);

public sealed record AuditPackageAttemptDto(Guid Id, int AttemptNumber, string Outcome,
    string? FailureCode, string? SafeSummary, DateTime StartedUtc, DateTime CompletedUtc);

public sealed record AuditPackageApprovalDto(Guid Id, Guid DecidedByUserId, string Decision,
    string? Reason, DateTime DecidedUtc);

public sealed record AuditPackageVerificationDto(Guid Id, Guid VerifiedByUserId, bool IsValid,
    string PackageChecksum, string ManifestChecksum, int CheckedItemCount, int MissingItemCount,
    int CorruptItemCount, string ResultCode, string SafeSummary, DateTime VerifiedUtc);

public sealed record AuditPackageDto(
    Guid Id, Guid CompanyId, Guid FiscalPeriodId, string FiscalPeriodName,
    string ScopeKey, string ScopeVersion, string ScopeHash, string SnapshotVersionsJson,
    string Status, bool IsFinal, string? ManifestChecksum, string? PackageChecksum,
    string? FileName, string? MediaType, long? ContentLength,
    Guid RequestedByUserId, Guid? ApprovedByUserId, DateTime RequestedUtc, DateTime UpdatedUtc,
    DateTime RetainUntilUtc, DateTime? FinalizedUtc, int AttemptCount, int MaxAttempts,
    bool CancellationRequested, string? FailureCode, string? SafeFailureSummary, long Version,
    IReadOnlyList<AuditPackageArtifactDto> Artifacts,
    IReadOnlyList<AuditPackageAttemptDto> Attempts,
    IReadOnlyList<AuditPackageApprovalDto> Approvals,
    IReadOnlyList<AuditPackageVerificationDto> Verifications,
    string IntegrityNotice = "A final label means the required package evidence was accessible and checksum-verifiable at generation time; it is not statutory approval.");

public sealed record AuditPackageWorkspaceDto(Guid CompanyId, int TotalCount, int FinalCount,
    int IncompleteCount, int PendingCount, IReadOnlyList<AuditPackageDto> Packages);

public sealed record RequestAuditPackageCommand(Guid CompanyId, Guid FiscalPeriodId,
    Guid ActorUserId, string ActorRole, string IdempotencyKey,
    string ScopeKey = AuditPackageScopeValues.PeriodClose,
    string ScopeVersion = AuditPackageScopeValues.CurrentVersion);

public sealed record ApproveAuditPackageCommand(Guid CompanyId, Guid PackageId,
    Guid ActorUserId, string? Reason, long ExpectedVersion);

public sealed record CancelAuditPackageCommand(Guid CompanyId, Guid PackageId,
    Guid ActorUserId, long ExpectedVersion);

public sealed record ListAuditPackagesQuery(Guid CompanyId, Guid? FiscalPeriodId = null,
    int Skip = 0, int Take = 100);

public sealed record CreateAuditPackageDownloadAuthorizationCommand(Guid CompanyId,
    Guid PackageId, Guid ActorUserId);

public sealed record AuditPackageDownloadAuthorizationDto(Guid AuthorizationId, Guid PackageId,
    string Token, DateTime ExpiresUtc, string DownloadPath);

public sealed record DownloadAuditPackageQuery(Guid CompanyId, Guid PackageId,
    Guid ActorUserId, string Token);

public sealed record AuditPackageDownloadDto(string FileName, string MediaType,
    Stream Content, long ContentLength, string PackageChecksum, string ManifestChecksum);

public sealed record VerifyAuditPackageCommand(Guid CompanyId, Guid PackageId, Guid ActorUserId);

public interface IAuditPackageService
{
    Task<AuditPackageWorkspaceDto> ListAsync(ListAuditPackagesQuery query, CancellationToken cancellationToken);
    Task<AuditPackageDto> GetAsync(Guid companyId, Guid packageId, CancellationToken cancellationToken);
    Task<AuditPackageDto> RequestAsync(RequestAuditPackageCommand command, CancellationToken cancellationToken);
    Task<AuditPackageDto> ApproveAsync(ApproveAuditPackageCommand command, CancellationToken cancellationToken);
    Task<AuditPackageDto> CancelAsync(CancelAuditPackageCommand command, CancellationToken cancellationToken);
    Task<AuditPackageDownloadAuthorizationDto> AuthorizeDownloadAsync(
        CreateAuditPackageDownloadAuthorizationCommand command, CancellationToken cancellationToken);
    Task<AuditPackageDownloadDto> DownloadAsync(DownloadAuditPackageQuery query, CancellationToken cancellationToken);
    Task<AuditPackageVerificationDto> VerifyAsync(VerifyAuditPackageCommand command, CancellationToken cancellationToken);
    Task<int> ProcessPendingAsync(int batchSize, CancellationToken cancellationToken);
    Task<int> ExpireAsync(int batchSize, CancellationToken cancellationToken);
}

public sealed class AuditPackageException : Exception
{
    public AuditPackageException(string reasonCode, string message, bool isConflict = false) : base(message)
    {
        ReasonCode = string.IsNullOrWhiteSpace(reasonCode)
            ? throw new ArgumentException("A reason code is required.", nameof(reasonCode))
            : reasonCode.Trim().ToLowerInvariant();
        IsConflict = isConflict;
    }
    public string ReasonCode { get; }
    public bool IsConflict { get; }
}
