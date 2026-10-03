using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Support;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Support;

// Persisted citations retain their identity. Current access is rechecked for reads and execution.
public sealed class SupportReplySourceAccess(VirtualCompanyDbContext db, IKnowledgeAccessPolicyEvaluator access) : ISupportReplySourceAccess
{
    public async Task<string?> FilterAsync(Guid companyId, string? sourceReferencesJson, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourceReferencesJson)) return null;
        JsonArray? sources;
        try { sources = JsonNode.Parse(sourceReferencesJson) as JsonArray; }
        catch (JsonException) { return null; }
        if (sources is null) return null;
        var result = new JsonArray();
        var context = new CompanyKnowledgeAccessContext(companyId, DataScopes: ["support", "knowledge"]);
        foreach (var source in sources.OfType<JsonObject>())
        {
            if (source["type"]?.ToString() != "knowledge_chunk") { result.Add(source.DeepClone()); continue; }
            if (!Guid.TryParse(source["documentId"]?.ToString(), out var documentId) ||
                !Guid.TryParse(source["entityId"]?.ToString(), out var chunkId)) continue;
            var chunk = await db.CompanyKnowledgeChunks.AsNoTracking().Include(x => x.Document)
                .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.DocumentId == documentId && x.Id == chunkId && x.IsActive, ct);
            if (chunk is null || chunk.Document.IngestionStatus != CompanyKnowledgeDocumentIngestionStatus.Processed ||
                chunk.Document.IndexingStatus != CompanyKnowledgeDocumentIndexingStatus.Indexed || !access.CanAccess(context, chunk.Document)) continue;
            result.Add(source.DeepClone());
        }
        return result.ToJsonString();
    }
}
