namespace VirtualCompany.Domain.Entities;

public sealed class CompanyDocumentPublicationRequest : ICompanyOwnedEntity
{
    private CompanyDocumentPublicationRequest() { }

    public CompanyDocumentPublicationRequest(Guid id, Guid companyId, Guid connectionId, string targetFolderItemId,
        string fileName, string? contentType, long sizeBytes, string contentSha256, string storageKey,
        Guid requestingAgentId, string requestingActorType, Guid requestingActorId, string idempotencyKey,
        DateTime createdUtc)
    {
        Id = Required(id, nameof(id));
        CompanyId = Required(companyId, nameof(companyId));
        ConnectionId = Required(connectionId, nameof(connectionId));
        TargetFolderItemId = Text(targetFolderItemId, nameof(targetFolderItemId), 160);
        FileName = FileNameValue(fileName);
        ContentType = Optional(contentType, 160);
        if (sizeBytes <= 0 || sizeBytes > 25 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(sizeBytes));
        SizeBytes = sizeBytes;
        ContentSha256 = Hash(contentSha256);
        StorageKey = Text(storageKey, nameof(storageKey), 512);
        RequestingAgentId = Required(requestingAgentId, nameof(requestingAgentId));
        RequestingActorType = Text(requestingActorType, nameof(requestingActorType), 32);
        RequestingActorId = Required(requestingActorId, nameof(requestingActorId));
        IdempotencyKey = Text(idempotencyKey, nameof(idempotencyKey), 200);
        Status = DocumentPublicationStatuses.Staged;
        OperationKind = DocumentPublicationOperationKinds.Create;
        CreatedUtc = Utc(createdUtc);
        UpdatedUtc = CreatedUtc;
    }

    public CompanyDocumentPublicationRequest(Guid id, Guid companyId, Guid connectionId, string targetFolderItemId,
        string targetItemId, string expectedRemoteVersion, string originalEvidenceVersion, string fileName,
        string? contentType, long sizeBytes, string contentSha256, string storageKey, long originalSizeBytes,
        string originalContentSha256, string originalStorageKey, Guid requestingAgentId, string requestingActorType,
        Guid requestingActorId, string idempotencyKey, Guid? stalePublicationRequestId, DateTime createdUtc)
        : this(id, companyId, connectionId, targetFolderItemId, fileName, contentType, sizeBytes, contentSha256,
            storageKey, requestingAgentId, requestingActorType, requestingActorId, idempotencyKey, createdUtc)
    {
        OperationKind = DocumentPublicationOperationKinds.Update;
        TargetItemId = Text(targetItemId, nameof(targetItemId), 200);
        ExpectedRemoteVersion = Text(expectedRemoteVersion, nameof(expectedRemoteVersion), 512);
        OriginalEvidenceVersion = Text(originalEvidenceVersion, nameof(originalEvidenceVersion), 512);
        if (originalSizeBytes <= 0 || originalSizeBytes > 25 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(originalSizeBytes));
        OriginalSizeBytes = originalSizeBytes;
        OriginalContentSha256 = Hash(originalContentSha256);
        OriginalStorageKey = Text(originalStorageKey, nameof(originalStorageKey), 512);
        StalePublicationRequestId = stalePublicationRequestId;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ConnectionId { get; private set; }
    public string TargetFolderItemId { get; private set; } = null!;
    public string FileName { get; private set; } = null!;
    public string? ContentType { get; private set; }
    public long SizeBytes { get; private set; }
    public string ContentSha256 { get; private set; } = null!;
    public string StorageKey { get; private set; } = null!;
    public Guid RequestingAgentId { get; private set; }
    public string RequestingActorType { get; private set; } = null!;
    public Guid RequestingActorId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string OperationKind { get; private set; } = null!;
    public string? TargetItemId { get; private set; }
    public string? ExpectedRemoteVersion { get; private set; }
    public string? OriginalEvidenceVersion { get; private set; }
    public long? OriginalSizeBytes { get; private set; }
    public string? OriginalContentSha256 { get; private set; }
    public string? OriginalStorageKey { get; private set; }
    public Guid? StalePublicationRequestId { get; private set; }
    public string? ConflictRemoteVersion { get; private set; }
    public Guid? ApprovalRequestId { get; private set; }
    public Guid? ToolExecutionAttemptId { get; private set; }
    public string? PolicyDecisionJson { get; private set; }
    public DateTime? ApprovalVersionUtc { get; private set; }
    public string? ProviderItemId { get; private set; }
    public string? ProviderVersion { get; private set; }
    public string? SourceWebUrl { get; private set; }
    public int AttemptCount { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public DateTime? LocalArtifactsPurgedUtc { get; private set; }
    public CompanyDocumentRepositoryConnection Connection { get; private set; } = null!;

    public void Queue(Guid approvalRequestId, Guid executionId, string policyDecisionJson, DateTime approvalVersionUtc, DateTime now)
    {
        if (Status == DocumentPublicationStatuses.Queued &&
            ApprovalRequestId == approvalRequestId && ToolExecutionAttemptId == executionId) return;
        if (Status != DocumentPublicationStatuses.Staged) throw new InvalidOperationException("Only a staged publication can be queued.");
        ApprovalRequestId = Required(approvalRequestId, nameof(approvalRequestId));
        ToolExecutionAttemptId = Required(executionId, nameof(executionId));
        PolicyDecisionJson = Text(policyDecisionJson, nameof(policyDecisionJson), 32000);
        ApprovalVersionUtc = Utc(approvalVersionUtc);
        Status = DocumentPublicationStatuses.Queued;
        FailureCode = FailureMessage = null;
        UpdatedUtc = Utc(now);
    }

    public void MarkSending(DateTime now)
    {
        if (Status is not (DocumentPublicationStatuses.Queued or DocumentPublicationStatuses.ReconciliationRequired or DocumentPublicationStatuses.Failed))
            throw new InvalidOperationException("The publication is not dispatchable.");
        Status = DocumentPublicationStatuses.Sending;
        AttemptCount++;
        FailureCode = FailureMessage = null;
        UpdatedUtc = Utc(now);
    }

    public void MarkReconciliationRequired(string code, string message, DateTime now)
    {
        Status = DocumentPublicationStatuses.ReconciliationRequired;
        FailureCode = Text(code, nameof(code), 64);
        FailureMessage = Text(message, nameof(message), 500);
        UpdatedUtc = Utc(now);
    }

    public void MarkFailed(string code, string message, DateTime now)
    {
        Status = DocumentPublicationStatuses.Failed;
        FailureCode = Text(code, nameof(code), 64);
        FailureMessage = Text(message, nameof(message), 500);
        UpdatedUtc = Utc(now);
    }

    public void MarkConflict(string currentRemoteVersion, DateTime now)
    {
        Status = DocumentPublicationStatuses.Conflict;
        ConflictRemoteVersion = Text(currentRemoteVersion, nameof(currentRemoteVersion), 512);
        FailureCode = "remote_version_conflict";
        FailureMessage = "A newer human edit was detected. Review the current version and prepare a new proposal.";
        UpdatedUtc = Utc(now);
    }

    public void MarkUnresolved(string currentRemoteVersion, DateTime now)
    {
        Status = DocumentPublicationStatuses.Unresolved;
        ConflictRemoteVersion = Text(currentRemoteVersion, nameof(currentRemoteVersion), 512);
        FailureCode = "ambiguous_update_unresolved";
        FailureMessage = "The update result cannot be reconciled because the remote file changed again. No overwrite will be attempted.";
        UpdatedUtc = Utc(now);
    }

    public void RequestReconciliation(DateTime now)
    {
        if (Status == DocumentPublicationStatuses.Delivered) return;
        if (Status is not (DocumentPublicationStatuses.ReconciliationRequired or DocumentPublicationStatuses.Unresolved))
            throw new InvalidOperationException("Only an uncertain publication can be reconciled.");
        Status = DocumentPublicationStatuses.ReconciliationRequired;
        FailureCode = "operator_reconciliation_requested";
        FailureMessage = "An administrator requested provider reconciliation. No unconditional upload will be attempted.";
        UpdatedUtc = Utc(now);
    }

    public void MarkDelivered(string providerItemId, string? providerVersion, string? sourceWebUrl, DateTime now)
    {
        ProviderItemId = Text(providerItemId, nameof(providerItemId), 200);
        ProviderVersion = Optional(providerVersion, 512);
        SourceWebUrl = Optional(sourceWebUrl, 2048);
        Status = DocumentPublicationStatuses.Delivered;
        FailureCode = FailureMessage = null;
        CompletedUtc = Utc(now);
        UpdatedUtc = CompletedUtc.Value;
    }

    public void MarkLocalArtifactsPurged(DateTime now)
    {
        if (Status is DocumentPublicationStatuses.Queued or DocumentPublicationStatuses.Sending or DocumentPublicationStatuses.ReconciliationRequired or DocumentPublicationStatuses.Unresolved)
            throw new InvalidOperationException("Artifacts required by pending approval or reconciliation cannot be purged.");
        if (Status == DocumentPublicationStatuses.Staged) Status = DocumentPublicationStatuses.Expired;
        LocalArtifactsPurgedUtc = UpdatedUtc = Utc(now);
    }

    private static Guid Required(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException(name + " is required.", name) : value;
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static string FileNameValue(string value)
    {
        var name = Text(value, nameof(value), 200);
        if (name is "." or ".." || name.IndexOfAny(['/', (char)92, ':', '*', '?', '"', '<', '>', '|']) >= 0)
            throw new ArgumentException("FileName contains unsupported characters.", nameof(value));
        return name;
    }
    private static string Hash(string value)
    {
        var hash = Text(value, nameof(value), 64).ToLowerInvariant();
        if (hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c))) throw new ArgumentException("A SHA-256 hash is required.", nameof(value));
        return hash;
    }
    private static string Text(string value, string name, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException(name + " is required.", name);
        var text = value.Trim();
        if (text.Length > max) throw new ArgumentOutOfRangeException(name);
        return text;
    }
    private static string? Optional(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null :
        value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(nameof(value));
}

public static class DocumentPublicationStatuses
{
    public const string Staged = "staged";
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string ReconciliationRequired = "reconciliation_required";
    public const string Failed = "failed";
    public const string Delivered = "delivered";
    public const string Conflict = "conflict";
    public const string Unresolved = "unresolved";
    public const string Expired = "expired";
}

public static class DocumentPublicationOperationKinds
{
    public const string Create = "create";
    public const string Update = "update";
}
