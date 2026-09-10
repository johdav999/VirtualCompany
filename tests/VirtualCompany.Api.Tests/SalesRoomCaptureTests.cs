using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class SalesRoomCaptureTests
{
    [Fact]
    public async Task Capture_is_atomic_deduplicated_and_preserves_browser_provenance()
    {
        await using var f = await RoomFixture.Create();
        var input = await Input(f);
        var service = new SalesRoomCaptureService(f.Db, f.Clock);
        var id = await service.RetainAsync(input, default);
        Assert.NotNull(id);
        Assert.Null(await service.RetainAsync(input, default));
        var segment = Assert.Single(await f.Db.SalesMeetingTranscriptSegments.IgnoreQueryFilters().ToListAsync());
        var provenance = Assert.Single(await f.Db.SalesRoomAgentTranscripts.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(id, segment.Id); Assert.Equal(segment.Id, provenance.TranscriptSegmentId);
        Assert.Equal(input.ParticipantId, provenance.ParticipantId);
        Assert.Equal(input.ConsentVersion, provenance.ParticipantConsentVersion);
        Assert.Equal(input.AgentGeneration, provenance.AgentGeneration);
        Assert.Equal(SalesMeetingInputSource.BrowserRoom, segment.InputSource);
        Assert.Empty(await f.Db.Set<SalesMeetingTranscriptProvenance>().IgnoreQueryFilters().ToListAsync());
        Assert.Equal(1, (await f.Db.SalesMeetingSessions.IgnoreQueryFilters().SingleAsync()).CaptureVersion);
    }
    [Theory]
    [InlineData("no_retention")]
    [InlineData("withdrawn")]
    [InlineData("regranted")]
    [InlineData("ended")]
    [InlineData("other_company")]
    [InlineData("old_owner")]
    [InlineData("consent_after_start")]
    public async Task Missing_or_changed_authority_discards_text(string reason)
    {
        await using var f = await RoomFixture.Create();
        var input = await Input(f);
        var participant = await f.Db.SalesRoomParticipants.IgnoreQueryFilters().SingleAsync();
        if (reason == "no_retention") input = input with { RetentionAllowedAtSubmission = false };
        if (reason is "withdrawn" or "regranted") participant.Consent("retained_transcript", false);
        if (reason == "regranted") participant.Consent("retained_transcript", true);
        if (reason == "ended") (await f.Db.SalesBrowserRooms.IgnoreQueryFilters().SingleAsync()).End();
        if (reason == "other_company") input = input with { CompanyId = Guid.NewGuid() };
        if (reason == "consent_after_start") input = input with { StartedUtc = f.Clock.Now.AddSeconds(-1) };
        if (reason == "old_owner") input = input with { LeaseOwnerId = Guid.NewGuid() };
        await f.Db.SaveChangesAsync();
        Assert.Null(await new SalesRoomCaptureService(f.Db,f.Clock).RetainAsync(input,default));
        Assert.Empty(await f.Db.SalesRoomAgentTranscripts.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await f.Db.SalesMeetingTranscriptSegments.IgnoreQueryFilters().ToListAsync());
        Assert.DoesNotContain(input.Text, input.ToString());
    }
    [Fact]
    public async Task Private_review_rejects_other_member_and_company()
    {
        await using var f = await RoomFixture.Create(); var input = await Input(f);
        var service = new SalesRoomCaptureService(f.Db,f.Clock);
        await service.RetainAsync(input,default);
        Assert.Equal("partial",(await service.GetReviewAsync(f.Company,f.Actor,f.Room,default)).Coverage);
        await Assert.ThrowsAsync<SalesRoomAccessException>(() => service.GetReviewAsync(f.Company,f.OtherActor,f.Room,default));
        await Assert.ThrowsAsync<SalesRoomAccessException>(() => service.GetReviewAsync(Guid.NewGuid(),f.Actor,f.Room,default));
    }
    [Fact]
    public async Task Expiry_deletes_only_browser_transcripts_and_repeats_safely()
    {
        await using var f = await RoomFixture.Create(); var input = await Input(f);
        var service = new SalesRoomCaptureService(f.Db,f.Clock); await service.RetainAsync(input,default);
        var graph = new SalesMeetingTranscriptSegment(Guid.NewGuid(),f.Company,f.Meeting,Guid.NewGuid(),2,
            SalesMeetingSpeakerType.Customer,"Teams speaker",SalesMeetingInputSource.TranscriptAdapter,"Preserved Teams evidence",
            f.Clock.Now,null,null,SalesMeetingReviewState.Reviewed,f.Actor,Guid.NewGuid(),f.Clock.Now);
        f.Db.SalesMeetingTranscriptSegments.Add(graph);
        var agent = await f.Db.Agents.IgnoreQueryFilters().SingleAsync();
        var question = new SalesMeetingQuestion(Guid.NewGuid(),f.Company,f.Meeting,Guid.NewGuid(),1,agent.Id,
            "Retained browser question",SalesMeetingSpeakerType.Customer,"Casey",SalesMeetingInputSource.BrowserRoom,null,1,f.Actor,f.Clock.Now);
        f.Db.SalesMeetingQuestions.Add(question);
        var speech = new SalesRoomAgentSpeech(Guid.NewGuid(),f.Company,f.Room,f.Meeting,Guid.NewGuid(),agent.Id,1,1,
            SalesRoomAgentSpeechKinds.Answer,f.Actor,f.Clock.Now,questionId:question.Id);
        speech.Complete("Retained answer","retained evidence",null,100,f.Clock.Now);
        f.Db.SalesRoomAgentSpeech.Add(speech);
        var run = new AgentOrchestrationRun(f.Company,agent.Id,f.Actor,"test","v1","v1","v1","browser-closing:"+f.Meeting.ToString("N"));
        run.Complete("completed","test","test",1m,"Retained summary","{}","[]",1,1,1);
        f.Db.AgentOrchestrationRuns.Add(run);
        var room = await f.Db.SalesBrowserRooms.IgnoreQueryFilters().SingleAsync(); room.Ended();
        await f.Db.SaveChangesAsync();
        Assert.Equal(0,await service.PurgeExpiredAsync(default));
        f.Clock.Now=f.Clock.Now.AddDays(366);
        Assert.Equal(1,await service.PurgeExpiredAsync(default));
        Assert.Equal(0,await service.PurgeExpiredAsync(default));
        Assert.Equal(graph.Id,(await f.Db.SalesMeetingTranscriptSegments.IgnoreQueryFilters().SingleAsync()).Id);
        Assert.Equal("expired",(await service.GetReviewAsync(f.Company,f.Actor,f.Room,default)).Coverage);
        Assert.Equal("[Expired meeting evidence]", (await f.Db.SalesMeetingQuestions.IgnoreQueryFilters().SingleAsync()).QuestionText);
        Assert.Null((await f.Db.SalesRoomAgentSpeech.IgnoreQueryFilters().SingleAsync()).ReleasedText);
        Assert.Null((await f.Db.SalesRoomAgentSpeech.IgnoreQueryFilters().SingleAsync()).EvidenceJson);
        Assert.Null((await f.Db.AgentOrchestrationRuns.IgnoreQueryFilters().SingleAsync()).ResultJson);
        Assert.Null((await f.Db.AgentOrchestrationRuns.IgnoreQueryFilters().SingleAsync()).Summary);
    }
    [Fact]
    public async Task Ending_with_two_command_ids_schedules_one_end_and_one_closing_transition()
    {
        await using var f = await RoomFixture.Create(); await f.Ready();
        var version = (await f.View()).Version;
        await f.Service.EndAsync(f.Company, f.Actor, f.Room, new(Guid.NewGuid(), version), default);
        var session = await f.Db.SalesMeetingSessions.IgnoreQueryFilters().SingleAsync();
        var closingVersion = session.ConcurrencyVersion;
        await f.Service.EndAsync(f.Company, f.Actor, f.Room, new(Guid.NewGuid(), version), default);
        Assert.Equal(closingVersion, session.ConcurrencyVersion);
        Assert.Equal(SalesMeetingSessionStatus.Closing, session.Status);
        Assert.Equal(1, await f.Db.SalesRoomOperations.IgnoreQueryFilters().CountAsync(x => x.Action == "end"));
        await f.Dispatch("end");
        await f.Service.EndAsync(f.Company, f.Actor, f.Room, new(Guid.NewGuid(), version), default);
        Assert.Equal(closingVersion, session.ConcurrencyVersion);
    }
    [Fact]
    public async Task Longer_minutes_retention_keeps_required_browser_evidence()
    {
        await using var f = await RoomFixture.Create(); var input = await Input(f);
        var service = new SalesRoomCaptureService(f.Db,f.Clock); await service.RetainAsync(input,default);
        var agent = await f.Db.Agents.IgnoreQueryFilters().SingleAsync();
        f.Db.SalesMeetingMinutes.Add(new(Guid.NewGuid(),f.Company,f.Meeting,Guid.NewGuid(),null,1,1,f.Clock.Now,
            agent.Id,null,"v1","sales-browser-room-closing-v1",f.Clock.Now.AddDays(500),f.Actor,f.Clock.Now));
        (await f.Db.SalesBrowserRooms.IgnoreQueryFilters().SingleAsync()).Ended();
        await f.Db.SaveChangesAsync(); f.Clock.Now=f.Clock.Now.AddDays(366);
        Assert.Equal(0,await service.PurgeExpiredAsync(default));
        Assert.Single(await f.Db.SalesRoomAgentTranscripts.IgnoreQueryFilters().ToListAsync());
    }
    private static async Task<RetainSalesRoomTranscript> Input(RoomFixture f)
    {
        await f.Ready();
        var agent = new Agent(Guid.NewGuid(), f.Company, "alex", "Alex", "Sales representative", "Sales", null, AgentSeniority.Senior, AgentStatus.Active);
        f.Db.Agents.Add(agent);
        var room=await f.Db.SalesBrowserRooms.IgnoreQueryFilters().SingleAsync();
        room.Start(f.Clock.Now,60);
        var owner=Guid.NewGuid(); room.StartAgent(agent.Id,f.Actor,owner,f.Clock.Now.AddSeconds(30),f.Clock.Now);
        var participant=await f.Db.SalesRoomParticipants.IgnoreQueryFilters().SingleAsync();
        participant.Consent("ai_processing",true);participant.Consent("retained_transcript",true);
        f.Db.SalesRoomConsents.Add(new SalesRoomConsent(participant,"retained_transcript",true,"notice-v1",f.Clock.Now));
        await f.Db.SaveChangesAsync();
        return new(f.Company,f.Room,participant.Id,participant.Version,true,room.AgentGeneration,owner,
            "microphone-track",1,f.Clock.Now,f.Clock.Now.AddSeconds(2),false,"Please send the implementation plan.");
    }
}
