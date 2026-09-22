namespace VirtualCompany.Domain.Entities;

public sealed class CompanyDocumentRepositoryProvisioning : ICompanyOwnedEntity
{
    private CompanyDocumentRepositoryProvisioning() { }

    public CompanyDocumentRepositoryProvisioning(Guid companyId, Guid onboardingSessionId, Guid initiatingUserId,
        string providerKind, Guid directoryTenantId, string? siteId, string driveId, string rootItemId,
        string sourceDisplayName, string rootDisplayName, bool enableWrites, string? writableFolderItemId,
        string? writableFolderDisplayName, string correlationId, DateTime createdUtc)
    {
        Id = Guid.NewGuid(); CompanyId = Required(companyId, nameof(companyId));
        OnboardingSessionId = Required(onboardingSessionId, nameof(onboardingSessionId));
        InitiatingUserId = Required(initiatingUserId, nameof(initiatingUserId));
        ProviderKind = DocumentRepositoryProviderKinds.Normalize(providerKind);
        DirectoryTenantId = Required(directoryTenantId, nameof(directoryTenantId)); SiteId = Optional(siteId, 256);
        DriveId = Text(driveId, nameof(driveId), 160); RootItemId = Text(rootItemId, nameof(rootItemId), 160);
        SourceDisplayName = Text(sourceDisplayName, nameof(sourceDisplayName), 200);
        RootDisplayName = Text(rootDisplayName, nameof(rootDisplayName), 200); EnableWrites = enableWrites;
        WritableFolderItemId = enableWrites ? Text(writableFolderItemId ?? string.Empty, nameof(writableFolderItemId), 160) : null;
        WritableFolderDisplayName = enableWrites ? Text(writableFolderDisplayName ?? string.Empty, nameof(writableFolderDisplayName), 200) : null;
        CorrelationId = Text(correlationId, nameof(correlationId), 100); Status = DocumentRepositoryProvisioningStatuses.Queued;
        CreatedUtc = Utc(createdUtc); UpdatedUtc = CreatedUtc; ConcurrencyVersion = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid OnboardingSessionId { get; private set; }
    public Guid InitiatingUserId { get; private set; }
    public string ProviderKind { get; private set; } = null!;
    public Guid DirectoryTenantId { get; private set; }
    public string? SiteId { get; private set; }
    public string DriveId { get; private set; } = null!;
    public string RootItemId { get; private set; } = null!;
    public string SourceDisplayName { get; private set; } = null!;
    public string RootDisplayName { get; private set; } = null!;
    public bool EnableWrites { get; private set; }
    public string? WritableFolderItemId { get; private set; }
    public string? WritableFolderDisplayName { get; private set; }
    public string Status { get; private set; } = null!;
    public string? RootPermissionId { get; private set; }
    public bool RootPermissionManaged { get; private set; }
    public string? WritePermissionId { get; private set; }
    public bool WritePermissionManaged { get; private set; }
    public Guid? ConnectionId { get; private set; }
    public int AttemptCount { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public DateTime? NextAttemptUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public string CorrelationId { get; private set; } = null!;
    public long ConcurrencyVersion { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public CompanyDocumentRepositoryOnboardingSession OnboardingSession { get; private set; } = null!;
    public ICollection<CompanyDocumentRepositoryProvisioningAgent> Agents { get; } = new List<CompanyDocumentRepositoryProvisioningAgent>();

    public void Start(DateTime now) { Status = DocumentRepositoryProvisioningStatuses.Provisioning; AttemptCount++; FailureCode = null; FailureSummary = null; NextAttemptUtc = null; Touch(now); }
    public void RecordRootPermission(string id, bool managed, DateTime now) { RootPermissionId = Text(id, nameof(id), 256); RootPermissionManaged = managed; Touch(now); }
    public void RecordWritePermission(string id, bool managed, DateTime now) { WritePermissionId = Text(id, nameof(id), 256); WritePermissionManaged = managed; Touch(now); }
    public void RecordConnection(Guid connectionId, DateTime now) { ConnectionId = Required(connectionId, nameof(connectionId)); Touch(now); }
    public void Complete(Guid connectionId, DateTime now) { ConnectionId = Required(connectionId, nameof(connectionId)); Status = DocumentRepositoryProvisioningStatuses.Connected; CompletedUtc = Utc(now); FailureCode = null; FailureSummary = null; Touch(now); }
    public void Fail(string code, string summary, bool retryable, DateTime now) { Status = retryable ? DocumentRepositoryProvisioningStatuses.ReconciliationRequired : DocumentRepositoryProvisioningStatuses.Failed; FailureCode = Text(code, nameof(code), 64); FailureSummary = Text(summary, nameof(summary), 500); NextAttemptUtc = retryable ? Utc(now).AddSeconds(Math.Min(300, 15 * Math.Max(1, AttemptCount))) : null; Touch(now); }
    public void QueueRetry(DateTime now)
    {
        if (!CanRetry) throw new InvalidOperationException("Provisioning is not retryable.");
        Status = Status is DocumentRepositoryProvisioningStatuses.CleanupReconciliationRequired or DocumentRepositoryProvisioningStatuses.CleanupFailed
            ? DocumentRepositoryProvisioningStatuses.CleanupQueued
            : DocumentRepositoryProvisioningStatuses.Queued;
        NextAttemptUtc = null;
        Touch(now);
    }
    public void QueueCleanup(DateTime now)
    {
        if (!CanCleanup) throw new InvalidOperationException("Provisioning cleanup is not available.");
        Status = DocumentRepositoryProvisioningStatuses.CleanupQueued; FailureCode = null; FailureSummary = null; NextAttemptUtc = null; Touch(now);
    }
    public void MarkCleaned(DateTime now) { Status = DocumentRepositoryProvisioningStatuses.Cleaned; CompletedUtc = Utc(now); FailureCode = null; FailureSummary = null; NextAttemptUtc = null; Touch(now); }
    public void FailCleanup(string code, string summary, bool retryable, DateTime now)
    {
        Status = retryable ? DocumentRepositoryProvisioningStatuses.CleanupReconciliationRequired : DocumentRepositoryProvisioningStatuses.CleanupFailed;
        FailureCode = Text(code, nameof(code), 64); FailureSummary = Text(summary, nameof(summary), 500);
        NextAttemptUtc = retryable ? Utc(now).AddSeconds(Math.Min(300, 15 * Math.Max(1, AttemptCount))) : null; Touch(now);
    }
    public bool CanRetry => Status is DocumentRepositoryProvisioningStatuses.ReconciliationRequired
        or DocumentRepositoryProvisioningStatuses.Failed
        or DocumentRepositoryProvisioningStatuses.CleanupReconciliationRequired
        or DocumentRepositoryProvisioningStatuses.CleanupFailed;
    public bool CanCleanup => ConnectionId is null &&
        Status is DocumentRepositoryProvisioningStatuses.ReconciliationRequired or DocumentRepositoryProvisioningStatuses.Failed &&
        ((RootPermissionManaged && RootPermissionId != null) || (WritePermissionManaged && WritePermissionId != null));
    private void Touch(DateTime value) { UpdatedUtc = Utc(value); ConcurrencyVersion++; }
    private static Guid Required(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static string Text(string value, string name, int max) { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name); var result = value.Trim(); return result.Length <= max ? result : throw new ArgumentOutOfRangeException(name); }
    private static string? Optional(string? value, int max) { if (string.IsNullOrWhiteSpace(value)) return null; var result = value.Trim(); return result.Length <= max ? result : throw new ArgumentOutOfRangeException(nameof(value)); }
}

public sealed class CompanyDocumentRepositoryProvisioningAgent : ICompanyOwnedEntity
{
    private CompanyDocumentRepositoryProvisioningAgent() { }
    public CompanyDocumentRepositoryProvisioningAgent(Guid companyId, Guid provisioningId, Guid agentId, DateTime createdUtc)
    { Id = Guid.NewGuid(); CompanyId = companyId; ProvisioningId = provisioningId; AgentId = agentId; CreatedUtc = createdUtc.Kind == DateTimeKind.Utc ? createdUtc : createdUtc.ToUniversalTime(); }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ProvisioningId { get; private set; }
    public Guid AgentId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public CompanyDocumentRepositoryProvisioning Provisioning { get; private set; } = null!;
    public Agent Agent { get; private set; } = null!;
}

public static class DocumentRepositoryProvisioningStatuses
{
    public const string Queued = "queued";
    public const string Provisioning = "provisioning";
    public const string ReconciliationRequired = "reconciliation_required";
    public const string Failed = "failed";
    public const string CleanupQueued = "cleanup_queued";
    public const string Cleaning = "cleaning";
    public const string CleanupReconciliationRequired = "cleanup_reconciliation_required";
    public const string CleanupFailed = "cleanup_failed";
    public const string Connected = "connected";
    public const string Cleaned = "cleaned";
}
