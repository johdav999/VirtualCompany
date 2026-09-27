using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Tests;

// Real relational persistence and the production playback worker, with deterministic media.
// The media hook changes room state in a SECOND DbContext while the first is playing.
public sealed class SalesRoomPlaybackWorkerTests
{
    [Fact]
    public async Task Slow_provider_operation_renews_lease_and_loss_cancels_pending_work()
    {
        await using var f = await Fixture.Create();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Source.Clock.Now = f.Source.Clock.Now.AddSeconds(20);
        var pending = f.RenewDuring(async ct =>
        {
            try { await Task.Delay(Timeout.Infinite, ct); return 1; }
            finally { cancelled.TrySetResult(); }
        }, deadline.Token);
        await Task.Delay(1300, deadline.Token);
        await using (var other = f.OtherDb())
        {
            var room = await other.SalesBrowserRooms.SingleAsync();
            Assert.Equal(f.Source.Clock.Now.AddSeconds(30), room.AgentLeaseExpiresUtc);
            room.StopAgent("host_stop", "Stopped by host", f.Source.Clock.Now);
            await other.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        Assert.True(cancelled.Task.IsCompleted);
    }

    [Fact]
    public async Task Status_explains_exhausted_audio_after_legacy_lease_cleanup_without_mutating_history()
    {
        await using var f = await Fixture.Create();
        var room = await f.Db.SalesBrowserRooms.SingleAsync();
        room.RecordAgentAudio(f.Work.LeaseOwnerId, f.Work.Generation, 3_620_580, 3_620_580, 3_620_580, 0, 0, 0);
        room.StopAgent("worker_lease_expired", "Old lease cleanup", f.Source.Clock.Now);
        await f.Db.SaveChangesAsync();
        var version = room.Version;
        var service = new SalesRoomAgentService(f.Db, new Sink(), null!, null!, null!, null!, new Publisher(),
            Options.Create(new SalesRoomAgentOptions { MaximumInputAudioSeconds = 3600 }).ToMonitor(),
            Options.Create(new SalesRoomLifecycleOptions()).ToMonitor(), f.Source.Clock, null!, []);
        var view = await service.GetAsync(f.Source.Company, f.Source.Actor, room.Id, default);
        Assert.Equal("quota_exceeded", view.LastErrorCode);
        Assert.Contains("60.3 minutes", view.LastErrorSummary);
        Assert.Contains("microphone input", view.LastErrorSummary);
        Assert.Equal(version, room.Version);
        Assert.Equal("worker_lease_expired", room.AgentLastErrorCode);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync(Guid.NewGuid(), f.Source.Actor, room.Id, default));
    }

    [Fact]
    public async Task Audio_quota_stop_releases_lease_preserves_usage_and_cannot_be_overwritten_by_reconciliation()
    {
        await using var f = await Fixture.Create();
        var room = await f.Db.SalesBrowserRooms.SingleAsync();
        room.RecordAgentAudio(f.Work.LeaseOwnerId, f.Work.Generation, 3_620_580, 3_620_580, 3_620_580, 0, 0, 0);
        var summary = SalesRoomOperationsPolicy.AudioLimitProblem(room, new SalesRoomAgentOptions { MaximumInputAudioSeconds = 3600 });
        await f.StopForQuota(room, f.Work, summary!);
        f.Db.ChangeTracker.Clear();
        room = await f.Db.SalesBrowserRooms.SingleAsync();
        Assert.Equal(SalesRoomAgentHealthStates.Stopped, room.AgentHealth);
        Assert.Equal("quota_exceeded", room.AgentLastErrorCode);
        Assert.Equal(summary, room.AgentLastErrorSummary);
        Assert.Null(room.AgentLeaseOwnerId);
        Assert.Equal(3_620_580, room.AgentForwardedAudioMilliseconds);
        Assert.False(SalesRoomAgentCoordinator.ShouldStopForReconciliation(room, false, f.Source.Clock.Now.AddMinutes(1)));
        Assert.Equal(SalesRoomAgentSpeechStates.Interrupted, (await f.Db.SalesRoomAgentSpeech.SingleAsync()).Status);
        Assert.Equal(SalesRoomFloorStates.Paused, (await f.Db.SalesRoomFloors.SingleAsync()).State);
    }

    [Fact]
    public async Task Audio_quota_stop_cannot_mutate_a_foreign_company_room()
    {
        await using var f = await Fixture.Create();
        var room = await f.Db.SalesBrowserRooms.SingleAsync();
        await f.StopForQuota(room, new(Guid.NewGuid(), room.Id, f.Work.LeaseOwnerId, f.Work.Generation, "start"), "quota");
        Assert.Equal(SalesRoomAgentHealthStates.Ready, room.AgentHealth);
        Assert.Equal(SalesRoomAgentSpeechStates.Queued, (await f.Db.SalesRoomAgentSpeech.SingleAsync()).Status);
    }

    [Fact]
    public async Task Resume_persists_the_floor_cursor_queues_matching_audio_and_replays_once()
    {
        await using var f = await Fixture.Create();
        var db = f.Db; var now = f.Source.Clock.Now;
        var room = await db.SalesBrowserRooms.SingleAsync();
        var session = await db.SalesMeetingSessions.SingleAsync();
        session.ApplyPresentationCommand(SalesPresentationCommandType.Goto, Guid.NewGuid(), 1,
            session.ConcurrencyVersion, 1, 0, null, f.Source.Actor, now);
        session.ApplyPresentationCommand(SalesPresentationCommandType.Pause, Guid.NewGuid(), 2,
            session.ConcurrencyVersion, 1, 0, "slide:1:talking-point:0", f.Source.Actor, now);
        var floor = await db.SalesRoomFloors.SingleAsync();
        floor.PresentationMoved(f.Participant.Id, session.ConcurrencyVersion, 1, 1, "slide:1:talking-point:1", now);
        floor.PauseAt(400, room.AgentTurnGeneration, now);
        var deck = await db.SalesPresentationDecks.SingleAsync();
        var audience = new SalesRoomPresentationAudience(f.Source.Company, room.Id, f.Participant.Id, f.Participant.Generation,
            deck.Id, deck.Version, 1, session.LastPresentationSequence, session.ConcurrencyVersion, now, now.AddSeconds(5));
        audience.Render(now); db.SalesRoomPresentationAudience.Add(audience);
        await db.SaveChangesAsync();
        var opts = new SalesRoomAgentOptions { Enabled = true, MaximumInputTokenCostPerMillionUsd = 1,
            MaximumOutputTokenCostPerMillionUsd = 1, TranscriptionCostPerMinuteUsd = 1, MaximumSpendPerCallUsd = 10,
            MaximumMonthlySpendPerCompanyUsd = 100, ProviderRateCheckedUtc = now, ProviderRateCardReference = "fixture" };
        var runtime = new SalesPresentationRuntimeService(db, f.Source.Clock, [], NullLogger<SalesPresentationRuntimeService>.Instance);
        var service = new SalesRoomAgentService(db, new Sink(), null!, null!, null!, null!, new Publisher(),
            Options.Create(opts).ToMonitor(), Options.Create(new SalesRoomLifecycleOptions { Enabled = true }).ToMonitor(),
            f.Source.Clock, runtime, []);
        var command = new ResumeSalesRoomAgent(Guid.NewGuid(), room.Version, floor.Version, session.ConcurrencyVersion);
        Assert.True(room.RenewAgentLease(f.Work.LeaseOwnerId, f.Work.Generation, now.AddMinutes(1), now));
        await db.SaveChangesAsync();
        var result = await service.ResumeAsync(f.Source.Company, f.Source.Actor, room.Id, command, default);
        var queued = await db.SalesRoomAgentSpeech.SingleAsync(x => x.CommandId == command.CommandId);
        Assert.Equal(f.First.NarrationSegmentId, queued.NarrationSegmentId);
        Assert.Equal(400, queued.OffsetMilliseconds);
        Assert.Equal(SalesMeetingSessionStatus.Presenting, session.Status);
        Assert.Equal(1, session.CurrentTalkingPointIndex);
        Assert.Equal(session.ConcurrencyVersion, floor.PresentationVersion);
        await service.ResumeAsync(f.Source.Company, f.Source.Actor, room.Id, command, default);
        Assert.Equal(1, await db.SalesRoomAgentSpeech.CountAsync(x => x.CommandId == command.CommandId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ResumeAsync(Guid.NewGuid(),
            f.Source.Actor, room.Id, command, default));
    }

    private sealed class Sink : ISalesRoomAgentCommandSink
    {
        public ValueTask SignalAsync(SalesRoomAgentWorkItem item, CancellationToken ct) => ValueTask.CompletedTask;
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task Released_answer_is_spoken_and_completed_after_generation_clears_the_tracking_scope(bool partial, bool approved, bool changed)
    {
        await using var f = await Fixture.Create();
        var question = new SalesMeetingQuestion(Guid.NewGuid(), f.Source.Company, f.Source.Session, Guid.NewGuid(), 1,
            f.First.AgentId, "What are the customer goals?", SalesMeetingSpeakerType.Customer, "Host",
            SalesMeetingInputSource.BrowserRoom, null, 1, f.Source.Actor, f.Source.Clock.Now);
        question.Complete("The approved goal is to improve service. Timeline requires follow-up.", 0.9m, partial, Guid.Empty, true, f.Source.Clock.Now, partial);
        Assert.False(SalesRoomAgentWorker.CanAutomaticallyRelease(question, "autonomous"));
        if (approved) question.ApproveForStage(f.Source.Actor, question.ConcurrencyVersion, f.Source.Clock.Now);
        question.Evidence.Add(new(Guid.NewGuid(), f.Source.Company, question.Id, 0, "Improve service", "fact", 0.9m,
            "synthetic-slide-2", "slide", "Customer goals", f.Source.Clock.Now));
        f.Db.SalesMeetingQuestions.Add(question);
        Assert.True(SalesRoomAgentWorker.CanAutomaticallyRelease(question, "autonomous"));
        Assert.False(SalesRoomAgentWorker.CanAutomaticallyRelease(question, "assisted"));
        Assert.False(SalesRoomAgentWorker.CanAutomaticallyRelease(question, "manual"));
        Assert.False(SalesRoomAgentWorker.CanAutomaticallyRelease(question, "unknown"));
        var answer = new SalesRoomAgentSpeech(Guid.NewGuid(), f.Source.Company, f.Work.RoomId, f.Source.Session,
            Guid.NewGuid(), f.First.AgentId, f.Work.Generation, f.First.TurnGeneration, SalesRoomAgentSpeechKinds.Answer,
            f.Source.Actor, f.Source.Clock.Now, questionId: question.Id, responseGeneration: f.First.ResponseGeneration);
        f.Db.SalesRoomAgentSpeech.Add(answer); await f.Db.SaveChangesAsync();
        if (changed) f.Source.Speech.BeforeReturn = async () =>
        {
            await using var other = f.OtherDb();
            var current = await other.SalesMeetingQuestions.SingleAsync(x => x.Id == question.Id);
            current.Complete("A different answer with limitations.", .9m, true, Guid.Empty, true, f.Source.Clock.Now, true);
            current.ApproveForStage(f.Source.Actor, current.ConcurrencyVersion, f.Source.Clock.Now);
            await other.SaveChangesAsync();
        };
        await f.Play(answer.Id);
        f.Db.ChangeTracker.Clear();
        var persisted = await f.Db.SalesRoomAgentSpeech.IgnoreQueryFilters().SingleAsync(x => x.Id == answer.Id);
        if (!approved || changed)
        {
            Assert.Equal(SalesRoomAgentSpeechStates.Withheld, persisted.Status);
            Assert.Equal(0, f.Media.Completions);
            return;
        }
        Assert.Equal(SalesRoomAgentSpeechStates.Spoken, persisted.Status);
        Assert.Contains(question.AnswerText!, persisted.ReleasedText);
        Assert.Equal(1, f.Media.Completions);
        Assert.True(persisted.DurationMilliseconds > 0);
    }

    [Fact]
    public async Task Lease_renewal_during_playback_completes_once_and_continues_the_entire_deck()
    {
        await using var f = await Fixture.Create();
        f.Media.OnSend = async () =>
        {
            await using var other = f.OtherDb();
            var room = await other.SalesBrowserRooms.IgnoreQueryFilters().SingleAsync();
            Assert.True(room.RenewAgentLease(f.Work.LeaseOwnerId, f.Work.Generation,
                f.Source.Clock.Now.AddMinutes(1), f.Source.Clock.Now));
            await other.SaveChangesAsync();
        };
        await f.Play(f.First.Id);
        f.Db.ChangeTracker.Clear();
        var first = await f.Db.SalesRoomAgentSpeech.IgnoreQueryFilters().SingleAsync(x => x.Id == f.First.Id);
        Assert.Equal(SalesRoomAgentSpeechStates.Spoken, first.Status);
        var next = await f.Db.SalesRoomAgentSpeech.IgnoreQueryFilters().SingleAsync(x => x.Status == SalesRoomAgentSpeechStates.Queued);
        Assert.Equal(2, (await f.Db.SalesRoomFloors.IgnoreQueryFilters().SingleAsync()).SlideNumber);
        await f.Play(next.Id);
        f.Db.ChangeTracker.Clear();
        Assert.All(await f.Db.SalesRoomAgentSpeech.IgnoreQueryFilters().ToListAsync(), x => Assert.Equal(SalesRoomAgentSpeechStates.Spoken, x.Status));
        Assert.Equal(SalesRoomFloorStates.Host, (await f.Db.SalesRoomFloors.IgnoreQueryFilters().SingleAsync()).State);
        Assert.Equal(2, f.Media.Completions);
        Assert.Equal(2, await f.Db.AuditEvents.CountAsync(x => x.Action == "sales.browser_room.agent_spoke"));
    }

    [Fact]
    public async Task Confirmed_question_accepts_only_its_own_persisted_cancellation_handoff()
    {
        await using var f = await Fixture.Create();
        f.Media.OnSend = () => { f.Control.CancelResponse(f.First.ResponseGeneration); return Task.CompletedTask; };
        await f.Play(f.First.Id);
        f.Db.ChangeTracker.Clear();
        var floor = await f.Db.SalesRoomFloors.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(SalesRoomAgentSpeechStates.Interrupted, (await f.Db.SalesRoomAgentSpeech.IgnoreQueryFilters().SingleAsync()).Status);
        Assert.True(f.Control.AcceptsConfirmedHandoff(f.First.ResponseGeneration, floor));
        floor.HumanStarted(f.Participant.Id, f.Participant.Generation, false, floor.TurnGeneration + 1,
            floor.PresentationVersion, floor.SlideNumber, floor.TalkingPointIndex, floor.ResumeMarker, f.Source.Clock.Now);
        floor.ProposeTurn(f.Participant.Id, f.Participant.Generation, true, false, Guid.NewGuid(), f.Source.Clock.Now);
        Assert.Equal(SalesRoomPendingTurnStates.Authorized, floor.PendingTurnState);
        Assert.False(f.Control.AcceptsConfirmedHandoff(f.First.ResponseGeneration, floor));
        Assert.Equal(0, f.Media.Completions);
    }

    [Fact]
    public async Task External_floor_change_cannot_be_mistaken_for_a_confirmed_question_handoff()
    {
        await using var f = await Fixture.Create();
        f.Media.OnSend = async () =>
        {
            await using var other = f.OtherDb();
            var floor = await other.SalesRoomFloors.IgnoreQueryFilters().SingleAsync();
            floor.PauseAt(0, floor.TurnGeneration, f.Source.Clock.Now);
            await other.SaveChangesAsync();
            f.Control.CancelResponse(f.First.ResponseGeneration);
        };
        await f.Play(f.First.Id);
        f.Db.ChangeTracker.Clear();
        Assert.False(f.Control.AcceptsConfirmedHandoff(f.First.ResponseGeneration,
            await f.Db.SalesRoomFloors.IgnoreQueryFilters().SingleAsync()));
        Assert.Single(await f.Db.SalesRoomAgentSpeech.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Current_media_presence_accepts_speech_despite_stale_database_presence_but_not_revocation()
    {
        await using var f = await Fixture.Create();
        Assert.False(f.Participant.Connected);
        Assert.True(SalesRoomAgentWorker.CanReceiveSpeech(f.Participant, true, f.Source.Clock.Now));
        Assert.False(SalesRoomAgentWorker.CanReceiveSpeech(f.Participant, false, f.Source.Clock.Now));
        Assert.False(SalesRoomAgentWorker.CanReceiveSpeech(f.Participant, true, f.Participant.ExpiresUtc));
        f.Participant.Consent("ai_processing", false);
        Assert.False(SalesRoomAgentWorker.CanReceiveSpeech(f.Participant, true, f.Source.Clock.Now));
    }

    [Fact]
    public async Task Question_can_span_autonomous_talking_points_but_not_a_new_turn_or_takeover()
    {
        await using var f = await Fixture.Create();
        var floor = await f.Db.SalesRoomFloors.IgnoreQueryFilters().SingleAsync();
        var original = floor.ResponseGeneration;
        floor.AgentAdvanced(1, 1, 2, "point:2", floor.TurnGeneration, f.Source.Clock.Now);
        Assert.True(SalesRoomAgentWorker.MatchesSpeechFloor(floor, f.First.TurnGeneration, original));
        Assert.False(SalesRoomAgentWorker.MatchesSpeechFloor(floor, f.First.TurnGeneration + 1, original));
        floor.PauseAt(100, floor.TurnGeneration, f.Source.Clock.Now);
        Assert.False(SalesRoomAgentWorker.MatchesSpeechFloor(floor, f.First.TurnGeneration, original));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public SalesNarrationTests.Fixture Source = null!;
        public VirtualCompanyDbContext Db => Source.Db;
        public SalesRoomAgentWorkItem Work = null!;
        public SalesRoomAgentSpeech First = null!;
        public SalesRoomParticipant Participant = null!;
        public SalesRoomAgentRunControl Control = new();
        public Media Media = new();
        private SalesRoomAgentWorker worker = null!;
        private ServiceProvider scopes = null!;
        public Task<T> RenewDuring<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct) =>
            worker.RunWithLeaseRenewalAsync(Work, operation, ct);
        public VirtualCompanyDbContext OtherDb() => new(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite(Db.Database.GetDbConnection()).Options, new SalesNarrationTests.NarrationContext(Source.Company, Source.Actor));
        public Task Play(Guid id) => worker.SpeakQueuedAsync(id, Media, Work, Control, Control.InterruptionVersion, default);
        public Task StopForQuota(SalesBrowserRoom room, SalesRoomAgentWorkItem work, string summary) =>
            worker.StopForPolicyAsync(room, work, "quota_exceeded", summary);
        public static async Task<Fixture> Create()
        {
            var f = new Fixture { Source = await SalesNarrationTests.Fixture.Create() };
            var source = f.Source;
            var revision = await source.Prepare(); await source.Approve(revision); await source.Generate();
            var agent = await f.Db.Agents.SingleAsync();
            var room = new SalesBrowserRoom(source.Company, source.Session, source.Actor, source.Clock.Now.AddHours(1), source.Clock.Now);
            room.Provisioned("synthetic-room"); room.Start(source.Clock.Now, 60);
            var owner = Guid.NewGuid(); room.StartAgent(agent.Id, source.Actor, owner, source.Clock.Now.AddSeconds(30), source.Clock.Now);
            room.AgentReady(owner, room.AgentGeneration);
            f.Work = new(source.Company, room.Id, owner, room.AgentGeneration, "start");
            f.Participant = new(source.Company, room.Id, "Host", null, room.ExpiresUtc, source.Actor);
            f.Participant.Consent("ai_processing", true);
            f.Db.SalesBrowserRooms.Add(room); f.Db.SalesRoomParticipants.Add(f.Participant);
            var floor = new SalesRoomFloor(source.Company, room.Id, f.Participant.Id, room.AgentTurnGeneration, 1, 1, 1,
                "slide:1:talking-point:1", "autonomous", source.Clock.Now);
            floor.AgentClaim(floor.ResponseGeneration, room.AgentTurnGeneration, source.Clock.Now);
            f.Db.SalesRoomFloors.Add(floor);
            var deck = await f.Db.SalesPresentationDecks.SingleAsync();
            for (var i = 1; i <= 2; i++)
            {
                var row = new SalesRoomPresentationAudience(source.Company, room.Id, f.Participant.Id, f.Participant.Generation,
                    deck.Id, deck.Version, i, i, i, source.Clock.Now, source.Clock.Now.AddSeconds(5));
                row.Render(source.Clock.Now); f.Db.SalesRoomPresentationAudience.Add(row);
            }
            var segment = await f.Db.SalesNarrationSegments.SingleAsync(x => x.RevisionId == revision.Id && x.SlideNumber == 1);
            f.First = new(Guid.NewGuid(), source.Company, room.Id, source.Session, Guid.NewGuid(), agent.Id,
                room.AgentGeneration, room.AgentTurnGeneration, SalesRoomAgentSpeechKinds.Narration, source.Actor, source.Clock.Now,
                revision.Id, segment.Id, responseGeneration: floor.ResponseGeneration);
            f.Db.SalesRoomAgentSpeech.Add(f.First); await f.Db.SaveChangesAsync();
            var context = new SalesNarrationTests.NarrationContext(source.Company, source.Actor);
            f.scopes = new ServiceCollection().AddScoped(_ => f.OtherDb()).BuildServiceProvider();
            f.worker = new(f.Db, f.scopes.GetRequiredService<IServiceScopeFactory>(), new CompanyExecutionScopeFactory(context), null!, null!, null!, source.Service,
                source.Speech, new Conductor(source.Session, deck.Id), null!, new Publisher(),
                Options.Create(new SalesRoomAgentOptions { RenewalSeconds = 1, LeaseSeconds = 30 }).ToMonitor(), Options.Create(new SalesRoomLifecycleOptions()).ToMonitor(),
                null!, source.Clock, NullLogger<SalesRoomAgentWorker>.Instance);
            return f;
        }
        public async ValueTask DisposeAsync() { await scopes.DisposeAsync(); await Source.DisposeAsync(); }
    }
    private sealed class Conductor(Guid session, Guid deck) : ISalesMeetingPresentationConductor
    {
        private int slide = 1;
        public Task<SalesPresentationNarrationPlan?> PrepareAsync(SalesPresentationNarrationRequest request, CancellationToken ct)
        {
            var last = request.ToolName == SalesPresentationToolNames.Next && slide == 2;
            if (request.ToolName == SalesPresentationToolNames.Next && !last) slide++;
            var stage = new SalesPresentationStageSnapshotDto(session, "presenting", slide, slide, deck, 1, slide, 2, "Slide", "Synthetic", null, 1600, 900);
            var privateStage = new SalesPresentationPrivateSnapshotDto(stage, "autonomous", 1, null, null, "Objective", 60, "Next", []);
            return Task.FromResult<SalesPresentationNarrationPlan?>(new("ready", last ? "last_slide_reached" : null, "autonomous",
                new(stage, privateStage), new(true, "rendered"), "Objective", ["Synthetic"], 1, null, "Next", true));
        }
        public CancellationToken GetNarrationCancellation(Guid company, Guid sessionId, long version) => default;
        public void Preempt(Guid company, Guid sessionId, long version) { }
    }
    private sealed class Publisher : ISalesRoomFloorEventPublisher
    {
        public Task RequestPlaybackStopAsync(Guid company, Guid session, SalesRoomPlaybackStopRequest request, CancellationToken ct) => Task.CompletedTask;
        public Task AllowPlaybackAsync(Guid company, Guid session, SalesRoomPlaybackStartRequest request, CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class Media : ISalesRoomMediaConnection
    {
        public Func<Task>? OnSend;
        public int Completions;
        private long generation = 1;
        public async Task<bool> SendAsync(long turn, int rate, ReadOnlyMemory<short> samples, CancellationToken ct)
        {
            if (OnSend is { } hook) { OnSend = null; await hook(); }
            return !ct.IsCancellationRequested && turn == generation;
        }
        public Task<bool> CompleteSpeechAsync(long turn, CancellationToken ct) { Completions++; return Task.FromResult(!ct.IsCancellationRequested); }
        public Task<long> CancelSpeechAsync(CancellationToken ct) => Task.FromResult(++generation);
        public Task RevokeInputAsync(Guid participant, CancellationToken ct) => Task.CompletedTask;
        public bool IsParticipantConnected(Guid participant) => true;
        public SalesRoomMediaStatistics GetStatistics() => new("connected", 0, 0, 0, 0, generation);
        public IAsyncEnumerable<SalesRoomAudioFrame> ReceiveAsync(CancellationToken ct) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
