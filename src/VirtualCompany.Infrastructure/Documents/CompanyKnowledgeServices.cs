using System.Data.Common;
using System.Data;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Documents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.BackgroundJobs;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Documents;

public sealed class CompanyDocumentTextExtractor : ICompanyDocumentTextExtractor
{
    private readonly ICompanyDocumentStorage _storage;

    public CompanyDocumentTextExtractor(ICompanyDocumentStorage storage)
    {
        _storage = storage;
    }

    public async Task<string> ExtractAsync(CompanyKnowledgeDocument document, CancellationToken cancellationToken)
    {
        if (TryGetMetadataString(document.Metadata, "extracted_text", out var extractedText))
        {
            return Normalize(extractedText);
        }

        if (document.FileExtension is ".txt" or ".md")
        {
            await using var stream = await _storage.OpenReadAsync(document.StorageKey, cancellationToken);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
            return Normalize(await reader.ReadToEndAsync(cancellationToken));
        }

        throw new PermanentBackgroundJobException(
            $"No extracted text is available for '{document.OriginalFileName}'. Persist extracted_text metadata or add a document parser for '{document.FileExtension}'.");
    }

    private static bool TryGetMetadataString(IReadOnlyDictionary<string, JsonNode?> metadata, string key, out string value)
    {
        value = string.Empty;
        if (!metadata.TryGetValue(key, out var node) || node is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var rawValue))
        {
            return false;
        }

        value = rawValue?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string Normalize(string value)
    {
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new PermanentBackgroundJobException("The document text extraction step produced no searchable content.");
        }

        return normalized;
    }
}

public sealed class DefaultKnowledgeChunker : IKnowledgeChunker
{
    private readonly KnowledgeChunkingOptions _options;

    public DefaultKnowledgeChunker(IOptions<KnowledgeChunkingOptions> options)
    {
        _options = options.Value;
    }

    public IReadOnlyList<KnowledgeChunkDraft> ChunkDocument(CompanyKnowledgeDocument document, string extractedText)
    {
        var normalizedText = extractedText.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return Array.Empty<KnowledgeChunkDraft>();
        }

        var segments = SplitSegments(normalizedText, _options.TargetChunkLength);
        var chunks = new List<KnowledgeChunkDraft>();
        var builder = new StringBuilder();
        var chunkStart = 0;
        var chunkIndex = 0;
        var cursor = 0;

        foreach (var segment in segments)
        {
            if (builder.Length == 0)
            {
                chunkStart = segment.StartOffset;
            }

            if (builder.Length > 0 && builder.Length + 2 + segment.Content.Length > _options.TargetChunkLength)
            {
                chunks.Add(CreateChunk(document, normalizedText, chunkIndex++, builder.ToString(), chunkStart, cursor - 1));
                if (chunks.Count >= _options.MaxChunkCountPerDocument)
                {
                    return chunks;
                }

                var overlap = TakeOverlap(builder.ToString(), _options.OverlapLength);
                builder.Clear();
                if (!string.IsNullOrWhiteSpace(overlap))
                {
                    builder.Append(overlap);
                    chunkStart = Math.Max(0, cursor - overlap.Length);
                }
                else
                {
                    chunkStart = segment.StartOffset;
                }
            }

            if (builder.Length > 0)
            {
                builder.Append("\n\n");
            }

            builder.Append(segment.Content);
            cursor = segment.EndOffset;
        }

        if (builder.Length > 0 && chunks.Count < _options.MaxChunkCountPerDocument)
        {
            chunks.Add(CreateChunk(document, normalizedText, chunkIndex, builder.ToString(), chunkStart, cursor));
        }

        return chunks;
    }

    private KnowledgeChunkDraft CreateChunk(
        CompanyKnowledgeDocument document,
        string fullText,
        int chunkIndex,
        string content,
        int startOffset,
        int endOffset)
    {
        var trimmedContent = content.Trim();
        var metadata = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["strategy"] = JsonValue.Create(_options.StrategyVersion),
            ["start_offset"] = JsonValue.Create(startOffset),
            ["end_offset"] = JsonValue.Create(endOffset),
            ["document_title"] = JsonValue.Create(document.Title),
            ["document_type"] = JsonValue.Create(document.DocumentType.ToStorageValue()),
            ["original_file_name"] = JsonValue.Create(document.OriginalFileName)
        };

        return new KnowledgeChunkDraft(
            chunkIndex,
            trimmedContent,
            startOffset,
            endOffset,
            $"{document.OriginalFileName}#chunk-{chunkIndex + 1}",
            metadata);
    }

    private static IReadOnlyList<(string Content, int StartOffset, int EndOffset)> SplitSegments(string text, int targetChunkLength)
    {
        var segments = new List<(string, int, int)>();
        var matches = Regex.Matches(text, @"\S(?:.*?\S)?(?:(?:\n\s*\n)|\z)", RegexOptions.Singleline);

        foreach (Match match in matches)
        {
            var content = match.Value.Trim();
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            if (content.Length <= targetChunkLength)
            {
                segments.Add((content, match.Index, match.Index + content.Length));
                continue;
            }

            var localStart = 0;
            foreach (var piece in SplitLargeSegment(content, targetChunkLength))
            {
                var start = content.IndexOf(piece, localStart, StringComparison.Ordinal);
                if (start < 0)
                {
                    start = localStart;
                }

                localStart = start + piece.Length;
                segments.Add((piece, match.Index + start, match.Index + start + piece.Length));
            }
        }

        return segments;
    }

    private static IEnumerable<string> SplitLargeSegment(string content, int maxLength)
    {
        foreach (var sentence in Regex.Split(content, @"(?<=[\.\!\?])\s+", RegexOptions.Singleline))
        {
            if (string.IsNullOrWhiteSpace(sentence))
            {
                continue;
            }

            if (sentence.Length <= maxLength)
            {
                yield return sentence.Trim();
                continue;
            }

            var start = 0;
            while (start < sentence.Length)
            {
                var length = Math.Min(maxLength, sentence.Length - start);
                yield return sentence.Substring(start, length).Trim();
                start += length;
            }
        }
    }

    private static string TakeOverlap(string content, int overlapLength)
    {
        if (string.IsNullOrWhiteSpace(content) || overlapLength <= 0)
        {
            return string.Empty;
        }

        var normalized = content.Trim();
        if (normalized.Length <= overlapLength)
        {
            return normalized;
        }

        return normalized[^overlapLength..].Trim();
    }
}

public sealed class OpenAiCompatibleEmbeddingGenerator : IEmbeddingGenerator
{
    public const string ClientName = "knowledge-embeddings";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly KnowledgeEmbeddingOptions _options;

    public OpenAiCompatibleEmbeddingGenerator(
        IHttpClientFactory httpClientFactory,
        IOptions<KnowledgeEmbeddingOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task<EmbeddingBatchResult> GenerateAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken)
    {
        var provider = NormalizeProvider(_options.Provider);

        if (inputs.Count == 0)
        {
            return new EmbeddingBatchResult(provider, _options.Model, _options.ModelVersion, _options.Dimensions, Array.Empty<EmbeddingVectorResult>());
        }

        if (string.Equals(provider, "deterministic", StringComparison.Ordinal))
        {
            var embeddings = inputs.Select(CreateDeterministicEmbedding).ToArray();
            return new EmbeddingBatchResult(provider, _options.Model, _options.ModelVersion, _options.Dimensions, embeddings);
        }

        if (!string.Equals(provider, "openai", StringComparison.Ordinal))
        {
            throw new PermanentBackgroundJobException($"Unsupported knowledge embedding provider '{provider}'.");
        }

        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new PermanentBackgroundJobException("Knowledge embedding provider BaseUrl is required.");
        }

        var client = _httpClientFactory.CreateClient(ClientName);
        client.BaseAddress = new Uri(_options.BaseUrl, UriKind.Absolute);
        client.Timeout = TimeSpan.FromSeconds(Math.Max(5, _options.TimeoutSeconds));
        client.DefaultRequestHeaders.Authorization =
            string.IsNullOrWhiteSpace(_options.ApiKey) ? null : new("Bearer", _options.ApiKey);

        var request = new Dictionary<string, object?>
        {
            ["model"] = _options.Model,
            ["input"] = inputs
        };

        if (_options.Dimensions > 0)
        {
            request["dimensions"] = _options.Dimensions;
        }

        using var response = await client.PostAsJsonAsync("embeddings", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Embedding provider returned {(int)response.StatusCode}: {responseBody}",
                null,
                response.StatusCode);
        }

        var payload = await response.Content.ReadFromJsonAsync<OpenAiEmbeddingResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Embedding provider returned an empty response.");

        var orderedEmbeddings = payload.Data
            .OrderBy(item => item.Index)
            .Select(item => new EmbeddingVectorResult(item.Embedding))
            .ToArray();

        return new EmbeddingBatchResult(
            provider,
            payload.Model ?? _options.Model,
            _options.ModelVersion,
            orderedEmbeddings.FirstOrDefault()?.Values.Count ?? _options.Dimensions,
            orderedEmbeddings);
    }

    private EmbeddingVectorResult CreateDeterministicEmbedding(string value)
    {
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        var vector = new float[_options.Dimensions];

        for (var index = 0; index < vector.Length; index++)
        {
            var byteValue = bytes[index % bytes.Length];
            vector[index] = (byteValue / 127.5f) - 1f;
        }

        Normalize(vector);
        return new EmbeddingVectorResult(vector);
    }

    private static string NormalizeProvider(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new PermanentBackgroundJobException("Knowledge embedding provider is required.");
        }

        return provider.Trim().ToLowerInvariant();
    }
    private static void Normalize(float[] vector)
    {
        var magnitude = Math.Sqrt(vector.Sum(x => x * x));
        if (magnitude <= 0d)
        {
            return;
        }

        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] = (float)(vector[index] / magnitude);
        }
    }

    private sealed class OpenAiEmbeddingResponse
    {
        public string? Model { get; set; }
        public List<OpenAiEmbeddingItem> Data { get; set; } = [];
    }

    private sealed class OpenAiEmbeddingItem
    {
        public int Index { get; set; }
        public List<float> Embedding { get; set; } = [];
    }
}

public sealed class CompanyKnowledgeIndexingProcessor : ICompanyKnowledgeIndexingProcessor
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyDocumentTextExtractor _textExtractor;
    private readonly IKnowledgeChunker _chunker;
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly IBackgroundJobExecutor _backgroundJobExecutor;
    private readonly KnowledgeIndexingOptions _options;
    private readonly ILogger<CompanyKnowledgeIndexingProcessor> _logger;

    public CompanyKnowledgeIndexingProcessor(
        VirtualCompanyDbContext dbContext,
        ICompanyDocumentTextExtractor textExtractor,
        IKnowledgeChunker chunker,
        IEmbeddingGenerator embeddingGenerator,
        IBackgroundJobExecutor backgroundJobExecutor,
        IOptions<KnowledgeIndexingOptions> options,
        ILogger<CompanyKnowledgeIndexingProcessor> logger)
    {
        _dbContext = dbContext;
        _textExtractor = textExtractor;
        _chunker = chunker;
        _embeddingGenerator = embeddingGenerator;
        _backgroundJobExecutor = backgroundJobExecutor;
        _options = options.Value;
        _logger = logger;
    }

    private TimeSpan ClaimTimeout => TimeSpan.FromSeconds(Math.Max(5, _options.ClaimTimeoutSeconds));

    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        var staleClaimCutoffUtc = DateTime.UtcNow - ClaimTimeout;

        var candidateDocuments = await _dbContext.CompanyKnowledgeDocuments
            .IgnoreQueryFilters()
            .Where(document =>
                (document.IndexingStatus == CompanyKnowledgeDocumentIndexingStatus.Queued ||
                 (document.IndexingStatus == CompanyKnowledgeDocumentIndexingStatus.Indexing &&
                  document.IndexingStartedUtc != null &&
                  document.IndexingStartedUtc <= staleClaimCutoffUtc)) &&
                (document.IngestionStatus == CompanyKnowledgeDocumentIngestionStatus.ScanClean ||
                 document.IngestionStatus == CompanyKnowledgeDocumentIngestionStatus.Processed))
            .OrderBy(document => document.IndexingRequestedUtc ?? document.UpdatedUtc)
            .Take(Math.Max(1, _options.BatchSize) * 2)
            .Select(document => new { document.CompanyId, document.Id })
            .ToListAsync(cancellationToken);

        var claimedDocuments = new List<(Guid CompanyId, Guid DocumentId)>(Math.Max(1, _options.BatchSize));
        foreach (var candidateDocument in candidateDocuments)
        {
            if (claimedDocuments.Count >= Math.Max(1, _options.BatchSize))
            {
                break;
            }

            if (await TryClaimDocumentAsync(candidateDocument.CompanyId, candidateDocument.Id, cancellationToken))
            {
                claimedDocuments.Add((candidateDocument.CompanyId, candidateDocument.Id));
            }
        }

        foreach (var claimedDocument in claimedDocuments)
        {
            _logger.LogInformation(
                "Claimed knowledge indexing job. CompanyId: {CompanyId}, DocumentId: {DocumentId}, ClaimTimeoutSeconds: {ClaimTimeoutSeconds}.",
                claimedDocument.CompanyId,
                claimedDocument.DocumentId,
                ClaimTimeout.TotalSeconds);

            await ProcessWithRetriesAsync(claimedDocument.CompanyId, claimedDocument.DocumentId, cancellationToken);
        }

        return claimedDocuments.Count;
    }

    private async Task<bool> TryClaimDocumentAsync(Guid companyId, Guid documentId, CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await LockDocumentRowAsync(companyId, documentId, cancellationToken);

            var document = await _dbContext.CompanyKnowledgeDocuments
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == documentId, cancellationToken);

            if (document is null || !document.TryAcquireIndexingLease(utcNow, ClaimTimeout))
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            document.SetMetadataValue(
                "last_indexing_attempt",
                BuildIndexingAttemptMetadata(
                    utcNow,
                    document.IndexingRequestedUtc,
                    document.IndexingStartedUtc,
                    ClaimTimeout,
                    _options.MaxAttempts));

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogDebug(
                ex,
                "Knowledge indexing claim contention detected. CompanyId: {CompanyId}, DocumentId: {DocumentId}.",
                companyId,
                documentId);

            _dbContext.ChangeTracker.Clear();
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static JsonObject BuildIndexingAttemptMetadata(
        DateTime claimedUtc,
        DateTime? requestedUtc,
        DateTime? startedUtc,
        TimeSpan claimTimeout,
        int maxAttempts)
    {
        return new JsonObject
        {
            ["claimed_utc"] = claimedUtc,
            ["requested_utc"] = requestedUtc,
            ["started_utc"] = startedUtc,
            ["claim_timeout_seconds"] = (int)Math.Max(0, claimTimeout.TotalSeconds),
            ["max_attempts"] = Math.Max(1, maxAttempts)
        };
    }

    public async Task IndexDocumentAsync(Guid companyId, Guid documentId, CancellationToken cancellationToken)
    {
        var indexedChunkSetVersion = 0;
        var indexedChunkCount = 0;

        var document = await _dbContext.CompanyKnowledgeDocuments
            .IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == companyId && x.Id == documentId, cancellationToken);

        if (document.IngestionStatus == CompanyKnowledgeDocumentIngestionStatus.ScanClean)
        {
            document.MarkProcessing();
        }

        document.MarkIndexing();
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Knowledge document indexing started. CompanyId: {CompanyId}, DocumentId: {DocumentId}, IngestionStatus: {IngestionStatus}.",
            companyId,
            documentId,
            document.IngestionStatus.ToStorageValue());

        var extractedText = await _textExtractor.ExtractAsync(document, cancellationToken);
        var chunkDrafts = _chunker.ChunkDocument(document, extractedText);
        if (chunkDrafts.Count == 0)
        {
            throw new PermanentBackgroundJobException("Chunking produced no searchable content.");
        }

        _logger.LogInformation(
            "Knowledge document chunking completed. CompanyId: {CompanyId}, DocumentId: {DocumentId}, ChunkCount: {ChunkCount}.",
            companyId,
            documentId,
            chunkDrafts.Count);

        _logger.LogInformation(
            "Knowledge embedding generation started. CompanyId: {CompanyId}, DocumentId: {DocumentId}, ChunkCount: {ChunkCount}.",
            companyId,
            documentId,
            chunkDrafts.Count);

        var embeddingBatch = await _embeddingGenerator.GenerateAsync(
            chunkDrafts.Select(chunk => chunk.Content).ToArray(),
            cancellationToken);

        if (embeddingBatch.Embeddings.Count != chunkDrafts.Count)
        {
            throw new InvalidOperationException("Embedding generation did not return a vector for every chunk.");
        }

        _logger.LogInformation(
            "Knowledge embedding generation completed. CompanyId: {CompanyId}, DocumentId: {DocumentId}, EmbeddingProvider: {EmbeddingProvider}, EmbeddingModel: {EmbeddingModel}, Dimensions: {Dimensions}.",
            companyId,
            documentId,
            embeddingBatch.Provider,
            embeddingBatch.Model,
            embeddingBatch.Dimensions);

        var chunkSetFingerprint = ComputeChunkSetFingerprint(chunkDrafts, embeddingBatch);

        try
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            await LockDocumentRowAsync(companyId, documentId, cancellationToken);
            await _dbContext.Entry(document).ReloadAsync(cancellationToken);

            var currentActiveChunks = await _dbContext.CompanyKnowledgeChunks
                .IgnoreQueryFilters()
                .Where(chunk => chunk.CompanyId == companyId && chunk.DocumentId == documentId && chunk.IsActive)
                .ToListAsync(cancellationToken);

            if (CanReuseCurrentChunkSet(document, currentActiveChunks, chunkDrafts.Count, chunkSetFingerprint))
            {
                document.SetExtractedText(extractedText);
                document.Metadata.Remove("last_indexing_failure");
                if (document.IngestionStatus == CompanyKnowledgeDocumentIngestionStatus.Processing)
                {
                    document.MarkProcessed();
                }

                document.MarkIndexed(
                    extractedText,
                    document.CurrentChunkSetVersion,
                    currentActiveChunks.Count,
                    embeddingBatch.Provider,
                    embeddingBatch.Model,
                    embeddingBatch.ModelVersion,
                    embeddingBatch.Dimensions,
                    chunkSetFingerprint);

                indexedChunkSetVersion = document.CurrentChunkSetVersion;
                indexedChunkCount = currentActiveChunks.Count;

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                var nextVersion = document.CurrentChunkSetVersion + 1;

                foreach (var currentActiveChunk in currentActiveChunks)
                {
                    currentActiveChunk.Deactivate();
                }

                if (IsPostgreSql())
                {
                    await InsertChunksPostgreSqlAsync(document, nextVersion, chunkDrafts, embeddingBatch, cancellationToken);
                }
                else
                {
                    _dbContext.CompanyKnowledgeChunks.AddRange(BuildChunkEntities(document, nextVersion, chunkDrafts, embeddingBatch));
                }

                document.SetExtractedText(extractedText);
                document.Metadata.Remove("last_indexing_failure");
                if (document.IngestionStatus == CompanyKnowledgeDocumentIngestionStatus.Processing)
                {
                    document.MarkProcessed();
                }

                document.MarkIndexed(
                    extractedText,
                    nextVersion,
                    chunkDrafts.Count,
                    embeddingBatch.Provider,
                    embeddingBatch.Model,
                    embeddingBatch.ModelVersion,
                    embeddingBatch.Dimensions,
                    chunkSetFingerprint);

                indexedChunkSetVersion = nextVersion;
                indexedChunkCount = chunkDrafts.Count;

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (Exception)
        {
            if (await TryTreatActivationConflictAsSuccessfulRetryAsync(
                companyId,
                documentId,
                chunkSetFingerprint,
                chunkDrafts.Count,
                cancellationToken))
            {
                _logger.LogInformation(
                    "Knowledge document indexing converged on an existing active chunk set. CompanyId: {CompanyId}, DocumentId: {DocumentId}.",
                    companyId,
                    documentId);
                return;
            }

            throw;
        }

        _logger.LogInformation(
            "Knowledge document indexed. CompanyId: {CompanyId}, DocumentId: {DocumentId}, ChunkSetVersion: {ChunkSetVersion}, ChunkCount: {ChunkCount}.",
            companyId,
            documentId,
            indexedChunkSetVersion,
            indexedChunkCount);
    }

    private async Task ProcessWithRetriesAsync(Guid companyId, Guid documentId, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= Math.Max(1, _options.MaxAttempts); attempt++)
        {
            Exception? attemptException = null;
            var result = await _backgroundJobExecutor.ExecuteAsync(
                new BackgroundJobExecutionContext(
                    "knowledge-document-indexing",
                    attempt,
                    Math.Max(1, _options.MaxAttempts),
                    companyId),
                async innerToken =>
                {
                    try
                    {
                        await IndexDocumentAsync(companyId, documentId, innerToken);
                    }
                    catch (Exception ex)
                    {
                        attemptException = ex;
                        throw;
                    }
                },
                TimeSpan.FromSeconds(Math.Max(1, _options.RetryDelaySeconds)),
                cancellationToken);

            if (result.Outcome == BackgroundJobExecutionOutcome.Succeeded ||
                result.Outcome == BackgroundJobExecutionOutcome.PermanentFailure ||
                result.Outcome == BackgroundJobExecutionOutcome.RetryExhausted)
            {
                if (result.Outcome != BackgroundJobExecutionOutcome.Succeeded)
                {
                    await HandleFailureAsync(
                        companyId,
                        documentId,
                        attemptException ?? new InvalidOperationException(result.ErrorMessage ?? "Knowledge indexing failed."),
                        cancellationToken);
                }

                return;
            }

            if (result.RetryDelay.HasValue && result.RetryDelay.Value > TimeSpan.Zero)
            {
                _logger.LogWarning(
                    "Retrying knowledge document indexing. CompanyId: {CompanyId}, DocumentId: {DocumentId}, Attempt: {Attempt}, MaxAttempts: {MaxAttempts}, RetryDelaySeconds: {RetryDelaySeconds}.",
                    companyId,
                    documentId,
                    attempt,
                    Math.Max(1, _options.MaxAttempts),
                    result.RetryDelay.Value.TotalSeconds);

                await RefreshIndexingLeaseAsync(companyId, documentId, cancellationToken);
                await Task.Delay(result.RetryDelay.Value, cancellationToken);
            }
        }
    }

    private async Task HandleFailureAsync(Guid companyId, Guid documentId, Exception exception, CancellationToken cancellationToken)
    {
        if (_dbContext.Database.CurrentTransaction is not null)
        {
            await _dbContext.Database.RollbackTransactionAsync(cancellationToken);
        }

        var document = await _dbContext.CompanyKnowledgeDocuments
            .IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == companyId && x.Id == documentId, cancellationToken);

        var retryable = exception is not PermanentBackgroundJobException;
        var failureMessage = exception is PermanentBackgroundJobException
            ? exception.Message
            : "Knowledge indexing failed. Review the indexing error and retry when the dependency is available.";

        if (document.IngestionStatus == CompanyKnowledgeDocumentIngestionStatus.Processing)
        {
            document.MarkFailed(
                "knowledge_indexing_failed",
                failureMessage,
                retryable
                    ? "Retry indexing after the embedding dependency or document parser is available."
                    : "Provide extracted text metadata or upload a supported plain-text document.",
                exception.Message,
                retryable);

            document.MarkIndexingFailed("knowledge_indexing_failed", failureMessage);
            document.SetMetadataValue("last_indexing_failure", BuildIndexingFailureMetadata(failureMessage, retryable, exception.Message));
        }

        if (document.IngestionStatus == CompanyKnowledgeDocumentIngestionStatus.Processed && document.HasIndexedChunkSet())
        {
            document.RestoreIndexedChunkSet();
            document.SetMetadataValue("last_indexing_failure", BuildIndexingFailureMetadata(failureMessage, retryable, exception.Message));
        }
        else
        {
            document.MarkIndexingFailed("knowledge_indexing_failed", failureMessage);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            exception,
            "Knowledge document indexing failed. CompanyId: {CompanyId}, DocumentId: {DocumentId}, Retryable: {Retryable}.",
            companyId,
            documentId,
            retryable);
    }

    private async Task RefreshIndexingLeaseAsync(Guid companyId, Guid documentId, CancellationToken cancellationToken)
    {
        var document = await _dbContext.CompanyKnowledgeDocuments
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == documentId, cancellationToken);

        if (document is null || document.IndexingStatus != CompanyKnowledgeDocumentIndexingStatus.Indexing)
        {
            return;
        }

        document.SetMetadataValue("last_indexing_attempt", BuildIndexingAttemptMetadata(DateTime.UtcNow, document.IndexingRequestedUtc, DateTime.UtcNow, ClaimTimeout, _options.MaxAttempts));
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static JsonObject BuildIndexingFailureMetadata(string failureMessage, bool retryable, string technicalDetail)
    {
        return new JsonObject
        {
            ["code"] = "knowledge_indexing_failed",
            ["message"] = failureMessage,
            ["retryable"] = retryable,
            ["technical_detail"] = technicalDetail,
            ["occurred_utc"] = DateTime.UtcNow
        };
    }

    private static bool CanReuseCurrentChunkSet(
        CompanyKnowledgeDocument document,
        IReadOnlyCollection<CompanyKnowledgeChunk> currentActiveChunks,
        int expectedChunkCount,
        string chunkSetFingerprint)
    {
        return document.HasIndexedChunkSet() &&
               document.CurrentChunkSetVersion > 0 &&
               document.ActiveChunkCount == expectedChunkCount &&
               currentActiveChunks.Count == expectedChunkCount &&
               currentActiveChunks.All(chunk => chunk.ChunkSetVersion == document.CurrentChunkSetVersion) &&
               string.Equals(document.CurrentChunkSetFingerprint, chunkSetFingerprint, StringComparison.Ordinal);
    }

    private async Task<bool> TryTreatActivationConflictAsSuccessfulRetryAsync(
        Guid companyId,
        Guid documentId,
        string chunkSetFingerprint,
        int expectedChunkCount,
        CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();

        var document = await _dbContext.CompanyKnowledgeDocuments
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == documentId, cancellationToken);

        if (document is null ||
            document.IndexingStatus != CompanyKnowledgeDocumentIndexingStatus.Indexed ||
            !document.HasIndexedChunkSet() ||
            document.ActiveChunkCount != expectedChunkCount ||
            !string.Equals(document.CurrentChunkSetFingerprint, chunkSetFingerprint, StringComparison.Ordinal))
        {
            return false;
        }

        var activeChunkCount = await _dbContext.CompanyKnowledgeChunks
            .IgnoreQueryFilters()
            .Where(chunk =>
                chunk.CompanyId == companyId &&
                chunk.DocumentId == documentId &&
                chunk.IsActive &&
                chunk.ChunkSetVersion == document.CurrentChunkSetVersion)
            .CountAsync(cancellationToken);

        return activeChunkCount == expectedChunkCount;
    }

    private async Task LockDocumentRowAsync(Guid companyId, Guid documentId, CancellationToken cancellationToken)
    {
        if (!IsPostgreSql())
        {
            return;
        }

        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText =
            """
            SELECT 1
            FROM knowledge_documents
            WHERE "CompanyId" = @companyId AND "Id" = @documentId
            FOR UPDATE;
            """;
        AddParameter(command, "@companyId", companyId);
        AddParameter(command, "@documentId", documentId);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private IReadOnlyList<CompanyKnowledgeChunk> BuildChunkEntities(
        CompanyKnowledgeDocument document,
        int chunkSetVersion,
        IReadOnlyList<KnowledgeChunkDraft> chunkDrafts,
        EmbeddingBatchResult embeddingBatch)
    {
        var results = new List<CompanyKnowledgeChunk>(chunkDrafts.Count);
        for (var index = 0; index < chunkDrafts.Count; index++)
        {
            var chunk = chunkDrafts[index];
            var embedding = embeddingBatch.Embeddings[index];
            results.Add(
                new CompanyKnowledgeChunk(
                    Guid.NewGuid(),
                    document.CompanyId,
                    document.Id,
                    chunkSetVersion,
                    chunk.ChunkIndex,
                    chunk.Content,
                    KnowledgeEmbeddingSerializer.Serialize(embedding.Values),
                    embeddingBatch.Provider,
                    embeddingBatch.Model,
                    embeddingBatch.ModelVersion,
                    embeddingBatch.Dimensions,
                    chunk.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase),
                    chunk.SourceReference,
                    chunk.StartOffset,
                    chunk.EndOffset));
        }

        return results;
    }

    private async Task InsertChunksPostgreSqlAsync(
        CompanyKnowledgeDocument document,
        int chunkSetVersion,
        IReadOnlyList<KnowledgeChunkDraft> chunkDrafts,
        EmbeddingBatchResult embeddingBatch,
        CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        for (var index = 0; index < chunkDrafts.Count; index++)
        {
            var chunk = chunkDrafts[index];
            var embedding = KnowledgeEmbeddingSerializer.Serialize(embeddingBatch.Embeddings[index].Values);
            await using var command = connection.CreateCommand();
            command.Transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText =
                """
                INSERT INTO knowledge_chunks
                ("Id", "CompanyId", "DocumentId", "ChunkSetVersion", "ChunkIndex", "IsActive", "Content", "Embedding", metadata_json, "SourceReference", "StartOffset", "EndOffset", "ContentHash", "EmbeddingProvider", "EmbeddingModel", "EmbeddingModelVersion", "EmbeddingDimensions", "CreatedUtc")
                VALUES
                (@id, @companyId, @documentId, @chunkSetVersion, @chunkIndex, TRUE, @content, CAST(@embedding AS vector), CAST(@metadata AS jsonb), @sourceReference, @startOffset, @endOffset, @contentHash, @embeddingProvider, @embeddingModel, @embeddingModelVersion, @embeddingDimensions, @createdUtc);
                """;

            AddParameter(command, "@id", Guid.NewGuid());
            AddParameter(command, "@companyId", document.CompanyId);
            AddParameter(command, "@documentId", document.Id);
            AddParameter(command, "@chunkSetVersion", chunkSetVersion);
            AddParameter(command, "@chunkIndex", chunk.ChunkIndex);
            AddParameter(command, "@content", chunk.Content);
            AddParameter(command, "@embedding", embedding);
            AddParameter(command, "@metadata", JsonSerializer.Serialize(chunk.Metadata));
            AddParameter(command, "@sourceReference", chunk.SourceReference);
            AddParameter(command, "@startOffset", chunk.StartOffset);
            AddParameter(command, "@endOffset", chunk.EndOffset);
            AddParameter(command, "@contentHash", ComputeContentHash(chunk.Content));
            AddParameter(command, "@embeddingProvider", embeddingBatch.Provider);
            AddParameter(command, "@embeddingModel", embeddingBatch.Model);
            AddParameter(command, "@embeddingModelVersion", embeddingBatch.ModelVersion);
            AddParameter(command, "@embeddingDimensions", embeddingBatch.Dimensions);
            AddParameter(command, "@createdUtc", DateTime.UtcNow);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static string ComputeContentHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string ComputeChunkSetFingerprint(
        IReadOnlyList<KnowledgeChunkDraft> chunkDrafts,
        EmbeddingBatchResult embeddingBatch)
    {
        var builder = new StringBuilder();
        builder.Append(embeddingBatch.Provider)
            .Append('|')
            .Append(embeddingBatch.Model)
            .Append('|')
            .Append(embeddingBatch.ModelVersion)
            .Append('|')
            .Append(embeddingBatch.Dimensions);

        foreach (var chunk in chunkDrafts.OrderBy(chunk => chunk.ChunkIndex))
        {
            chunk.Metadata.TryGetValue("strategy", out var strategy);
            builder.AppendLine()
                .Append(chunk.ChunkIndex).Append('|')
                .Append(strategy?.ToJsonString() ?? string.Empty).Append('|')
                .Append(chunk.SourceReference).Append('|')
                .Append(ComputeContentHash(chunk.Content));
        }

        return ComputeContentHash(builder.ToString());
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private bool IsPostgreSql() =>
        string.Equals(_dbContext.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal);
}

public sealed class CompanyKnowledgeIndexingBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<KnowledgeIndexingOptions> _options;
    private readonly ILogger<CompanyKnowledgeIndexingBackgroundService> _logger;
    private bool _disabledDueToMissingSchema;

    public CompanyKnowledgeIndexingBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<KnowledgeIndexingOptions> options,
        ILogger<CompanyKnowledgeIndexingBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("Knowledge indexing background service is disabled.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _options.Value.PollIntervalSeconds)));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_disabledDueToMissingSchema)
                {
                    break;
                }

                using var scope = _scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<ICompanyKnowledgeIndexingProcessor>();
                await processor.ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (IsMissingKnowledgeSchema(ex))
            {
                _disabledDueToMissingSchema = true;
                _logger.LogWarning(
                    ex,
                    "Knowledge indexing background service is disabled because the knowledge document schema is not available in the current database.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Knowledge indexing polling loop failed unexpectedly.");
            }

            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    private static bool IsMissingKnowledgeSchema(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is SqliteException sqliteException &&
                sqliteException.SqliteErrorCode == 1 &&
                sqliteException.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase) &&
                sqliteException.Message.Contains("knowledge_documents", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (current is SqlException sqlException &&
                sqlException.Number == 208 &&
                sqlException.Message.Contains("knowledge_documents", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

public sealed class CompanyKnowledgeSearchService : ICompanyKnowledgeSearchService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly ICompanyMembershipContextResolver _membershipContextResolver;
    private readonly IKnowledgeAccessPolicyEvaluator _accessPolicyEvaluator;

    public CompanyKnowledgeSearchService(
        VirtualCompanyDbContext dbContext,
        IEmbeddingGenerator embeddingGenerator,
        ICompanyMembershipContextResolver membershipContextResolver,
        IKnowledgeAccessPolicyEvaluator accessPolicyEvaluator)
    {
        _dbContext = dbContext;
        _embeddingGenerator = embeddingGenerator;
        _membershipContextResolver = membershipContextResolver;
        _accessPolicyEvaluator = accessPolicyEvaluator;
    }

    public async Task<IReadOnlyList<CompanyKnowledgeSearchResultDto>> SearchAsync(
        CompanyKnowledgeSemanticSearchQuery query,
        CancellationToken cancellationToken)
    {
        if (query.CompanyId == Guid.Empty)
        {
            throw new CompanyKnowledgeSearchValidationException("CompanyId is required.");
        }

        if (string.IsNullOrWhiteSpace(query.QueryText))
        {
            throw new CompanyKnowledgeSearchValidationException("QueryText is required.");
        }

        var membership = await _membershipContextResolver.ResolveAsync(query.CompanyId, cancellationToken);
        if (membership is null && !HasRequestedMembership(query.AccessContext))
        {
            throw new UnauthorizedAccessException("The current user cannot search knowledge for this company.");
        }

        if (query.AccessContext is not null && query.AccessContext.CompanyId != Guid.Empty && query.AccessContext.CompanyId != query.CompanyId)
        {
            throw new CompanyKnowledgeSearchValidationException("AccessContext.CompanyId must match CompanyId.");
        }

        if (query.TopN is < 1 or > 20)
        {
            throw new CompanyKnowledgeSearchValidationException("TopN must be between 1 and 20.");
        }

        var scopedSearch = new ScopedKnowledgeSearchRequest(
            query.CompanyId,
            query.QueryText.Trim(),
            query.TopN,
            BuildAccessContext(query.CompanyId, membership, query.AccessContext));
        var embeddingBatch = await _embeddingGenerator.GenerateAsync([scopedSearch.QueryText], cancellationToken);
        if (embeddingBatch.Embeddings.Count == 0)
        {
            return Array.Empty<CompanyKnowledgeSearchResultDto>();
        }

        var queryEmbedding = KnowledgeEmbeddingSerializer.Serialize(embeddingBatch.Embeddings[0].Values);

        return IsPostgreSql()
            ? await SearchPostgreSqlAsync(scopedSearch, queryEmbedding, cancellationToken)
            : await SearchFallbackAsync(scopedSearch, embeddingBatch.Embeddings[0].Values, cancellationToken);
    }

    // Providers without PostgreSQL jsonb operators still fail closed by prefiltering
    // allowed documents before any similarity scoring happens in memory.
    private async Task<IReadOnlyDictionary<Guid, AllowedKnowledgeDocumentDescriptor>> LoadAllowedDocumentsAsync(
        Guid companyId,
        CompanyKnowledgeAccessContext accessContext,
        CancellationToken cancellationToken)
    {
        var documents = await _dbContext.CompanyKnowledgeDocuments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(document =>
                document.CompanyId == companyId &&
                document.IngestionStatus == CompanyKnowledgeDocumentIngestionStatus.Processed &&
                document.IndexingStatus == CompanyKnowledgeDocumentIndexingStatus.Indexed &&
                document.ActiveChunkCount > 0)
            .ToListAsync(cancellationToken);

        return documents
            .Where(document => _accessPolicyEvaluator.CanAccess(accessContext, document))
            .ToDictionary(
                document => document.Id,
                document => new AllowedKnowledgeDocumentDescriptor(document.Id));
    }

    private async Task<IReadOnlyList<CompanyKnowledgeSearchResultDto>> SearchPostgreSqlAsync(
        ScopedKnowledgeSearchRequest request,
        string queryEmbedding,
        CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        var sql = new StringBuilder(
            """
            -- Company and access predicates must be applied in SQL before pgvector
            -- similarity ordering and LIMIT so ranking only considers authorized chunks.
            WITH scoped_documents AS (
                SELECT
                    d."Id",
                    d."Title",
                    d."DocumentType",
                    d."SourceType",
                    d."SourceRef",
                    d."CurrentChunkSetVersion"
                FROM knowledge_documents d
                WHERE
                    d."CompanyId" = @companyId
                    AND d."IngestionStatus" = 'processed'
                    AND d."IndexingStatus" = 'indexed'
                    AND d."ActiveChunkCount" > 0
                    AND lower(COALESCE(d.access_scope_json ->> 'visibility', '')) = @companyVisibility
                    AND COALESCE(d.access_scope_json ->> 'company_id', '') = @companyIdText
            """);

        AppendPostgreSqlAccessPolicyPredicate(sql, command, request.AccessContext);
        sql.Append(
            """
            )
            SELECT
                kc."Id",
                kc."Content",
                kc."DocumentId",
                kc."ChunkIndex",
                kc."SourceReference",
                kc.metadata_json,
                d."Title",
                d."DocumentType",
                d."SourceType",
                d."SourceRef",
                1 - (kc."Embedding" <=> CAST(@queryEmbedding AS vector)) AS score
            FROM knowledge_chunks kc
            INNER JOIN scoped_documents d ON d."Id" = kc."DocumentId"
            WHERE
                kc."CompanyId" = @companyId
                AND kc."ChunkSetVersion" = d."CurrentChunkSetVersion"
                AND kc."IsActive" = TRUE
            ORDER BY kc."Embedding" <=> CAST(@queryEmbedding AS vector), kc."ChunkIndex" ASC, kc."DocumentId" ASC
            """);
        sql.AppendLine("LIMIT @topN;");

        command.CommandText = sql.ToString();
        AddParameter(command, "@companyId", request.CompanyId);
        AddParameter(command, "@companyIdText", request.CompanyId.ToString("D"));
        AddParameter(command, "@companyVisibility", CompanyKnowledgeDocumentAccessScope.CompanyVisibility);
        AddParameter(command, "@queryEmbedding", queryEmbedding);
        AddParameter(command, "@topN", request.TopN);

        var results = new List<CompanyKnowledgeSearchResultDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var chunkId = reader.GetGuid(0);
            var documentId = reader.GetGuid(2);
            var chunkIndex = reader.GetInt32(3);
            var chunkSourceReference = reader.GetString(4);
            var documentTitle = reader.GetString(6);
            var documentType = reader.GetString(7);
            var sourceType = reader.GetString(8);
            var sourceRef = reader.IsDBNull(9) ? null : reader.GetString(9);

            results.Add(
                new CompanyKnowledgeSearchResultDto(
                    chunkId,
                    reader.GetString(1),
                    reader.GetDouble(10),
                    documentId,
                    documentTitle,
                    chunkIndex,
                    chunkSourceReference,
                    DeserializeDictionary(reader.IsDBNull(5) ? "{}" : reader.GetString(5)),
                    new CompanyKnowledgeSourceReferenceDto(
                        documentId,
                        documentTitle,
                        documentType,
                        sourceType,
                        sourceRef,
                        chunkId,
                        chunkIndex,
                        chunkSourceReference),
                    new CompanyKnowledgeSourceDocumentDto(
                        documentId,
                        documentTitle,
                        documentType,
                        sourceType,
                        sourceRef)));
        }

        return results;
    }

    private static void AppendPostgreSqlAccessPolicyPredicate(
        StringBuilder sql,
        DbCommand command,
        CompanyKnowledgeAccessContext accessContext)
    {
        const string accessScopeColumn = "d.access_scope_json";

        AddParameter(command, "@membershipRoleLower", NormalizePostgreSqlIdentifier(accessContext.MembershipRole));
        AddParameter(command, "@agentIdLower", accessContext.AgentId?.ToString("D").ToLowerInvariant());
        var dataScopeParameters = AddNormalizedIdentifierParameters(command, "@dataScope", accessContext.DataScopes);

        sql.AppendLine($"                AND {BuildSingleIdentifierConstraintPredicate(accessScopeColumn, KnowledgeAccessPolicyEvaluator.RoleKeys, "@membershipRoleLower")}");
        sql.AppendLine($"                AND {BuildMultiIdentifierConstraintPredicate(accessScopeColumn, KnowledgeAccessPolicyEvaluator.ScopeKeys, dataScopeParameters)}");
        sql.AppendLine($"                AND {BuildSingleIdentifierConstraintPredicate(accessScopeColumn, KnowledgeAccessPolicyEvaluator.AgentKeys, "@agentIdLower")}");
        sql.AppendLine($"                AND {BuildRestrictedWithoutExplicitConstraintPredicate(accessScopeColumn)}");
    }

    private static IReadOnlyList<string> AddNormalizedIdentifierParameters(
        DbCommand command,
        string parameterPrefix,
        IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return Array.Empty<string>();
        }

        var normalizedValues = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalizedValues.Length == 0)
        {
            return Array.Empty<string>();
        }

        var parameterNames = new string[normalizedValues.Length];
        for (var index = 0; index < normalizedValues.Length; index++)
        {
            var parameterName = $"{parameterPrefix}{index}";
            parameterNames[index] = parameterName;
            AddParameter(command, parameterName, normalizedValues[index]);
        }

        return parameterNames;
    }

    private static string? NormalizePostgreSqlIdentifier(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static IReadOnlyList<string> NormalizeIdentifiers(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return Array.Empty<string>();
        }

        return values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string BuildSingleIdentifierConstraintPredicate(string jsonColumn, IReadOnlyList<string> keys, string parameterName)
    {
        var keyExists = BuildKeyExistsExpression(jsonColumn, keys);
        var allKeysValid = BuildAllKeysValidExpression(jsonColumn, keys);
        var anyMatch = string.Join(" OR ", keys.Select(key => BuildKeyMatchesAnyParameterExpression(jsonColumn, key, [parameterName])));
        return $"(NOT ({keyExists}) OR ({allKeysValid} AND {parameterName} IS NOT NULL AND ({anyMatch})))";
    }

    private static string BuildMultiIdentifierConstraintPredicate(string jsonColumn, IReadOnlyList<string> keys, IReadOnlyList<string> parameterNames)
    {
        var keyExists = BuildKeyExistsExpression(jsonColumn, keys);
        if (parameterNames.Count == 0)
        {
            return $"(NOT ({keyExists}))";
        }

        var allKeysValid = BuildAllKeysValidExpression(jsonColumn, keys);
        var anyMatch = string.Join(" OR ", keys.Select(key => BuildKeyMatchesAnyParameterExpression(jsonColumn, key, parameterNames)));
        return $"(NOT ({keyExists}) OR ({allKeysValid} AND ({anyMatch})))";
    }

    private static string BuildRestrictedWithoutExplicitConstraintPredicate(string jsonColumn)
    {
        var restrictedOrPrivate = $"{BuildAnyTrueBooleanExpression(jsonColumn, KnowledgeAccessPolicyEvaluator.RestrictedKeys)} OR {BuildAnyTrueBooleanExpression(jsonColumn, KnowledgeAccessPolicyEvaluator.PrivateKeys)}";
        var hasExplicitConstraint = string.Join(
            " OR ",
            [
                BuildConfiguredConstraintGroupExpression(jsonColumn, KnowledgeAccessPolicyEvaluator.RoleKeys),
                BuildConfiguredConstraintGroupExpression(jsonColumn, KnowledgeAccessPolicyEvaluator.ScopeKeys),
                BuildConfiguredConstraintGroupExpression(jsonColumn, KnowledgeAccessPolicyEvaluator.AgentKeys)
            ]);

        return $"(NOT ({restrictedOrPrivate}) OR ({hasExplicitConstraint}))";
    }

    private static string BuildConfiguredConstraintGroupExpression(string jsonColumn, IReadOnlyList<string> keys) =>
        $"({BuildAllKeysValidExpression(jsonColumn, keys)} AND {BuildAnyConfiguredIdentifiersExpression(jsonColumn, keys)})";

    private static string BuildKeyExistsExpression(string jsonColumn, IReadOnlyList<string> keys) =>
        string.Join(" OR ", keys.Select(key => $"{jsonColumn} ? '{key}'"));

    private static string BuildAllKeysValidExpression(string jsonColumn, IReadOnlyList<string> keys) =>
        string.Join(" AND ", keys.Select(key => BuildKeyValidExpression(jsonColumn, key)));

    private static string BuildAnyConfiguredIdentifiersExpression(string jsonColumn, IReadOnlyList<string> keys) =>
        "(" + string.Join(" OR ", keys.Select(key => BuildConfiguredIdentifiersExpressionForKey(jsonColumn, key))) + ")";

    private static string BuildKeyValidExpression(string jsonColumn, string key) =>
        $"(NOT ({jsonColumn} ? '{key}') OR ((jsonb_typeof({jsonColumn} -> '{key}') = 'string' AND btrim(COALESCE({jsonColumn} ->> '{key}', '')) <> '') OR (jsonb_typeof({jsonColumn} -> '{key}') = 'array' AND NOT EXISTS (SELECT 1 FROM jsonb_array_elements({jsonColumn} -> '{key}') AS item(value) WHERE jsonb_typeof(item.value) <> 'string' OR btrim(trim(BOTH '\"' FROM item.value::text)) = ''))))";

    private static string BuildConfiguredIdentifiersExpressionForKey(string jsonColumn, string key) =>
        $"((jsonb_typeof({jsonColumn} -> '{key}') = 'string' AND btrim(COALESCE({jsonColumn} ->> '{key}', '')) <> '') OR (jsonb_typeof({jsonColumn} -> '{key}') = 'array' AND EXISTS (SELECT 1 FROM jsonb_array_elements({jsonColumn} -> '{key}') AS item(value) WHERE jsonb_typeof(item.value) = 'string' AND btrim(trim(BOTH '\"' FROM item.value::text)) <> '')))";

    private static string BuildKeyMatchesAnyParameterExpression(string jsonColumn, string key, IReadOnlyList<string> parameterNames)
    {
        var scalarComparisons = string.Join(" OR ", parameterNames.Select(parameterName => $"lower({jsonColumn} ->> '{key}') = {parameterName}"));
        var arrayComparisons = string.Join(" OR ", parameterNames.Select(parameterName => $"lower(item.value) = {parameterName}"));
        return $"((jsonb_typeof({jsonColumn} -> '{key}') = 'string' AND ({scalarComparisons})) OR (jsonb_typeof({jsonColumn} -> '{key}') = 'array' AND EXISTS (SELECT 1 FROM jsonb_array_elements_text({jsonColumn} -> '{key}') AS item(value) WHERE {arrayComparisons})))";
    }

    private static string BuildAnyTrueBooleanExpression(string jsonColumn, IReadOnlyList<string> keys) =>
        "(" + string.Join(" OR ", keys.Select(key => $"(jsonb_typeof({jsonColumn} -> '{key}') = 'boolean' AND lower({jsonColumn} ->> '{key}') = 'true')")) + ")";

    private static CompanyKnowledgeAccessContext BuildAccessContext(
        Guid companyId,
        VirtualCompany.Application.Auth.ResolvedCompanyMembershipContext? membership,
        CompanyKnowledgeAccessContext? requestedContext)
    {
        var membershipId = requestedContext?.MembershipId ?? membership?.MembershipId;
        var userId = requestedContext?.UserId ?? membership?.UserId;
        var membershipRole = requestedContext?.MembershipRole
            ?? (membership is null ? null : membership.MembershipRole.ToStorageValue());

        if (!membershipId.HasValue || !userId.HasValue || string.IsNullOrWhiteSpace(membershipRole))
        {
            throw new UnauthorizedAccessException("The current user cannot search knowledge for this company.");
        }

        return new CompanyKnowledgeAccessContext(
            companyId,
            membershipId,
            userId,
            membershipRole,
            NormalizeIdentifiers(requestedContext?.DataScopes),
            requestedContext?.AgentId);
    }

    private static bool HasRequestedMembership(CompanyKnowledgeAccessContext? requestedContext)
    {
        return requestedContext is not null &&
               requestedContext.MembershipId.HasValue &&
               requestedContext.MembershipId.Value != Guid.Empty &&
               requestedContext.UserId.HasValue &&
               requestedContext.UserId.Value != Guid.Empty &&
               !string.IsNullOrWhiteSpace(requestedContext.MembershipRole);
    }

    private sealed record ScopedKnowledgeSearchRequest(
        Guid CompanyId,
        string QueryText,
        int TopN,
        CompanyKnowledgeAccessContext AccessContext);

    private async Task<IReadOnlyList<CompanyKnowledgeSearchResultDto>> SearchFallbackAsync(
        ScopedKnowledgeSearchRequest request,
        IReadOnlyList<float> queryEmbedding,
        CancellationToken cancellationToken)
    {
        var allowedDocuments = await LoadAllowedDocumentsAsync(request.CompanyId, request.AccessContext, cancellationToken);
        var allowedDocumentIds = allowedDocuments.Keys.ToArray();
        if (allowedDocumentIds.Length == 0)
        {
            return Array.Empty<CompanyKnowledgeSearchResultDto>();
        }

        var candidates = await _dbContext.CompanyKnowledgeChunks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(chunk =>
                chunk.CompanyId == request.CompanyId &&
                chunk.IsActive &&
                chunk.ChunkSetVersion == chunk.Document.CurrentChunkSetVersion &&
                allowedDocumentIds.Contains(chunk.DocumentId))
            .Select(chunk => new
            {
                chunk.Id,
                chunk.Content,
                chunk.DocumentId,
                chunk.ChunkIndex,
                chunk.SourceReference,
                chunk.Metadata,
                chunk.Embedding,
                DocumentTitle = chunk.Document.Title,
                DocumentType = chunk.Document.DocumentType,
                SourceType = chunk.Document.SourceType,
                chunk.Document.SourceRef
            })
            .ToListAsync(cancellationToken);

        return candidates
            .Select(chunk =>
            {
                var documentType = chunk.DocumentType.ToStorageValue();
                var sourceType = chunk.SourceType.ToStorageValue();
                return new CompanyKnowledgeSearchResultDto(
                    chunk.Id,
                    chunk.Content,
                    CosineSimilarity(queryEmbedding, KnowledgeEmbeddingSerializer.Deserialize(chunk.Embedding)),
                    chunk.DocumentId,
                    chunk.DocumentTitle,
                    chunk.ChunkIndex,
                    chunk.SourceReference,
                    chunk.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase),
                    new CompanyKnowledgeSourceReferenceDto(
                        chunk.DocumentId,
                        chunk.DocumentTitle,
                        documentType,
                        sourceType,
                        chunk.SourceRef,
                        chunk.Id,
                        chunk.ChunkIndex,
                        chunk.SourceReference),
                    new CompanyKnowledgeSourceDocumentDto(
                        chunk.DocumentId,
                        chunk.DocumentTitle,
                        documentType,
                        sourceType,
                        chunk.SourceRef));
            })
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.ChunkIndex)
            .ThenBy(result => result.DocumentId)
            .Take(request.TopN)
            .ToArray();
    }

    private static double CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        if (left.Count == 0 || right.Count == 0 || left.Count != right.Count)
        {
            return 0d;
        }

        double dot = 0d;
        double leftMagnitude = 0d;
        double rightMagnitude = 0d;

        for (var index = 0; index < left.Count; index++)
        {
            dot += left[index] * right[index];
            leftMagnitude += left[index] * left[index];
            rightMagnitude += right[index] * right[index];
        }

        if (leftMagnitude <= 0d || rightMagnitude <= 0d)
        {
            return 0d;
        }

        return dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude));
    }

    private static Dictionary<string, JsonNode?> DeserializeDictionary(string json)
    {
        var parsed = JsonNode.Parse(json) as JsonObject;
        if (parsed is null)
        {
            return new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        }

        return parsed.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
    }

    private bool IsPostgreSql() =>
        string.Equals(_dbContext.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal);

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private sealed record AllowedKnowledgeDocumentDescriptor(Guid DocumentId);
}

public static class KnowledgeEmbeddingSerializer
{
    public static string Serialize(IReadOnlyList<float> values)
    {
        var builder = new StringBuilder();
        builder.Append('[');
        for (var index = 0; index < values.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append(values[index].ToString("G9", System.Globalization.CultureInfo.InvariantCulture));
        }

        builder.Append(']');
        return builder.ToString();
    }

    public static IReadOnlyList<float> Deserialize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<float>();
        }

        var normalized = value.Trim();
        if (normalized.StartsWith("[", StringComparison.Ordinal))
        {
            normalized = normalized[1..];
        }

        if (normalized.EndsWith("]", StringComparison.Ordinal))
        {
            normalized = normalized[..^1];
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Array.Empty<float>();
        }

        return normalized
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => float.Parse(item, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
    }
}
