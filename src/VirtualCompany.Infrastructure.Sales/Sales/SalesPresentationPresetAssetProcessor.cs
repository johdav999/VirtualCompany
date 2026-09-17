using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesPresentationPresetAssetProcessor(VirtualCompanyDbContext db, ICompanyDocumentStorage storage,
    ICompanyDocumentVirusScanner virusScanner, ISalesPresentationDeckExtractor extractor, ISalesPresentationSlideRenderer renderer,
    IOptions<SalesPresentationOptions> options, TimeProvider timeProvider, ILogger<SalesPresentationPresetAssetProcessor> logger)
    : ISalesPresentationPresetAssetProcessor
{
    public async Task<int> ProcessPendingAsync(CancellationToken ct)
    {
        var stale = UtcNow().AddSeconds(-options.Value.ClaimTimeoutSeconds);
        var candidates = await db.SalesPresentationPresetAssets.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Status == SalesPresentationPresetAssetStatus.PendingScan ||
                        x.Status == SalesPresentationPresetAssetStatus.Processing && x.ProcessingStartedUtc <= stale)
            .OrderBy(x => x.UpdatedUtc).ThenBy(x => x.Id).Take(options.Value.BatchSize)
            .Select(x => new { x.CompanyId, x.Id }).ToListAsync(ct);
        foreach (var candidate in candidates) await ProcessAsync(candidate.CompanyId, candidate.Id, ct);
        return candidates.Count;
    }

    public async Task ProcessAsync(Guid companyId, Guid assetId, CancellationToken ct)
    {
        var asset = await db.SalesPresentationPresetAssets.IgnoreQueryFilters().Include(x => x.PresetVersion)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == assetId, ct);
        if (asset is null || asset.PresetVersion.Lifecycle != SalesPresentationPresetVersionLifecycle.Draft) return;
        try { asset.BeginProcessing(UtcNow(), TimeSpan.FromSeconds(options.Value.ClaimTimeoutSeconds)); await db.SaveChangesAsync(ct); }
        catch (InvalidOperationException) { return; }
        catch (DbUpdateConcurrencyException) { return; }

        try
        {
            await using var source = await storage.OpenReadAsync(asset.StorageKey, ct); await using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, ct); var bytes = buffer.ToArray();
            var scan = await virusScanner.ScanAsync(new CompanyDocumentVirusScanRequest(asset.CompanyId, asset.Id,
                asset.StorageKey, asset.StorageUrl, asset.OriginalFileName, asset.ContentType, asset.FileSizeBytes,
                new Dictionary<string, JsonNode?> { ["purpose"] = JsonValue.Create("sales_presentation_preset"), ["presetVersionId"] = JsonValue.Create(asset.PresetVersionId) }), ct);
            if (scan.Outcome == CompanyDocumentVirusScanOutcome.Blocked) throw new SalesPresentationProcessingException(scan.FailureCode ?? "malware_blocked", scan.Message ?? "The presentation was blocked by the malware scan.", false, true);
            if (scan.Outcome == CompanyDocumentVirusScanOutcome.Error) throw new SalesPresentationProcessingException(scan.FailureCode ?? "virus_scan_unavailable", "The presentation could not be cleared by malware scanning. Try again later.", true);
            await using var extraction = new MemoryStream(bytes, false); var deck = await extractor.ExtractAsync(extraction, options.Value.MaximumSlides, ct);
            if (deck.Slides.Count == 0) throw new SalesPresentationProcessingException("pptx_has_no_slides", "The PowerPoint file contains no slides.", false, true);
            var old = await db.SalesPresentationPresetSlides.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.AssetId == assetId && x.ProcessingVersion == asset.ProcessingVersion).ToListAsync(ct); db.RemoveRange(old);
            SalesPresentationRenderedSlide? rendering = null; var now = UtcNow(); var perSlide = Math.Max(15, asset.PresetVersion.DurationMinutes * 60 / deck.Slides.Count);
            foreach (var slide in deck.Slides)
            {
                rendering = await renderer.RenderAsync(new SalesPresentationRenderRequest(companyId, asset.Id, asset.ProcessingVersion,
                    slide, deck.Slides.Count, bytes, options.Value.RenderWidthPixels, options.Value.RenderHeightPixels), ct);
                var key = $"companies/{companyId:N}/sales/presentation-presets/{asset.PresetVersion.PresetId:N}/versions/{asset.PresetVersion.VersionNumber:D4}/assets/{asset.Id:N}/v{asset.ProcessingVersion}/slides/{slide.SlideNumber:D4}{rendering.FileExtension}";
                await using var image = new MemoryStream(rendering.Content, false); var stored = await storage.WriteAsync(new(companyId, asset.Id, key, Path.GetFileName(key), rendering.ContentType, image), ct);
                var next = deck.Slides.FirstOrDefault(x => x.SlideNumber == slide.SlideNumber + 1)?.Title;
                var title = string.IsNullOrWhiteSpace(slide.Title) ? $"slide {slide.SlideNumber}" : slide.Title.Trim();
                var objective = $"Explain {title} clearly for the preset's intended audience.";
                var talkingPoints = BuildTalkingPoints(slide, title);
                var transition = string.IsNullOrWhiteSpace(next) ? "Invite questions and summarize the key point." : $"Connect this point to {next.Trim()}.";
                db.Add(new SalesPresentationPresetSlide(Guid.NewGuid(), companyId, asset.Id, asset.ProcessingVersion,
                    slide.SlideNumber, slide.Title, slide.ExtractedText, slide.SpeakerNotes, stored.StorageKey, stored.StorageUrl,
                    rendering.WidthPixels, rendering.HeightPixels, deck.SourceWidthEmus, deck.SourceHeightEmus, slide.ContentHash,
                    objective, talkingPoints, perSlide, transition, now));
            }
            asset.MarkProcessed(deck.Slides.Count, rendering!.RendererName, rendering.RendererVersion, rendering.AnimationHandling, UtcNow());
            AddAudit(asset, "sales.presentation_preset.asset_processed", AuditEventOutcomes.Succeeded, "The reusable presentation source was scanned, extracted, and rendered without customer context.");
            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (SalesPresentationProcessingException exception) { await FailAsync(asset, exception, ct); }
        catch (Exception exception)
        {
            logger.LogError(exception, "Preset asset processing failed for {AssetId} in company {CompanyId}.", asset.Id, companyId);
            await FailAsync(asset, new SalesPresentationProcessingException(SalesPresentationProblemCodes.ProcessingFailed,
                "The presentation could not be processed. Retry when the dependency is available.", true, false, exception), ct);
        }
    }

    private async Task FailAsync(SalesPresentationPresetAsset asset, SalesPresentationProcessingException exception, CancellationToken ct)
    {
        foreach (var entry in db.ChangeTracker.Entries<SalesPresentationPresetSlide>().Where(x => x.Entity.AssetId == asset.Id))
        {
            if (entry.State == EntityState.Added) entry.State = EntityState.Detached;
            else if (entry.State == EntityState.Deleted) entry.State = EntityState.Unchanged;
        }
        asset.MarkFailed(exception.Code, exception.SafeMessage, exception.CanRetry && asset.ProcessingAttemptCount < options.Value.MaximumAttempts, exception.Blocked, UtcNow());
        AddAudit(asset, "sales.presentation_preset.asset_processing_failed", AuditEventOutcomes.Failed, "Reusable presentation processing failed safely.");
        await db.SaveChangesAsync(ct);
    }
    private static string BuildTalkingPoints(ExtractedPresentationSlide slide, string title)
    {
        var source = string.IsNullOrWhiteSpace(slide.SpeakerNotes) ? slide.ExtractedText : slide.SpeakerNotes;
        var text = string.IsNullOrWhiteSpace(source) ? $"Introduce {title}; explain the visual; confirm audience understanding." : source.Trim();
        return text.Length <= 8000 ? text : text[..8000];
    }
    private void AddAudit(SalesPresentationPresetAsset asset, string action, string outcome, string rationale) => db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), asset.CompanyId, AuditActorTypes.System, null, action, "sales_presentation_preset_asset", asset.Id.ToString("D"), outcome, rationale, ["presentation preset asset"], new Dictionary<string, string?> { ["presetVersionId"] = asset.PresetVersionId.ToString("D"), ["processingVersion"] = asset.ProcessingVersion.ToString(), ["attempt"] = asset.ProcessingAttemptCount.ToString(), ["status"] = asset.Status.ToStorageValue() }, null, UtcNow()));
    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
