using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class SalesPresetNarrationTests
{
    [Fact]
    public async Task Preset_authoring_has_no_meeting_owner_and_reuses_generated_audio()
    {
        await using var f = await SalesNarrationTests.Fixture.Create();
        var version = await Seed(f);
        var meetings = await f.Db.SalesMeetingSessions.CountAsync();
        var draft = await f.Service.PreparePresetAsync(f.Company, f.Actor, version.Id, new("en"), default);
        var again = await f.Service.PreparePresetAsync(f.Company, f.Actor, version.Id, new("en"), default);
        Assert.Equal(draft.Id, again.Id);
        var stored = await f.Db.SalesNarrationRevisions.SingleAsync(x => x.Id == draft.Id);
        Assert.Equal(version.Id, stored.PresetVersionId);
        Assert.Null(stored.SessionId); Assert.Null(stored.DeckId); Assert.Null(stored.AudienceId);
        Assert.Equal(meetings, await f.Db.SalesMeetingSessions.CountAsync());
        Assert.Equal(0, f.Speech.Calls);
        await Assert.ThrowsAsync<SalesNarrationException>(() => f.Service.PreviewPresetAsync(f.Company,f.Actor,version.Id,draft.Id,draft.Segments[0].Id,default));
        await f.Approve(draft); await f.Generate();
        var ready = (await f.Service.GetPresetAsync(f.Company,f.Actor,version.Id,default)).Revisions.Single();
        Assert.Equal("ready", ready.Status);
        Assert.Single(ready.Segments);
        var audio = await f.Service.PreviewPresetAsync(f.Company,f.Actor,version.Id,ready.Id,ready.Segments[0].Id,default);
        Assert.NotEmpty(audio.Audio);
        await f.Generate();
        Assert.Equal(1, f.Speech.Calls);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.GetPresetAsync(f.Company,f.OtherActor,version.Id,default));
        await Assert.ThrowsAnyAsync<Exception>(() => f.Service.GetPresetAsync(Guid.NewGuid(),f.Actor,version.Id,default));
    }

    [Fact]
    public async Task Applying_published_preset_reuses_release_and_revocation_blocks_meeting_preview()
    {
        await using var f = await SalesNarrationTests.Fixture.Create();
        var version = await Seed(f);
        var draft = await f.Service.PreparePresetAsync(f.Company,f.Actor,version.Id,new("en"),default);
        await f.Approve(draft); await f.Generate();
        var storedVersion = await f.Db.SalesPresentationPresetVersions.SingleAsync(x=>x.Id==version.Id);
        var preset = await f.Db.SalesPresentationPresets.SingleAsync(x=>x.Id==version.PresetId);
        storedVersion.Publish(f.Actor,f.Clock.Now); preset.Publish(version.Id,f.Clock.Now);
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<SalesNarrationException>(() => f.Service.PreparePresetAsync(f.Company,f.Actor,version.Id,new("en"),default));
        var beforeConsent = (await f.Db.SalesMeetingSessions.AsNoTracking().SingleAsync(x=>x.Id==f.Session)).ConsentStatus;
        var runs = new SalesPresentationRunService(f.Db,null!,f.Clock,new SalesMeetingSessionService(f.Db,f.Clock),f.Service);
        var run = await runs.ApplyAsync(f.Company,f.Actor,f.Session,new(preset.Id,version.Id,null,null,null,null,null,null,null,false,null),null,default);
        Assert.NotNull(run);
        var bound = (await f.Service.GetAsync(f.Company,f.Actor,f.Session,default)).Revisions.Single();
        Assert.Equal("ready",bound.Status);
        Assert.All(bound.Segments,x=>Assert.True(x.Reused));
        await f.Service.PreviewAsync(f.Company,f.Actor,f.Request(bound),default);
        Assert.Equal(1,f.Speech.Calls);
        var meeting = await f.Db.SalesMeetingSessions.AsNoTracking().SingleAsync(x=>x.Id==f.Session);
        Assert.Equal(beforeConsent,meeting.ConsentStatus);
        Assert.Equal(180,meeting.RetentionDays);
        var source = (await f.Service.GetPresetAsync(f.Company,f.Actor,version.Id,default)).Revisions.Single();
        await f.Service.DecideAsync(f.Company,f.Actor,source.Id,"revoke",new(source.Version),default);
        await Assert.ThrowsAsync<SalesNarrationException>(()=>f.Service.PreviewAsync(f.Company,f.Actor,f.Request(bound),default));
    }

    [Fact]
    public async Task Replacing_source_retains_script_history_but_invalidates_release()
    {
        await using var f = await SalesNarrationTests.Fixture.Create();
        var version = await Seed(f);
        var draft = await f.Service.PreparePresetAsync(f.Company,f.Actor,version.Id,new("en"),default);
        await f.Approve(draft); await f.Generate();
        f.Db.SalesPresentationPresetSlides.RemoveRange(await f.Db.SalesPresentationPresetSlides.ToListAsync());
        await f.Db.SaveChangesAsync();
        Assert.True(await f.Db.SalesNarrationSegments.AnyAsync(x=>x.RevisionId==draft.Id));
        await Assert.ThrowsAsync<SalesNarrationException>(()=>f.Service.PreviewPresetAsync(f.Company,f.Actor,version.Id,draft.Id,draft.Segments[0].Id,default));
    }

    [Fact]
    public async Task New_draft_retains_source_and_edited_script_but_requires_fresh_approval()
    {
        await using var f=await SalesNarrationTests.Fixture.Create();
        var version=await Seed(f);
        var script=await f.Service.PreparePresetAsync(f.Company,f.Actor,version.Id,new("en",[new(1,1,"Reviewed reusable wording.")]),default);
        await f.Approve(script);await f.Generate();
        var current=await f.Db.SalesPresentationPresetVersions.SingleAsync(x=>x.Id==version.Id);
        var preset=await f.Db.SalesPresentationPresets.SingleAsync(x=>x.Id==version.PresetId);
        current.Publish(f.Actor,f.Clock.Now);preset.Publish(version.Id,f.Clock.Now);await f.Db.SaveChangesAsync();
        var presets=new SalesPresentationPresetService(f.Db,f.Storage,Microsoft.Extensions.Options.Options.Create(new SalesPresentationOptions()),f.Clock);
        var next=await presets.CreateNextDraftAsync(f.Company,f.Actor,preset.Id,preset.ConcurrencyVersion,null,default);
        Assert.Equal(1,next!.CurrentDraft!.Asset!.SlideCount);
        var draft=await f.Service.PreparePresetAsync(f.Company,f.Actor,next.CurrentDraft.Id,new("en"),default);
        Assert.Equal("Reviewed reusable wording.",draft.Segments.Single().Script);
        Assert.True(draft.Segments.Single().Reused);
        Assert.Null(draft.ApprovedUtc);
        Assert.Equal("draft",draft.Status);
        Assert.Equal(1,f.Speech.Calls);
    }

    [Fact]
    public async Task Edited_notes_persist_and_block_old_audio_until_a_new_revision_is_approved()
    {
        await using var f = await SalesNarrationTests.Fixture.Create();
        var version = await Seed(f);
        var presets = new SalesPresentationPresetService(f.Db, f.Storage, Microsoft.Extensions.Options.Options.Create(new SalesPresentationOptions()), f.Clock);
        var draft = await f.Service.PreparePresetAsync(f.Company, f.Actor, version.Id, new("en"), default);
        await f.Approve(draft); await f.Generate();
        var slide = await f.Db.SalesPresentationPresetSlides.SingleAsync();
        // Covers audio created before notes snapshots were introduced.
        (await f.Db.SalesNarrationSegments.SingleAsync()).SourceNotesHash = null;
        await f.Db.SaveChangesAsync();
        var updated = await presets.UpdateSpeakerNotesAsync(f.Company, f.Actor, version.PresetId, version.Id, slide.Id,
            version.ConcurrencyVersion, "Updated presenter wording.", null, default);
        f.Db.ChangeTracker.Clear();
        Assert.Equal("Updated presenter wording.", (await f.Db.SalesPresentationPresetSlides.SingleAsync()).SpeakerNotes);
        var outdated = (await f.Service.GetPresetAsync(f.Company, f.Actor, version.Id, default)).Revisions.Single();
        Assert.Equal("outdated", outdated.Status);
        Assert.Equal("needs_update", outdated.Segments.Single().Status);
        Assert.Equal("speaker_notes_changed", outdated.Segments.Single().FailureCode);
        Assert.True((await presets.GetSlidesAsync(f.Company, f.Actor, version.PresetId, version.Id, default))!.Single().AudioNeedsUpdate);
        await Assert.ThrowsAsync<SalesNarrationException>(() => f.Service.PreviewPresetAsync(f.Company, f.Actor, version.Id, draft.Id, draft.Segments[0].Id, default));
        await Assert.ThrowsAsync<SalesPresentationPresetConflictException>(() => presets.UpdateSpeakerNotesAsync(f.Company, f.Actor,
            version.PresetId, version.Id, slide.Id, version.ConcurrencyVersion, "Stale edit", null, default));
        f.Clock.Now = f.Clock.Now.AddSeconds(1);
        var replacement = await f.Service.PreparePresetAsync(f.Company, f.Actor, version.Id, new("en"), default);
        Assert.NotEqual(draft.Id, replacement.Id);
        Assert.Equal("Updated presenter wording.", replacement.Segments.Single().Script);
        Assert.Null(replacement.ApprovedUtc);
        Assert.True((await presets.GetSlidesAsync(f.Company, f.Actor, version.PresetId, version.Id, default))!.Single().AudioNeedsUpdate);
        await f.Approve(replacement); await f.Generate();
        Assert.Equal(2, f.Speech.Calls);
        Assert.False((await presets.GetSlidesAsync(f.Company, f.Actor, version.PresetId, version.Id, default))!.Single().AudioNeedsUpdate);
        Assert.Equal("ready", (await f.Service.GetPresetAsync(f.Company, f.Actor, version.Id, default)).Revisions.First().Status);
        // An unchanged save is idempotent and preserves ready audio.
        await presets.UpdateSpeakerNotesAsync(f.Company, f.Actor, version.PresetId, version.Id, slide.Id,
            updated!.CurrentDraft!.ConcurrencyVersion, " Updated presenter wording. ", null, default);
        Assert.Equal("ready", (await f.Service.GetPresetAsync(f.Company, f.Actor, version.Id, default)).Revisions.First().Status);
    }

    [Fact]
    public async Task Published_notes_are_immutable_and_changed_new_draft_notes_do_not_inherit_old_script()
    {
        await using var f = await SalesNarrationTests.Fixture.Create();
        var version = await Seed(f);
        var draft = await f.Service.PreparePresetAsync(f.Company, f.Actor, version.Id, new("en", [new(1, 1, "Reviewed original.")]), default);
        var current = await f.Db.SalesPresentationPresetVersions.SingleAsync(x => x.Id == version.Id);
        var preset = await f.Db.SalesPresentationPresets.SingleAsync();
        var slide = await f.Db.SalesPresentationPresetSlides.SingleAsync();
        (await f.Db.SalesNarrationSegments.SingleAsync()).SourceNotesHash = null;
        current.Publish(f.Actor, f.Clock.Now); preset.Publish(version.Id, f.Clock.Now); await f.Db.SaveChangesAsync();
        var presets = new SalesPresentationPresetService(f.Db, f.Storage, Microsoft.Extensions.Options.Options.Create(new SalesPresentationOptions()), f.Clock);
        await Assert.ThrowsAsync<SalesPresentationPresetConflictException>(() => presets.UpdateSpeakerNotesAsync(f.Company, f.Actor,
            preset.Id, version.Id, slide.Id, current.ConcurrencyVersion, "Cannot change", null, default));
        var next = await presets.CreateNextDraftAsync(f.Company, f.Actor, preset.Id, preset.ConcurrencyVersion, null, default);
        var nextSlide = (await presets.GetSlidesAsync(f.Company, f.Actor, preset.Id, next!.CurrentDraft!.Id, default))!.Single();
        Assert.Null(await presets.UpdateSpeakerNotesAsync(f.Company, f.Actor, preset.Id, next.CurrentDraft.Id, slide.Id,
            next.CurrentDraft.ConcurrencyVersion, "Wrong slide", null, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => presets.UpdateSpeakerNotesAsync(Guid.NewGuid(), f.Actor,
            preset.Id, next.CurrentDraft.Id, nextSlide.Id, next.CurrentDraft.ConcurrencyVersion, "Wrong company", null, default));
        await presets.UpdateSpeakerNotesAsync(f.Company, f.Actor, preset.Id, next.CurrentDraft.Id, nextSlide.Id,
            next.CurrentDraft.ConcurrencyVersion, "New draft notes.", null, default);
        var prepared = await f.Service.PreparePresetAsync(f.Company, f.Actor, next.CurrentDraft.Id, new("en"), default);
        Assert.Equal("New draft notes.", prepared.Segments.Single().Script);
        Assert.Null((await f.Db.SalesPresentationPresetSlides.AsNoTracking().SingleAsync(x => x.Id == slide.Id)).SpeakerNotes);
    }

    [Fact]
    public async Task Only_changed_slide_audio_is_invalidated_and_cleared_notes_use_slide_text()
    {
        await using var f = await SalesNarrationTests.Fixture.Create();
        var version = await Seed(f);
        var first = await f.Db.SalesPresentationPresetSlides.SingleAsync();
        first.UpdateSpeakerNotes("Original first slide notes.");
        f.Db.Add(new SalesPresentationPresetSlide(Guid.NewGuid(), f.Company, first.AssetId, 1, 2, "Next", "Second source.", null,
            "safe/preset/second", null, 1600, 900, 100, 100, new string('c', 64), "Explain next", "Second narration.", 60, "End", f.Clock.Now));
        await f.Db.SaveChangesAsync();
        var draft = await f.Service.PreparePresetAsync(f.Company, f.Actor, version.Id, new("en"), default);
        await f.Approve(draft); await f.Generate();
        Assert.Equal(2, f.Speech.Calls);
        var presets = new SalesPresentationPresetService(f.Db, f.Storage, Microsoft.Extensions.Options.Options.Create(new SalesPresentationOptions()), f.Clock);
        await presets.UpdateSpeakerNotesAsync(f.Company, f.Actor, version.PresetId, version.Id, first.Id, version.ConcurrencyVersion, "", null, default);
        var old = (await f.Service.GetPresetAsync(f.Company, f.Actor, version.Id, default)).Revisions.Single();
        Assert.Equal("needs_update", old.Segments.Single(x => x.SlideNumber == 1).Status);
        Assert.Equal("ready", old.Segments.Single(x => x.SlideNumber == 2).Status);
        var slides = await presets.GetSlidesAsync(f.Company, f.Actor, version.PresetId, version.Id, default);
        Assert.True(slides!.Single(x => x.SlideNumber == 1).AudioNeedsUpdate);
        Assert.False(slides.Single(x => x.SlideNumber == 2).AudioNeedsUpdate);
        f.Clock.Now = f.Clock.Now.AddSeconds(1);
        var next = await f.Service.PreparePresetAsync(f.Company, f.Actor, version.Id, new("en"), default);
        Assert.Equal(first.ExtractedText, next.Segments.Single(x => x.SlideNumber == 1).Script);
        Assert.True(next.Segments.Single(x => x.SlideNumber == 2).Reused);
        await f.Approve(next); await f.Generate();
        Assert.Equal(3, f.Speech.Calls);
    }

    [Fact]
    public async Task Voice_selection_creates_a_distinct_unapproved_revision_and_separate_cached_audio()
    {
        await using var f = await SalesNarrationTests.Fixture.Create();
        var version = await Seed(f);
        var original = await f.Service.PreparePresetAsync(f.Company, f.Actor, version.Id, new("en"), default);
        await f.Approve(original); await f.Generate();
        f.Clock.Now = f.Clock.Now.AddSeconds(1);
        var changed = await f.Service.PreparePresetAsync(f.Company, f.Actor, version.Id, new("en", Voice: "cedar"), default);
        Assert.Equal("cedar", changed.Voice);
        Assert.NotEqual(original.Id, changed.Id);
        Assert.Null(changed.ApprovedUtc);
        Assert.False(changed.Segments.Single().Reused);
        Assert.Equal(1, f.Speech.Calls);
        Assert.Equal(changed.Id, (await f.Service.PreparePresetAsync(f.Company, f.Actor, version.Id, new("en", Voice: "cedar"), default)).Id);
        await f.Approve(changed); await f.Generate();
        Assert.Equal(2, f.Speech.Calls);
        Assert.Equal("cedar", f.Speech.LastVoice);
        var workspace = await f.Service.GetPresetAsync(f.Company, f.Actor, version.Id, default);
        Assert.Equal("cedar", workspace.Revisions.First().Voice);
        Assert.Equal("ready", workspace.Revisions.Single(x => x.Id == original.Id).Status);
        Assert.Equal("marin", workspace.Speech.Voice);
        Assert.Equal(2, await f.Db.SalesNarrationAssets.CountAsync());
        var invalid = await Assert.ThrowsAsync<SalesNarrationException>(() => f.Service.PreparePresetAsync(f.Company, f.Actor, version.Id, new("en", Voice: "unknown"), default));
        Assert.Equal(400, invalid.StatusCode);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.PreparePresetAsync(Guid.NewGuid(), f.Actor, version.Id, new("en", Voice: "cedar"), default));
    }

    private static async Task<SalesPresentationPresetVersion> Seed(SalesNarrationTests.Fixture f)
    {
        var now=f.Clock.Now;
        var agentId=await f.Db.SalesPresentationDecks.Select(x=>x.AgentId).SingleAsync();
        var preset=new SalesPresentationPreset(Guid.NewGuid(),f.Company,"Reusable demo",null,f.Actor,now);
        var version=new SalesPresentationPresetVersion(Guid.NewGuid(),f.Company,preset.Id,1,agentId,
            "{\"retentionDays\":180}","Explain the product","Business leaders",30,null,"assisted","en",true,false,true,null,now);
        var asset=new SalesPresentationPresetAsset(Guid.NewGuid(),f.Company,version.Id,"demo.pptx","application/octet-stream",100,new string('a',64),"safe/preset",null,f.Actor,now);
        asset.BeginProcessing(now,TimeSpan.FromMinutes(5)); asset.MarkProcessed(1,"renderer","1","flattened",now);
        f.Db.AddRange(preset,version,asset);
        f.Db.Add(new SalesPresentationPresetSlide(Guid.NewGuid(),f.Company,asset.Id,1,1,"Overview","Product overview.",null,
            "safe/preset/slide",null,1600,900,100,100,new string('b',64),"Explain product","Welcome to our product.",60,"Continue",now));
        await f.Db.SaveChangesAsync();
        return version;
    }
}
