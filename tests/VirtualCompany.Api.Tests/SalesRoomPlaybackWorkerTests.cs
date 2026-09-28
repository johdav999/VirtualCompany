using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Agents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Tests;

// Real relational persistence and the production playback worker, with deterministic media.
// The media hook changes room state in a SECOND DbContext while the first is playing.
public sealed partial class SalesRoomPlaybackWorkerTests
{
    [Fact]
    public async Task Obsolete_narration_failure_cannot_pause_the_new_autonomous_answer()
    {
        await using var f = await Fixture.Create(true);
        var room = await f.Db.SalesBrowserRooms.SingleAsync();
        room.PreemptAgent(f.Work.LeaseOwnerId, f.Work.Generation, "Customer asked a question");
        var floor = await f.Db.SalesRoomFloors.SingleAsync();
        floor.PauseAt(250, room.AgentTurnGeneration, f.Source.Clock.Now);
        await f.Db.SaveChangesAsync();
        await f.Ask("How does onboarding work?", new GroundedAcceptanceAnswer(f));
        var answer = await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.Kind == SalesRoomAgentSpeechKinds.Answer);
        var answerTurn = answer.TurnGeneration;
        var response = answer.ResponseGeneration;

        // Reproduce the old playback task failing after the new answer was queued.
        await f.Play(f.First.Id);
        f.Db.ChangeTracker.Clear();
        room = await f.Db.SalesBrowserRooms.SingleAsync();
        floor = await f.Db.SalesRoomFloors.SingleAsync();
        Assert.Equal(SalesRoomAgentHealthStates.Ready, room.AgentHealth);
        Assert.Equal(answerTurn, room.AgentTurnGeneration);
        Assert.Equal(response, floor.ResponseGeneration);
        Assert.Equal(SalesRoomFloorStates.Agent, floor.State);
        Assert.Equal(0, f.Media.Cancellations);
        Assert.Equal(SalesRoomAgentSpeechStates.Withheld,
            (await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.Id == f.First.Id)).Status);
        await f.Play(answer.Id);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(SalesRoomAgentSpeechStates.Spoken,
            (await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.Id == answer.Id)).Status);
        Assert.False(f.Media.Disposed);
    }

    [Fact]
    public async Task Obsolete_response_in_same_turn_cannot_pause_current_narration_floor()
    {
        await using var f = await Fixture.Create(true);
        var floor = await f.Db.SalesRoomFloors.SingleAsync();
        floor.AgentAdvanced(floor.PresentationVersion, floor.SlideNumber, 2, "point:2",
            floor.TurnGeneration, f.Source.Clock.Now);
        var version = floor.Version;
        await f.Db.SaveChangesAsync();
        await f.Play(f.First.Id);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(SalesRoomAgentHealthStates.Ready, (await f.Db.SalesBrowserRooms.SingleAsync()).AgentHealth);
        floor = await f.Db.SalesRoomFloors.SingleAsync();
        Assert.Equal(SalesRoomFloorStates.Agent, floor.State);
        Assert.Equal(version, floor.Version);
        Assert.Equal(0, f.Media.Cancellations);
    }

    [Fact]
    public async Task Old_playback_failure_does_not_cancel_the_new_turn_shared_track()
    {
        await using var f = await Fixture.Create(true);
        long turn = 0;
        var cancellationsAtHandoff = 0;
        f.Media.OnSend = async () =>
        {
            cancellationsAtHandoff = f.Media.Cancellations;
            await using var other = f.OtherDb();
            var room = await other.SalesBrowserRooms.SingleAsync();
            room.ResumeAgent(f.Work.LeaseOwnerId, f.Work.Generation);
            turn = room.AgentTurnGeneration;
            var floor = await other.SalesRoomFloors.SingleAsync();
            floor.AgentClaim(floor.ResponseGeneration, turn, f.Source.Clock.Now);
            await other.SaveChangesAsync();
        };
        await f.Play(f.First.Id);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(cancellationsAtHandoff, f.Media.Cancellations);
        var room = await f.Db.SalesBrowserRooms.SingleAsync();
        Assert.Equal(turn, room.AgentTurnGeneration);
        Assert.Equal(SalesRoomAgentHealthStates.Ready, room.AgentHealth);
        Assert.Equal(SalesRoomFloorStates.Agent, (await f.Db.SalesRoomFloors.SingleAsync()).State);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rejected_question_keeps_agent_lease_and_media_and_accepts_next_question(bool conflict)
    {
        await using var f = await Fixture.Create(true);
        f.Reasoner.Intent = AgentConversationIntent.Question;
        var answering = new GroundedAcceptanceAnswer(f) { Failure = conflict
            ? new SalesMeetingCaptureConflictException("capture_changed", "Capture changed")
            : new SalesMeetingCaptureValidationException(new Dictionary<string, string[]>()) };
        await f.Ask("How does onboarding work?", answering);
        f.Db.ChangeTracker.Clear();
        var room = await f.Db.SalesBrowserRooms.SingleAsync();
        Assert.Equal(SalesRoomAgentHealthStates.Ready, room.AgentHealth);
        Assert.Equal("healthy", room.AgentVoiceHealth);
        Assert.True(room.IsAgentOwner(f.Work.LeaseOwnerId, f.Work.Generation, f.Source.Clock.Now));
        Assert.Null(room.AgentStoppedUtc);
        Assert.False(f.Media.Disposed);
        Assert.Equal("question_rejected", room.AgentLastErrorCode);
        Assert.Equal(SalesRoomFloorStates.Paused, (await f.Db.SalesRoomFloors.SingleAsync()).State);
        answering.Failure = null;
        await f.Ask("How does onboarding work?", answering);
        Assert.Equal(2, answering.Calls);
        Assert.Contains(await f.Db.SalesRoomAgentSpeech.ToListAsync(), x =>
            x.Kind == SalesRoomAgentSpeechKinds.Answer && x.Status == SalesRoomAgentSpeechStates.Queued);
    }

    [Fact]
    public async Task Question_cancellation_is_not_converted_to_a_recoverable_turn()
    {
        await using var f = await Fixture.Create(true);
        f.Reasoner.Intent = AgentConversationIntent.Question;
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Ask("How does onboarding work?",
            new GroundedAcceptanceAnswer(f) { Failure = new OperationCanceledException() }));
    }
    [Fact]
    public async Task Autonomous_no_evidence_uses_only_exact_safe_limitation_lane()
    {
        await using var f = await Fixture.Create(true);
        var question = new SalesMeetingQuestion(Guid.NewGuid(), f.Source.Company, f.Source.Session, Guid.NewGuid(), 1,
            f.First.AgentId, "What is the unverified timeline?", SalesMeetingSpeakerType.Customer, "Host",
            SalesMeetingInputSource.BrowserRoom, null, 1, f.Source.Actor, f.Source.Clock.Now);
        question.Complete(SalesMeetingQuestion.SafeNoEvidenceLimitation, 0m, true, Guid.Empty, false,
            f.Source.Clock.Now);
        Assert.True(question.IsSafeNoEvidenceLimitation);
        Assert.True(SalesRoomAgentWorker.CanAutomaticallyRelease(question, "autonomous"));
        Assert.False(SalesRoomAgentWorker.CanAutomaticallyRelease(question, "assisted"));
        question.ApproveSafeLimitationForStage(f.Source.Actor, question.ConcurrencyVersion, f.Source.Clock.Now);
        f.Db.SalesMeetingQuestions.Add(question);
        var item = new SalesRoomAgentSpeech(Guid.NewGuid(), f.Source.Company, f.Work.RoomId, f.Source.Session,
            question.ClientQuestionId, f.First.AgentId, f.Work.Generation, f.First.TurnGeneration,
            SalesRoomAgentSpeechKinds.Limitation, f.Source.Actor, f.Source.Clock.Now,
            questionId: question.Id, responseGeneration: f.First.ResponseGeneration);
        f.Db.SalesRoomAgentSpeech.Add(item); await f.Db.SaveChangesAsync();
        await f.Play(item.Id); f.Db.ChangeTracker.Clear();
        var spoken = await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.Id == item.Id);
        Assert.Equal(SalesRoomAgentSpeechStates.Spoken, spoken.Status);
        Assert.Equal(SalesMeetingQuestion.SafeNoEvidenceLimitation, spoken.ReleasedText);
        Assert.Empty(await f.Db.SalesRoomAgentSpeech.Where(x => x.Kind == SalesRoomAgentSpeechKinds.Bridge).ToListAsync());
    }

    [Fact]
    public void An_unverified_model_claim_or_provider_failure_cannot_impersonate_safe_limitation()
    {
        var now = DateTime.UtcNow;
        var question = new SalesMeetingQuestion(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            Guid.NewGuid(), "Question", SalesMeetingSpeakerType.Customer, "Host", SalesMeetingInputSource.BrowserRoom,
            null, 1, Guid.NewGuid(), now);
        question.Complete("We guarantee the result.", 0m, true, Guid.Empty, false, now);
        Assert.False(question.IsSafeNoEvidenceLimitation);
        Assert.False(SalesRoomAgentWorker.CanAutomaticallyRelease(question, "autonomous"));
        Assert.Throws<InvalidOperationException>(() => question.ApproveSafeLimitationForStage(
            Guid.NewGuid(), question.ConcurrencyVersion, now));
        question.Fail("provider_unavailable", "Provider failed", false, now);
        Assert.False(question.IsSafeNoEvidenceLimitation);
    }

    [Theory]
    [InlineData("played")]
    [InlineData("downgraded")]
    [InlineData("answer_changed")]
    [InlineData("consent_revoked")]
    [InlineData("flag_disabled")]
    [InlineData("downgrade_during_proposal")]
    public async Task Autonomous_grounded_answer_queues_separate_validated_bridge_and_plays_it(
        string scenario)
    {
        await using var f = await Fixture.Create(true);
        var question = new SalesMeetingQuestion(Guid.NewGuid(), f.Source.Company, f.Source.Session, Guid.NewGuid(), 1,
            f.First.AgentId, "How does onboarding work?", SalesMeetingSpeakerType.Customer, "Host",
            SalesMeetingInputSource.BrowserRoom, null, 1, f.Source.Actor, f.Source.Clock.Now);
        question.Complete("Company setup starts onboarding. Timing needs confirmation.", .9m, true,
            Guid.Empty, true, f.Source.Clock.Now, true);
        question.ApproveForStage(f.Source.Actor, question.ConcurrencyVersion, f.Source.Clock.Now);
        question.Evidence.Add(new(Guid.NewGuid(), f.Source.Company, question.Id, 0, "Company setup", "fact", .9m,
            "source", "document", "Approved knowledge", f.Source.Clock.Now));
        f.Db.SalesMeetingQuestions.Add(question);
        var floor = await f.Db.SalesRoomFloors.SingleAsync();
        floor.ProposeTurn(f.Participant.Id, f.Participant.Generation, true, false, question.Id, f.Source.Clock.Now);
        floor.AuthorizeAgentResponse(f.Participant.Id, f.First.TurnGeneration, f.Source.Clock.Now);
        var answer = new SalesRoomAgentSpeech(Guid.NewGuid(), f.Source.Company, f.Work.RoomId, f.Source.Session,
            question.ClientQuestionId, f.First.AgentId, f.Work.Generation, f.First.TurnGeneration,
            SalesRoomAgentSpeechKinds.Answer, f.Source.Actor, f.Source.Clock.Now,
            questionId: question.Id, responseGeneration: floor.ResponseGeneration);
        f.Db.SalesRoomAgentSpeech.Add(answer); await f.Db.SaveChangesAsync();
        f.Reasoner.Bridge = new("Would you like to explore the remaining details?", Guid.NewGuid(), Guid.NewGuid());
        if (scenario == "downgrade_during_proposal")
            f.Reasoner.BeforeReturn = async () =>
            {
                await using var other = f.OtherDb();
                var currentFloor = await other.SalesRoomFloors.SingleAsync();
                currentFloor.SetMode("manual", currentFloor.PresentationVersion + 1, f.Source.Clock.Now, true);
                await other.SaveChangesAsync();
            };

        await f.Play(answer.Id); f.Db.ChangeTracker.Clear();
        var savedAnswer = await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.Id == answer.Id);
        if (scenario == "downgrade_during_proposal")
        {
            Assert.Equal(SalesRoomAgentSpeechStates.Spoken, savedAnswer.Status);
            Assert.Empty(await f.Db.SalesRoomAgentSpeech.Where(x => x.Kind == SalesRoomAgentSpeechKinds.Bridge).ToListAsync());
            Assert.Equal(1, f.Media.Completions);
            return;
        }
        var bridge = await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.Kind == SalesRoomAgentSpeechKinds.Bridge);
        Assert.Equal(SalesRoomAgentSpeechStates.Spoken, savedAnswer.Status);
        Assert.Equal(SalesRoomAgentSpeechStates.Queued, bridge.Status);
        Assert.Equal(question.Id, bridge.QuestionId);
        Assert.Equal(SalesRoomFloorStates.Agent, (await f.Db.SalesRoomFloors.SingleAsync()).State);
        Assert.DoesNotContain("Would you like", savedAnswer.ReleasedText);

        if (scenario == "flag_disabled") f.Options.HybridConversationEnabled = false;
        if (scenario is not ("played" or "flag_disabled"))
            f.Source.Speech.BeforeReturn = async () =>
            {
                await using var other = f.OtherDb();
                if (scenario == "downgraded")
                {
                    var currentFloor = await other.SalesRoomFloors.SingleAsync();
                    currentFloor.SetMode("manual", currentFloor.PresentationVersion + 1, f.Source.Clock.Now, true);
                }
                else if (scenario == "answer_changed")
                {
                    var currentQuestion = await other.SalesMeetingQuestions.SingleAsync(x => x.Id == question.Id);
                    currentQuestion.Complete("A different approved answer.", .9m, false,
                        Guid.Empty, true, f.Source.Clock.Now);
                    currentQuestion.ApproveForStage(f.Source.Actor, currentQuestion.ConcurrencyVersion, f.Source.Clock.Now);
                }
                else
                {
                    var currentParticipant = await other.SalesRoomParticipants.SingleAsync(x => x.Id == f.Participant.Id);
                    currentParticipant.Consent("ai_processing", false);
                }
                await other.SaveChangesAsync();
            };
        await f.Play(bridge.Id); f.Db.ChangeTracker.Clear();
        var spokenBridge = await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.Id == bridge.Id);
        Assert.Equal(scenario == "played" ? SalesRoomAgentSpeechStates.Spoken : SalesRoomAgentSpeechStates.Withheld,
            spokenBridge.Status);
        if (scenario == "flag_disabled") Assert.Equal("conversation_disabled", spokenBridge.FailureCode);
        if (scenario == "played") Assert.Equal(f.Reasoner.Bridge.Text, spokenBridge.ReleasedText);
        if (scenario is "played" or "downgraded")
            Assert.Equal(SalesRoomFloorStates.Host, (await f.Db.SalesRoomFloors.SingleAsync()).State);
        Assert.Equal(scenario == "played" ? 2 : 1, f.Media.Completions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hybrid_policy_fences_speech_when_mode_changes_during_generation(bool enabled)
    {
        await using var f = await Fixture.Create(enabled);
        var question = new SalesMeetingQuestion(Guid.NewGuid(), f.Source.Company, f.Source.Session, Guid.NewGuid(), 1,
            f.First.AgentId, "How does onboarding work?", SalesMeetingSpeakerType.Customer, "Host",
            SalesMeetingInputSource.BrowserRoom, null, 1, f.Source.Actor, f.Source.Clock.Now);
        question.Complete("Company setup starts onboarding. Timing needs follow-up.", .9m, true, Guid.Empty, true, f.Source.Clock.Now, true);
        question.ApproveForStage(f.Source.Actor, question.ConcurrencyVersion, f.Source.Clock.Now);
        question.Evidence.Add(new(Guid.NewGuid(), f.Source.Company, question.Id, 0, "Company setup", "fact", .9m,
            "source", "document", "Approved knowledge", f.Source.Clock.Now));
        f.Db.SalesMeetingQuestions.Add(question);
        var answer = new SalesRoomAgentSpeech(Guid.NewGuid(), f.Source.Company, f.Work.RoomId, f.Source.Session,
            Guid.NewGuid(), f.First.AgentId, f.Work.Generation, f.First.TurnGeneration, SalesRoomAgentSpeechKinds.Answer,
            f.Source.Actor, f.Source.Clock.Now, questionId: question.Id, responseGeneration: f.First.ResponseGeneration);
        f.Db.SalesRoomAgentSpeech.Add(answer); await f.Db.SaveChangesAsync();
        f.Source.Speech.BeforeReturn = async () =>
        {
            await using var other = f.OtherDb();
            var floor = await other.SalesRoomFloors.SingleAsync();
            floor.SetMode("manual", 2, f.Source.Clock.Now, enabled);
            await other.SaveChangesAsync();
        };
        await f.Play(answer.Id); f.Db.ChangeTracker.Clear();
        var saved = await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.Id == answer.Id);
        Assert.Equal(enabled ? SalesRoomAgentSpeechStates.Withheld : SalesRoomAgentSpeechStates.Spoken, saved.Status);
        Assert.Equal(enabled ? 0 : 1, f.Media.Completions);
    }

    [Fact]
    public async Task Hybrid_input_policy_rechecks_database_authority_and_rejects_foreign_company_and_takeover()
    {
        await using var f = await Fixture.Create(true);
        var turn = await SalesRoomConversationPolicy.BeginInputAsync(f.Db, f.Options, f.Source.Company, f.Work.RoomId,
            f.Participant.Id, Guid.NewGuid(), f.Source.Clock.Now, default);
        Assert.NotNull(turn);
        Assert.True((await SalesRoomConversationPolicy.RecheckAsync(f.Db, f.Options, turn!, f.Source.Clock.Now, default)).Allowed);
        await Assert.ThrowsAsync<SalesRoomAgentException>(() => SalesRoomConversationPolicy.BeginInputAsync(f.Db, f.Options,
            Guid.NewGuid(), f.Work.RoomId, f.Participant.Id, Guid.NewGuid(), f.Source.Clock.Now, default));
        await using (var other = f.OtherDb())
        {
            var room = await other.SalesBrowserRooms.SingleAsync();
            room.TakeOverAgent("Host took over"); await other.SaveChangesAsync();
        }
        Assert.False((await SalesRoomConversationPolicy.RecheckAsync(f.Db, f.Options, turn!, f.Source.Clock.Now, default)).Allowed);
        f.Options.HybridConversationEnabled = false;
        Assert.Null(await SalesRoomConversationPolicy.BeginInputAsync(f.Db, f.Options, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), f.Source.Clock.Now, default));
    }

    [Fact]
    public async Task Disabling_conversation_stops_lease_and_queued_speech_without_ending_human_room()
    {
        await using var f = await Fixture.Create(true);
        f.Options.HybridConversationEnabled = false;
        var room = await f.Db.SalesBrowserRooms.SingleAsync();
        await f.StopForRollback(room);
        f.Db.ChangeTracker.Clear();
        var saved = await f.Db.SalesBrowserRooms.SingleAsync();
        var speech = await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.Id == f.First.Id);
        var floor = await f.Db.SalesRoomFloors.SingleAsync();
        Assert.Equal(SalesBrowserRoomStates.Live, saved.State);
        Assert.Equal(SalesRoomAgentHealthStates.Stopped, saved.AgentHealth);
        Assert.Equal("conversation_disabled", saved.AgentLastErrorCode);
        Assert.Equal(SalesRoomAgentSpeechStates.Interrupted, speech.Status);
        Assert.Equal(SalesRoomFloorStates.Paused, floor.State);
        Assert.Equal(0, saved.AgentForwardedAudioMilliseconds);
    }

    [Fact]
    public async Task Hybrid_policy_rejects_removed_participant_for_existing_and_new_turns()
    {
        await using var f = await Fixture.Create(true);
        var turn = await SalesRoomConversationPolicy.BeginInputAsync(f.Db, f.Options, f.Source.Company, f.Work.RoomId,
            f.Participant.Id, Guid.NewGuid(), f.Source.Clock.Now, default);
        await using (var other = f.OtherDb())
        {
            var participant = await other.SalesRoomParticipants.SingleAsync();
            participant.Revoke(); await other.SaveChangesAsync();
        }
        Assert.False((await SalesRoomConversationPolicy.RecheckAsync(f.Db, f.Options, turn!, f.Source.Clock.Now, default)).Allowed);
        await Assert.ThrowsAsync<SalesRoomAgentException>(() => SalesRoomConversationPolicy.BeginInputAsync(f.Db, f.Options,
            f.Source.Company, f.Work.RoomId, f.Participant.Id, Guid.NewGuid(), f.Source.Clock.Now, default));
    }

    [Fact]
    public async Task Hybrid_playback_rechecks_mode_while_audio_is_streaming()
    {
        await using var f = await Fixture.Create(true);
        var cancellationsBeforeModeChange = 0;
        f.Media.OnSend = async () =>
        {
            cancellationsBeforeModeChange = f.Media.Cancellations;
            await using var other = f.OtherDb();
            var floor = await other.SalesRoomFloors.SingleAsync();
            floor.SetMode("manual", 2, f.Source.Clock.Now, true); await other.SaveChangesAsync();
        };
        await f.Play(f.First.Id); f.Db.ChangeTracker.Clear();
        Assert.Equal(SalesRoomAgentSpeechStates.Withheld, (await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.Id == f.First.Id)).Status);
        Assert.Equal(0, f.Media.Completions);
        Assert.Equal(cancellationsBeforeModeChange + 1, f.Media.Cancellations);
    }

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
    [InlineData(false, true, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(false, true, false, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, true, true)]
    public async Task Released_answer_is_spoken_and_completed_after_generation_clears_the_tracking_scope(bool partial, bool approved, bool changed, bool hybrid)
    {
        await using var f = await Fixture.Create(hybrid);
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
        public SalesRoomAgentOptions Options = new() { RenewalSeconds = 1, LeaseSeconds = 30 };
        public ConversationReasoner Reasoner = new();
        private SalesRoomAgentWorker worker = null!;
        private ServiceProvider scopes = null!;
        public Task<T> RenewDuring<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct) =>
            worker.RunWithLeaseRenewalAsync(Work, operation, ct);
        public VirtualCompanyDbContext OtherDb() => new(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite(Db.Database.GetDbConnection()).Options, new SalesNarrationTests.NarrationContext(Source.Company, Source.Actor));
        public Task Play(Guid id) => worker.SpeakQueuedAsync(id, Media, Work, Control, Control.InterruptionVersion, default);
        public Task<SalesRoomConversationToolResult> Acknowledge(SalesRoomConversationTurn turn, string text) =>
            worker.AcknowledgeConversationAsync(turn, text, Work, default);
        public async Task Ask(string text, ISalesMeetingQuestionAnsweringService answering)
        {
            Db.ChangeTracker.Clear();
            var room = await Db.SalesBrowserRooms.SingleAsync();
            await worker.HandleTranscriptAsync(room, await Db.SalesRoomParticipants.SingleAsync(),
                new(Participant.Id, "test-mic", 1, Source.Clock.GetUtcNow(), Source.Clock.GetUtcNow(), false, 500, 500, ReadOnlyMemory<short>.Empty),
                text, true, true, Media, Work, new Publisher(), answering, default);
        }
        public Task StopForQuota(SalesBrowserRoom room, SalesRoomAgentWorkItem work, string summary) =>
            worker.StopForPolicyAsync(room, work, "quota_exceeded", summary);
        public Task StopForRollback(SalesBrowserRoom room) =>
            worker.StopForPolicyAsync(room, Work, "conversation_disabled");
        public static async Task<Fixture> Create(bool hybrid = false)
        {
            var f = new Fixture { Source = await SalesNarrationTests.Fixture.Create() };
            f.Options.HybridConversationEnabled = hybrid;
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
            f.worker = new(f.Db, f.scopes.GetRequiredService<IServiceScopeFactory>(), new CompanyExecutionScopeFactory(context), null!, null!, null!, null!, source.Service,
                source.Speech, f.Reasoner, new Conductor(source.Session, deck.Id), null!, new Publisher(),
                Microsoft.Extensions.Options.Options.Create(f.Options).ToMonitor(), Microsoft.Extensions.Options.Options.Create(new SalesRoomLifecycleOptions()).ToMonitor(),
                null!, source.Clock, NullLogger<SalesRoomAgentWorker>.Instance);
            return f;
        }
        public async ValueTask DisposeAsync() { await scopes.DisposeAsync(); await Source.DisposeAsync(); }
    }
    private sealed class ConversationReasoner : ISalesRoomConversationReasoner
    {
        public AgentConversationIntent Intent = AgentConversationIntent.Question;
        public bool Complete = true;
        public Task<bool> IsCompleteAsync(SalesRoomConversationContext context, string heard, CancellationToken ct) => Task.FromResult(Complete);
        public ValidatedConversationBridge? Bridge;
        public Func<Task>? BeforeReturn;
        public async Task<ValidatedConversationBridge?> ProposeBridgeAsync(SalesRoomConversationContext context, CancellationToken ct)
        {
            if (BeforeReturn is { } action) await action();
            return Bridge;
        }
        public Task<AgentConversationIntent> InterpretAsync(SalesRoomConversationContext context, string heard,
            CancellationToken ct) => Task.FromResult(Intent);
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
        public int Cancellations;
        private long generation = 1;
        public async Task<bool> SendAsync(long turn, int rate, ReadOnlyMemory<short> samples, CancellationToken ct)
        {
            if (OnSend is { } hook) { OnSend = null; await hook(); }
            return !ct.IsCancellationRequested && turn == generation;
        }
        public Task<bool> CompleteSpeechAsync(long turn, CancellationToken ct) { Completions++; return Task.FromResult(!ct.IsCancellationRequested); }
        public Task<long> CancelSpeechAsync(CancellationToken ct) { Cancellations++; return Task.FromResult(++generation); }
        public Task RevokeInputAsync(Guid participant, CancellationToken ct) => Task.CompletedTask;
        public bool IsParticipantConnected(Guid participant) => true;
        public SalesRoomMediaStatistics GetStatistics() => new("connected", 0, 0, 0, 0, generation);
        public IAsyncEnumerable<SalesRoomAudioFrame> ReceiveAsync(CancellationToken ct) => throw new NotSupportedException();
        public bool Disposed;
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
