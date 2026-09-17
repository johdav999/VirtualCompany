using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class SalesPresentationPresetProcessorTests
{
    [Fact]
    public async Task Processing_creates_only_reusable_company_scoped_slide_content_and_stable_storage_path()
    {
        var company = Guid.NewGuid(); var presetId = Guid.NewGuid(); var versionId = Guid.NewGuid(); var assetId = Guid.NewGuid();
        await using var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseInMemoryDatabase($"preset-processor-{Guid.NewGuid():N}").Options, new EmptyContext());
        db.Add(new Company(company, "Processor Company"));
        db.Add(new SalesPresentationPreset(presetId, company, "Reusable deck", null, Guid.NewGuid(), DateTime.UtcNow));
        db.Add(new SalesPresentationPresetVersion(versionId, company, presetId, 1, null, null, "Explain value", "Executives", 30,
            null, "manual", "en", true, false, false, null, DateTime.UtcNow));
        db.Add(new SalesPresentationPresetAsset(assetId, company, versionId, "deck.pptx", "application/octet-stream", 100,
            new string('a', 64), "source.pptx", null, Guid.NewGuid(), DateTime.UtcNow)); await db.SaveChangesAsync();
        var storage = new Storage();
        var processor = new SalesPresentationPresetAssetProcessor(db, storage, new CleanScanner(), new Extractor(), new Renderer(),
            Options.Create(new SalesPresentationOptions()), TimeProvider.System, NullLogger<SalesPresentationPresetAssetProcessor>.Instance);

        await processor.ProcessAsync(company, assetId, default);

        var asset = await db.SalesPresentationPresetAssets.IgnoreQueryFilters().SingleAsync();
        var slide = await db.SalesPresentationPresetSlides.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("processed", asset.Status.ToStorageValue());
        Assert.Contains($"companies/{company:N}/sales/presentation-presets/{presetId:N}/versions/0001/assets/{assetId:N}/v1/slides/0001", storage.LastKey);
        Assert.DoesNotContain("customer", slide.Objective, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lead", slide.BaselineTalkingPoints, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Reusable product value", slide.ExtractedText);
    }

    private sealed class Extractor : ISalesPresentationDeckExtractor
    {
        public Task<ExtractedPresentationDeck> ExtractAsync(Stream content, int maximumSlides, CancellationToken ct) =>
            Task.FromResult(new ExtractedPresentationDeck(12_192_000, 6_858_000,
                [new(1, "Value", "Reusable product value", "Explain the reusable evidence", new string('b', 64))]));
    }
    private sealed class Renderer : ISalesPresentationSlideRenderer
    {
        public Task<SalesPresentationRenderedSlide> RenderAsync(SalesPresentationRenderRequest request, CancellationToken ct) =>
            Task.FromResult(new SalesPresentationRenderedSlide([1, 2, 3], "image/png", ".png", 1600, 900, "test", "1", "flattened"));
    }
    private sealed class CleanScanner : ICompanyDocumentVirusScanner
    {
        public Task<CompanyDocumentVirusScanResult> ScanAsync(CompanyDocumentVirusScanRequest request, CancellationToken ct) =>
            Task.FromResult(CompanyDocumentVirusScanResult.CleanPlaceholder("test"));
    }
    private sealed class Storage : ICompanyDocumentStorage
    {
        public string LastKey { get; private set; } = string.Empty;
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream([1]));
        public Task<DocumentStorageWriteResult> WriteAsync(DocumentStorageWriteRequest request, CancellationToken ct) { LastKey = request.StorageKey; return Task.FromResult(new DocumentStorageWriteResult(request.StorageKey, null)); }
        public Task DeleteAsync(string key, CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class EmptyContext : ICompanyContextAccessor
    {
        public Guid? CompanyId => null; public Guid? UserId => null; public bool IsResolved => false; public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? companyId) { } public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext) { }
    }
}
