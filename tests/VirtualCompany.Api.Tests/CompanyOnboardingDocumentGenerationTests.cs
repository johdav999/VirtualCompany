using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Documents;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;

public sealed class CompanyOnboardingDocumentGenerationTests
{
    [Fact]
    public async Task Replayed_generation_reuses_the_same_document_and_storage_key()
    {
        var options=new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db=new VirtualCompanyDbContext(options);
        var storage=new RecordingStorage();var ingestion=new RecordingIngestion();
        var service=new CompanyOnboardingDocumentGenerationService(db,storage,ingestion);
        var request=new OnboardingDocumentGenerationRequestedMessage(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"company-overview","Company overview","company-overview.md","# Example\n\nReviewed.","hash-1","1.0",null);

        await service.ProcessAsync(request,CancellationToken.None);
        await service.ProcessAsync(request,CancellationToken.None);

        Assert.Single(await db.CompanyKnowledgeDocuments.IgnoreQueryFilters().ToListAsync());
        Assert.Single(storage.Writes);
        Assert.Equal(2,ingestion.DocumentIds.Count);
        var document=await db.CompanyKnowledgeDocuments.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("onboarding_foundation",document.Metadata["purpose"]!.GetValue<string>());
        Assert.Equal(request.SessionId.ToString("D"),document.Metadata["guidedWorkSessionId"]!.GetValue<string>());
    }

    private sealed class RecordingStorage:ICompanyDocumentStorage
    {
        public List<string> Writes{get;}=[];
        public Task DeleteAsync(string storageKey,CancellationToken cancellationToken)=>Task.CompletedTask;
        public Task<Stream> OpenReadAsync(string storageKey,CancellationToken cancellationToken)=>throw new NotSupportedException();
        public Task<DocumentStorageWriteResult> WriteAsync(DocumentStorageWriteRequest request,CancellationToken cancellationToken){Writes.Add(request.StorageKey);return Task.FromResult(new DocumentStorageWriteResult(request.StorageKey,null));}
    }
    private sealed class RecordingIngestion:IDocumentIngestionOrchestrator
    {
        public List<Guid> DocumentIds{get;}=[];
        public Task ProcessUploadedAsync(Guid companyId,Guid documentId,CancellationToken cancellationToken){DocumentIds.Add(documentId);return Task.CompletedTask;}
    }
}
