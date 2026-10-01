using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed partial class SalesRoomPlaybackWorkerTests
{
    private sealed class TraceLogger : Microsoft.Extensions.Logging.ILogger<SalesRoomAgentWorker>
    {
        public readonly System.Collections.Concurrent.ConcurrentQueue<string> Events = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel level) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel level, Microsoft.Extensions.Logging.EventId id,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Events.Enqueue(formatter(state, exception));
    }
    [Theory]
    [InlineData("valid")]
    [InlineData("question")]
    [InlineData("question_presenting")]
    [InlineData("question_presenting_active")]
    [InlineData("question_presenting_gap")]
    [InlineData("question_slow")]
    [InlineData("authority_argument")]
    [InlineData("wrong_turn")]
    [InlineData("provider_failed")]
    [InlineData("start")]
    [InlineData("resume")]
    [InlineData("state")]
    [InlineData("state_retention_revoked")]
    public async Task Running_semantic_worker_dispatches_complete_current_proposals_only_and_stays_connected(string scenario)
    {
        await using var f = await Fixture.Create(true, true);
        if (scenario is "start" or "resume" or "state" or "state_retention_revoked") await PlaybackTurn(f);
        else if (scenario is "question_presenting" or "question_presenting_active" or "question_presenting_gap")
        {
            // Cover both an active narration stream and the gap between segments.
            if (scenario is "question_presenting" or "question_presenting_gap")
                f.First.Fail(SalesRoomAgentSpeechStates.Interrupted, "test", "Synthetic gap between segments", f.Source.Clock.Now);
            f.Participant.Consent("retained_transcript", true);
            var floor = await f.Db.SalesRoomFloors.SingleAsync();
            var session = await f.Db.SalesMeetingSessions.SingleAsync();
            session.SetPresentationControlMode("autonomous", session.ConcurrencyVersion, f.Source.Actor, f.Source.Clock.Now);
            session.ApplyPresentationCommand(VirtualCompany.Domain.Enums.SalesPresentationCommandType.Goto,
                Guid.NewGuid(), session.LastPresentationSequence + 1, session.ConcurrencyVersion,
                1, 1, null, f.Source.Actor, f.Source.Clock.Now);
            floor.SetMode("autonomous", session.ConcurrencyVersion, f.Source.Clock.Now);
            if (scenario == "question_presenting_gap")
                floor.AgentCompleted(f.Participant.Id, f.Source.Clock.Now, preserveNarrationCheckpoint: true);
            await f.Db.SaveChangesAsync();
        }
        else await DialogueTurn(f);
        f.Db.SalesRoomConsents.Add(new(await f.Db.SalesRoomParticipants.SingleAsync(), "retained_transcript", true, "test", f.Source.Clock.Now.AddSeconds(-1)));
        await f.Db.SaveChangesAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var releaseLookup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Guid? queuedNarration = null;
        if (scenario == "question_presenting_gap")
            f.Reasoner.BeforeJudge = async () =>
            {
                // Input starts while the host owns the floor. Narration becomes ready while
                // completeness is judged, with no active speech task yet to signal interruption.
                await using var next = f.OtherDb();
                var floor = await next.SalesRoomFloors.SingleAsync();
                floor.AgentClaim(floor.ResponseGeneration, floor.TurnGeneration, f.Source.Clock.Now);
                var speech = new SalesRoomAgentSpeech(Guid.NewGuid(), f.Work.CompanyId, f.Work.RoomId,
                    f.First.SessionId, Guid.NewGuid(), f.First.AgentId, f.Work.Generation, floor.TurnGeneration,
                    SalesRoomAgentSpeechKinds.Narration, f.Source.Actor, f.Source.Clock.Now,
                    f.First.NarrationRevisionId, f.First.NarrationSegmentId, responseGeneration: floor.ResponseGeneration);
                queuedNarration = speech.Id;
                next.SalesRoomAgentSpeech.Add(speech);
                await next.SaveChangesAsync();
            };
        if (scenario == "question_slow") f.Answerer.BeforeReturn = () => releaseLookup.Task.WaitAsync(deadline.Token);
        if (scenario == "question_presenting_active")
            f.Media.OnSend = async () =>
            {
                var cancellations = f.Media.Cancellations;
                while (f.Media.Cancellations == cancellations) await Task.Delay(10, deadline.Token);
            };
        var running = f.Run(deadline.Token);
        try
        {
            await f.Pcm.Connected.Task.WaitAsync(deadline.Token);
            Assert.Contains(f.Pcm.Request!.Tools, x => x.Name == SalesRoomDialoguePolicy.Social);
            if (scenario == "question_presenting_active")
                while (f.Media.SentFrames == 0) await Task.Delay(20, deadline.Token);
            for (var i = 0; i < 50; i++)
                await f.Media.Input.Writer.WriteAsync(new(f.Participant.Id, "rode", 1, i,
                    f.Source.Clock.GetUtcNow().AddMilliseconds((i + 1) * 20), 24000, Enumerable.Repeat((short)(i % 2 == 0 ? 1000 : 4000), 480).ToArray()), deadline.Token);
            var inputDeadline = DateTime.UtcNow.AddSeconds(4);
            while (Interlocked.Read(ref f.Pcm.AudioBytes) < 48000)
            {
                if (running.IsCompleted)
                {
                    await running;
                    Assert.Fail("Worker exited before forwarding input: " + string.Join("\n", f.Trace.Events));
                }
                if (DateTime.UtcNow >= inputDeadline)
                {
                    await using var observed = f.OtherDb();
                    Assert.Fail("No microphone input forwarded. Bytes=" + Interlocked.Read(ref f.Pcm.AudioBytes) +
                        " Floor=" + (await observed.SalesRoomFloors.SingleAsync()).State +
                        " Health=" + (await observed.SalesBrowserRooms.SingleAsync()).AgentHealth +
                        " Trace=" + string.Join("\n", f.Trace.Events));
                }
                await Task.Delay(20, deadline.Token);
            }
            string[] replay = [
                """{"type":"input_audio_buffer.speech_started","item_id":"hello","audio_start_ms":0}""",
                """{"type":"input_audio_buffer.speech_stopped","item_id":"hello","audio_end_ms":1000}""",
                """{"type":"input_audio_buffer.committed","item_id":"hello"}""",
                """{"type":"conversation.item.input_audio_transcription.completed","item_id":"hello","transcript":"Welcome to the meeting Alex"}""" ];
            long sequence = 0;
            foreach (var json in replay) await Send(scenario is "question" or "question_slow" or "question_presenting" or "question_presenting_active" or "question_presenting_gap"
                ? json.Replace("Welcome to the meeting Alex", "How does onboarding work?") : json, null);
            var routeDeadline = DateTime.UtcNow.AddSeconds(5);
            while (f.Conversation.Requests.IsEmpty)
            {
                if (DateTime.UtcNow >= routeDeadline)
                {
                    await using var observed = f.OtherDb();
                    Assert.Fail("No route requested. Floor=" + (await observed.SalesRoomFloors.SingleAsync()).State +
                        " Health=" + (await observed.SalesBrowserRooms.SingleAsync()).AgentHealth +
                        " Trace=" + string.Join("\n", f.Trace.Events));
                }
                await Task.Delay(20, deadline.Token);
            }
            var request = Assert.Single(f.Conversation.Requests);
            if (scenario is "question_presenting" or "question_presenting_active" or "question_presenting_gap")
            {
                await using var reserved = f.OtherDb();
                Assert.True((await reserved.SalesRoomFloors.SingleAsync()).State == SalesRoomFloorStates.Human,
                    string.Join("\n", f.Trace.Events));
                Assert.Equal(SalesRoomAgentSpeechStates.Interrupted,
                    (await reserved.SalesRoomAgentSpeech.SingleAsync(x => x.Id == f.First.Id)).Status);
                Assert.Contains(f.Trace.Events, x => x.Contains("Stage=turn_reserved"));
                if (scenario == "question_presenting_gap")
                {
                    Assert.Contains(f.Trace.Events, x => x.Contains("Stage=turn_completed") && x.Contains("DuringPresentation=False"));
                    Assert.Equal(SalesRoomAgentSpeechStates.Interrupted,
                        (await reserved.SalesRoomAgentSpeech.SingleAsync(x => x.Id == queuedNarration)).Status);
                    Assert.DoesNotContain(f.Trace.Events, x => x.Contains("Stage=queued_speech_start") && x.Contains("Kind=narration"));
                }
            }
            Assert.True(request.AutomaticToolChoice); Assert.Null(request.RequiredToolName);
            Assert.Null(f.Reasoner.LastContext); // No separate intent classifier chose the tool.
            if (scenario == "state_retention_revoked")
            {
                await using var changed = f.OtherDb();
                (await changed.SalesRoomParticipants.SingleAsync()).Consent("retained_transcript", false);
                await changed.SaveChangesAsync();
            }
            var turnId = scenario == "wrong_turn" ? Guid.NewGuid() : request.TurnId;
            if (scenario == "provider_failed")
                await Send("""{"type":"response.done","response":{"id":"resp_hello","status":"failed"}}""", turnId);
            else
            {
                var json = JsonSerializer.Serialize(new { type = "response.output_item.done", response_id = "resp_hello",
                    item = new { type = "function_call", status = "completed", call_id = "call_hello", name = scenario switch {
                        "start" => SalesRoomDialoguePolicy.Start, "resume" => SalesRoomDialoguePolicy.Resume,
                        "state" or "state_retention_revoked" => SalesRoomDialoguePolicy.State,
                        "question" or "question_slow" or "question_presenting" or "question_presenting_active" or "question_presenting_gap" => SalesRoomConversationTools.Question, _ => SalesRoomDialoguePolicy.Social },
                        arguments = scenario == "authority_argument" ? "{\"companyId\":\"other\"}" : "{}" } });
                await Send(json, turnId);
                while (f.Conversation.ToolResults.IsEmpty)
                {
                    if (running.IsCompleted)
                    {
                        await running;
                        await using var stoppedDb = f.OtherDb();
                        var stopped = await stoppedDb.SalesBrowserRooms.SingleAsync();
                        Assert.Fail("Worker exited before returning the tool result: " + stopped.AgentLastErrorCode + "\n" + string.Join("\n", f.Trace.Events));
                    }
                    await Task.Delay(20, deadline.Token);
                }
                if (scenario == "valid")
                {
                    while (f.Media.Completions == 0) await Task.Delay(20, deadline.Token);
                    await Send(json, turnId); // Replayed completion may return rejection but cannot replay speech.
                }
                if (scenario == "question_slow")
                {
                    // Acknowledgement plays while the evidence provider is still blocked.
                    while (f.Media.Completions == 0) await Task.Delay(20, deadline.Token);
                    Assert.False(releaseLookup.Task.IsCompleted);
                    var before = Interlocked.Read(ref f.Pcm.AudioBytes);
                    for (var i = 50; i < 80; i++)
                        await f.Media.Input.Writer.WriteAsync(new(f.Participant.Id, "rode", 1, i,
                            f.Source.Clock.GetUtcNow().AddMilliseconds((i + 1) * 20), 24000,
                            Enumerable.Repeat((short)(i % 2 == 0 ? 1000 : 4000), 480).ToArray()), deadline.Token);
                    while (Interlocked.Read(ref f.Pcm.AudioBytes) <= before) await Task.Delay(20, deadline.Token);
                    Assert.False(running.IsCompleted);
                    releaseLookup.TrySetResult();
                }
            }
            await Task.Delay(350, deadline.Token);
            if (scenario is "start" or "resume")
            {
                using var result = JsonDocument.Parse(f.Conversation.ToolResults.First());
                Assert.True(result.RootElement.GetProperty("Accepted").GetBoolean(), result.RootElement.ToString());
                await using var verify = f.OtherDb();
                Assert.Single(await verify.SalesRoomOperations.Where(x => x.Action.StartsWith("dialogue_")).ToListAsync());
            }
            else if (scenario is "question" or "question_slow" or "question_presenting" or "question_presenting_active" or "question_presenting_gap")
            {
                using var tool = JsonDocument.Parse(f.Conversation.ToolResults.First());
                Assert.True(tool.RootElement.GetProperty("Accepted").GetBoolean(), tool.RootElement.ToString());
                await using var verify = f.OtherDb();
                Assert.Single(await verify.SalesMeetingQuestions.ToListAsync());
                var acknowledgement = await verify.SalesRoomAgentSpeech.SingleAsync(x => x.Kind == SalesRoomAgentSpeechKinds.Conversation);
                Assert.Equal(SalesRoomAgentSpeechStates.Spoken, acknowledgement.Status);
                Assert.Contains("checking_sources", acknowledgement.EvidenceJson);
                Assert.NotEqual(request.TurnId, acknowledgement.CommandId);
                Assert.Single(await verify.SalesRoomAgentSpeech.Where(x => x.Kind == SalesRoomAgentSpeechKinds.Answer &&
                    (x.Status == SalesRoomAgentSpeechStates.Queued || x.Status == SalesRoomAgentSpeechStates.Processing ||
                     x.Status == SalesRoomAgentSpeechStates.Spoken)).ToListAsync());
                var trace = f.Trace.Events.ToArray();
                Assert.Contains(trace, x => x.Contains("Stage=source_lookup_start"));
                Assert.Contains(trace, x => x.Contains("Stage=dialogue_playback_complete"));
                if (scenario == "question_slow") Assert.True(Array.FindIndex(trace, x => x.Contains("Stage=dialogue_playback_complete")) <
                    Array.FindIndex(trace, x => x.Contains("Stage=source_lookup_complete")));
                Assert.DoesNotContain(trace, x => x.Contains("How does onboarding work?"));
            }
            else Assert.Equal(scenario == "valid" ? 1 : 0, f.Media.Completions);
            if (scenario == "state_retention_revoked")
            {
                using var denied = JsonDocument.Parse(f.Conversation.ToolResults.First());
                Assert.False(denied.RootElement.GetProperty("Accepted").GetBoolean());
            }
            Assert.False(running.IsCompleted); Assert.False(f.Media.Disposed); Assert.Equal(0, f.Pcm.Terminations);
            async Task Send(string json, Guid? id) => await f.Pcm.Events.Writer.WriteAsync(new("test-session", ++sequence,
                f.Source.Clock.Now, default, "event" + sequence, json, TurnId: id), deadline.Token);
        }
        finally { await deadline.CancelAsync(); await running; }
    }

    private static async Task<SalesRoomDialogueTurn> DialogueTurn(Fixture f)
    {
        var participant = await f.Db.SalesRoomParticipants.SingleAsync();
        participant.Consent("retained_transcript", true);
        var floor = await f.Db.SalesRoomFloors.SingleAsync();
        floor.PauseAt(20, floor.TurnGeneration, f.Source.Clock.Now);
        f.First.Fail(SalesRoomAgentSpeechStates.Interrupted, "test", "Test pause", f.Source.Clock.Now);
        await f.Db.SaveChangesAsync();
        var authority = await SalesRoomConversationPolicy.LoadAsync(f.Db, f.Options, f.Work.CompanyId, f.Work.RoomId,
            participant.Id, f.Source.Clock.Now, default);
        return new(authority!.Binding, Guid.NewGuid(), participant.Version, f.Source.Clock.Now.AddMinutes(1));
    }

    [Fact]
    public async Task Withheld_acknowledgement_logs_exact_stage_and_code_without_publishing_or_speech_text()
    {
        await using var f = await Fixture.Create(true, true);
        var turn = await DialogueTurn(f);
        f.Reasoner.DialogueValidation = null;
        await f.Dialogue(turn, SalesRoomDialoguePolicy.Checking);
        var message = Assert.Single(f.Trace.Events.Where(x => x.Contains("Stage=dialogue_withheld")));
        Assert.Contains("ContentClass=checking_sources", message);
        Assert.Contains("Code=conversation_restricted", message);
        Assert.Contains("ReleaseStage=content_validation", message);
        Assert.Contains("PublishedAudioMs=0", message);
        Assert.DoesNotContain("release_or_transport_failure", message);
        Assert.DoesNotContain("Hello! Glad to be here.", message);
        Assert.DoesNotContain(f.Trace.Events, x => x.Contains("Stage=acknowledgement_candidate_received"));
        Assert.Equal(0, f.Media.SentFrames);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Multi_item_acknowledgement_is_validated_as_one_reply_before_any_media_release(bool validated)
    {
        await using var f = await Fixture.Create(true, true);
        var turn = await DialogueTurn(f);
        f.Conversation.Candidate = new(new byte[1920], "Let me check. One moment.", "response_fixture", 5, 10,
            Items: [new("item_1", 0, 960), new("item_2", 960, 960)]);
        if (!validated) f.Reasoner.DialogueValidation = null;
        await f.Dialogue(turn, SalesRoomDialoguePolicy.Checking);
        Assert.Equal(validated ? 2 : 0, f.Media.SentFrames);
        Assert.Empty(f.Conversation.Played); // Lookup acknowledgement is not the grounded answer context.
        Assert.Contains(f.Trace.Events, x => x.Contains("Stage=dialogue_buffered") && x.Contains("AudioItemCount=2"));
        var receipt = Assert.Single(f.Conversation.OutputReceipts);
        Assert.Equal(validated, receipt.Completed);
        f.Db.ChangeTracker.Clear();
        var speech = await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.Kind == SalesRoomAgentSpeechKinds.Conversation);
        Assert.Equal(validated ? SalesRoomAgentSpeechStates.Spoken : SalesRoomAgentSpeechStates.Withheld, speech.Status);
    }

    [Fact]
    public async Task Room_scoped_acknowledgement_diagnostic_logs_generated_text_before_withholding()
    {
        await using var f = await Fixture.Create(true, true);
        var turn = await DialogueTurn(f);
        f.Options.AcknowledgementTextDiagnosticRoomId = f.Work.RoomId;
        f.Reasoner.DialogueValidation = null;

        await f.Dialogue(turn, SalesRoomDialoguePolicy.Checking);

        var message = Assert.Single(f.Trace.Events.Where(x => x.Contains("Stage=acknowledgement_candidate_received")));
        Assert.Contains($"RoomId={f.Work.RoomId}", message);
        Assert.Contains($"TurnId={turn.InputId}", message);
        Assert.Contains("CandidateTextJson=\"Hello! Glad to be here.\"", message);
        var validationStart = Assert.Single(f.Trace.Events.Where(x => x.Contains("Stage=dialogue_validation_start")));
        Assert.Contains("ValidationPromptVersion=1.1.0", validationStart);
        Assert.Contains($"TurnId={turn.InputId}", validationStart);
        var validationComplete = Assert.Single(f.Trace.Events.Where(x => x.Contains("Stage=dialogue_validation_complete")));
        Assert.Contains("Allowed=False", validationComplete);
        Assert.Contains("PublishedAudioMs=0", validationComplete);
        Assert.Contains("GeneratedAudioMs=20", validationComplete);
        Assert.Contains(f.Trace.Events, x => x.Contains("Stage=dialogue_withheld"));
        Assert.Equal(0, f.Media.SentFrames);
    }

    [Fact]
    public async Task Acknowledgement_diagnostic_does_not_log_text_for_another_room()
    {
        await using var f = await Fixture.Create(true, true);
        var turn = await DialogueTurn(f);
        f.Options.AcknowledgementTextDiagnosticRoomId = Guid.NewGuid();
        f.Reasoner.DialogueValidation = null;

        await f.Dialogue(turn, SalesRoomDialoguePolicy.Checking);

        Assert.DoesNotContain(f.Trace.Events, x => x.Contains("Stage=acknowledgement_candidate_received"));
        Assert.DoesNotContain(f.Trace.Events, x => x.Contains("Hello! Glad to be here."));
        Assert.Contains(f.Trace.Events, x => x.Contains("Stage=dialogue_validation_complete") && x.Contains("Allowed=False"));
    }

    [Fact]
    public async Task Initial_greeting_has_real_media_release_without_question_row_and_duplicate_does_not_replay()
    {
        await using var f = await Fixture.Create(true, true);
        var turn = await DialogueTurn(f);
        await f.Dialogue(turn);
        f.Db.ChangeTracker.Clear();
        var speech = await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.Kind == SalesRoomAgentSpeechKinds.Conversation);
        Assert.Equal(SalesRoomAgentSpeechStates.Spoken, speech.Status);
        Assert.Equal(1, f.Media.SentFrames);
        Assert.Single(f.Conversation.Played);
        Assert.Empty(await f.Db.SalesMeetingQuestions.ToListAsync());
        var floor = await f.Db.SalesRoomFloors.SingleAsync();
        Assert.Equal(turn.Binding.Point, floor.TalkingPointIndex);
        Assert.Equal(turn.Binding.OffsetMilliseconds, floor.ResumeOffsetMilliseconds);
        await f.Dialogue(turn);
        Assert.Equal(1, f.Conversation.Generated);
        Assert.Equal(1, f.Media.SentFrames);
    }

    [Fact]
    public async Task Restricted_reply_has_zero_audio_leakage_and_next_greeting_can_speak()
    {
        await using var f = await Fixture.Create(true, true);
        var turn = await DialogueTurn(f);
        f.Reasoner.DialogueValidation = null;
        await f.Dialogue(turn, SalesRoomDialoguePolicy.General);
        Assert.Equal(0, f.Media.SentFrames);
        Assert.Empty(f.Conversation.Played);
        f.Db.ChangeTracker.Clear();
        var room = await f.Db.SalesBrowserRooms.SingleAsync();
        Assert.Equal(SalesRoomAgentHealthStates.Ready, room.AgentHealth);
        Assert.Equal("conversation_reply_withheld", room.AgentLastErrorCode);
        Assert.True(room.IsAgentOwner(f.Work.LeaseOwnerId, f.Work.Generation, f.Source.Clock.Now));
        Assert.False(f.Media.Disposed);
        f.Reasoner.DialogueValidation = Guid.NewGuid();
        await f.Dialogue(turn with { InputId = Guid.NewGuid() });
        Assert.Equal(1, f.Media.SentFrames);
    }

    [Theory]
    [InlineData("consent")]
    [InlineData("retention")]
    [InlineData("mode")]
    [InlineData("ended")]
    [InlineData("generation")]
    public async Task Authority_changed_during_generation_never_releases_candidate(string change)
    {
        await using var f = await Fixture.Create(true, true);
        var turn = await DialogueTurn(f);
        f.Conversation.BeforeAudio = async () =>
        {
            Assert.Equal(0, f.Media.SentFrames);
            await using var other = f.OtherDb();
            var room = await other.SalesBrowserRooms.SingleAsync();
            if (change == "consent") (await other.SalesRoomParticipants.SingleAsync()).Consent("ai_processing", false);
            if (change == "retention") (await other.SalesRoomParticipants.SingleAsync()).Consent("retained_transcript", false);
            if (change == "mode") { var floor = await other.SalesRoomFloors.SingleAsync(); floor.SetMode("manual", floor.PresentationVersion, f.Source.Clock.Now, true); }
            if (change == "ended") room.Ended();
            if (change == "generation") room.PreemptAgent(f.Work.LeaseOwnerId, f.Work.Generation, "new turn");
            await other.SaveChangesAsync();
        };
        await f.Dialogue(turn);
        Assert.Equal(1, f.Conversation.Generated);
        Assert.Equal(0, f.Media.SentFrames);
        Assert.Empty(f.Conversation.Played);
    }

    [Fact]
    public async Task Cross_company_dialogue_cannot_generate_or_publish()
    {
        await using var f = await Fixture.Create(true, true);
        var turn = await DialogueTurn(f);
        await f.Dialogue(turn with { Binding = turn.Binding with { CompanyId = Guid.NewGuid() } });
        Assert.Equal(0, f.Conversation.Generated);
        Assert.Equal(0, f.Media.SentFrames);
    }
}
