namespace VirtualCompany.Application.Finance;

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

public sealed record DownloadAuditPackageQuery(Guid CompanyId, Guid PackageId,
    Guid ActorUserId, string Token);

public sealed record AuditPackageDownloadDto(string FileName, string MediaType,
    Stream Content, long ContentLength, string PackageChecksum, string ManifestChecksum);

public sealed record VerifyAuditPackageCommand(Guid CompanyId, Guid PackageId, Guid ActorUserId);

public sealed record PreviewAuditPackageQuery(Guid CompanyId, Guid FiscalPeriodId,
    string ScopeKey = AuditPackageScopeValues.PeriodClose,
    string ScopeVersion = AuditPackageScopeValues.CurrentVersion);

public sealed record AuditPackagePreviewDto(Guid CompanyId, Guid FiscalPeriodId, string FiscalPeriodName,
    string ScopeKey, string ScopeVersion, string ScopeHash, string SnapshotVersionsJson,
    bool IsEligible, IReadOnlyList<string> Blockers, Guid? ExistingPackageId,
    string? ExistingPackageStatus, long? ExistingPackageVersion, bool ArtifactGenerated,
    string IntegrityNotice = "This is a technical package definition preview, not an artifact or statutory approval.");

public interface IAuditPackageService
{
    Task<AuditPackageWorkspaceDto> ListAsync(ListAuditPackagesQuery query, CancellationToken cancellationToken);
    Task<AuditPackageDto> GetAsync(Guid companyId, Guid packageId, CancellationToken cancellationToken);
    Task<AuditPackagePreviewDto> PreviewAsync(PreviewAuditPackageQuery query, CancellationToken cancellationToken);
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
