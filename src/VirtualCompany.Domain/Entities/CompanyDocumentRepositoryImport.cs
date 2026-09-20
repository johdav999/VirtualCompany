namespace VirtualCompany.Domain.Entities;

public sealed class CompanyDocumentRepositoryImportJob : ICompanyOwnedEntity
{
    private CompanyDocumentRepositoryImportJob() { }
    public CompanyDocumentRepositoryImportJob(Guid companyId, Guid connectionId, string key, string correlationId, DateTime now)
    { Id = Guid.NewGuid(); CompanyId = companyId; ConnectionId = connectionId; IdempotencyKey = key; CorrelationId = correlationId; Status = DocumentRepositoryImportStates.Queued; CreatedUtc = now; UpdatedUtc = now; }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ConnectionId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public int DiscoveredCount { get; private set; }
    public int ProcessedCount { get; private set; }
    public int FailedCount { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? StartedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public CompanyDocumentRepositoryConnection Connection { get; private set; } = null!;
    public ICollection<CompanyDocumentRepositoryImportItem> Items { get; } = new List<CompanyDocumentRepositoryImportItem>();
    public void Start(DateTime now) { Status = DocumentRepositoryImportStates.Running; StartedUtc ??= now; UpdatedUtc = now; }
    public void SetDiscoveredCount(int value, DateTime now) { DiscoveredCount = value; UpdatedUtc = now; }
    public void RecordProcessed(DateTime now) { ProcessedCount++; UpdatedUtc = now; }
    public void RecordFailed(DateTime now) { FailedCount++; UpdatedUtc = now; }
    public void Complete(DateTime now) { Status = FailedCount == 0 ? DocumentRepositoryImportStates.Completed : ProcessedCount == 0 ? DocumentRepositoryImportStates.Failed : DocumentRepositoryImportStates.PartiallyFailed; CompletedUtc = now; UpdatedUtc = now; }
    public void Fail(string code, string message, DateTime now) { Status = DocumentRepositoryImportStates.Failed; FailureCode = code; FailureMessage = message; CompletedUtc = now; UpdatedUtc = now; }
}

public sealed class CompanyDocumentRepositoryImportItem : ICompanyOwnedEntity
{
    private CompanyDocumentRepositoryImportItem() { }
    public CompanyDocumentRepositoryImportItem(Guid companyId, Guid jobId, string driveId, string itemId, string name, string version, string? webUrl, string? contentType, long size, DateTime? modified, DateTime now)
    { Id = Guid.NewGuid(); CompanyId = companyId; JobId = jobId; DriveId = driveId; ItemId = itemId; Name = name; RemoteVersion = version; SourceWebUrl = webUrl; ContentType = contentType; SizeBytes = size; LastModifiedUtc = modified; Status = DocumentRepositoryImportItemStates.Pending; CreatedUtc = now; UpdatedUtc = now; }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid JobId { get; private set; }
    public Guid? DocumentId { get; private set; }
    public string DriveId { get; private set; } = null!;
    public string ItemId { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string RemoteVersion { get; private set; } = null!;
    public string? SourceWebUrl { get; private set; }
    public string? ContentType { get; private set; }
    public long SizeBytes { get; private set; }
    public DateTime? LastModifiedUtc { get; private set; }
    public string Status { get; private set; } = null!;
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }
    public bool CanRetry { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public CompanyDocumentRepositoryImportJob Job { get; private set; } = null!;
    public void Complete(Guid documentId, DateTime now) { DocumentId = documentId; Status = DocumentRepositoryImportItemStates.Completed; UpdatedUtc = now; }
    public void Skip(Guid documentId, DateTime now) { DocumentId = documentId; Status = DocumentRepositoryImportItemStates.Unchanged; UpdatedUtc = now; }
    public void Fail(string code, string message, bool retry, DateTime now) { Status = DocumentRepositoryImportItemStates.Failed; FailureCode = code; FailureMessage = message; CanRetry = retry; UpdatedUtc = now; }
}

public static class DocumentRepositoryImportStates
{
    public const string Queued = "queued", Running = "running", Completed = "completed", PartiallyFailed = "partially_failed", Failed = "failed";
}

public sealed class CompanyKnowledgeDocumentRemoteSource : ICompanyOwnedEntity
{
    private CompanyKnowledgeDocumentRemoteSource() { }
    public CompanyKnowledgeDocumentRemoteSource(Guid companyId, Guid connectionId, Guid documentId, string driveId, string itemId, string version, string webUrl, string hash, DateTime now)
    { Id = Guid.NewGuid(); CompanyId = companyId; ConnectionId = connectionId; DocumentId = documentId; DriveId = driveId; ItemId = itemId; RemoteVersion = version; ObservedRemoteVersion = version; SourceWebUrl = webUrl; ContentHash = hash; ImportState = DocumentRepositoryImportItemStates.Completed; IsAvailable = true; LastSeenUtc = now; LastValidatedUtc = now; CreatedUtc = now; UpdatedUtc = now; }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ConnectionId { get; private set; }
    public Guid DocumentId { get; private set; }
    public string DriveId { get; private set; } = null!;
    public string ItemId { get; private set; } = null!;
    public string RemoteVersion { get; private set; } = null!;
    public string ObservedRemoteVersion { get; private set; } = null!;
    public string SourceWebUrl { get; private set; } = null!;
    public string ContentHash { get; private set; } = null!;
    public string ImportState { get; private set; } = null!;
    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }
    public DateTime LastSeenUtc { get; private set; }
    public DateTime? LastValidatedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public CompanyDocumentRepositoryConnection Connection { get; private set; } = null!;
    public CompanyKnowledgeDocument Document { get; private set; } = null!;
    public void ObserveVersion(string version, string webUrl, DateTime now) { ObservedRemoteVersion = version; SourceWebUrl = webUrl; LastSeenUtc = now; IsAvailable = string.Equals(RemoteVersion, version, StringComparison.Ordinal); UnavailableReason = IsAvailable ? null : "replacement_pending"; ImportState = IsAvailable ? DocumentRepositoryImportItemStates.Completed : DocumentRepositoryImportItemStates.Pending; UpdatedUtc = now; }
    public void CompleteVersion(string version, string hash, string webUrl, DateTime now) { RemoteVersion = ObservedRemoteVersion = version; ContentHash = hash; SourceWebUrl = webUrl; ImportState = DocumentRepositoryImportItemStates.Completed; IsAvailable = true; UnavailableReason = null; LastSeenUtc = now; LastValidatedUtc = now; UpdatedUtc = now; }
    public void MarkUnavailable(string reason, DateTime now) { IsAvailable = false; UnavailableReason = reason; ImportState = "unavailable"; LastValidatedUtc = UpdatedUtc = now; }
    public void MarkValidated(string currentVersion, DateTime now) { LastValidatedUtc = now; if (!string.Equals(currentVersion, ObservedRemoteVersion, StringComparison.Ordinal)) { ObservedRemoteVersion = currentVersion; IsAvailable = false; UnavailableReason = "replacement_pending"; ImportState = DocumentRepositoryImportItemStates.Pending; } UpdatedUtc = now; }
}

public static class DocumentRepositoryImportItemStates
{
    public const string Pending = "pending", Completed = "completed", Unchanged = "unchanged", Failed = "failed";
}
