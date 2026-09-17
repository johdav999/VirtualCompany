using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed partial class SalesPresentationPresetService(VirtualCompanyDbContext db, ICompanyDocumentStorage storage,
    IOptions<SalesPresentationOptions> options, TimeProvider timeProvider) : ISalesPresentationPresetService
{
    public async Task<IReadOnlyList<SalesPresentationPresetListItemDto>> ListAsync(Guid companyId, Guid actorUserId,
        string? search, bool includeArchived, CancellationToken ct)
    {
        await EnsureMemberAsync(companyId, actorUserId, ct);
        var query = db.SalesPresentationPresets.AsNoTracking().Where(x => x.CompanyId == companyId);
        if (!includeArchived) query = query.Where(x => x.Lifecycle != SalesPresentationPresetLifecycle.Archived);
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim(); query = query.Where(x => x.Name.Contains(term) || x.Description != null && x.Description.Contains(term)); }
        var presets = await query.OrderBy(x => x.Name).ToListAsync(ct);
        var ids = presets.Select(x => x.Id).ToArray();
        var usage = await db.SalesPresentationRuns.AsNoTracking().Where(x => x.CompanyId == companyId && ids.Contains(x.PresetVersion.PresetId))
            .GroupBy(x => x.PresetVersion.PresetId).Select(x => new { PresetId = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.PresetId, x => x.Count, ct);
        var versions = await db.SalesPresentationPresetVersions.AsNoTracking().Where(x => x.CompanyId == companyId && ids.Contains(x.PresetId))
            .Select(x => new { x.PresetId, x.VersionNumber, x.Lifecycle, Asset = x.Asset == null ? null : new { x.Asset.Status, x.Asset.SlideCount, CoverSlideId = x.Asset.Status == SalesPresentationPresetAssetStatus.Processed ? x.Asset.Slides.Where(s => s.CompanyId == companyId && s.SlideNumber == 1 && s.ProcessingVersion == x.Asset.ProcessingVersion).Select(s => (Guid?)s.Id).FirstOrDefault() : null } }).ToListAsync(ct);
        return presets.Select(p =>
        {
            var related = versions.Where(x => x.PresetId == p.Id).ToArray();
            var draft = related.Where(x => x.Lifecycle == SalesPresentationPresetVersionLifecycle.Draft).OrderByDescending(x => x.VersionNumber).FirstOrDefault();
            var published = related.Where(x => x.Lifecycle == SalesPresentationPresetVersionLifecycle.Published).OrderByDescending(x => x.VersionNumber).FirstOrDefault();
            return new SalesPresentationPresetListItemDto(p.Id, p.Name, p.Description, p.OwnerUserId, p.Lifecycle.ToStorageValue(),
                published?.VersionNumber, draft?.VersionNumber, draft?.Asset?.Status.ToStorageValue() ?? published?.Asset?.Status.ToStorageValue(),
                draft?.Asset?.SlideCount ?? published?.Asset?.SlideCount ?? 0, usage.GetValueOrDefault(p.Id), p.UpdatedUtc, p.ConcurrencyVersion, (draft?.Asset ?? published?.Asset)?.CoverSlideId);
        }).ToArray();
    }

    public async Task<SalesPresentationPresetDto?> GetAsync(Guid companyId, Guid actorUserId, Guid presetId, CancellationToken ct)
    { await EnsureMemberAsync(companyId, actorUserId, ct); return await MapPresetAsync(companyId, presetId, ct); }

    public async Task<SalesPresentationPresetDto> CreateAsync(Guid companyId, Guid actorUserId,
        CreateSalesPresentationPresetCommand command, string? correlationId, CancellationToken ct)
    {
        await EnsureMemberAsync(companyId, actorUserId, ct); await EnsureOwnerAsync(companyId, command.OwnerUserId, ct);
        await EnsurePresenterAsync(companyId, command.DefaultPresenterAgentId, ct); ValidateBehavior(command.BehaviorSettingsJson);
        var contexts = Contexts(command.AllowedContextTypes); var now = UtcNow(); var preset = new SalesPresentationPreset(Guid.NewGuid(), companyId, command.Name, command.Description, command.OwnerUserId, now);
        var version = new SalesPresentationPresetVersion(Guid.NewGuid(), companyId, preset.Id, 1, command.DefaultPresenterAgentId,
            command.BehaviorSettingsJson, command.Goal, command.Audience, command.DurationMinutes, command.DemoScenario,
            command.ControlMode, command.Language, contexts.meeting, contexts.campaign, contexts.adHoc, command.RequiredKnowledgeScope, now);
        db.AddRange(preset, version); AddAudit(companyId, actorUserId, "sales.presentation_preset.created", preset.Id, version.Id, null,
            "A reusable presentation preset and its first draft were created.", correlationId);
        await db.SaveChangesAsync(ct); return (await MapPresetAsync(companyId, preset.Id, ct))!;
    }

    public async Task<SalesPresentationPresetDto?> UpdateDraftAsync(Guid companyId, Guid actorUserId, Guid presetId,
        UpdateSalesPresentationPresetDraftCommand command, string? correlationId, CancellationToken ct)
    {
        await EnsureMemberAsync(companyId, actorUserId, ct); await EnsureOwnerAsync(companyId, command.OwnerUserId, ct);
        await EnsurePresenterAsync(companyId, command.DefaultPresenterAgentId, ct); ValidateBehavior(command.BehaviorSettingsJson);
        var preset = await db.SalesPresentationPresets.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == presetId, ct); if (preset is null) return null;
        Expected(preset.ConcurrencyVersion, command.ExpectedPresetVersion);
        var draft = await db.SalesPresentationPresetVersions.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.PresetId == presetId && x.Lifecycle == SalesPresentationPresetVersionLifecycle.Draft, ct);
        if (draft is null)
        {
            var published = await CurrentPublishedAsync(companyId, preset, ct) ?? throw Conflict(SalesPresentationPresetProblemCodes.InvalidRequest, "There is no draft or published version to edit.");
            draft = CloneDraft(published, await NextVersionNumberAsync(companyId, presetId, ct), UtcNow()); db.Add(draft);
        }
        else Expected(draft.ConcurrencyVersion, command.ExpectedDraftVersion);
        var contexts = Contexts(command.AllowedContextTypes); var now = UtcNow(); preset.Update(command.Name, command.Description, command.OwnerUserId, now);
        draft.Update(command.DefaultPresenterAgentId, command.BehaviorSettingsJson, command.Goal, command.Audience,
            command.DurationMinutes, command.DemoScenario, command.ControlMode, command.Language, contexts.meeting, contexts.campaign,
            contexts.adHoc, command.RequiredKnowledgeScope, now);
        AddAudit(companyId, actorUserId, "sales.presentation_preset.draft_updated", preset.Id, draft.Id, null, "Presentation preset draft defaults were updated.", correlationId);
        await SaveConflictAsync(ct); return await MapPresetAsync(companyId, presetId, ct);
    }

    public async Task<SalesPresentationPresetAssetDto?> ImportAssetAsync(Guid companyId, Guid actorUserId, Guid presetId,
        Guid versionId, ImportSalesPresentationPresetAssetCommand command, string? correlationId, CancellationToken ct)
    {
        await EnsureMemberAsync(companyId, actorUserId, ct); var version = await DraftAsync(companyId, presetId, versionId, ct); if (version is null) return null;
        var fileName = Path.GetFileName(command.OriginalFileName?.Trim()); if (!string.Equals(Path.GetExtension(fileName), ".pptx", StringComparison.OrdinalIgnoreCase)) throw Validation("file", "Upload a PowerPoint .pptx file.");
        if (command.Length is < 1 || command.Length > options.Value.MaximumUploadBytes) throw Validation("file", $"The PowerPoint file must be between 1 byte and {options.Value.MaximumUploadBytes} bytes.");
        if (!SupportedContentType(command.ContentType)) throw Validation("file", "The uploaded content type is not supported.");
        await using var buffered = await ReadBoundedAsync(command.Content, options.Value.MaximumUploadBytes, ct); ValidatePptx(buffered);
        var hash = Convert.ToHexString(SHA256.HashData(buffered.ToArray())).ToLowerInvariant(); var assetId = Guid.NewGuid();
        var key = $"companies/{companyId:N}/sales/presentation-presets/{presetId:N}/versions/{version.VersionNumber:D4}/assets/{assetId:N}/original.pptx";
        buffered.Position = 0; var stored = await storage.WriteAsync(new(companyId, assetId, key, fileName, NormalizeContentType(command.ContentType), buffered), ct);
        var existing = await db.SalesPresentationPresetAssets.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.PresetVersionId == versionId, ct);
        string? oldKey = null;
        if (existing is not null) { oldKey = existing.StorageKey; var oldSlides = await db.SalesPresentationPresetSlides.Where(x => x.CompanyId == companyId && x.AssetId == existing.Id).ToListAsync(ct); db.RemoveRange(oldSlides); db.Remove(existing); }
        var asset = new SalesPresentationPresetAsset(assetId, companyId, versionId, fileName, NormalizeContentType(command.ContentType), buffered.Length, hash, stored.StorageKey, stored.StorageUrl, actorUserId, UtcNow()); db.Add(asset);
        AddAudit(companyId, actorUserId, "sales.presentation_preset.asset_imported", presetId, versionId, asset.Id, "A PowerPoint source was stored and queued for reusable processing.", correlationId);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { await storage.DeleteAsync(stored.StorageKey, CancellationToken.None); throw Conflict(SalesPresentationPresetProblemCodes.Conflict, "The draft asset changed concurrently. Refresh and try again."); }
        catch { await storage.DeleteAsync(stored.StorageKey, CancellationToken.None); throw; }
        if (oldKey is not null && !string.Equals(oldKey, stored.StorageKey, StringComparison.Ordinal) && await CanDeleteStoredObjectAsync(companyId, oldKey, ct))
            await storage.DeleteAsync(oldKey, CancellationToken.None);
        return MapAsset(asset);
    }

    public async Task<SalesPresentationPresetAssetDto?> RetryAssetAsync(Guid companyId, Guid actorUserId, Guid presetId,
        Guid versionId, string? correlationId, CancellationToken ct)
    {
        await EnsureMemberAsync(companyId, actorUserId, ct); var version = await DraftAsync(companyId, presetId, versionId, ct); if (version is null) return null;
        var asset = await db.SalesPresentationPresetAssets.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.PresetVersionId == versionId, ct); if (asset is null) return null;
        try { asset.QueueRetry(UtcNow()); } catch (InvalidOperationException e) { throw Conflict(SalesPresentationPresetProblemCodes.Conflict, e.Message); }
        AddAudit(companyId, actorUserId, "sales.presentation_preset.asset_retry_queued", presetId, versionId, asset.Id, "Reusable presentation processing was queued for retry.", correlationId); await SaveConflictAsync(ct); return MapAsset(asset);
    }

    public async Task<SalesPresentationPresetReadinessDto?> EvaluateReadinessAsync(Guid companyId, Guid actorUserId,
        Guid presetId, Guid versionId, CancellationToken ct)
    { await EnsureMemberAsync(companyId, actorUserId, ct); var version = await db.SalesPresentationPresetVersions.AsNoTracking().Include(x => x.Asset).SingleOrDefaultAsync(x => x.CompanyId == companyId && x.PresetId == presetId && x.Id == versionId, ct); return version is null ? null : await ReadinessAsync(companyId, version, ct); }

    public async Task<SalesPresentationPresetDto?> PublishAsync(Guid companyId, Guid actorUserId, Guid presetId, Guid versionId,
        long expectedPresetVersion, long expectedVersion, string? correlationId, CancellationToken ct)
    {
        await EnsureMemberAsync(companyId, actorUserId, ct); var preset = await db.SalesPresentationPresets.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == presetId, ct); if (preset is null) return null; Expected(preset.ConcurrencyVersion, expectedPresetVersion);
        var version = await db.SalesPresentationPresetVersions.Include(x => x.Asset).SingleOrDefaultAsync(x => x.CompanyId == companyId && x.PresetId == presetId && x.Id == versionId, ct); if (version is null) return null; Expected(version.ConcurrencyVersion, expectedVersion);
        var readiness = await ReadinessAsync(companyId, version, ct); if (!readiness.IsReady) throw Conflict(SalesPresentationPresetProblemCodes.NotReady, "Resolve all publication readiness blockers before publishing.");
        version.Publish(actorUserId, UtcNow()); preset.Publish(version.Id, UtcNow()); AddAudit(companyId, actorUserId, "sales.presentation_preset.published", presetId, versionId, version.Asset?.Id, "An immutable presentation preset version was published.", correlationId); await SaveConflictAsync(ct); return await MapPresetAsync(companyId, presetId, ct);
    }

    public async Task<SalesPresentationPresetDto?> CreateNextDraftAsync(Guid companyId, Guid actorUserId, Guid presetId,
        long expectedPresetVersion, string? correlationId, CancellationToken ct)
    {
        await EnsureMemberAsync(companyId, actorUserId, ct); var preset = await db.SalesPresentationPresets.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == presetId, ct); if (preset is null) return null; Expected(preset.ConcurrencyVersion, expectedPresetVersion);
        if (preset.Lifecycle == SalesPresentationPresetLifecycle.Archived) throw Conflict(SalesPresentationPresetProblemCodes.Archived, "Archived presets cannot create drafts.");
        if (await db.SalesPresentationPresetVersions.AnyAsync(x => x.CompanyId == companyId && x.PresetId == presetId && x.Lifecycle == SalesPresentationPresetVersionLifecycle.Draft, ct)) throw Conflict(SalesPresentationPresetProblemCodes.Conflict, "This preset already has an editable draft.");
        var published = await CurrentPublishedAsync(companyId, preset, ct); if (published is null) throw Conflict(SalesPresentationPresetProblemCodes.InvalidRequest, "Publish a version before creating the next draft.");
        var draft = CloneDraft(published, await NextVersionNumberAsync(companyId, presetId, ct), UtcNow()); db.Add(draft); await CopyDraftAssetAsync(companyId,actorUserId,published.Id,draft.Id,ct); AddAudit(companyId, actorUserId, "sales.presentation_preset.version_created", presetId, draft.Id, null, "A new editable draft was created from the published defaults.", correlationId); await SaveConflictAsync(ct); return await MapPresetAsync(companyId, presetId, ct);
    }

    public async Task<SalesPresentationPresetDto?> ArchiveAsync(Guid companyId, Guid actorUserId, Guid presetId,
        long expectedPresetVersion, string? rationale, string? correlationId, CancellationToken ct)
    {
        await EnsureMemberAsync(companyId, actorUserId, ct); var preset = await db.SalesPresentationPresets.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == presetId, ct); if (preset is null) return null; Expected(preset.ConcurrencyVersion, expectedPresetVersion); preset.Archive(UtcNow()); AddAudit(companyId, actorUserId, "sales.presentation_preset.archived", presetId, null, null, string.IsNullOrWhiteSpace(rationale) ? "The presentation preset was archived and cannot be used for new applications." : rationale.Trim(), correlationId); await SaveConflictAsync(ct); return await MapPresetAsync(companyId, presetId, ct);
    }

    public async Task<IReadOnlyList<SalesPresentationPresetSlideDto>?> GetSlidesAsync(Guid companyId, Guid actorUserId,
        Guid presetId, Guid versionId, CancellationToken ct)
    {
        await EnsureMemberAsync(companyId, actorUserId, ct); var asset = await db.SalesPresentationPresetAssets.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.PresetVersionId == versionId && x.PresetVersion.PresetId == presetId && x.Status == SalesPresentationPresetAssetStatus.Processed, ct); if (asset is null) return null;
        var slides = await db.SalesPresentationPresetSlides.AsNoTracking().Where(x => x.CompanyId == companyId && x.AssetId == asset.Id && x.ProcessingVersion == asset.ProcessingVersion).OrderBy(x => x.SlideNumber).Select(x => new SalesPresentationPresetSlideDto(x.Id, x.SlideNumber, x.Title, x.ExtractedText, x.SpeakerNotes, x.ImageStorageKey, x.ImageStorageUrl, x.ImageWidthPixels, x.ImageHeightPixels, x.SourceWidthEmus, x.SourceHeightEmus, x.ContentHash, x.Objective, x.BaselineTalkingPoints, x.ExpectedDurationSeconds, x.TransitionText, false)).ToListAsync(ct);
        var revision = await db.SalesNarrationRevisions.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.PresetVersionId == versionId)
            .OrderByDescending(x => x.CreatedUtc).FirstOrDefaultAsync(ct);
        if (revision is null) return slides;
        var parts = await (from segment in db.SalesNarrationSegments.AsNoTracking()
            join audio in db.SalesNarrationAssets.AsNoTracking() on new { segment.CompanyId, Id = segment.AssetId } equals new { audio.CompanyId, audio.Id }
            where segment.CompanyId == companyId && segment.RevisionId == revision.Id
            select new { segment.PresetSlideId, segment.SourceNotesHash, audio.Status }).ToListAsync(ct);
        return slides.Select(slide => slide with { AudioNeedsUpdate = parts.Where(x => x.PresetSlideId == slide.Id).Any(x =>
            (x.SourceNotesHash is not null && x.SourceNotesHash != SalesNarrationService.Hash(slide.SpeakerNotes ?? "")) ||
            revision.ApprovedUtc is null || revision.RevokedUtc is not null || x.Status != SalesNarrationAsset.Ready) }).ToArray();
    }

    private async Task<SalesPresentationPresetReadinessDto> ReadinessAsync(Guid companyId, SalesPresentationPresetVersion version, CancellationToken ct)
    {
        var blockers = new List<SalesPresentationPresetReadinessBlockerDto>(); var asset = version.Asset;
        if (asset is null) blockers.Add(Block(SalesPresentationPresetBlockerCodes.AssetMissing, "Upload a PowerPoint source before publishing.", SalesPresentationPresetActions.UploadAsset));
        else if (asset.Status is SalesPresentationPresetAssetStatus.PendingScan or SalesPresentationPresetAssetStatus.Processing) blockers.Add(Block(SalesPresentationPresetBlockerCodes.AssetProcessing, "The PowerPoint source is still processing."));
        else if (asset.Status is SalesPresentationPresetAssetStatus.Failed or SalesPresentationPresetAssetStatus.Blocked) blockers.Add(Block(SalesPresentationPresetBlockerCodes.AssetFailed, asset.FailureSummary ?? "The PowerPoint source could not be processed.", asset.CanRetry ? SalesPresentationPresetActions.RetryProcessing : SalesPresentationPresetActions.UploadAsset));
        else if (asset.SlideCount < 1) blockers.Add(Block(SalesPresentationPresetBlockerCodes.SlidesMissing, "The processed presentation contains no reusable slides.", SalesPresentationPresetActions.UploadAsset));
        if (version.DurationMinutes is < 1 or > 480) blockers.Add(Block(SalesPresentationPresetBlockerCodes.DurationInvalid, "Choose a duration between 1 and 480 minutes.", SalesPresentationPresetActions.UpdateDefaults));
        if (!version.AllowSalesMeeting && !version.AllowCampaignActivity && !version.AllowAdHoc) blockers.Add(Block(SalesPresentationPresetBlockerCodes.ContextUnsupported, "Choose at least one supported presentation context.", SalesPresentationPresetActions.UpdateDefaults));
        if (version.DefaultPresenterAgentId.HasValue && !await db.Agents.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == version.DefaultPresenterAgentId && x.Status == AgentStatus.Active && x.Department == "Sales", ct)) blockers.Add(Block(SalesPresentationPresetBlockerCodes.PresenterInvalid, "The default presenter must be an active company-owned Sales agent.", SalesPresentationPresetActions.ChoosePresenter));
        if (!string.IsNullOrWhiteSpace(version.RequiredKnowledgeScope))
        {
            var scope = version.RequiredKnowledgeScope;
            var available = await db.CompanyKnowledgeDocuments.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.IngestionStatus == CompanyKnowledgeDocumentIngestionStatus.Processed && x.ActiveChunkCount > 0 && (x.SourceRef == scope || x.Title.Contains(scope)), ct);
            if (!available) blockers.Add(Block(SalesPresentationPresetBlockerCodes.KnowledgeScopeUnavailable, "The required company knowledge scope is not available.", SalesPresentationPresetActions.UpdateDefaults, true));
        }
        var ready = blockers.Count == 0; return new(ready ? "ready" : "blocked", ready, blockers, ready ? [SalesPresentationPresetActions.Publish] : blockers.SelectMany(x => x.CorrectiveActions).Distinct().ToArray(), blockers.Any(x => x.RequiresReview));
    }

    private async Task<SalesPresentationPresetDto?> MapPresetAsync(Guid companyId, Guid presetId, CancellationToken ct)
    {
        var preset = await db.SalesPresentationPresets.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == presetId, ct); if (preset is null) return null;
        var versions = await db.SalesPresentationPresetVersions.AsNoTracking().Include(x => x.Asset).Where(x => x.CompanyId == companyId && x.PresetId == presetId).OrderByDescending(x => x.VersionNumber).ToListAsync(ct);
        var mapped = versions.Select(MapVersion).ToArray(); var actions = new List<string>(); if (preset.Lifecycle != SalesPresentationPresetLifecycle.Archived) { actions.Add(SalesPresentationPresetActions.Archive); actions.Add(versions.Any(x => x.Lifecycle == SalesPresentationPresetVersionLifecycle.Draft) ? SalesPresentationPresetActions.UpdateDefaults : SalesPresentationPresetActions.CreateDraft); }
        var whereUsed = await db.SalesPresentationRuns.AsNoTracking().CountAsync(x => x.CompanyId == companyId && x.PresetVersion.PresetId == presetId, ct);
        return new(preset.Id, preset.CompanyId, preset.Name, preset.Description, preset.OwnerUserId, preset.Lifecycle.ToStorageValue(), preset.CurrentPublishedVersionId, preset.CreatedUtc, preset.UpdatedUtc, preset.ArchivedUtc, preset.ConcurrencyVersion, whereUsed,
            mapped.FirstOrDefault(x => x.Lifecycle == "draft"), mapped.FirstOrDefault(x => x.Id == preset.CurrentPublishedVersionId), mapped, actions);
    }
    private static SalesPresentationPresetVersionDto MapVersion(SalesPresentationPresetVersion x) => new(x.Id, x.VersionNumber, x.Lifecycle.ToStorageValue(), x.DefaultPresenterAgentId, x.BehaviorSettingsJson, x.Goal, x.Audience, x.DurationMinutes, x.DemoScenario, x.ControlMode, x.Language,
        new[] { x.AllowSalesMeeting ? "sales_meeting" : null, x.AllowCampaignActivity ? "campaign_activity" : null, x.AllowAdHoc ? "ad_hoc" : null }.Where(v => v is not null).Cast<string>().ToArray(), x.RequiredKnowledgeScope, x.PublishedByUserId, x.PublishedUtc, x.CreatedUtc, x.UpdatedUtc, x.ConcurrencyVersion, x.Asset is null ? null : MapAsset(x.Asset));
    private static SalesPresentationPresetAssetDto MapAsset(SalesPresentationPresetAsset x) => new(x.Id, x.OriginalFileName, x.ContentType, x.FileSizeBytes, x.ContentHash, x.Status.ToStorageValue(), x.ProcessingVersion, x.ProcessingAttemptCount, x.SlideCount, x.RendererName, x.RendererVersion, x.AnimationHandling, x.FailureCode, x.FailureSummary, x.CanRetry, x.CreatedUtc, x.UpdatedUtc, x.ProcessedUtc, x.ConcurrencyVersion);
    private async Task<SalesPresentationPresetVersion?> DraftAsync(Guid companyId, Guid presetId, Guid versionId, CancellationToken ct) => await db.SalesPresentationPresetVersions.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.PresetId == presetId && x.Id == versionId && x.Lifecycle == SalesPresentationPresetVersionLifecycle.Draft && x.Preset.Lifecycle != SalesPresentationPresetLifecycle.Archived, ct);
    private async Task<bool> CanDeleteStoredObjectAsync(Guid companyId,string storageKey,CancellationToken ct) =>
        !await db.SalesPresentationPresetAssets.IgnoreQueryFilters().AsNoTracking().AnyAsync(x=>x.CompanyId==companyId&&x.StorageKey==storageKey,ct) &&
        !await db.SalesPresentationDecks.IgnoreQueryFilters().AsNoTracking().AnyAsync(x=>x.CompanyId==companyId&&x.StorageKey==storageKey,ct) &&
        !await db.SalesPresentationPresetSlides.IgnoreQueryFilters().AsNoTracking().AnyAsync(x=>x.CompanyId==companyId&&x.ImageStorageKey==storageKey,ct) &&
        !await db.SalesPresentationSlides.IgnoreQueryFilters().AsNoTracking().AnyAsync(x=>x.CompanyId==companyId&&x.ImageStorageKey==storageKey,ct);
    private async Task<SalesPresentationPresetVersion?> CurrentPublishedAsync(Guid companyId, SalesPresentationPreset preset, CancellationToken ct) => preset.CurrentPublishedVersionId is not Guid id ? null : await db.SalesPresentationPresetVersions.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id && x.PresetId == preset.Id, ct);
    private async Task<int> NextVersionNumberAsync(Guid companyId, Guid presetId, CancellationToken ct) => (await db.SalesPresentationPresetVersions.Where(x => x.CompanyId == companyId && x.PresetId == presetId).MaxAsync(x => (int?)x.VersionNumber, ct) ?? 0) + 1;
    private static SalesPresentationPresetVersion CloneDraft(SalesPresentationPresetVersion source, int number, DateTime now) => new(Guid.NewGuid(), source.CompanyId, source.PresetId, number, source.DefaultPresenterAgentId, source.BehaviorSettingsJson, source.Goal, source.Audience, source.DurationMinutes, source.DemoScenario, source.ControlMode, source.Language, source.AllowSalesMeeting, source.AllowCampaignActivity, source.AllowAdHoc, source.RequiredKnowledgeScope, now);
    private async Task EnsureMemberAsync(Guid companyId, Guid userId, CancellationToken ct) { if (companyId == Guid.Empty || userId == Guid.Empty || !await db.CompanyMemberships.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.UserId == userId && x.Status == CompanyMembershipStatus.Active, ct)) throw new UnauthorizedAccessException("An active company membership is required."); }
    private async Task EnsureOwnerAsync(Guid companyId, Guid userId, CancellationToken ct) { if (!await db.CompanyMemberships.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.UserId == userId && x.Status == CompanyMembershipStatus.Active, ct)) throw Validation("ownerUserId", "Choose an active company member as owner."); }
    private async Task EnsurePresenterAsync(Guid companyId, Guid? id, CancellationToken ct) { if (id.HasValue && !await db.Agents.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == id && x.Status == AgentStatus.Active && x.Department == "Sales", ct)) throw Validation("defaultPresenterAgentId", "Choose an active company-owned Sales agent."); }
    private static (bool meeting, bool campaign, bool adHoc) Contexts(IReadOnlyCollection<string> values) { var set = values.Select(x => x?.Trim().ToLowerInvariant()).ToHashSet(StringComparer.Ordinal); if (set.Count == 0 || set.Any(x => x is not ("sales_meeting" or "campaign_activity" or "ad_hoc"))) throw Validation("allowedContextTypes", "Choose one or more supported context types: sales_meeting, campaign_activity, ad_hoc."); return (set.Contains("sales_meeting"), set.Contains("campaign_activity"), set.Contains("ad_hoc")); }
    private async Task CopyDraftAssetAsync(Guid companyId,Guid actor,Guid sourceVersionId,Guid draftId,CancellationToken ct)
    {
        var source=await db.SalesPresentationPresetAssets.AsNoTracking().Include(x=>x.Slides)
            .SingleOrDefaultAsync(x=>x.CompanyId==companyId&&x.PresetVersionId==sourceVersionId&&x.Status==SalesPresentationPresetAssetStatus.Processed,ct);
        if(source is null)return;
        var now=UtcNow();
        var asset=new SalesPresentationPresetAsset(Guid.NewGuid(),companyId,draftId,source.OriginalFileName,source.ContentType,source.FileSizeBytes,
            source.ContentHash,source.StorageKey,source.StorageUrl,actor,now);
        asset.BeginProcessing(now,TimeSpan.Zero);
        asset.MarkProcessed(source.SlideCount,source.RendererName??"preset",source.RendererVersion??"1",source.AnimationHandling??"flattened",now);
        db.Add(asset);
        db.AddRange(source.Slides.Select(x=>new SalesPresentationPresetSlide(Guid.NewGuid(),companyId,asset.Id,asset.ProcessingVersion,
            x.SlideNumber,x.Title,x.ExtractedText,x.SpeakerNotes,x.ImageStorageKey,x.ImageStorageUrl,x.ImageWidthPixels,x.ImageHeightPixels,
            x.SourceWidthEmus,x.SourceHeightEmus,x.ContentHash,x.Objective,x.BaselineTalkingPoints,x.ExpectedDurationSeconds,x.TransitionText,now)));
    }

    private static void ValidateBehavior(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            if (doc.RootElement.TryGetProperty("retentionDays", out var retention) &&
                (retention.ValueKind != JsonValueKind.Number || !retention.TryGetInt32(out var days) || days is < 1 or > 3650))
                throw Validation("behaviorSettingsJson", "Meeting record retention must be between 1 and 3,650 days.");
            var reserved = new HashSet<string>(["autonomy", "permissions", "tools", "approval", "scopes"], StringComparer.OrdinalIgnoreCase);
            if (doc.RootElement.EnumerateObject().Any(x => reserved.Contains(x.Name)))
                throw Validation("behaviorSettingsJson", "Presentation behavior cannot change agent permissions, tools, scopes, approval, or autonomy.");
        }
        catch (SalesPresentationPresetValidationException) { throw; }
        catch (JsonException) { throw Validation("behaviorSettingsJson", "Presentation behavior settings must be a JSON object."); }
    }
    private void AddAudit(Guid companyId, Guid actor, string action, Guid preset, Guid? version, Guid? asset, string rationale, string? correlation) => db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), companyId, AuditActorTypes.User, actor, action, "sales_presentation_preset", preset.ToString("D"), AuditEventOutcomes.Succeeded, rationale, ["presentation preset"], new Dictionary<string, string?> { ["presetId"] = preset.ToString("D"), ["versionId"] = version?.ToString("D"), ["assetId"] = asset?.ToString("D") }, correlation, UtcNow()));
    private async Task SaveConflictAsync(CancellationToken ct) { try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { throw Conflict(SalesPresentationPresetProblemCodes.Conflict, "The presentation preset changed. Refresh and try again."); } catch (DbUpdateException) { throw Conflict(SalesPresentationPresetProblemCodes.Conflict, "The presentation preset changed concurrently. Refresh and try again."); } }
    private static void Expected(long actual, long expected) { if (expected < 1 || actual != expected) throw Conflict(SalesPresentationPresetProblemCodes.Conflict, "The presentation preset changed. Refresh and try again."); }
    private static SalesPresentationPresetReadinessBlockerDto Block(string code, string text, string? action = null, bool review = false) => new(code, text, action is null ? [] : [action], review);
    private static SalesPresentationPresetValidationException Validation(string field, string message) => new(new Dictionary<string, string[]> { [field] = [message] });
    private static SalesPresentationPresetConflictException Conflict(string code, string message) => new(code, message);
    private static bool SupportedContentType(string? value) => string.IsNullOrWhiteSpace(value) || value.Split(';')[0].Trim().ToLowerInvariant() is "application/vnd.openxmlformats-officedocument.presentationml.presentation" or "application/octet-stream" or "application/zip";
    private static string? NormalizeContentType(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Split(';')[0].Trim().ToLowerInvariant();
    private static async Task<MemoryStream> ReadBoundedAsync(Stream source, long maximum, CancellationToken ct) { var result = new MemoryStream(); var buffer = new byte[81920]; while (true) { var read = await source.ReadAsync(buffer, ct); if (read == 0) break; if (result.Length + read > maximum) { await result.DisposeAsync(); throw Validation("file", $"The PowerPoint file exceeds the {maximum}-byte upload limit."); } await result.WriteAsync(buffer.AsMemory(0, read), ct); } if (result.Length == 0) { await result.DisposeAsync(); throw Validation("file", "The PowerPoint file is empty."); } result.Position = 0; return result; }
    private static void ValidatePptx(Stream content) { try { content.Position = 0; Span<byte> header = stackalloc byte[4]; if (content.Read(header) != 4 || header[0] != 'P' || header[1] != 'K') throw Validation("file", "The file is not an unencrypted PowerPoint package."); content.Position = 0; using var archive = new ZipArchive(content, ZipArchiveMode.Read, true); if (archive.GetEntry("[Content_Types].xml") is null || archive.GetEntry("ppt/presentation.xml") is null) throw Validation("file", "The file is not a valid PowerPoint presentation."); } catch (InvalidDataException) { throw Validation("file", "The PowerPoint package is malformed or encrypted."); } finally { if (content.CanSeek) content.Position = 0; } }
    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
