using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed partial class SalesRoomPlaybackWorkerTests
{
    [Theory]
    [InlineData(true, SalesRoomConversationTools.Wait, 10, true)]
    [InlineData(true, SalesRoomConversationTools.Wait, 0, false)]
    [InlineData(false, SalesRoomConversationTools.Wait, 10, false)]
    [InlineData(true, SalesRoomConversationTools.Continue, 10, false)]
    [InlineData(true, SalesRoomConversationTools.Question, 10, false)]
    public void Waiting_keeps_only_the_original_bounded_reply_window(bool accepted, string tool, int seconds, bool keep)
    {
        var now = DateTime.UtcNow;
        var deadline = now.AddSeconds(seconds);
        var result = SalesRoomAgentWorker.ReplyDeadlineAfterTool(accepted, tool, deadline, now);
        Assert.Equal(keep ? deadline : (DateTime?)null, result);
        if (keep)
        {
            Assert.True(SalesRoomAgentWorker.ShouldRouteContextualReply(true, false, result > now, false, 1, "yes"));
            Assert.Null(SalesRoomAgentWorker.ReplyDeadlineAfterTool(true, tool, result, deadline));
        }
    }

    [Theory]
    [InlineData("manual")]
    [InlineData("assisted")]
    [InlineData("consent")]
    public async Task Control_change_during_bridge_playback_cancels_buffered_output(string change)
    {
        await using var f = await Fixture.Create(true);
        await PrepareContinuation(f);
        f.Db.ChangeTracker.Clear();
        var floor = await f.Db.SalesRoomFloors.SingleAsync();
        var previous = await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.Kind == SalesRoomAgentSpeechKinds.Bridge);
        floor.AgentClaim(floor.ResponseGeneration, floor.TurnGeneration, f.Source.Clock.Now);
        var bridge = new SalesRoomAgentSpeech(Guid.NewGuid(), f.Source.Company, f.Work.RoomId, f.Source.Session,
            Guid.NewGuid(), f.First.AgentId, f.Work.Generation, floor.TurnGeneration, SalesRoomAgentSpeechKinds.Bridge,
            f.Source.Actor, f.Source.Clock.Now, questionId: previous.QuestionId, responseGeneration: floor.ResponseGeneration);
        bridge.PrepareBridge(previous.ReleasedText!, previous.EvidenceJson!, f.Source.Clock.Now);
        f.Db.SalesRoomAgentSpeech.Add(bridge); await f.Db.SaveChangesAsync();
        f.Media.OnSend = async () =>
        {
            await using var db = f.OtherDb();
            if (change == "consent") (await db.SalesRoomParticipants.SingleAsync()).Consent("ai_processing", false);
            else { var current = await db.SalesRoomFloors.SingleAsync(); current.SetMode(change, current.PresentationVersion, f.Source.Clock.Now, true); }
            await db.SaveChangesAsync();
        };
        await f.Play(bridge.Id); f.Db.ChangeTracker.Clear();
        Assert.Equal(SalesRoomAgentSpeechStates.Withheld, (await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.Id == bridge.Id)).Status);
        Assert.Equal(2, f.Media.Completions);
        Assert.True(f.Media.Cancellations > 0);
    }

    [Fact]
    public async Task Answer_bridge_exact_resume_then_second_question_uses_new_release_and_never_replays_first_answer()
    {
        await using var f = await Fixture.Create(true);
        var continuation = await PrepareContinuation(f); // production answer/bridge playback + relational checkpoint
        Assert.Equal(2, f.Media.Completions);
        Assert.True((await ConversationService(f, f.Db).ContinueConversationAsync(continuation, default)).Accepted);
        var resumed = await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.CommandId == continuation.CommandId);
        Assert.Equal(20, resumed.OffsetMilliseconds);
        Assert.Equal(f.First.NarrationSegmentId, resumed.NarrationSegmentId);
        // Second confirmed question interrupts the resumed segment using the real cancellation handoff.
        f.Media.OnSend = () => { f.Control.CancelResponse(resumed.ResponseGeneration); return Task.CompletedTask; };
        await f.Play(resumed.Id);
        f.Source.Clock.Now = f.Source.Clock.Now.AddSeconds(1);
        var answering = new GroundedAcceptanceAnswer(f);
        await f.Ask("Alex, what can the finance agent do?", answering);
        f.Db.ChangeTracker.Clear();
        var second = await f.Db.SalesMeetingQuestions.SingleAsync(x => x.Sequence == 2);
        Assert.Equal(SalesMeetingAnswerVisibility.ApprovedForStage, second.Visibility);
        var speech = await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.QuestionId == second.Id);
        await f.Play(speech.Id);
        f.Db.ChangeTracker.Clear();
        var bridge = await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.QuestionId == second.Id && x.Kind == SalesRoomAgentSpeechKinds.Bridge);
        await f.Play(bridge.Id);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(4, f.Media.Completions);
        Assert.Equal(2, await f.Db.SalesRoomAgentSpeech.CountAsync(x => x.Kind == SalesRoomAgentSpeechKinds.Answer && x.Status == SalesRoomAgentSpeechStates.Spoken));
        Assert.Equal(1, answering.Calls);
        Assert.False((await ConversationService(f, f.Db).ContinueConversationAsync(
            continuation with { HeardTurnId = Guid.NewGuid() }, default)).Accepted);
    }

    [Theory]
    [InlineData("manual", false)]
    [InlineData("assisted", false)]
    [InlineData("manual", true)]
    [InlineData("assisted", true)]
    public async Task Nonautonomous_or_downgraded_during_retrieval_never_auto_releases(string mode, bool duringRetrieval)
    {
        await using var f = await Fixture.Create(true);
        await PrepareContinuation(f);
        async Task ChangeMode()
        {
            await using var db = f.OtherDb();
            var floor = await db.SalesRoomFloors.SingleAsync();
            floor.SetMode(mode, floor.PresentationVersion, f.Source.Clock.Now, true);
            await db.SaveChangesAsync();
        }
        if (!duringRetrieval) await ChangeMode();
        var answering = new GroundedAcceptanceAnswer(f) { BeforeReturn = duringRetrieval ? ChangeMode : null };
        await f.Ask("Alex, what can the finance agent do?", answering);
        f.Db.ChangeTracker.Clear();
        var second = await f.Db.SalesMeetingQuestions.SingleAsync(x => x.Sequence == 2);
        Assert.NotEqual(SalesMeetingAnswerVisibility.ApprovedForStage, second.Visibility);
        Assert.False(await f.Db.SalesRoomAgentSpeech.AnyAsync(x => x.QuestionId == second.Id));
        Assert.Equal(2, f.Media.Completions);
    }

    // Retrieval/TTS/media are deterministic seams: this test verifies workflow, not model accuracy
    // or physical audibility. Source retrieval/claim validation have their own focused suites.
    private sealed class GroundedAcceptanceAnswer(Fixture f) : ISalesMeetingQuestionAnsweringService
    {
        public int Calls;
        public Exception? Failure;
        public Func<Task>? BeforeReturn;
        public async Task<SalesMeetingQuestionDto?> AskAsync(Guid company, Guid user, Guid session,
            AskSalesMeetingQuestionRequest request, string? correlation, CancellationToken ct)
        {
            Assert.Equal(f.Source.Company, company); Assert.Equal(f.Source.Session, session);
            Calls++;
            if (Failure is not null) throw Failure;
            await using var db = f.OtherDb();
            var now = f.Source.Clock.Now;
            var q = new SalesMeetingQuestion(Guid.NewGuid(), company, session, request.ClientQuestionId,
                request.Sequence, request.AgentId, request.Question, SalesMeetingSpeakerType.Customer, "Host",
                SalesMeetingInputSource.BrowserRoom, null, 1, user, now);
            q.Complete("The finance agent prepares work for human review.", .9m, false, Guid.NewGuid(), true, now);
            q.Evidence.Add(new(Guid.NewGuid(), company, q.Id, 0, "Prepares work", "fact", .9m,
                "approved-test-source", "document", "Finance", now));
            db.SalesMeetingQuestions.Add(q); await db.SaveChangesAsync(ct);
            if (BeforeReturn is { } hook) await hook();
            return new(q.Id, request.ClientQuestionId, request.Sequence, request.AgentId, request.Question,
                q.AnswerText, "customer", "Host", "browser_room", null, 1, "completed", .9m, false,
                "verified", "private", null, null, null, now, now, null, now, q.ConcurrencyVersion, []);
        }
        public Task<IReadOnlyList<SalesMeetingQuestionDto>> ListQuestionsAsync(Guid c, Guid u, Guid s, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<SalesMeetingStageAnswerDto>> ListStageAnswersAsync(Guid c, Guid u, Guid s, CancellationToken ct) => throw new NotSupportedException();
        public Task<SalesMeetingQuestionDto?> GetQuestionAsync(Guid c, Guid u, Guid s, Guid q, CancellationToken ct) => throw new NotSupportedException();
        public Task<SalesMeetingQuestionDto?> ApproveForStageAsync(Guid c, Guid u, Guid s, Guid q, long v, string? correlation, CancellationToken ct) => throw new NotSupportedException();
    }
}
