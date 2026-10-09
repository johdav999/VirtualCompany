namespace VirtualCompany.Application.Finance;
public sealed record AuditPackageDownloadAuthorizationDto(Guid AuthorizationId, Guid PackageId, string Token, DateTime ExpiresUtc, string DownloadPath)
{
    public Guid AuthorizationId { get; set; } = AuthorizationId;
    public Guid PackageId { get; set; } = PackageId;
    public string Token { get; set; } = Token;
    public DateTime ExpiresUtc { get; set; } = ExpiresUtc;
    public string DownloadPath { get; set; } = DownloadPath;

    public AuditPackageDownloadAuthorizationDto() : this(default !, default !, string.Empty, default !, string.Empty)
    {
    }
}

public sealed record AuditPackageDto(Guid Id, Guid CompanyId, Guid FiscalPeriodId, string FiscalPeriodName, string ScopeKey, string ScopeVersion, string ScopeHash, string SnapshotVersionsJson, string Status, bool IsFinal, string? ManifestChecksum, string? PackageChecksum, string? FileName, string? MediaType, long? ContentLength, Guid RequestedByUserId, Guid? ApprovedByUserId, DateTime RequestedUtc, DateTime UpdatedUtc, DateTime RetainUntilUtc, DateTime? FinalizedUtc, int AttemptCount, int MaxAttempts, bool CancellationRequested, string? FailureCode, string? SafeFailureSummary, long Version, IReadOnlyList<AuditPackageArtifactDto> Artifacts, IReadOnlyList<AuditPackageAttemptDto> Attempts, IReadOnlyList<AuditPackageApprovalDto> Approvals, IReadOnlyList<AuditPackageVerificationDto> Verifications, string IntegrityNotice = "A final label means the required package evidence was accessible and checksum-verifiable at generation time; it is not statutory approval.")
{
    public Guid Id { get; set; } = Id;
    public Guid CompanyId { get; set; } = CompanyId;
    public Guid FiscalPeriodId { get; set; } = FiscalPeriodId;
    public string FiscalPeriodName { get; set; } = FiscalPeriodName;
    public string ScopeKey { get; set; } = ScopeKey;
    public string ScopeVersion { get; set; } = ScopeVersion;
    public string ScopeHash { get; set; } = ScopeHash;
    public string SnapshotVersionsJson { get; set; } = SnapshotVersionsJson;
    public string Status { get; set; } = Status;
    public bool IsFinal { get; set; } = IsFinal;
    public string? ManifestChecksum { get; set; } = ManifestChecksum;
    public string? PackageChecksum { get; set; } = PackageChecksum;
    public string? FileName { get; set; } = FileName;
    public string? MediaType { get; set; } = MediaType;
    public long? ContentLength { get; set; } = ContentLength;
    public Guid RequestedByUserId { get; set; } = RequestedByUserId;
    public Guid? ApprovedByUserId { get; set; } = ApprovedByUserId;
    public DateTime RequestedUtc { get; set; } = RequestedUtc;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;
    public DateTime RetainUntilUtc { get; set; } = RetainUntilUtc;
    public DateTime? FinalizedUtc { get; set; } = FinalizedUtc;
    public int AttemptCount { get; set; } = AttemptCount;
    public int MaxAttempts { get; set; } = MaxAttempts;
    public bool CancellationRequested { get; set; } = CancellationRequested;
    public string? FailureCode { get; set; } = FailureCode;
    public string? SafeFailureSummary { get; set; } = SafeFailureSummary;
    public long Version { get; set; } = Version;
    public IReadOnlyList<AuditPackageArtifactDto> Artifacts { get; set; } = Artifacts;
    public IReadOnlyList<AuditPackageAttemptDto> Attempts { get; set; } = Attempts;
    public IReadOnlyList<AuditPackageApprovalDto> Approvals { get; set; } = Approvals;
    public IReadOnlyList<AuditPackageVerificationDto> Verifications { get; set; } = Verifications;
    public string IntegrityNotice { get; set; } = IntegrityNotice;

    public AuditPackageDto() : this(default !, default !, default !, string.Empty, string.Empty, string.Empty, string.Empty, "{}", string.Empty, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, [], [], [], [], string.Empty)
    {
    }
}

public sealed record AuditPackageAttemptDto(Guid Id, int AttemptNumber, string Outcome, string? FailureCode, string? SafeSummary, DateTime StartedUtc, DateTime CompletedUtc)
{
    public Guid Id { get; set; } = Id;
    public int AttemptNumber { get; set; } = AttemptNumber;
    public string Outcome { get; set; } = Outcome;
    public string? FailureCode { get; set; } = FailureCode;
    public string? SafeSummary { get; set; } = SafeSummary;
    public DateTime StartedUtc { get; set; } = StartedUtc;
    public DateTime CompletedUtc { get; set; } = CompletedUtc;

    public AuditPackageAttemptDto() : this(default !, default !, string.Empty, default !, default !, default !, default !)
    {
    }
}

public sealed record AuditPackageVerificationDto(Guid Id, Guid VerifiedByUserId, bool IsValid, string PackageChecksum, string ManifestChecksum, int CheckedItemCount, int MissingItemCount, int CorruptItemCount, string ResultCode, string SafeSummary, DateTime VerifiedUtc)
{
    public Guid Id { get; set; } = Id;
    public Guid VerifiedByUserId { get; set; } = VerifiedByUserId;
    public bool IsValid { get; set; } = IsValid;
    public string PackageChecksum { get; set; } = PackageChecksum;
    public string ManifestChecksum { get; set; } = ManifestChecksum;
    public int CheckedItemCount { get; set; } = CheckedItemCount;
    public int MissingItemCount { get; set; } = MissingItemCount;
    public int CorruptItemCount { get; set; } = CorruptItemCount;
    public string ResultCode { get; set; } = ResultCode;
    public string SafeSummary { get; set; } = SafeSummary;
    public DateTime VerifiedUtc { get; set; } = VerifiedUtc;

    public AuditPackageVerificationDto() : this(default !, default !, default !, string.Empty, string.Empty, default !, default !, default !, string.Empty, string.Empty, default !)
    {
    }
}

public sealed record AuditPackageArtifactDto(Guid Id, int Sequence, string ArtifactType, string Path, string Status, bool IsRequired, string SourceType, string SourceReference, string? SourceVersion, string? DefinitionVersion, string? Checksum, long? ContentLength, string? SafeDetail)
{
    public Guid Id { get; set; } = Id;
    public int Sequence { get; set; } = Sequence;
    public string ArtifactType { get; set; } = ArtifactType;
    public string Path { get; set; } = Path;
    public string Status { get; set; } = Status;
    public bool IsRequired { get; set; } = IsRequired;
    public string SourceType { get; set; } = SourceType;
    public string SourceReference { get; set; } = SourceReference;
    public string? SourceVersion { get; set; } = SourceVersion;
    public string? DefinitionVersion { get; set; } = DefinitionVersion;
    public string? Checksum { get; set; } = Checksum;
    public long? ContentLength { get; set; } = ContentLength;
    public string? SafeDetail { get; set; } = SafeDetail;

    public AuditPackageArtifactDto() : this(default !, default !, string.Empty, string.Empty, string.Empty, default !, string.Empty, string.Empty, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record AuditPackageWorkspaceDto(Guid CompanyId, int TotalCount, int FinalCount, int IncompleteCount, int PendingCount, IReadOnlyList<AuditPackageDto> Packages)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public int TotalCount { get; set; } = TotalCount;
    public int FinalCount { get; set; } = FinalCount;
    public int IncompleteCount { get; set; } = IncompleteCount;
    public int PendingCount { get; set; } = PendingCount;
    public IReadOnlyList<AuditPackageDto> Packages { get; set; } = Packages;

    public AuditPackageWorkspaceDto() : this(default !, default !, default !, default !, default !, [])
    {
    }
}

public sealed record AuditPackageApprovalDto(Guid Id, Guid DecidedByUserId, string Decision, string? Reason, DateTime DecidedUtc)
{
    public Guid Id { get; set; } = Id;
    public Guid DecidedByUserId { get; set; } = DecidedByUserId;
    public string Decision { get; set; } = Decision;
    public string? Reason { get; set; } = Reason;
    public DateTime DecidedUtc { get; set; } = DecidedUtc;

    public AuditPackageApprovalDto() : this(default !, default !, string.Empty, default !, default !)
    {
    }
}

public static class AuditPackageScopeValues
{
    public const string PeriodClose = "period_close";
    public const string CurrentVersion = "audit-package-v1";
}
