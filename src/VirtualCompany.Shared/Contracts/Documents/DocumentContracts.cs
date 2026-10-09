using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using VirtualCompany.Shared.Contracts.Documents;
namespace VirtualCompany.Application.Documents;


public sealed record CompanyKnowledgeDocumentDto(
    Guid Id,
    Guid CompanyId,
    string Title,
    string DocumentType,
    string SourceType,
    string OriginalFileName,
    string? ContentType,
    string FileExtension,
    long FileSizeBytes,
    string StorageKey,
    string? StorageUrl,
    IReadOnlyDictionary<string, JsonNode?> Metadata,
    CompanyKnowledgeDocumentAccessScopeDto AccessScope,
    string IngestionStatus,
    string? FailureCode,
    string? FailureMessage,
    string? FailureAction,
    bool CanRetry,
    string IndexingStatus,
    string? IndexingFailureCode,
    string? IndexingFailureMessage,
    int CurrentChunkSetVersion,
    int ActiveChunkCount,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    DateTime? IndexedUtc,
    DateTime? UploadedUtc,
    DateTime? ProcessingStartedUtc,
    DateTime? ProcessedUtc,
    DateTime? FailedUtc,
    string? SourceRef = null);
