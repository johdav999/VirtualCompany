using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Sales;

public sealed partial class SalesNarrationService
{
    // Called within the presentation-run transaction; it binds an approved reusable
    // release to the actual meeting, without regenerating audio or granting consent.
    public async Task BindPresetAsync(Guid companyId,Guid actor,Guid versionId,Guid sessionId,Guid deckId,CancellationToken ct)
    {
        var session=await AuthorizeAsync(companyId,actor,sessionId,ct);
        var deck=await db.SalesPresentationDecks.AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==companyId&&x.Id==deckId&&x.SessionId==sessionId&&x.IsActive,ct)
            ??throw new SalesNarrationException("The active meeting deck is unavailable.");
        if(!await db.SalesPresentationRuns.AnyAsync(x=>x.CompanyId==companyId&&x.Id==deck.PresentationRunId&&x.PresetVersionId==versionId&&x.MeetingSessionId==sessionId&&x.IsActive,ct))
            throw new SalesNarrationException("The presentation does not belong to this preset version.");
        var now=clock.GetUtcNow().UtcDateTime;
        var candidates=await db.SalesNarrationRevisions.AsNoTracking().Where(x=>x.CompanyId==companyId&&x.PresetVersionId==versionId&&
            x.ApprovedUtc!=null&&x.RevokedUtc==null&&x.RetainUntilUtc>now).OrderByDescending(x=>x.CreatedUtc).Take(10).ToListAsync(ct);
        SalesNarrationRevision? source=null;
        foreach(var candidate in candidates)
        {
            try{await EnsureReleasedAsync(candidate,ct);source=candidate;break;}
            catch(SalesNarrationException){}
        }
        if(source is null)return;
        if(await db.SalesNarrationRevisions.AnyAsync(x=>x.CompanyId==companyId&&x.DeckId==deckId&&x.SourcePresetRevisionId==source.Id,ct))return;
        var parts=await db.SalesNarrationSegments.AsNoTracking().Where(x=>x.CompanyId==companyId&&x.RevisionId==source.Id).ToListAsync(ct);
        var slides=await db.SalesPresentationSlides.AsNoTracking().Where(x=>x.CompanyId==companyId&&x.DeckId==deckId).ToListAsync(ct);
        var audience=Audience(session);
        var revision=new SalesNarrationRevision {
            Id=Guid.NewGuid(),CompanyId=companyId,SessionId=sessionId,DeckId=deckId,DeckVersion=deck.Version,
            ProcessingVersion=deck.ProcessingVersion,SourcePresetRevisionId=source.Id,AudienceId=session.CustomerCompanyId,
            AudienceHash=audience,ManifestHash=Hash(source.ManifestHash+"|"+deckId+"|"+audience),
            Language=source.Language,Voice=source.Voice,Model=source.Model,ConfigurationVersion=source.ConfigurationVersion,
            CreatedByUserId=actor,CreatedUtc=now,ApprovedByUserId=source.ApprovedByUserId,ApprovedUtc=source.ApprovedUtc,
            RetainUntilUtc=source.RetainUntilUtc
        };
        db.SalesNarrationRevisions.Add(revision);
        foreach(var part in parts)
        {
            var slide=slides.Single(x=>x.SlideNumber==part.SlideNumber);
            db.SalesNarrationSegments.Add(new(){Id=Guid.NewGuid(),CompanyId=companyId,RevisionId=revision.Id,
                SourceSlideId=slide.Id,SlideNumber=part.SlideNumber,TalkingPoint=part.TalkingPoint,
                SourceHash=Hash(slide.ContentHash+"|"+slide.ExtractedText),SourceText=slide.ExtractedText,
                Script=part.Script,ScriptHash=part.ScriptHash,AssetId=part.AssetId,Reused=true});
        }
        await db.SaveChangesAsync(ct);
    }
}
