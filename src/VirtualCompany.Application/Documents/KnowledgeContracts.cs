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
    CompanyKnowledgeAccessContext? AccessContext = null);

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
    CompanyKnowledgeSourceDocumentDto SourceDocument);

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
}

public interface IKnowledgeAccessPolicyEvaluator
{
    bool CanAccess(CompanyKnowledgeAccessContext accessContext, CompanyKnowledgeDocument document);
}

public sealed class CompanyKnowledgeSearchValidationException : Exception
{
    public CompanyKnowledgeSearchValidationException(string message) : base(message) { }
}