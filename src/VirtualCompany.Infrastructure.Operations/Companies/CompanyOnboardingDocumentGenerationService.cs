using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Documents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyOnboardingDocumentGenerationService(
    VirtualCompanyDbContext db,
    ICompanyDocumentStorage storage,
    IDocumentIngestionOrchestrator ingestion) : ICompanyOnboardingDocumentGenerationService
{
    public async Task ProcessAsync(OnboardingDocumentGenerationRequestedMessage request,CancellationToken ct)
    {
        if(request.CompanyId==Guid.Empty||request.SessionId==Guid.Empty||string.IsNullOrWhiteSpace(request.DocumentKey)||string.IsNullOrWhiteSpace(request.Markdown))
            throw new InvalidOperationException("The onboarding document request is incomplete.");
        var existing=(await db.CompanyKnowledgeDocuments.IgnoreQueryFilters().AsNoTracking().Where(x=>x.CompanyId==request.CompanyId).ToListAsync(ct))
            .SingleOrDefault(x=>MetadataEquals(x.Metadata,"guidedWorkSessionId",request.SessionId.ToString("D"))&&MetadataEquals(x.Metadata,"onboardingDocumentKey",request.DocumentKey)&&MetadataEquals(x.Metadata,"contentHash",request.ContentHash));
        if(existing is not null)
        {
            if(existing.IngestionStatus is CompanyKnowledgeDocumentIngestionStatus.Uploaded or CompanyKnowledgeDocumentIngestionStatus.PendingScan or CompanyKnowledgeDocumentIngestionStatus.ScanClean)
                await ingestion.ProcessUploadedAsync(request.CompanyId,existing.Id,ct);
            return;
        }

        var bytes=Encoding.UTF8.GetBytes(request.Markdown);
        var documentId=DeterministicGuid($"{request.CompanyId:N}:{request.SessionId:N}:{request.DocumentKey}:{request.ContentHash}");
        var storageKey=$"companies/{request.CompanyId:N}/knowledge/{documentId:N}/{request.FileName}";
        await using var stream=new MemoryStream(bytes,writable:false);
        var stored=await storage.WriteAsync(new DocumentStorageWriteRequest(request.CompanyId,documentId,storageKey,request.FileName,"text/markdown",stream),ct);
        var metadata=new Dictionary<string,JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["purpose"]="onboarding_foundation",["onboardingDocumentKey"]=request.DocumentKey,["guidedWorkSessionId"]=request.SessionId.ToString("D"),
            ["source"]="guided_onboarding",["contentHash"]=request.ContentHash,["schemaVersion"]=request.SchemaVersion,["generatedAtUtc"]=DateTime.UtcNow.ToString("O")
        };
        var scope=new CompanyKnowledgeDocumentAccessScope(request.CompanyId,CompanyKnowledgeDocumentAccessScope.CompanyVisibility,
            new Dictionary<string,JsonNode?>{{"data_scopes",new JsonArray("knowledge","operations","sales","marketing","support")}});
        var document=new CompanyKnowledgeDocument(documentId,request.CompanyId,request.Title,CompanyKnowledgeDocumentType.Reference,stored.StorageKey,stored.StorageUrl,
            request.FileName,"text/markdown",".md",bytes.LongLength,metadata,scope,$"guided-onboarding:{request.SessionId:N}:{request.DocumentKey}");
        db.CompanyKnowledgeDocuments.Add(document);
        await db.SaveChangesAsync(ct);
        await ingestion.ProcessUploadedAsync(request.CompanyId,document.Id,ct);
    }

    private static bool MetadataEquals(IReadOnlyDictionary<string,JsonNode?> metadata,string key,string expected)=>metadata.TryGetValue(key,out var node)&&node is JsonValue value&&value.TryGetValue<string>(out var text)&&string.Equals(text,expected,StringComparison.OrdinalIgnoreCase);
    private static Guid DeterministicGuid(string value){var hash=System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value));return new Guid(hash.AsSpan(0,16));}
}
