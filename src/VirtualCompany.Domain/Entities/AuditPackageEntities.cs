namespace VirtualCompany.Domain.Entities;

public static class AuditPackageStatuses
{
    public const string PendingApproval = "pending_approval";
    public const string Queued = "queued";
    public const string Generating = "generating";
    public const string RetryScheduled = "retry_scheduled";
    public const string Incomplete = "incomplete";
    public const string Final = "final";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";
    public const string Expired = "expired";
}

public static class AuditPackageArtifactStatuses
{
    public const string Included = "included";
    public const string Missing = "missing";
    public const string Inaccessible = "inaccessible";
    public const string Corrupt = "corrupt";
}

public sealed class AuditPackage : ICompanyOwnedEntity
{
    private AuditPackage() { }

    public AuditPackage(Guid id, Guid companyId, Guid fiscalPeriodId, string scopeKey, string scopeVersion,
        string scopeHash, string snapshotVersionsJson, Guid requestedByUserId, string requestedByRole,
        string idempotencyKey, DateTime requestedUtc, DateTime retainUntilUtc, int maxAttempts)
    {
        Id = AuditPackageValue.RequiredId(id, nameof(id));
        CompanyId = AuditPackageValue.RequiredId(companyId, nameof(companyId));
        FiscalPeriodId = AuditPackageValue.RequiredId(fiscalPeriodId, nameof(fiscalPeriodId));
        ScopeKey = AuditPackageValue.Required(scopeKey, nameof(scopeKey), 100).ToLowerInvariant();
        ScopeVersion = AuditPackageValue.Required(scopeVersion, nameof(scopeVersion), 64);
        ScopeHash = AuditPackageValue.Hash(scopeHash, nameof(scopeHash));
        SnapshotVersionsJson = AuditPackageValue.Required(snapshotVersionsJson, nameof(snapshotVersionsJson), 16000);
        RequestedByUserId = AuditPackageValue.RequiredId(requestedByUserId, nameof(requestedByUserId));
        RequestedByRole = AuditPackageValue.Required(requestedByRole, nameof(requestedByRole), 64).ToLowerInvariant();
        IdempotencyKey = AuditPackageValue.Required(idempotencyKey, nameof(idempotencyKey), 200);
        RequestedUtc = UpdatedUtc = AuditPackageValue.Utc(requestedUtc);
        if (retainUntilUtc <= RequestedUtc) throw new ArgumentOutOfRangeException(nameof(retainUntilUtc));
        RetainUntilUtc = AuditPackageValue.Utc(retainUntilUtc);
        if (maxAttempts is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        MaxAttempts = maxAttempts;
        Status = AuditPackageStatuses.PendingApproval;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid FiscalPeriodId { get; private set; }
    public string ScopeKey { get; private set; } = null!;
    public string ScopeVersion { get; private set; } = null!;
    public string ScopeHash { get; private set; } = null!;
    public string SnapshotVersionsJson { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public bool IsFinal { get; private set; }
    public string? ManifestJson { get; private set; }
    public string? ManifestChecksum { get; private set; }
    public string? PackageChecksum { get; private set; }
    public string? StorageKey { get; private set; }
    public string? FileName { get; private set; }
    public string? MediaType { get; private set; }
    public long? ContentLength { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public string RequestedByRole { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public Guid? ApprovedByUserId { get; private set; }
    public DateTime? ApprovedUtc { get; private set; }
    public DateTime RequestedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime RetainUntilUtc { get; private set; }
    public DateTime? StartedUtc { get; private set; }
    public DateTime? FinalizedUtc { get; private set; }
    public DateTime? NextAttemptUtc { get; private set; }
    public DateTime? LeaseExpiresUtc { get; private set; }
    public bool CancellationRequested { get; private set; }
    public int AttemptCount { get; private set; }
    public int MaxAttempts { get; private set; }
    public string? FailureCode { get; private set; }
    public string? SafeFailureSummary { get; private set; }
    public long Version { get; private set; }
    public FiscalPeriod FiscalPeriod { get; private set; } = null!;
    public ICollection<AuditPackageApproval> Approvals { get; } = new List<AuditPackageApproval>();
    public ICollection<AuditPackageArtifact> Artifacts { get; } = new List<AuditPackageArtifact>();
    public ICollection<AuditPackageGenerationAttempt> GenerationAttempts { get; } = new List<AuditPackageGenerationAttempt>();
    public ICollection<AuditPackageDownloadAuthorization> DownloadAuthorizations { get; } = new List<AuditPackageDownloadAuthorization>();
    public ICollection<AuditPackageVerificationResult> VerificationResults { get; } = new List<AuditPackageVerificationResult>();

    public void Approve(Guid actorUserId, DateTime utcNow)
    {
        if (Status != AuditPackageStatuses.PendingApproval)
            throw new InvalidOperationException("Only a pending audit package can be approved.");
        if (actorUserId == RequestedByUserId)
            throw new InvalidOperationException("Audit package approval must be independent from the requester.");
        ApprovedByUserId = AuditPackageValue.RequiredId(actorUserId, nameof(actorUserId));
        ApprovedUtc = AuditPackageValue.Utc(utcNow);
        Status = AuditPackageStatuses.Queued;
        Touch(utcNow);
    }

    public bool TryStart(DateTime utcNow, TimeSpan? leaseDuration = null)
    {
        if (CancellationRequested)
        {
            Cancel(utcNow);
            return false;
        }
        var expiredLease = Status == AuditPackageStatuses.Generating && LeaseExpiresUtc.HasValue && LeaseExpiresUtc.Value <= utcNow;
        if (Status is not (AuditPackageStatuses.Queued or AuditPackageStatuses.RetryScheduled) && !expiredLease) return false;
        if (NextAttemptUtc.HasValue && NextAttemptUtc.Value > utcNow) return false;
        Status = AuditPackageStatuses.Generating;
        AttemptCount++;
        StartedUtc = AuditPackageValue.Utc(utcNow);
        var lease = leaseDuration ?? TimeSpan.FromMinutes(5);
        LeaseExpiresUtc = AuditPackageValue.Utc(utcNow.Add(lease <= TimeSpan.Zero ? TimeSpan.FromMinutes(5) : lease));
        NextAttemptUtc = null;
        FailureCode = SafeFailureSummary = null;
        Touch(utcNow);
        return true;
    }

    public void RequestCancellation(DateTime utcNow)
    {
        if (Status is AuditPackageStatuses.Final or AuditPackageStatuses.Incomplete or AuditPackageStatuses.Cancelled)
            throw new InvalidOperationException("A finalized audit package cannot be cancelled.");
        CancellationRequested = true;
        if (Status != AuditPackageStatuses.Generating) Cancel(utcNow);
        else Touch(utcNow);
    }

    public void ReplaceArtifacts(IEnumerable<AuditPackageArtifact> artifacts)
    {
        Artifacts.Clear();
        foreach (var artifact in artifacts.OrderBy(x => x.Sequence)) Artifacts.Add(artifact);
    }

    public void Complete(string manifestJson, string manifestChecksum, string packageChecksum,
        string storageKey, string fileName, string mediaType, long contentLength, bool complete, DateTime utcNow)
    {
        if (Status != AuditPackageStatuses.Generating)
            throw new InvalidOperationException("The package must be generating before it can be finalized.");
        if (CancellationRequested)
        {
            Cancel(utcNow);
            return;
        }
        ManifestJson = AuditPackageValue.Required(manifestJson, nameof(manifestJson), 2_000_000);
        ManifestChecksum = AuditPackageValue.Hash(manifestChecksum, nameof(manifestChecksum));
        PackageChecksum = AuditPackageValue.Hash(packageChecksum, nameof(packageChecksum));
        StorageKey = AuditPackageValue.Required(storageKey, nameof(storageKey), 1024);
        FileName = AuditPackageValue.Required(fileName, nameof(fileName), 255);
        MediaType = AuditPackageValue.Required(mediaType, nameof(mediaType), 100);
        ContentLength = contentLength > 0 ? contentLength : throw new ArgumentOutOfRangeException(nameof(contentLength));
        IsFinal = complete;
        Status = complete ? AuditPackageStatuses.Final : AuditPackageStatuses.Incomplete;
        FinalizedUtc = AuditPackageValue.Utc(utcNow);
        LeaseExpiresUtc = null;
        Touch(utcNow);
    }

    public void ScheduleRetry(string code, string summary, DateTime nextAttemptUtc, DateTime utcNow)
    {
        FailureCode = AuditPackageValue.Required(code, nameof(code), 100).ToLowerInvariant();
        SafeFailureSummary = AuditPackageValue.Required(summary, nameof(summary), 1000);
        if (AttemptCount >= MaxAttempts)
        {
            Status = AuditPackageStatuses.Failed;
            NextAttemptUtc = null;
        }
        else
        {
            Status = AuditPackageStatuses.RetryScheduled;
            NextAttemptUtc = AuditPackageValue.Utc(nextAttemptUtc);
        }
        LeaseExpiresUtc = null;
        Touch(utcNow);
    }

    public void Expire(DateTime utcNow)
    {
        if (Status is AuditPackageStatuses.Final or AuditPackageStatuses.Incomplete)
        {
            Status = AuditPackageStatuses.Expired;
            Touch(utcNow);
        }
    }

    private void Cancel(DateTime utcNow)
    {
        Status = AuditPackageStatuses.Cancelled;
        NextAttemptUtc = null;
        LeaseExpiresUtc = null;
        Touch(utcNow);
    }

    private void Touch(DateTime utcNow)
    {
        UpdatedUtc = AuditPackageValue.Utc(utcNow);
        Version++;
    }
}

public sealed class AuditPackageApproval : ICompanyOwnedEntity
{
    private AuditPackageApproval() { }
    public AuditPackageApproval(Guid id, Guid companyId, Guid packageId, Guid decidedByUserId,
        string decision, string? reason, DateTime decidedUtc)
    {
        Id = AuditPackageValue.RequiredId(id, nameof(id));
        CompanyId = AuditPackageValue.RequiredId(companyId, nameof(companyId));
        PackageId = AuditPackageValue.RequiredId(packageId, nameof(packageId));
        DecidedByUserId = AuditPackageValue.RequiredId(decidedByUserId, nameof(decidedByUserId));
        Decision = AuditPackageValue.Required(decision, nameof(decision), 32).ToLowerInvariant();
        Reason = AuditPackageValue.Optional(reason, 1000);
        DecidedUtc = AuditPackageValue.Utc(decidedUtc);
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid PackageId { get; private set; }
    public Guid DecidedByUserId { get; private set; }
    public string Decision { get; private set; } = null!;
    public string? Reason { get; private set; }
    public DateTime DecidedUtc { get; private set; }
    public AuditPackage Package { get; private set; } = null!;
}

public sealed class AuditPackageArtifact : ICompanyOwnedEntity
{
    private AuditPackageArtifact() { }
    public AuditPackageArtifact(Guid id, Guid companyId, Guid packageId, int sequence, string artifactType,
        string path, string status, bool isRequired, string sourceType, string sourceReference,
        string? sourceVersion, string? definitionVersion, string? checksum, long? contentLength,
        string? safeDetail)
    {
        Id = AuditPackageValue.RequiredId(id, nameof(id));
        CompanyId = AuditPackageValue.RequiredId(companyId, nameof(companyId));
        PackageId = AuditPackageValue.RequiredId(packageId, nameof(packageId));
        if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
        Sequence = sequence;
        ArtifactType = AuditPackageValue.Required(artifactType, nameof(artifactType), 100).ToLowerInvariant();
        Path = AuditPackageValue.Required(path, nameof(path), 500);
        Status = AuditPackageValue.Required(status, nameof(status), 32).ToLowerInvariant();
        IsRequired = isRequired;
        SourceType = AuditPackageValue.Required(sourceType, nameof(sourceType), 100).ToLowerInvariant();
        SourceReference = AuditPackageValue.Required(sourceReference, nameof(sourceReference), 500);
        SourceVersion = AuditPackageValue.Optional(sourceVersion, 128);
        DefinitionVersion = AuditPackageValue.Optional(definitionVersion, 128);
        Checksum = checksum is null ? null : AuditPackageValue.Hash(checksum, nameof(checksum));
        ContentLength = contentLength;
        SafeDetail = AuditPackageValue.Optional(safeDetail, 1000);
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid PackageId { get; private set; }
    public int Sequence { get; private set; }
    public string ArtifactType { get; private set; } = null!;
    public string Path { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public bool IsRequired { get; private set; }
    public string SourceType { get; private set; } = null!;
    public string SourceReference { get; private set; } = null!;
    public string? SourceVersion { get; private set; }
    public string? DefinitionVersion { get; private set; }
    public string? Checksum { get; private set; }
    public long? ContentLength { get; private set; }
    public string? SafeDetail { get; private set; }
    public AuditPackage Package { get; private set; } = null!;
}

public sealed class AuditPackageGenerationAttempt : ICompanyOwnedEntity
{
    private AuditPackageGenerationAttempt() { }
    public AuditPackageGenerationAttempt(Guid id, Guid companyId, Guid packageId, int attemptNumber,
        string outcome, string? failureCode, string? safeSummary, DateTime startedUtc, DateTime completedUtc)
    {
        Id = AuditPackageValue.RequiredId(id, nameof(id)); CompanyId = AuditPackageValue.RequiredId(companyId, nameof(companyId));
        PackageId = AuditPackageValue.RequiredId(packageId, nameof(packageId));
        if (attemptNumber < 1) throw new ArgumentOutOfRangeException(nameof(attemptNumber));
        AttemptNumber = attemptNumber;
        Outcome = AuditPackageValue.Required(outcome, nameof(outcome), 32).ToLowerInvariant();
        FailureCode = AuditPackageValue.Optional(failureCode, 100);
        SafeSummary = AuditPackageValue.Optional(safeSummary, 1000);
        StartedUtc = AuditPackageValue.Utc(startedUtc); CompletedUtc = AuditPackageValue.Utc(completedUtc);
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid PackageId { get; private set; }
    public int AttemptNumber { get; private set; } public string Outcome { get; private set; } = null!;
    public string? FailureCode { get; private set; } public string? SafeSummary { get; private set; }
    public DateTime StartedUtc { get; private set; } public DateTime CompletedUtc { get; private set; }
    public AuditPackage Package { get; private set; } = null!;
}

public sealed class AuditPackageDownloadAuthorization : ICompanyOwnedEntity
{
    private AuditPackageDownloadAuthorization() { }
    public AuditPackageDownloadAuthorization(Guid id, Guid companyId, Guid packageId, Guid userId,
        string tokenHash, DateTime createdUtc, DateTime expiresUtc)
    {
        Id = AuditPackageValue.RequiredId(id, nameof(id)); CompanyId = AuditPackageValue.RequiredId(companyId, nameof(companyId));
        PackageId = AuditPackageValue.RequiredId(packageId, nameof(packageId)); UserId = AuditPackageValue.RequiredId(userId, nameof(userId));
        TokenHash = AuditPackageValue.Hash(tokenHash, nameof(tokenHash)); CreatedUtc = AuditPackageValue.Utc(createdUtc);
        ExpiresUtc = AuditPackageValue.Utc(expiresUtc); if (ExpiresUtc <= CreatedUtc) throw new ArgumentOutOfRangeException(nameof(expiresUtc));
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid PackageId { get; private set; }
    public Guid UserId { get; private set; } public string TokenHash { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; } public DateTime ExpiresUtc { get; private set; }
    public DateTime? RedeemedUtc { get; private set; } public AuditPackage Package { get; private set; } = null!;
    public void Redeem(DateTime utcNow)
    {
        if (RedeemedUtc.HasValue) throw new InvalidOperationException("The download authorization has already been used.");
        if (ExpiresUtc <= utcNow) throw new InvalidOperationException("The download authorization has expired.");
        RedeemedUtc = AuditPackageValue.Utc(utcNow);
    }
}

public sealed class AuditPackageVerificationResult : ICompanyOwnedEntity
{
    private AuditPackageVerificationResult() { }
    public AuditPackageVerificationResult(Guid id, Guid companyId, Guid packageId, Guid verifiedByUserId,
        bool isValid, string packageChecksum, string manifestChecksum, int checkedItemCount,
        int missingItemCount, int corruptItemCount, string resultCode, string safeSummary, DateTime verifiedUtc)
    {
        Id = AuditPackageValue.RequiredId(id, nameof(id)); CompanyId = AuditPackageValue.RequiredId(companyId, nameof(companyId));
        PackageId = AuditPackageValue.RequiredId(packageId, nameof(packageId)); VerifiedByUserId = AuditPackageValue.RequiredId(verifiedByUserId, nameof(verifiedByUserId));
        IsValid = isValid; PackageChecksum = AuditPackageValue.Hash(packageChecksum, nameof(packageChecksum));
        ManifestChecksum = AuditPackageValue.Hash(manifestChecksum, nameof(manifestChecksum));
        CheckedItemCount = Math.Max(0, checkedItemCount); MissingItemCount = Math.Max(0, missingItemCount); CorruptItemCount = Math.Max(0, corruptItemCount);
        ResultCode = AuditPackageValue.Required(resultCode, nameof(resultCode), 100).ToLowerInvariant();
        SafeSummary = AuditPackageValue.Required(safeSummary, nameof(safeSummary), 1000); VerifiedUtc = AuditPackageValue.Utc(verifiedUtc);
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid PackageId { get; private set; }
    public Guid VerifiedByUserId { get; private set; } public bool IsValid { get; private set; }
    public string PackageChecksum { get; private set; } = null!; public string ManifestChecksum { get; private set; } = null!;
    public int CheckedItemCount { get; private set; } public int MissingItemCount { get; private set; } public int CorruptItemCount { get; private set; }
    public string ResultCode { get; private set; } = null!; public string SafeSummary { get; private set; } = null!;
    public DateTime VerifiedUtc { get; private set; } public AuditPackage Package { get; private set; } = null!;
}

internal static class AuditPackageValue
{
    public static Guid RequiredId(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;
    public static string Required(string? value, string name, int maximum)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) throw new ArgumentException($"{name} is required.", name);
        return normalized.Length <= maximum ? normalized : throw new ArgumentOutOfRangeException(name);
    }
    public static string? Optional(string? value, int maximum) => string.IsNullOrWhiteSpace(value) ? null : Required(value, nameof(value), maximum);
    public static string Hash(string value, string name)
    {
        var normalized = Required(value, name, 64).ToLowerInvariant();
        return normalized.Length == 64 && normalized.All(Uri.IsHexDigit) ? normalized : throw new ArgumentException($"{name} must be a SHA-256 value.", name);
    }
    public static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
