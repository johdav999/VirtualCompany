using System.Text.Json.Nodes;
using System.Security.Cryptography;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class CompanyKnowledgeDocument : ICompanyOwnedEntity
{
    private CompanyKnowledgeDocument()
    {
    }

    public CompanyKnowledgeDocument(
        Guid id,
        Guid companyId,
        string title,
        CompanyKnowledgeDocumentType documentType,
        string storageKey,
        string? storageUrl,
        string originalFileName,
        string? contentType,
        string fileExtension,
        long fileSizeBytes,
        Dictionary<string, JsonNode?>? metadata = null,
        CompanyKnowledgeDocumentAccessScope? accessScope = null,
        string? sourceRef = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (fileSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileSizeBytes), "FileSizeBytes must be greater than zero.");
        }

        CompanyKnowledgeDocumentTypeValues.EnsureSupported(documentType, nameof(documentType));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Title = NormalizeRequired(title, nameof(title), 200);
        DocumentType = documentType;
        SourceType = CompanyKnowledgeDocumentSourceType.Upload;
        SourceRef = NormalizeOptional(sourceRef, nameof(sourceRef), 512);
        StorageKey = NormalizeRequired(storageKey, nameof(storageKey), 1024);
        StorageUrl = NormalizeOptional(storageUrl, nameof(storageUrl), 2048);
        OriginalFileName = NormalizeRequired(originalFileName, nameof(originalFileName), 255);
        ContentType = NormalizeOptional(contentType, nameof(contentType), 255);
        FileExtension = NormalizeRequired(fileExtension, nameof(fileExtension), 16).ToLowerInvariant();
        FileSizeBytes = fileSizeBytes;
        Metadata = NormalizeJsonDictionary(metadata);
        AccessScope = (accessScope ?? throw new ArgumentNullException(nameof(accessScope))).NormalizeForCompany(companyId);
        IngestionStatus = CompanyKnowledgeDocumentIngestionStatus.Uploaded;
        CreatedUtc = DateTime.UtcNow;
        IndexingStatus = CompanyKnowledgeDocumentIndexingStatus.NotIndexed;
        CurrentChunkSetVersion = 0;
        ActiveChunkCount = 0;
        CurrentChunkSetFingerprint = null;
        UpdatedUtc = CreatedUtc;
        UploadedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Title { get; private set; } = null!;
    public CompanyKnowledgeDocumentType DocumentType { get; private set; }
    public CompanyKnowledgeDocumentSourceType SourceType { get; private set; }
    public string? SourceRef { get; private set; }
    public string StorageKey { get; private set; } = null!;
    public string? StorageUrl { get; private set; }
    public string OriginalFileName { get; private set; } = null!;
    public string? ContentType { get; private set; }
    public string FileExtension { get; private set; } = null!;
    public long FileSizeBytes { get; private set; }
    public Dictionary<string, JsonNode?> Metadata { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public CompanyKnowledgeDocumentAccessScope AccessScope { get; private set; } = new();
    public CompanyKnowledgeDocumentIngestionStatus IngestionStatus { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }
    public string? FailureAction { get; private set; }
    public string? FailureTechnicalDetail { get; private set; }
    public string? ExtractedText { get; private set; }
    public CompanyKnowledgeDocumentIndexingStatus IndexingStatus { get; private set; }
    public string? IndexingFailureCode { get; private set; }
    public string? IndexingFailureMessage { get; private set; }
    public string? EmbeddingProvider { get; private set; }
    public string? EmbeddingModel { get; private set; }
    public string? EmbeddingModelVersion { get; private set; }
    public int? EmbeddingDimensions { get; private set; }
    public int CurrentChunkSetVersion { get; private set; }
    public int ActiveChunkCount { get; private set; }
    public string? CurrentChunkSetFingerprint { get; private set; }
    public bool CanRetry { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? UploadedUtc { get; private set; }
    public DateTime? IndexingRequestedUtc { get; private set; }
    public DateTime? ProcessingStartedUtc { get; private set; }
    public DateTime? IndexingStartedUtc { get; private set; }
    public DateTime? IndexedUtc { get; private set; }
    public DateTime? IndexingFailedUtc { get; private set; }
    public DateTime? ProcessedUtc { get; private set; }
    public DateTime? FailedUtc { get; private set; }
    public Company Company { get; private set; } = null!;

    public bool TryAcquireIndexingLease(DateTime utcNow, TimeSpan claimTimeout)
    {
        if (IngestionStatus is CompanyKnowledgeDocumentIngestionStatus.Uploaded or
            CompanyKnowledgeDocumentIngestionStatus.PendingScan or
            CompanyKnowledgeDocumentIngestionStatus.Blocked)
        {
            return false;
        }

        if (IndexingStatus == CompanyKnowledgeDocumentIndexingStatus.Queued)
        {
            IndexingStatus = CompanyKnowledgeDocumentIndexingStatus.Indexing;
            IndexingRequestedUtc ??= utcNow;
            IndexingStartedUtc = utcNow;
            ClearIndexingFailure();
            UpdatedUtc = utcNow;
            return true;
        }

        return IndexingStatus == CompanyKnowledgeDocumentIndexingStatus.Indexing && IndexingStartedUtc.HasValue && IndexingStartedUtc.Value <= utcNow.Subtract(claimTimeout) && RefreshIndexingLease(utcNow);
    }

    public bool MarkUploaded(string storageKey, string? storageUrl = null)
    {
        StorageKey = NormalizeRequired(storageKey, nameof(storageKey), 1024);
        StorageUrl = NormalizeOptional(storageUrl, nameof(storageUrl), 2048);
        IngestionStatus = CompanyKnowledgeDocumentIngestionStatus.Uploaded;
        UploadedUtc = DateTime.UtcNow;
        ProcessingStartedUtc = null;
        ProcessedUtc = null;
        QueueIndexingInternal();
        ClearFailure();
        UpdatedUtc = UploadedUtc.Value;
        return true;
    }

    public bool MarkPendingScan()
    {
        if (IngestionStatus == CompanyKnowledgeDocumentIngestionStatus.PendingScan)
        {
            return false;
        }

        EnsureStatus(nameof(MarkPendingScan), CompanyKnowledgeDocumentIngestionStatus.Uploaded);
        IngestionStatus = CompanyKnowledgeDocumentIngestionStatus.PendingScan;
        ClearFailure();
        UpdatedUtc = DateTime.UtcNow;
        return true;
    }

    public bool MarkProcessing()
    {
        if (IngestionStatus == CompanyKnowledgeDocumentIngestionStatus.Processing)
        {
            return false;
        }

        EnsureStatus(nameof(MarkProcessing), CompanyKnowledgeDocumentIngestionStatus.ScanClean);
        IngestionStatus = CompanyKnowledgeDocumentIngestionStatus.Processing;
        ProcessingStartedUtc ??= DateTime.UtcNow;
        ProcessedUtc = null;
        ClearFailure();
        UpdatedUtc = DateTime.UtcNow;
        return true;
    }

    public bool MarkScanClean()
    {
        if (IngestionStatus == CompanyKnowledgeDocumentIngestionStatus.ScanClean)
        {
            return false;
        }

        EnsureStatus(nameof(MarkScanClean), CompanyKnowledgeDocumentIngestionStatus.Uploaded, CompanyKnowledgeDocumentIngestionStatus.PendingScan);
        IngestionStatus = CompanyKnowledgeDocumentIngestionStatus.ScanClean;
        ProcessingStartedUtc = null;
        ProcessedUtc = null;
        QueueIndexingInternal();
        ClearFailure();
        UpdatedUtc = DateTime.UtcNow;
        return true;
    }

    public bool MarkBlocked(
        string failureCode,
        string failureMessage,
        string failureAction,
        string? failureTechnicalDetail = null,
        bool canRetry = false)
    {
        var normalizedFailureCode = NormalizeRequired(failureCode, nameof(failureCode), 100);
        var normalizedFailureMessage = NormalizeRequired(failureMessage, nameof(failureMessage), 2000);
        var normalizedFailureAction = NormalizeRequired(failureAction, nameof(failureAction), 500);
        var normalizedFailureTechnicalDetail = NormalizeOptional(failureTechnicalDetail, nameof(failureTechnicalDetail), 4000);

        if (IngestionStatus == CompanyKnowledgeDocumentIngestionStatus.Blocked)
        {
            var changed =
                !string.Equals(FailureCode, normalizedFailureCode, StringComparison.Ordinal) ||
                !string.Equals(FailureMessage, normalizedFailureMessage, StringComparison.Ordinal) ||
                !string.Equals(FailureAction, normalizedFailureAction, StringComparison.Ordinal) ||
                !string.Equals(FailureTechnicalDetail, normalizedFailureTechnicalDetail, StringComparison.Ordinal) ||
                CanRetry != canRetry;

            if (!changed)
            {
                return false;
            }
        }
        else
        {
            EnsureStatus(nameof(MarkBlocked),
                CompanyKnowledgeDocumentIngestionStatus.Uploaded,
                CompanyKnowledgeDocumentIngestionStatus.PendingScan,
                CompanyKnowledgeDocumentIngestionStatus.ScanClean);
            IngestionStatus = CompanyKnowledgeDocumentIngestionStatus.Blocked;
        }

        FailureCode = normalizedFailureCode;
        FailureMessage = normalizedFailureMessage;
        FailureAction = normalizedFailureAction;
        FailureTechnicalDetail = normalizedFailureTechnicalDetail;
        CanRetry = canRetry;
        FailedUtc = DateTime.UtcNow;
        UpdatedUtc = FailedUtc.Value;
        return true;
    }

    public bool MarkProcessed()
    {
        if (IngestionStatus == CompanyKnowledgeDocumentIngestionStatus.Processed)
        {
            return false;
        }

        EnsureStatus(nameof(MarkProcessed), CompanyKnowledgeDocumentIngestionStatus.Processing);
        IngestionStatus = CompanyKnowledgeDocumentIngestionStatus.Processed;
        ProcessedUtc = DateTime.UtcNow;
        ClearFailure();
        UpdatedUtc = ProcessedUtc.Value;
        return true;
    }

    public bool QueueIndexing()
    {
        if (IndexingStatus == CompanyKnowledgeDocumentIndexingStatus.Queued)
        {
            return false;
        }

        if (IngestionStatus is CompanyKnowledgeDocumentIngestionStatus.Uploaded or CompanyKnowledgeDocumentIngestionStatus.PendingScan or CompanyKnowledgeDocumentIngestionStatus.Blocked)
        {
            throw new InvalidOperationException("Document indexing can only be queued after the document is scan-clean or processed.");
        }

        QueueIndexingInternal();
        UpdatedUtc = DateTime.UtcNow;
        return true;
    }

    public bool MarkIndexing()
    {
        if (IndexingStatus == CompanyKnowledgeDocumentIndexingStatus.Indexing)
        {
            return false;
        }

        if (IngestionStatus is CompanyKnowledgeDocumentIngestionStatus.Uploaded or CompanyKnowledgeDocumentIngestionStatus.PendingScan or CompanyKnowledgeDocumentIngestionStatus.Blocked)
        {
            throw new InvalidOperationException("Document indexing cannot start before the document clears scanning.");
        }

        IndexingStatus = CompanyKnowledgeDocumentIndexingStatus.Indexing;
        IndexingStartedUtc = DateTime.UtcNow;
        IndexingRequestedUtc ??= IndexingStartedUtc;
        ClearIndexingFailure();
        UpdatedUtc = IndexingStartedUtc.Value;
        return true;
    }

    public bool MarkIndexed(
        string extractedText,
        int chunkSetVersion,
        int activeChunkCount,
        string embeddingProvider,
        string embeddingModel,
        string? embeddingModelVersion,
        int embeddingDimensions,
        string chunkSetFingerprint)
    {
        if (chunkSetVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSetVersion), "ChunkSetVersion must be greater than zero.");
        }

        if (activeChunkCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(activeChunkCount), "ActiveChunkCount must be greater than zero.");
        }

        ExtractedText = NormalizeExtractedText(extractedText);
        IndexingStatus = CompanyKnowledgeDocumentIndexingStatus.Indexed;
        CurrentChunkSetVersion = chunkSetVersion;
        ActiveChunkCount = activeChunkCount;
        EmbeddingProvider = NormalizeRequired(embeddingProvider, nameof(embeddingProvider), 100);
        EmbeddingModel = NormalizeRequired(embeddingModel, nameof(embeddingModel), 200);
        EmbeddingModelVersion = NormalizeOptional(embeddingModelVersion, nameof(embeddingModelVersion), 100);
        EmbeddingDimensions = embeddingDimensions > 0 ? embeddingDimensions : throw new ArgumentOutOfRangeException(nameof(embeddingDimensions));
        CurrentChunkSetFingerprint = NormalizeRequired(chunkSetFingerprint, nameof(chunkSetFingerprint), 128);
        IndexedUtc = DateTime.UtcNow;
        ClearIndexingFailure();
        UpdatedUtc = IndexedUtc.Value;
        return true;
    }

    public bool MarkIndexingFailed(string failureCode, string failureMessage)
    {
        var normalizedFailureCode = NormalizeRequired(failureCode, nameof(failureCode), 100);
        var normalizedFailureMessage = NormalizeRequired(failureMessage, nameof(failureMessage), 2000);

        if (IndexingStatus == CompanyKnowledgeDocumentIndexingStatus.Failed &&
            string.Equals(IndexingFailureCode, normalizedFailureCode, StringComparison.Ordinal) &&
            string.Equals(IndexingFailureMessage, normalizedFailureMessage, StringComparison.Ordinal))
        {
            return false;
        }

        IndexingStatus = CompanyKnowledgeDocumentIndexingStatus.Failed;
        IndexingFailureCode = normalizedFailureCode;
        IndexingFailureMessage = normalizedFailureMessage;
        IndexingFailedUtc = DateTime.UtcNow;
        UpdatedUtc = IndexingFailedUtc.Value;
        return true;
    }

    public bool HasIndexedChunkSet() =>
        CurrentChunkSetVersion > 0 &&
        ActiveChunkCount > 0 &&
        !string.IsNullOrWhiteSpace(EmbeddingModel) &&
        EmbeddingDimensions is > 0;

    public void RestoreIndexedChunkSet()
    {
        if (!HasIndexedChunkSet())
        {
            throw new InvalidOperationException("An indexed chunk set is required before the document can be restored to indexed state.");
        }

        IndexingStatus = CompanyKnowledgeDocumentIndexingStatus.Indexed;
        ClearIndexingFailure();
        UpdatedUtc = DateTime.UtcNow;
    }

    public void SetExtractedText(string extractedText) =>
        ExtractedText = NormalizeExtractedText(extractedText);

    public bool MarkFailed(
        string failureCode,
        string failureMessage,
        string failureAction,
        string? failureTechnicalDetail = null,
        bool canRetry = false)
    {
        var normalizedFailureCode = NormalizeRequired(failureCode, nameof(failureCode), 100);
        var normalizedFailureMessage = NormalizeRequired(failureMessage, nameof(failureMessage), 2000);
        var normalizedFailureAction = NormalizeRequired(failureAction, nameof(failureAction), 500);
        var normalizedFailureTechnicalDetail = NormalizeOptional(failureTechnicalDetail, nameof(failureTechnicalDetail), 4000);

        if (IngestionStatus == CompanyKnowledgeDocumentIngestionStatus.Processed)
        {
            throw new InvalidOperationException("Processed documents cannot transition to failed.");
        }

        if (IngestionStatus is CompanyKnowledgeDocumentIngestionStatus.Failed)
        {
            var changed =
                !string.Equals(FailureCode, normalizedFailureCode, StringComparison.Ordinal) ||
                !string.Equals(FailureMessage, normalizedFailureMessage, StringComparison.Ordinal) ||
                !string.Equals(FailureAction, normalizedFailureAction, StringComparison.Ordinal) ||
                !string.Equals(FailureTechnicalDetail, normalizedFailureTechnicalDetail, StringComparison.Ordinal) ||
                CanRetry != canRetry;

            if (!changed)
            {
                return false;
            }

            FailureCode = normalizedFailureCode;
            FailureMessage = normalizedFailureMessage;
            FailureAction = normalizedFailureAction;
            FailureTechnicalDetail = normalizedFailureTechnicalDetail;
            CanRetry = canRetry;
            FailedUtc ??= DateTime.UtcNow;
            UpdatedUtc = DateTime.UtcNow;
            return true;
        }

        EnsureStatus(
            nameof(MarkFailed),
            CompanyKnowledgeDocumentIngestionStatus.Uploaded,
            CompanyKnowledgeDocumentIngestionStatus.PendingScan,
            CompanyKnowledgeDocumentIngestionStatus.ScanClean,
            CompanyKnowledgeDocumentIngestionStatus.Processing);
        IngestionStatus = CompanyKnowledgeDocumentIngestionStatus.Failed;
        FailureCode = normalizedFailureCode;
        FailureMessage = normalizedFailureMessage;
        FailureAction = normalizedFailureAction;
        FailureTechnicalDetail = normalizedFailureTechnicalDetail;
        CanRetry = canRetry;
        FailedUtc = DateTime.UtcNow;
        UpdatedUtc = FailedUtc.Value;
        return true;
    }

    private void EnsureStatus(string operationName, params CompanyKnowledgeDocumentIngestionStatus[] allowedStatuses)
    {
        if (allowedStatuses.Contains(IngestionStatus))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Document ingestion status cannot execute '{operationName}' from '{IngestionStatus.ToStorageValue()}'.");
    }

    public void SetMetadataValue(string key, JsonNode? value)
    {
        var normalizedKey = NormalizeRequired(key, nameof(key), 200);
        Metadata[normalizedKey] = value?.DeepClone();
        UpdatedUtc = DateTime.UtcNow;
    }

    private void ClearFailure()
    {
        FailureCode = null;
        FailureMessage = null;
        FailureAction = null;
        FailureTechnicalDetail = null;
        CanRetry = false;
        FailedUtc = null;
    }

    private bool RefreshIndexingLease(DateTime utcNow)
    {
        if (IndexingStatus != CompanyKnowledgeDocumentIndexingStatus.Indexing)
        {
            return false;
        }

        IndexingStartedUtc = utcNow;
        UpdatedUtc = utcNow;
        return true;
    }

    private void QueueIndexingInternal()
    {
        if (IngestionStatus == CompanyKnowledgeDocumentIngestionStatus.Failed &&
            string.Equals(FailureCode, "knowledge_indexing_failed", StringComparison.Ordinal))
        {
            IngestionStatus = CompanyKnowledgeDocumentIngestionStatus.ScanClean;
            ProcessingStartedUtc = null;
            ProcessedUtc = null;
            ClearFailure();
        }

        IndexingStatus = CompanyKnowledgeDocumentIndexingStatus.Queued;
        IndexingRequestedUtc = DateTime.UtcNow;
        IndexingStartedUtc = null;
        IndexingFailedUtc = null;
        IndexedUtc = null;
        ClearIndexingFailure();
    }

    private void ClearIndexingFailure()
    {
        IndexingFailureCode = null;
        IndexingFailureMessage = null;
    }

    private static string NormalizeExtractedText(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("ExtractedText is required.", nameof(value))
            : value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private static Dictionary<string, JsonNode?> NormalizeJsonDictionary(Dictionary<string, JsonNode?>? value)
    {
        var normalized = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);

        if (value is null)
        {
            return normalized;
        }

        foreach (var pair in value)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            normalized[pair.Key.Trim()] = pair.Value?.DeepClone();
        }

        return normalized;
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }
}

public sealed class CompanyKnowledgeChunk : ICompanyOwnedEntity
{
    private CompanyKnowledgeChunk()
    {
    }

    public CompanyKnowledgeChunk(
        Guid id,
        Guid companyId,
        Guid documentId,
        int chunkSetVersion,
        int chunkIndex,
        string content,
        string embedding,
        string embeddingProvider,
        string embeddingModel,
        string? embeddingModelVersion,
        int embeddingDimensions,
        Dictionary<string, JsonNode?>? metadata = null,
        string? sourceReference = null,
        int? startOffset = null,
        int? endOffset = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (documentId == Guid.Empty)
        {
            throw new ArgumentException("DocumentId is required.", nameof(documentId));
        }

        if (chunkSetVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSetVersion));
        }

        if (chunkIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkIndex));
        }

        if (embeddingDimensions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(embeddingDimensions));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        DocumentId = documentId;
        ChunkSetVersion = chunkSetVersion;
        ChunkIndex = chunkIndex;
        Content = NormalizeRequired(content, nameof(content), 16000);
        Embedding = NormalizeRequired(embedding, nameof(embedding), 1024 * 1024);
        Metadata = NormalizeJsonDictionary(metadata);
        SourceReference = NormalizeOptional(sourceReference, nameof(sourceReference), 1024) ?? $"chunk:{chunkIndex}";
        StartOffset = startOffset;
        EndOffset = endOffset;
        ContentHash = ComputeContentHash(Content);
        EmbeddingProvider = NormalizeRequired(embeddingProvider, nameof(embeddingProvider), 100);
        EmbeddingModel = NormalizeRequired(embeddingModel, nameof(embeddingModel), 200);
        EmbeddingModelVersion = NormalizeOptional(embeddingModelVersion, nameof(embeddingModelVersion), 100);
        EmbeddingDimensions = embeddingDimensions;
        IsActive = true;
        CreatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid DocumentId { get; private set; }
    public int ChunkSetVersion { get; private set; }
    public int ChunkIndex { get; private set; }
    public bool IsActive { get; private set; }
    public string Content { get; private set; } = null!;
    public string Embedding { get; private set; } = null!;
    public Dictionary<string, JsonNode?> Metadata { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public string SourceReference { get; private set; } = null!;
    public int? StartOffset { get; private set; }
    public int? EndOffset { get; private set; }
    public string ContentHash { get; private set; } = null!;
    public string? EmbeddingProvider { get; private set; }
    public string EmbeddingModel { get; private set; } = null!;
    public string? EmbeddingModelVersion { get; private set; }
    public int EmbeddingDimensions { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public CompanyKnowledgeDocument Document { get; private set; } = null!;

    public void Deactivate()
    {
        IsActive = false;
    }

    private static string ComputeContentHash(string content)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static Dictionary<string, JsonNode?> NormalizeJsonDictionary(Dictionary<string, JsonNode?>? value)
    {
        var normalized = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);

        if (value is null)
        {
            return normalized;
        }

        foreach (var pair in value)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            normalized[pair.Key.Trim()] = pair.Value?.DeepClone();
        }

        return normalized;
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : NormalizeRequired(value, name, maxLength);
}