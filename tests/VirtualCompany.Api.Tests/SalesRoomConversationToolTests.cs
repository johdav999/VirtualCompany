using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Sales;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Agents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed partial class SalesRoomPlaybackWorkerTests
{
    [Fact]
    public async Task Concurrent_conversation_resume_commands_have_one_durable_effect()
    {
        await using var f = await Fixture.Create(true);
        var turn = await PrepareContinuation(f);
        var file = Path.Combine(Path.GetTempPath(), "vc-conversation-" + Guid.NewGuid().ToString("N") + ".db");
        var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            { DataSource = file, Pooling = false, DefaultTimeout = 5 }.ToString();
        try
        {
            await using (var copy = new Microsoft.Data.Sqlite.SqliteConnection(connectionString))
            {
                await copy.OpenAsync();
                ((Microsoft.Data.Sqlite.SqliteConnection)f.Db.Database.GetDbConnection()).BackupDatabase(copy);
            }
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<SalesRoomConversationToolResult> Execute()
            {
                await start.Task;
                await using var context = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                    .UseSqlite(connectionString).Options, new SalesNarrationTests.NarrationContext(f.Source.Company, f.Source.Actor));
                return await ConversationService(f, context).ContinueConversationAsync(turn, default);
            }
            var first = Task.Run(Execute); var second = Task.Run(Execute); start.SetResult();
            var results = await Task.WhenAll(first, second);
            Assert.Contains(results, x => x.Accepted);
            await using var verify = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseSqlite(connectionString).Options, new SalesNarrationTests.NarrationContext(f.Source.Company, f.Source.Actor));
            Assert.Equal(1, await verify.SalesRoomOperations.CountAsync(x => x.CommandId == turn.CommandId));
            Assert.Equal(1, await verify.SalesRoomAgentSpeech.CountAsync(x => x.CommandId == turn.CommandId));
            Assert.Equal(turn.Binding.TurnGeneration + 1, (await verify.SalesBrowserRooms.SingleAsync()).AgentTurnGeneration);
        }
        finally { File.Delete(file); }
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("company")]
    [InlineData("session")]
    [InlineData("participant")]
    [InlineData("worker")]
    [InlineData("checkpoint")]
    [InlineData("manual")]
    [InlineData("assisted")]
    [InlineData("question")]
    [InlineData("unknown")]
    [InlineData("expired")]
    [InlineData("overlap")]
    [InlineData("new_question")]
    [InlineData("stop")]
    [InlineData("takeover")]
    [InlineData("consent")]
    [InlineData("budget")]
    [InlineData("narration")]
    [InlineData("audio_unready")]
    [InlineData("audience")]
    [InlineData("bridge_unfinished")]
    public async Task Conversation_continuation_requires_current_played_turn_and_authority(string scenario)
    {
        await using var f = await Fixture.Create(true);
        var turn = await PrepareContinuation(f);
        var db = f.Db;
        var room = await db.SalesBrowserRooms.SingleAsync();
        var floor = await db.SalesRoomFloors.SingleAsync();
        var now = f.Source.Clock.Now;
        switch (scenario)
        {
            case "company": turn = turn with { Binding = turn.Binding with { CompanyId = Guid.NewGuid() } }; break;
            case "session": turn = turn with { Binding = turn.Binding with { SessionId = Guid.NewGuid() } }; break;
            case "participant": turn = turn with { Binding = turn.Binding with { ParticipantId = Guid.NewGuid() } }; break;
            case "worker": turn = turn with { Binding = turn.Binding with { OwnerGeneration = turn.Binding.OwnerGeneration + 1 } }; break;
            case "checkpoint": floor.PresentationMoved(f.Participant.Id, floor.PresentationVersion + 1, 2, 0, null, now); break;
            case "manual": case "assisted": floor.SetMode(scenario, floor.PresentationVersion, now, true); break;
            case "question": turn = turn with { Intent = AgentConversationIntent.Question }; break;
            case "unknown": turn = turn with { Intent = AgentConversationIntent.Unknown }; break;
            case "expired": turn = turn with { ExpiresUtc = now }; break;
            case "overlap": floor.HumanStarted(f.Participant.Id, f.Participant.Generation, true,
                room.AgentTurnGeneration, floor.PresentationVersion, floor.SlideNumber, floor.TalkingPointIndex, floor.ResumeMarker, now); break;
            case "new_question":
                db.SalesMeetingQuestions.Add(new(Guid.NewGuid(), f.Source.Company, f.Source.Session, Guid.NewGuid(), 2,
                    f.First.AgentId, "Another question", SalesMeetingSpeakerType.Customer, "Host",
                    SalesMeetingInputSource.BrowserRoom, null, 1, f.Source.Actor, now)); break;
            case "stop": room.StopAgent("host_stopped", "Stopped", now); break;
            case "takeover": floor.TakeOver(f.Participant.Id, floor.Version, Guid.NewGuid(), 0, now,
                room.AgentTurnGeneration, floor.PresentationVersion, floor.SlideNumber, floor.TalkingPointIndex, floor.ResumeMarker, now); break;
            case "consent": (await db.SalesRoomParticipants.SingleAsync()).Consent("ai_processing", false); break;
            case "budget": f.Options.MaximumSpendPerCallUsd = 0; break;
            case "narration": (await db.SalesNarrationRevisions.SingleAsync()).RevokedUtc = now; break;
            case "audio_unready":
                foreach (var asset in await db.SalesNarrationAssets.ToListAsync()) asset.Status = SalesNarrationAsset.NeedsReview;
                break;
            case "audience": db.SalesRoomPresentationAudience.RemoveRange(await db.SalesRoomPresentationAudience.ToListAsync()); break;
            case "bridge_unfinished":
                var extra = new SalesRoomAgentSpeech(Guid.NewGuid(), f.Source.Company, room.Id, f.Source.Session,
                    Guid.NewGuid(), f.First.AgentId, f.Work.Generation, room.AgentTurnGeneration,
                    SalesRoomAgentSpeechKinds.Bridge, f.Source.Actor, now, questionId: (await db.SalesMeetingQuestions.SingleAsync()).Id,
                    responseGeneration: floor.ResponseGeneration);
                db.SalesRoomAgentSpeech.Add(extra); break;
        }
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var result = await ConversationService(f, db).ContinueConversationAsync(turn, default);
        Assert.Equal(scenario == "ok", result.Accepted);
        var queued = await db.SalesRoomAgentSpeech.Where(x => x.CommandId == turn.CommandId).ToListAsync();
        if (scenario != "ok")
        {
            Assert.Empty(queued);
            if (scenario is "audience" or "narration" or "audio_unready")
            {
                db.ChangeTracker.Clear();
                Assert.Equal(SalesRoomFloorStates.Paused, (await db.SalesRoomFloors.SingleAsync()).State);
                Assert.False(string.IsNullOrWhiteSpace((await db.SalesBrowserRooms.SingleAsync()).AgentLastErrorSummary));
            }
            return;
        }
        Assert.Equal(20, Assert.Single(queued).OffsetMilliseconds);
        Assert.Equal(f.First.NarrationSegmentId, queued[0].NarrationSegmentId);
        Assert.Equal(1, (await db.SalesMeetingSessions.SingleAsync()).CurrentTalkingPointIndex);
        Assert.Single(await db.SalesRoomOperations.Where(x => x.CommandId == turn.CommandId && x.Action == "conversation_resume").ToListAsync());

        // A retry after reconnect uses a new scoped service/DbContext, not an in-memory call cache.
        await using var reconnect = f.OtherDb();
        Assert.True((await ConversationService(f, reconnect).ContinueConversationAsync(turn, default)).Accepted);
        Assert.Equal(1, await reconnect.SalesRoomAgentSpeech.CountAsync(x => x.CommandId == turn.CommandId));
        var anotherCall = turn with { HeardTurnId = Guid.NewGuid() };
        Assert.False((await ConversationService(f, reconnect).ContinueConversationAsync(anotherCall, default)).Accepted);
        await f.Play(queued[0].Id); db.ChangeTracker.Clear();
        var resumed = await db.SalesRoomAgentSpeech.SingleAsync(x => x.Id == queued[0].Id);
        Assert.True(resumed.Status == SalesRoomAgentSpeechStates.Spoken, resumed.FailureCode + ": " + resumed.FailureSummary);
        Assert.Equal(2, (await db.SalesRoomFloors.SingleAsync()).SlideNumber);
        var next = await db.SalesRoomAgentSpeech.SingleAsync(x => x.Status == SalesRoomAgentSpeechStates.Queued);
        await f.Play(next.Id);
        Assert.Equal(4, f.Media.Completions); // answer, bridge, resumed slide, next slide
    }

    [Theory]
    [InlineData("manual")]
    [InlineData("assisted")]
    [InlineData("stop")]
    [InlineData("disabled")]
    public async Task Conversation_continuation_rechecks_control_before_playback(string change)
    {
        await using var f = await Fixture.Create(true);
        var turn = await PrepareContinuation(f);
        Assert.True((await ConversationService(f, f.Db).ContinueConversationAsync(turn, default)).Accepted);
        await using (var other = f.OtherDb())
        {
            if (change == "stop") (await other.SalesBrowserRooms.SingleAsync()).StopAgent("host_stopped", "Stopped", f.Source.Clock.Now);
            else if (change == "disabled") f.Options.HybridConversationEnabled = false;
            else
            {
                var floor = await other.SalesRoomFloors.SingleAsync();
                floor.SetMode(change, floor.PresentationVersion, f.Source.Clock.Now, true);
            }
            await other.SaveChangesAsync();
        }
        var item = await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.CommandId == turn.CommandId);
        await f.Play(item.Id);
        Assert.Equal(2, f.Media.Completions); // no narration after mode/stop fencing
    }

    private static async Task<SalesRoomConversationTurn> PrepareContinuation(Fixture f, bool generateBridge = true)
    {
        var db = f.Db; var now = f.Source.Clock.Now;
        f.Options.Enabled = true; f.Options.MaximumSpendPerCallUsd = 10;
        f.Options.MaximumInputTokenCostPerMillionUsd = 1; f.Options.MaximumOutputTokenCostPerMillionUsd = 1;
        f.Options.TranscriptionCostPerMinuteUsd = 1; f.Options.MaximumMonthlySpendPerCompanyUsd = 100;
        f.Options.ProviderRateCheckedUtc = now; f.Options.ProviderRateCardReference = "fixture";
        var room = await db.SalesBrowserRooms.SingleAsync();
        var session = await db.SalesMeetingSessions.SingleAsync();
        session.SetPresentationControlMode("autonomous", session.ConcurrencyVersion, f.Source.Actor, now);
        session.ApplyPresentationCommand(SalesPresentationCommandType.Goto, Guid.NewGuid(), 1,
            session.ConcurrencyVersion, 1, 0, null, f.Source.Actor, now);
        session.ApplyPresentationCommand(SalesPresentationCommandType.Pause, Guid.NewGuid(), 2,
            session.ConcurrencyVersion, 1, 0, "slide:1:talking-point:0", f.Source.Actor, now);
        var floor = await db.SalesRoomFloors.SingleAsync();
        floor.PresentationMoved(f.Participant.Id, session.ConcurrencyVersion, 1, 1, "slide:1:talking-point:1", now);
        floor.PauseAt(20, room.AgentTurnGeneration, now);
        f.First.Fail(SalesRoomAgentSpeechStates.Interrupted, "human_speaking", "Question", now);
        var question = new SalesMeetingQuestion(Guid.NewGuid(), f.Source.Company, f.Source.Session, Guid.NewGuid(), 1,
            f.First.AgentId, "How does onboarding work?", SalesMeetingSpeakerType.Customer, "Host",
            SalesMeetingInputSource.BrowserRoom, null, 1, f.Source.Actor, now);
        question.Complete("Company setup starts onboarding.", .9m, false, Guid.Empty, true, now);
        question.ApproveForStage(f.Source.Actor, question.ConcurrencyVersion, now);
        question.Evidence.Add(new(Guid.NewGuid(), f.Source.Company, question.Id, 0, "Company setup", "fact", .9m,
            "source", "document", "Approved knowledge", now));
        db.SalesMeetingQuestions.Add(question);
        floor.ProposeTurn(f.Participant.Id, f.Participant.Generation, true, false, question.Id, now);
        floor.AuthorizeAgentResponse(f.Participant.Id, room.AgentTurnGeneration, now);
        var answer = new SalesRoomAgentSpeech(Guid.NewGuid(), f.Source.Company, room.Id, f.Source.Session,
            question.ClientQuestionId, f.First.AgentId, f.Work.Generation, room.AgentTurnGeneration,
            SalesRoomAgentSpeechKinds.Answer, f.Source.Actor, now, questionId: question.Id, responseGeneration: floor.ResponseGeneration);
        db.SalesRoomAgentSpeech.Add(answer);
        var deck = await db.SalesPresentationDecks.SingleAsync();
        var audience = new SalesRoomPresentationAudience(f.Source.Company, room.Id, f.Participant.Id, f.Participant.Generation,
            deck.Id, deck.Version, 1, session.LastPresentationSequence, session.ConcurrencyVersion, now, now.AddSeconds(5));
        audience.Render(now); db.SalesRoomPresentationAudience.Add(audience);
        await db.SaveChangesAsync();
        f.Reasoner.Bridge = generateBridge ? new("Shall I continue the presentation?", Guid.NewGuid(), Guid.NewGuid()) : null;
        await f.Play(answer.Id); db.ChangeTracker.Clear();
        var contextSpeech = answer;
        if (generateBridge)
        {
            contextSpeech = await db.SalesRoomAgentSpeech.SingleAsync(x => x.Kind == SalesRoomAgentSpeechKinds.Bridge);
            await f.Play(contextSpeech.Id); db.ChangeTracker.Clear();
        }
        floor = await db.SalesRoomFloors.SingleAsync();
        Assert.Equal(20, floor.ResumeOffsetMilliseconds);
        Assert.Equal(SalesRoomFloorStates.Host, floor.State);
        var authority = await SalesRoomConversationPolicy.LoadAsync(db, f.Options, f.Source.Company,
            room.Id, f.Participant.Id, now, default);
        return new(authority!.Binding, Guid.NewGuid(), contextSpeech.Id, floor.Version, now.AddSeconds(15), AgentConversationIntent.Continue);
    }

    private static SalesRoomAgentService ConversationService(Fixture f, VirtualCompanyDbContext db,
        IEnumerable<ISalesPresentationEventPublisher>? events = null) =>
        new(db, new Sink(), new ConversationHealth(), null!, null!, null!, new Publisher(), Options.Create(f.Options).ToMonitor(),
            Options.Create(new SalesRoomLifecycleOptions { Enabled = true }).ToMonitor(), f.Source.Clock,
            new SalesPresentationRuntimeService(db, f.Source.Clock, [], NullLogger<SalesPresentationRuntimeService>.Instance), events ?? []);

    private sealed class ConversationHealth : IRealtimeAgentSessionGateway
    {
        public Task<RealtimeAgentHealth> GetHealthAsync(CancellationToken ct) =>
            Task.FromResult(new RealtimeAgentHealth(true, true, true, "fixture", "fixture", "available"));
        public Task<RealtimeAgentSessionConnection> CreateSessionAsync(RealtimeAgentSessionCreateRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<RealtimeAgentEvent> NormalizeEventAsync(RealtimeAgentProviderEvent value, CancellationToken ct) => throw new NotSupportedException();
        public Task<RealtimeAgentControlResult> CancelResponseAsync(string session, string? response, CancellationToken ct) => throw new NotSupportedException();
        public Task TerminateSessionAsync(string session, CancellationToken ct) => throw new NotSupportedException();
    }
}

public sealed class SalesRoomConversationToolTests
{
    [Theory]
    [InlineData(SalesRoomConversationTools.Continue, "{}", AgentConversationIntent.Continue, true)]
    [InlineData(SalesRoomConversationTools.Continue, "{}", AgentConversationIntent.Question, false)]
    [InlineData(SalesRoomConversationTools.Continue, "{}", AgentConversationIntent.Unknown, false)]
    [InlineData(SalesRoomConversationTools.Question, "{}", AgentConversationIntent.Question, true)]
    [InlineData(SalesRoomConversationTools.Wait, "{}", AgentConversationIntent.Wait, true)]
    [InlineData(SalesRoomConversationTools.Wait, "{}", AgentConversationIntent.Unknown, true)]
    [InlineData(SalesRoomConversationTools.Wait, "{}", AgentConversationIntent.Acknowledgement, true)]
    [InlineData(SalesRoomConversationTools.Continue, "{}", AgentConversationIntent.Acknowledgement, false)]
    [InlineData(SalesRoomConversationTools.Continue, "{\"companyId\":\"foreign\"}", AgentConversationIntent.Continue, false)]
    [InlineData(SalesRoomConversationTools.Continue, "{\"slide\":2}", AgentConversationIntent.Continue, false)]
    [InlineData(SalesRoomConversationTools.Continue, "[]", AgentConversationIntent.Continue, false)]
    [InlineData("execute_sql", "{}", AgentConversationIntent.Continue, false)]
    public void Tools_have_no_model_supplied_authority_or_position(string name, string args, AgentConversationIntent intent, bool expected) =>
        Assert.Equal(expected, SalesRoomConversationTools.Matches(name, args, intent));
}
