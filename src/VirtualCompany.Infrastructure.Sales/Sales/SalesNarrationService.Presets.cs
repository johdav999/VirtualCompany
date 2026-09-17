using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Sales;

public sealed partial class SalesNarrationService
{
    private sealed record NarrationSource(Guid Id,int SlideNumber,string ContentHash,string ExtractedText,string Baseline,string NotesHash);

    public async Task<SalesNarrationWorkspace> GetPresetAsync(Guid companyId,Guid userId,Guid versionId,CancellationToken ct)
    {
        await AuthorizePresetAsync(companyId,userId,versionId,ct);
        var revisions=await db.SalesNarrationRevisions.AsNoTracking().Where(x=>x.CompanyId==companyId&&x.PresetVersionId==versionId)
            .OrderByDescending(x=>x.CreatedUtc).Take(50).ToListAsync(ct);
        var results=new List<SalesNarrationRevisionDto>();
        foreach(var revision in revisions)results.Add(await MapAsync(revision,ct));
        var profile=await speech.GetProfileAsync(ct);
        return new(profile with {Available=profile.Available&&options.Value.CanGenerate},results);
    }

    public async Task<SalesNarrationRevisionDto> PreparePresetAsync(Guid companyId,Guid userId,Guid versionId,PrepareSalesNarration command,CancellationToken ct)
    {
        var version=await AuthorizePresetAsync(companyId,userId,versionId,ct);
        if(version.Lifecycle!=SalesPresentationPresetVersionLifecycle.Draft)
            throw new SalesNarrationException("Create a new preset draft to change reusable narration.");
        if(version.Asset?.Status!=SalesPresentationPresetAssetStatus.Processed)
            throw new SalesNarrationException("Upload and process the preset PowerPoint before preparing its script.");
        if(version.DefaultPresenterAgentId is null)
            throw new SalesNarrationException("Choose the preset presenter in Settings before preparing audio.");
        var sources=version.Asset.Slides.OrderBy(x=>x.SlideNumber)
            .Select(x=>new NarrationSource(x.Id,x.SlideNumber,x.ContentHash,x.ExtractedText,
                string.IsNullOrWhiteSpace(x.SpeakerNotes)?x.BaselineTalkingPoints:x.SpeakerNotes,Hash(x.SpeakerNotes??""))).ToArray();
        // Carry reviewed text forward only for unchanged source evidence. The new
        // version still requires an explicit approval; meeting-specific text is excluded.
        if(command.Scripts is null)
        {
            var priorId=await db.SalesNarrationRevisions.AsNoTracking()
                .Where(x=>x.CompanyId==companyId&&x.PresetVersionId!=null&&
                    db.SalesPresentationPresetVersions.Any(v=>v.CompanyId==companyId&&v.Id==x.PresetVersionId&&v.PresetId==version.PresetId))
                .OrderByDescending(x=>x.CreatedUtc).ThenByDescending(x=>x.DeckVersion).Select(x=>(Guid?)x.Id).FirstOrDefaultAsync(ct);
            if(priorId is Guid prior)
            {
                var previous=await db.SalesNarrationSegments.AsNoTracking().Where(x=>x.CompanyId==companyId&&x.RevisionId==prior).ToListAsync(ct);
                var previousSlideIds=previous.Where(x=>x.PresetSlideId.HasValue).Select(x=>x.PresetSlideId!.Value).ToArray();
                var previousNotes=await db.SalesPresentationPresetSlides.AsNoTracking()
                    .Where(x=>x.CompanyId==companyId&&previousSlideIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,x=>x.SpeakerNotes,ct);
                var scripts=sources.SelectMany(source=>{
                    var matches=previous.Where(x=>x.SlideNumber==source.SlideNumber&&x.SourceHash==Hash(source.ContentHash+"|"+source.ExtractedText)&&
                        (x.SourceNotesHash??(x.PresetSlideId is Guid id&&previousNotes.TryGetValue(id,out var notes)?Hash(notes??""):null))==source.NotesHash)
                        .OrderBy(x=>x.TalkingPoint).ToArray();
                    return matches.Length>0?matches.Select(x=>new SalesNarrationScript(source.SlideNumber,x.TalkingPoint,x.Script)):
                        (string.IsNullOrWhiteSpace(source.Baseline)?source.ExtractedText:source.Baseline)
                        .Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)
                        .Select((text,index)=>new SalesNarrationScript(source.SlideNumber,index+1,text));
                }).ToArray();
                command=command with { Scripts=scripts };
            }
        }
        return await PrepareManifestAsync(new SalesNarrationRevision {
            CompanyId=companyId,PresetVersionId=versionId,DeckVersion=version.VersionNumber,
            ProcessingVersion=version.Asset.ProcessingVersion,AudienceHash=PresetAudience(version),
            CreatedByUserId=userId
        },sources,command,ct);
    }

    private async Task<SalesNarrationRevisionDto> PrepareManifestAsync(SalesNarrationRevision owner,
        IReadOnlyList<NarrationSource> sources,PrepareSalesNarration command,CancellationToken ct)
    {
        if(command.Language is not ("en" or "sv"))throw new SalesNarrationException("Choose English or Swedish.",400);
        var scripts=command.Scripts?.ToArray()??sources.SelectMany(x=>
            (string.IsNullOrWhiteSpace(x.Baseline)?x.ExtractedText:x.Baseline)
                .Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)
                .Select((text,index)=>new SalesNarrationScript(x.SlideNumber,index+1,text))).ToArray();
        if(sources.Count==0||scripts.Length is <1 or >100||scripts.Any(x=>string.IsNullOrWhiteSpace(x.Text)||x.Text.Length>3000||
            x.TalkingPoint<1||!sources.Any(s=>s.SlideNumber==x.SlideNumber))||
            scripts.Select(x=>(x.SlideNumber,x.TalkingPoint)).Distinct().Count()!=scripts.Length||
            sources.Any(x=>!scripts.Any(s=>s.SlideNumber==x.SlideNumber)))
            throw new SalesNarrationException("Supply 1–100 talking points, at most 3,000 characters each, covering every slide.",400);
        scripts=scripts.OrderBy(x=>x.SlideNumber).ThenBy(x=>x.TalkingPoint).ToArray();
        var profile=SelectVoice(await speech.GetProfileAsync(ct), command.Voice);
        var manifest=Hash(JsonSerializer.Serialize(new {owner.SessionId,owner.DeckId,owner.PresetVersionId,
            owner.DeckVersion,owner.ProcessingVersion,owner.AudienceHash,command.Language,
            profile.Voice,profile.ConfigurationVersion,scripts,sources=sources.Select(x=>new{x.Id,x.ContentHash,x.NotesHash})}));
        var revisionId=await db.Database.CreateExecutionStrategy().ExecuteAsync(async()=>{
            db.ChangeTracker.Clear();
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var existing=await db.SalesNarrationRevisions.SingleOrDefaultAsync(x=>x.CompanyId==owner.CompanyId&&
                x.SessionId==owner.SessionId&&x.PresetVersionId==owner.PresetVersionId&&x.ManifestHash==manifest,ct);
            if(existing is not null)return existing.Id;
            var now=clock.GetUtcNow().UtcDateTime;
            var revision=new SalesNarrationRevision {
                Id=Guid.NewGuid(),CompanyId=owner.CompanyId,SessionId=owner.SessionId,DeckId=owner.DeckId,
                PresetVersionId=owner.PresetVersionId,DeckVersion=owner.DeckVersion,ProcessingVersion=owner.ProcessingVersion,
                AudienceId=owner.AudienceId,AudienceHash=owner.AudienceHash,ManifestHash=manifest,
                Language=command.Language,Voice=profile.Voice,Model=profile.Model,ConfigurationVersion=profile.ConfigurationVersion,
                CreatedByUserId=owner.CreatedByUserId,CreatedUtc=now,RetainUntilUtc=now.AddDays(90)
            };
            db.SalesNarrationRevisions.Add(revision);
            foreach(var script in scripts)
            {
                var source=sources.Single(x=>x.SlideNumber==script.SlideNumber);
                var sourceHash=Hash(source.ContentHash+"|"+source.ExtractedText);
                var scriptHash=Hash(script.Text);
                var key=AssetKey(owner.CompanyId,sourceHash,scriptHash,command.Language,profile.Voice,profile.ConfigurationVersion,owner.AudienceHash);
                var asset=db.SalesNarrationAssets.Local.FirstOrDefault(x=>x.CompanyId==owner.CompanyId&&x.CacheKey==key)
                    ??await db.SalesNarrationAssets.SingleOrDefaultAsync(x=>x.CompanyId==owner.CompanyId&&x.CacheKey==key,ct);
                SalesRoomBenchmarkTelemetry.RecordCache(asset?.Status==SalesNarrationAsset.Ready);
                if(asset?.FailureCode=="retention_deletion")throw new SalesNarrationException("Audio cleanup is in progress. Retry shortly.");
                if(asset is null){asset=new(){Id=Guid.NewGuid(),CompanyId=owner.CompanyId,CacheKey=key,CreatedUtc=now,UpdatedUtc=now};db.SalesNarrationAssets.Add(asset);}
                db.SalesNarrationSegments.Add(new(){Id=Guid.NewGuid(),CompanyId=owner.CompanyId,RevisionId=revision.Id,
                    SourceSlideId=owner.PresetVersionId.HasValue?null:source.Id,PresetSlideId=owner.PresetVersionId.HasValue?source.Id:null,
                    SlideNumber=script.SlideNumber,TalkingPoint=script.TalkingPoint,SourceHash=sourceHash,SourceNotesHash=source.NotesHash,SourceText=source.ExtractedText,
                    Script=script.Text,ScriptHash=scriptHash,AssetId=asset.Id,Reused=asset.Status==SalesNarrationAsset.Ready});
            }
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return revision.Id;
        });
        db.ChangeTracker.Clear();
        return await MapAsync(await db.SalesNarrationRevisions.AsNoTracking().SingleAsync(x=>x.CompanyId==owner.CompanyId&&x.Id==revisionId,ct),ct);
    }

    private async Task<SalesPresentationPresetVersion> AuthorizePresetAsync(Guid companyId,Guid userId,Guid versionId,CancellationToken ct)
    {
        if(!await db.CompanyMemberships.AsNoTracking().AnyAsync(x=>x.CompanyId==companyId&&x.UserId==userId&&x.Status==CompanyMembershipStatus.Active,ct))
            throw new UnauthorizedAccessException("Active company membership is required.");
        var version=await db.SalesPresentationPresetVersions.AsNoTracking().Include(x=>x.Preset).Include(x=>x.Asset).ThenInclude(x=>x!.Slides)
            .SingleOrDefaultAsync(x=>x.CompanyId==companyId&&x.Id==versionId,ct)??throw new KeyNotFoundException();
        if(version.Preset.OwnerUserId!=userId)throw new UnauthorizedAccessException("Only the preset owner can edit and preview its narration.");
        return version;
    }

    private static string PresetAudience(SalesPresentationPresetVersion version)=>Hash(JsonSerializer.Serialize(new {
        scope="reusable_preset",version.CompanyId,version.PresetId,version.Audience,version.Goal,
        version.DefaultPresenterAgentId,version.Language,version.ControlMode,version.BehaviorSettingsJson
    }));

    private async Task EnsurePresetCurrentAsync(SalesNarrationRevision revision,CancellationToken ct)
    {
        var version=await db.SalesPresentationPresetVersions.AsNoTracking().Include(x=>x.Preset).Include(x=>x.Asset).ThenInclude(x=>x!.Slides)
            .SingleOrDefaultAsync(x=>x.CompanyId==revision.CompanyId&&x.Id==revision.PresetVersionId,ct);
        if(version is null||version.Asset?.Status!=SalesPresentationPresetAssetStatus.Processed||
            version.Asset.ProcessingVersion!=revision.ProcessingVersion||PresetAudience(version)!=revision.AudienceHash)
            throw new SalesNarrationException("Preset source or settings changed. Prepare a new narration revision.");
        var segments=await db.SalesNarrationSegments.AsNoTracking().Where(x=>x.CompanyId==revision.CompanyId&&x.RevisionId==revision.Id).ToListAsync(ct);
        if(segments.Count==0||segments.Any(s=>!version.Asset.Slides.Any(x=>x.Id==s.PresetSlideId&&Hash(x.ContentHash+"|"+x.ExtractedText)==s.SourceHash&&
            (s.SourceNotesHash is null||s.SourceNotesHash==Hash(x.SpeakerNotes??"")))))
            throw new SalesNarrationException("Preset slide evidence changed. Prepare a new narration revision.");
    }

    public async Task<SalesNarrationPreview> PreviewPresetAsync(Guid companyId,Guid userId,Guid versionId,Guid revisionId,Guid segmentId,CancellationToken ct)
    {
        await AuthorizePresetAsync(companyId,userId,versionId,ct);
        var revision=await db.SalesNarrationRevisions.AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==companyId&&x.Id==revisionId&&x.PresetVersionId==versionId,ct)
            ??throw new KeyNotFoundException();
        await EnsureReleasedAsync(revision,ct);
        var segment=await db.SalesNarrationSegments.AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==companyId&&x.Id==segmentId&&x.RevisionId==revisionId,ct)
            ??throw new KeyNotFoundException();
        var asset=await db.SalesNarrationAssets.AsNoTracking().SingleAsync(x=>x.CompanyId==companyId&&x.Id==segment.AssetId,ct);
        if(asset.Status!=SalesNarrationAsset.Ready||asset.StorageKey is null)throw new SalesNarrationException("Audio is not ready.");
        await using var stream=await storage.OpenReadAsync(asset.StorageKey,ct);
        using var buffer=new MemoryStream();
        var bytes=new byte[16384];int count;
        while((count=await stream.ReadAsync(bytes,ct))>0){if(buffer.Length+count>5_760_044)throw new SalesNarrationException("Stored audio is invalid.");buffer.Write(bytes,0,count);}
        var audio=buffer.ToArray();
        if(audio.Length!=asset.Bytes||audio.Length<46||Hash(audio)!=asset.AudioHash)throw new SalesNarrationException("Stored audio failed integrity validation.");
        // Object I/O can overlap approval revocation or membership changes.
        revision=await db.SalesNarrationRevisions.AsNoTracking().SingleAsync(x=>x.CompanyId==companyId&&x.Id==revisionId,ct);
        await EnsureReleasedAsync(revision,ct);
        await AuthorizePresetAsync(companyId,userId,versionId,ct);
        await db.SalesNarrationAssets.Where(x=>x.CompanyId==companyId&&x.Id==asset.Id&&x.Status==SalesNarrationAsset.Ready)
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.PreviewCount,x=>x.PreviewCount+1)
                .SetProperty(x=>x.ReusedMilliseconds,x=>x.ReusedMilliseconds+asset.DurationMilliseconds),ct);
        return new(audio,"audio/wav");
    }
}
