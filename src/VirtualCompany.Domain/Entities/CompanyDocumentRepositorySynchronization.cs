namespace VirtualCompany.Domain.Entities;

public sealed class CompanyDocumentRepositorySynchronizationJob : ICompanyOwnedEntity
{
    private CompanyDocumentRepositorySynchronizationJob() { }

    public CompanyDocumentRepositorySynchronizationJob(Guid companyId, Guid connectionId, string idempotencyKey,
        bool forceFullReconciliation, string correlationId, DateTime now)
    {
        Id = Guid.NewGuid();
        CompanyId = Require(companyId, nameof(companyId));
        ConnectionId = Require(connectionId, nameof(connectionId));
        IdempotencyKey = Normalize(idempotencyKey, nameof(idempotencyKey), 200);
        CorrelationId = Normalize(correlationId, nameof(correlationId), 128);
        Mode = forceFullReconciliation ? DocumentRepositorySynchronizationModes.FullReconciliation : DocumentRepositorySynchronizationModes.Delta;
        Status = DocumentRepositorySynchronizationStates.Queued;
        ReconciliationGeneration = Guid.NewGuid();
        CreatedUtc = UpdatedUtc = Utc(now);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ConnectionId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string Mode { get; private set; } = null!;
    public Guid ReconciliationGeneration { get; private set; }
    public string? PageCursor { get; private set; }
    public string? PendingDeltaCursor { get; private set; }
    public int AttemptCount { get; private set; }
    public int ObservedCount { get; private set; }
    public int ChangedCount { get; private set; }
    public int RemovedCount { get; private set; }
    public int FailedCount { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTime? LeaseExpiresUtc { get; private set; }
    public DateTime? NextRetryUtc { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? StartedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public CompanyDocumentRepositoryConnection Connection { get; private set; } = null!;

    public bool TryClaim(string owner, DateTime now, TimeSpan lease)
    {
        now = Utc(now);
        if (Status == DocumentRepositorySynchronizationStates.Cancelled ||
            Status == DocumentRepositorySynchronizationStates.Completed ||
            (NextRetryUtc.HasValue && NextRetryUtc > now) ||
            (Status == DocumentRepositorySynchronizationStates.Running && LeaseExpiresUtc > now)) return false;
        Status = DocumentRepositorySynchronizationStates.Running;
        LeaseOwner = Normalize(owner, nameof(owner), 128);
        LeaseExpiresUtc = now.Add(lease);
        StartedUtc ??= now;
        AttemptCount++;
        FailureCode = FailureMessage = null;
        UpdatedUtc = now;
        return true;
    }

    public void StartFrom(string? cursor, DateTime now) { PageCursor = string.IsNullOrWhiteSpace(cursor) ? null : cursor; UpdatedUtc = Utc(now); }

    public void UseFullReconciliation(DateTime now)
    {
        Mode = DocumentRepositorySynchronizationModes.FullReconciliation;
        ReconciliationGeneration = Guid.NewGuid();
        PageCursor = null;
        PendingDeltaCursor = null;
        UpdatedUtc = Utc(now);
    }

    public void RecordPage(string? nextCursor, string? deltaCursor, int observed, int changed, int removed, int failed, DateTime now)
    {
        PageCursor = nextCursor;
        PendingDeltaCursor = deltaCursor ?? PendingDeltaCursor;
        ObservedCount += observed;
        ChangedCount += changed;
        RemovedCount += removed;
        FailedCount += failed;
        if (nextCursor is not null) { Status = DocumentRepositorySynchronizationStates.Queued; LeaseOwner = null; LeaseExpiresUtc = null; }
        else LeaseExpiresUtc = Utc(now).AddMinutes(2);
        UpdatedUtc = Utc(now);
    }

    public void Complete(DateTime now)
    {
        Status = DocumentRepositorySynchronizationStates.Completed;
        PageCursor = null;
        LeaseOwner = null;
        LeaseExpiresUtc = null;
        NextRetryUtc = null;
        CompletedUtc = UpdatedUtc = Utc(now);
    }

    public void Retry(string code, string message, DateTime nextRetryUtc, DateTime now)
    {
        Status = DocumentRepositorySynchronizationStates.RetryWaiting;
        FailureCode = Normalize(code, nameof(code), 100);
        FailureMessage = Normalize(message, nameof(message), 1000);
        NextRetryUtc = Utc(nextRetryUtc);
        LeaseOwner = null;
        LeaseExpiresUtc = null;
        UpdatedUtc = Utc(now);
    }

    public void Fail(string code, string message, DateTime now)
    {
        Status = DocumentRepositorySynchronizationStates.Failed;
        FailureCode = Normalize(code, nameof(code), 100);
        FailureMessage = Normalize(message, nameof(message), 1000);
        LeaseOwner = null;
        LeaseExpiresUtc = null;
        CompletedUtc = UpdatedUtc = Utc(now);
    }

    public void Cancel(DateTime now)
    {
        if (Status == DocumentRepositorySynchronizationStates.Completed) return;
        Status = DocumentRepositorySynchronizationStates.Cancelled;
        LeaseOwner = null;
        LeaseExpiresUtc = null;
        CompletedUtc = UpdatedUtc = Utc(now);
    }

    public void Defer(DateTime now)
    {
        if (Status is DocumentRepositorySynchronizationStates.Completed or DocumentRepositorySynchronizationStates.Cancelled) return;
        Status = DocumentRepositorySynchronizationStates.Queued;
        LeaseOwner = null;
        LeaseExpiresUtc = null;
        NextRetryUtc = null;
        UpdatedUtc = Utc(now);
    }

    private static Guid Require(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static string Normalize(string value, string name, int max) => string.IsNullOrWhiteSpace(value) || value.Trim().Length > max ? throw new ArgumentException($"{name} is required and must be at most {max} characters.", name) : value.Trim();
}

public sealed class CompanyDocumentRepositoryTrackedItem : ICompanyOwnedEntity
{
    private CompanyDocumentRepositoryTrackedItem() { }
    public CompanyDocumentRepositoryTrackedItem(Guid companyId, Guid connectionId, string driveId, string itemId,
        string? parentItemId, string name, bool isFolder, string? remoteVersion, Guid generation, DateTime now)
    {
        Id = Guid.NewGuid(); CompanyId = companyId; ConnectionId = connectionId; DriveId = driveId; ItemId = itemId;
        ParentItemId = parentItemId; Name = name; IsFolder = isFolder; RemoteVersion = remoteVersion;
        IsAvailable = true; LastSeenGeneration = generation; LastSeenUtc = CreatedUtc = UpdatedUtc = now;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ConnectionId { get; private set; }
    public string DriveId { get; private set; } = null!;
    public string ItemId { get; private set; } = null!;
    public string? ParentItemId { get; private set; }
    public string Name { get; private set; } = null!;
    public bool IsFolder { get; private set; }
    public string? RemoteVersion { get; private set; }
    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }
    public Guid LastSeenGeneration { get; private set; }
    public DateTime LastSeenUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public CompanyDocumentRepositoryConnection Connection { get; private set; } = null!;

    public void Observe(string? parentItemId, string name, bool isFolder, string? version, Guid generation, DateTime now)
    { ParentItemId = parentItemId; Name = name; IsFolder = isFolder; RemoteVersion = version; IsAvailable = true; UnavailableReason = null; LastSeenGeneration = generation; LastSeenUtc = UpdatedUtc = now; }
    public void MarkUnavailable(string reason, DateTime now) { IsAvailable = false; UnavailableReason = reason; UpdatedUtc = now; }
}

public static class DocumentRepositorySynchronizationStates
{
    public const string Queued = "queued", Running = "running", RetryWaiting = "retry_waiting", Completed = "completed", Failed = "failed", Cancelled = "cancelled";
}

public static class DocumentRepositorySynchronizationModes
{
    public const string Delta = "delta", FullReconciliation = "full_reconciliation";
}
