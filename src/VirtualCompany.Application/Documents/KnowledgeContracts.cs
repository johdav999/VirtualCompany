using System.Text.Json.Nodes;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Application.Documents;

public sealed record KnowledgeChunkDraft(
    int ChunkIndex,
    string Content,
    int StartOffset,
    int EndOffset,
    string SourceReference,
    IReadOnlyDictionary<string, JsonNode?> Metadata);

public sealed record EmbeddingVectorResult(IReadOnlyList<float> Values);

public sealed record EmbeddingBatchResult(
    string Provider,
    string Model,
    string? ModelVersion,
    int Dimensions,
    IReadOnlyList<EmbeddingVectorResult> Embeddings);

public sealed record CompanyKnowledgeAccessContext(
    Guid CompanyId,
    Guid? MembershipId = null,
    Guid? UserId = null,
    string? MembershipRole = null,
    IReadOnlyList<string>? DataScopes = null,
    Guid? AgentId = null);

public sealed record CompanyKnowledgeSemanticSearchQuery(
    Guid CompanyId,
    string QueryText,
    int TopN = 5,
    CompanyKnowledgeAccessContext? AccessContext = null,
    IReadOnlyList<Guid>? AllowedDocumentIds = null);

public sealed record CompanyKnowledgeSourceDocumentDto(
    Guid DocumentId,
    string Title,
    string DocumentType,
    string SourceType,
    string? SourceRef);

public sealed record CompanyKnowledgeSourceReferenceDto(
    Guid DocumentId,
    string DocumentTitle,
    string DocumentType,
    string SourceType,
    string? SourceRef,
    Guid ChunkId,
    int ChunkIndex,
    string ChunkSourceReference);

public sealed record CompanyKnowledgeSearchResultDto(
    Guid ChunkId,
    string Content,
    double Score,
    Guid DocumentId,
    string DocumentTitle,
    int ChunkIndex,
    string SourceReference,
    IReadOnlyDictionary<string, JsonNode?> SourceMetadata,
    CompanyKnowledgeSourceReferenceDto SourceReferenceInfo,
    CompanyKnowledgeSourceDocumentDto SourceDocument,
    string? ContentVersion = null,
    string? DocumentHandle = null,
    string EvidenceClassification = CompanyKnowledgeEvidenceClassifications.UntrustedEvidence);

public static class DocumentKnowledgeToolNames
{
    public const string Search = "knowledge.search";
    public const string List = "documents.list";
    public const string Read = "documents.read";
    public static IReadOnlySet<string> RepositoryGrantTools { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Search, List, Read };
    public static IReadOnlySet<string> RepositoryReadTools { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { List, Read };

    public static bool IsRepositoryGrantTool(string toolName) =>
        !string.IsNullOrWhiteSpace(toolName) && RepositoryGrantTools.Contains(toolName.Trim());

    public static bool IsRepositoryReadTool(string toolName) =>
        !string.IsNullOrWhiteSpace(toolName) && RepositoryReadTools.Contains(toolName.Trim());
}

public static class CompanyKnowledgeRetrievalStatuses
{
    public const string Available = "available";
    public const string NoMatch = "no_match";
    public const string NotFound = "not_found";
    public const string SourceUnavailable = "source_unavailable";
    public const string NotYetIndexed = "not_yet_indexed";
}

public static class CompanyKnowledgeEvidenceClassifications
{
    public const string UntrustedEvidence = "untrusted_evidence";
}

public sealed record CompanyKnowledgeSearchPageDto(
    string Status,
    IReadOnlyList<CompanyKnowledgeSearchResultDto> Results);

public sealed record CompanyKnowledgeRepositoryListQuery(
    Guid CompanyId,
    int PageSize = 20,
    string? Cursor = null,
    CompanyKnowledgeAccessContext? AccessContext = null);

public sealed record CompanyKnowledgeRepositoryDocumentDto(
    string DocumentHandle,
    string RepositoryHandle,
    string Title,
    string DocumentType,
    string SourceLink,
    string ContentVersion,
    int ActiveChunkCount,
    string Availability,
    DateTime UpdatedUtc,
    string EvidenceClassification = CompanyKnowledgeEvidenceClassifications.UntrustedEvidence);

public sealed record CompanyKnowledgeRepositoryListPageDto(
    string Status,
    IReadOnlyList<CompanyKnowledgeRepositoryDocumentDto> Items,
    string? NextCursor);

public sealed record CompanyKnowledgeRepositoryReadQuery(
    Guid CompanyId,
    string DocumentHandle,
    int MaxCharacters = 4000,
    string? Cursor = null,
    CompanyKnowledgeAccessContext? AccessContext = null);

public sealed record CompanyKnowledgeEvidenceCitationDto(
    string DocumentHandle,
    Guid ChunkId,
    int ChunkIndex,
    string ChunkReference,
    string SourceLink,
    string ContentVersion);

public sealed record CompanyKnowledgeRepositoryReadResultDto(
    string Status,
    string DocumentHandle,
    string? RepositoryHandle,
    string? Title,
    string? Content,
    string? SourceLink,
    string? ContentVersion,
    IReadOnlyList<CompanyKnowledgeEvidenceCitationDto> Citations,
    string? NextCursor,
    string EvidenceClassification = CompanyKnowledgeEvidenceClassifications.UntrustedEvidence,
    bool MayAuthorizeActions = false);

public interface ICompanyDocumentTextExtractor
{
    Task<string> ExtractAsync(CompanyKnowledgeDocument document, CancellationToken cancellationToken);
}

public interface IKnowledgeChunker
{
    IReadOnlyList<KnowledgeChunkDraft> ChunkDocument(CompanyKnowledgeDocument document, string extractedText);
}

public interface IEmbeddingGenerator
{
    Task<EmbeddingBatchResult> GenerateAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken);
}

public interface ICompanyKnowledgeIndexingProcessor
{
    Task<int> ProcessPendingAsync(CancellationToken cancellationToken);
    Task IndexDocumentAsync(Guid companyId, Guid documentId, CancellationToken cancellationToken);
}

public interface ICompanyKnowledgeSearchService
{
    Task<IReadOnlyList<CompanyKnowledgeSearchResultDto>> SearchAsync(
        CompanyKnowledgeSemanticSearchQuery query,
        CancellationToken cancellationToken);

    async Task<CompanyKnowledgeSearchPageDto> SearchDetailedAsync(
        CompanyKnowledgeSemanticSearchQuery query,
        CancellationToken cancellationToken)
    {
        var results = await SearchAsync(query, cancellationToken);
        return new CompanyKnowledgeSearchPageDto(
            results.Count == 0 ? CompanyKnowledgeRetrievalStatuses.NoMatch : CompanyKnowledgeRetrievalStatuses.Available,
            results);
    }

    Task<CompanyKnowledgeRepositoryListPageDto> ListRepositoryDocumentsAsync(
        CompanyKnowledgeRepositoryListQuery query,
        CancellationToken cancellationToken) =>
        Task.FromException<CompanyKnowledgeRepositoryListPageDto>(
            new NotSupportedException("Repository document listing is not implemented by this knowledge service."));

    Task<CompanyKnowledgeRepositoryReadResultDto> ReadRepositoryDocumentAsync(
        CompanyKnowledgeRepositoryReadQuery query,
        CancellationToken cancellationToken) =>
        Task.FromException<CompanyKnowledgeRepositoryReadResultDto>(
            new NotSupportedException("Repository document reading is not implemented by this knowledge service."));
}

public interface IKnowledgeAccessPolicyEvaluator
{
    bool CanAccess(CompanyKnowledgeAccessContext accessContext, CompanyKnowledgeDocument document);
}

public sealed class CompanyKnowledgeSearchValidationException : Exception
{
    public CompanyKnowledgeSearchValidationException(string message) : base(message) { }
}
