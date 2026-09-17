using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Sales;

public sealed partial class SalesPresentationPresetService
{
    public async Task<SalesPresentationPresetDto?> UpdateSpeakerNotesAsync(Guid companyId, Guid actorUserId,
        Guid presetId, Guid versionId, Guid slideId, long expectedVersion, string? notes, string? correlationId, CancellationToken ct)
    {
        await EnsureMemberAsync(companyId, actorUserId, ct);
        var version = await db.SalesPresentationPresetVersions.Include(x => x.Preset).Include(x => x.Asset)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.PresetId == presetId && x.Id == versionId, ct);
        if (version is null) return null;
        if (version.Lifecycle != SalesPresentationPresetVersionLifecycle.Draft || version.Preset.Lifecycle == SalesPresentationPresetLifecycle.Archived)
            throw Conflict(SalesPresentationPresetProblemCodes.ImmutableVersion, "Create an editable draft before changing speaker notes.");
        Expected(version.ConcurrencyVersion, expectedVersion);
        if (notes?.Length > 16000) throw Validation("speakerNotes", "Speaker notes cannot exceed 16,000 characters.");
        if (version.Asset?.Status != SalesPresentationPresetAssetStatus.Processed)
            throw Conflict(SalesPresentationPresetProblemCodes.NotReady, "Wait for PowerPoint processing before editing notes.");
        var slide = await db.SalesPresentationPresetSlides.SingleOrDefaultAsync(x => x.CompanyId == companyId &&
            x.Id == slideId && x.AssetId == version.Asset.Id && x.ProcessingVersion == version.Asset.ProcessingVersion, ct);
        if (slide is null) return null;
        var normalized = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (slide.SpeakerNotes == normalized) return await MapPresetAsync(companyId, presetId, ct);
        // Existing releases predate note snapshots. Capture their original evidence
        // before editing; do not mutate shared audio assets or historical scripts.
        var legacy = await db.SalesNarrationSegments.Where(x => x.CompanyId == companyId &&
            x.PresetSlideId == slideId && x.SourceNotesHash == null).ToListAsync(ct);
        foreach (var segment in legacy) segment.SourceNotesHash = SalesNarrationService.Hash(slide.SpeakerNotes ?? "");
        slide.UpdateSpeakerNotes(normalized);
        // The existing version token serializes note edits against other edits/publication.
        version.Update(version.DefaultPresenterAgentId, version.BehaviorSettingsJson, version.Goal, version.Audience,
            version.DurationMinutes, version.DemoScenario, version.ControlMode, version.Language,
            version.AllowSalesMeeting, version.AllowCampaignActivity, version.AllowAdHoc, version.RequiredKnowledgeScope, UtcNow());
        AddAudit(companyId, actorUserId, "sales.presentation_preset.speaker_notes_updated", presetId, versionId, version.Asset.Id,
            $"Speaker notes for slide {slide.SlideNumber} changed; narration must be reviewed against the new notes.", correlationId);
        await SaveConflictAsync(ct);
        return await MapPresetAsync(companyId, presetId, ct);
    }
}
