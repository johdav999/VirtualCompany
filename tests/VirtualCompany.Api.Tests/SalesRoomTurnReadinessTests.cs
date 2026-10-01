using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed partial class SalesRoomPlaybackWorkerTests
{
    [Theory]
    [InlineData("clarify")]
    [InlineData("timeout")]
    [InlineData("continuation")]
    [InlineData("stale_turn")]
    [InlineData("revoked")]
    [InlineData("manual")]
    [InlineData("invalid_clarification")]
    [InlineData("clarify_presenting")]
    [InlineData("timeout_presenting")]
    public async Task Semantic_readiness_recovers_without_inventing_questions_or_losing_the_listener(string scenario)
    {
        await using var f = await Fixture.Create(true, true);
        var presenting = scenario.EndsWith("_presenting", StringComparison.Ordinal);
        if (!presenting) await DialogueTurn(f);
        else
        {
            f.Participant.Consent("retained_transcript", true);
            var session = await f.Db.SalesMeetingSessions.SingleAsync();
            session.SetPresentationControlMode("autonomous", session.ConcurrencyVersion, f.Source.Actor, f.Source.Clock.Now);
            session.ApplyPresentationCommand(VirtualCompany.Domain.Enums.SalesPresentationCommandType.Goto,
                Guid.NewGuid(), session.LastPresentationSequence + 1, session.ConcurrencyVersion,
                1, 1, null, f.Source.Actor, f.Source.Clock.Now);
            (await f.Db.SalesRoomFloors.SingleAsync()).SetMode("autonomous", session.ConcurrencyVersion, f.Source.Clock.Now);
        }
        f.Db.SalesRoomConsents.Add(new(f.Participant, "retained_transcript", true, "test", f.Source.Clock.Now.AddSeconds(-1)));
        if (scenario == "manual")
        {
            var session = await f.Db.SalesMeetingSessions.SingleAsync();
            session.SetPresentationControlMode("manual", session.ConcurrencyVersion, f.Source.Actor, f.Source.Clock.Now);
            (await f.Db.SalesRoomFloors.SingleAsync()).SetMode("manual", session.ConcurrencyVersion, f.Source.Clock.Now);
        }
        await f.Db.SaveChangesAsync();
        var waiting = scenario is "timeout" or "timeout_presenting" or "continuation" or "stale_turn" or "revoked";
        f.Reasoner.Readiness = waiting ? SalesRoomTurnReadiness.Wait : SalesRoomTurnReadiness.Clarify;
        if (scenario == "invalid_clarification") f.Reasoner.DialogueValidation = null;
        f.Conversation.Candidate = new(new byte[960], "Could you finish your question?", "response_clarify", 5, 10);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var heldNarration = false;
        if (presenting) f.Media.OnSend = async () =>
        {
            // Hold only prerecorded output; the clarification must still reach media.
            if (heldNarration) return;
            heldNarration = true;
            var cancellations = f.Media.Cancellations;
            while (f.Media.Cancellations == cancellations) await Task.Delay(10, deadline.Token);
        };
        var running = f.Run(deadline.Token);
        try
        {
            await f.Pcm.Connected.Task.WaitAsync(deadline.Token);
            if (presenting) while (f.Media.SentFrames == 0) await Task.Delay(20, deadline.Token);
            long sequence = 0;
            await Input("one", "A question how", 0);
            if (waiting)
            {
                await Until(() => f.Trace.Events.Any(x => x.Contains("Stage=turn_held")));
                Assert.Equal(0, f.Conversation.Generated);
                if (scenario == "continuation")
                {
                    f.Reasoner.Readiness = SalesRoomTurnReadiness.Proceed;
                    await Input("two", "do you do onboarding of companies to the solution?", 50);
                    await Until(() => f.Conversation.Requests.Count == 1);
                    Assert.Equal("A question how do you do onboarding of companies to the solution?",
                        f.Conversation.Inputs.Single().Text);
                }
                else
                {
                    if (scenario is "stale_turn" or "revoked")
                    {
                        await using var changed = f.OtherDb();
                        if (scenario == "revoked") (await changed.SalesRoomParticipants.SingleAsync()).Consent("retained_transcript", false);
                        else (await changed.SalesBrowserRooms.SingleAsync()).ResumeAgent(f.Work.LeaseOwnerId, f.Work.Generation);
                        await changed.SaveChangesAsync();
                    }
                    f.Source.Clock.Now = f.Source.Clock.Now.AddSeconds(13);
                }
            }
            if (scenario is "clarify" or "timeout" or "clarify_presenting" or "timeout_presenting")
            {
                await Until(() => f.Media.Completions == 1);
                await using var check = f.OtherDb();
                var speech = Assert.Single(await check.SalesRoomAgentSpeech.Where(x => x.Kind == SalesRoomAgentSpeechKinds.Conversation).ToListAsync());
                Assert.Equal(SalesRoomAgentSpeechStates.Spoken, speech.Status);
                Assert.Contains(SalesRoomDialoguePolicy.Clarify, speech.EvidenceJson);
                Assert.Single(f.Reasoner.JudgedTurns); // Recovery does not re-run the failed gate.
                if (presenting)
                {
                    Assert.Equal(SalesRoomAgentSpeechStates.Interrupted,
                        (await check.SalesRoomAgentSpeech.SingleAsync(x => x.Id == f.First.Id)).Status);
                    Assert.Contains(f.Trace.Events, x => x.Contains("Stage=turn_reserved"));
                }
            }
            else if (scenario == "invalid_clarification")
                await Until(() => f.Trace.Events.Any(x => x.Contains("Stage=dialogue_withheld")));
            else if (scenario != "continuation") await Task.Delay(700, deadline.Token);
            await using var verify = f.OtherDb();
            Assert.Empty(await verify.SalesMeetingQuestions.ToListAsync());
            if (scenario != "continuation") Assert.Empty(f.Conversation.Requests);
            if (scenario is "stale_turn" or "revoked" or "manual" or "invalid_clarification") Assert.Equal(0, f.Media.SentFrames);
            if (scenario != "revoked")
            {
                Assert.False(running.IsCompleted, string.Join("\n", f.Trace.Events));
                Assert.False(f.Media.Disposed); Assert.Equal(0, f.Pcm.Terminations);
            }
            Assert.DoesNotContain(f.Trace.Events, x => x.Contains("A question how"));

            async Task Until(Func<bool> condition)
            {
                var until = DateTime.UtcNow.AddSeconds(5);
                while (!condition())
                {
                    Assert.False(running.IsCompleted, string.Join("\n", f.Trace.Events));
                    Assert.True(DateTime.UtcNow < until, string.Join("\n", f.Trace.Events));
                    await Task.Delay(20, deadline.Token);
                }
            }
            async Task Input(string id, string text, int start)
            {
                for (var i = start; i < start + 50; i++)
                    await f.Media.Input.Writer.WriteAsync(new(f.Participant.Id, "rode", 1, i,
                        f.Source.Clock.GetUtcNow().AddMilliseconds((i + 1) * 20), 24000,
                        Enumerable.Repeat((short)(i % 2 == 0 ? 1000 : 4000), 480).ToArray()), deadline.Token);
                await Until(() => Interlocked.Read(ref f.Pcm.AudioBytes) >= (start + 50) * 960);
                string[] events = [
                    JsonSerializer.Serialize(new { type = "input_audio_buffer.speech_started", item_id = id, audio_start_ms = start * 20 }),
                    JsonSerializer.Serialize(new { type = "input_audio_buffer.speech_stopped", item_id = id, audio_end_ms = (start + 50) * 20 }),
                    JsonSerializer.Serialize(new { type = "input_audio_buffer.committed", item_id = id }),
                    JsonSerializer.Serialize(new { type = "conversation.item.input_audio_transcription.completed", item_id = id, transcript = text }) ];
                foreach (var json in events) await f.Pcm.Events.Writer.WriteAsync(new("test-session", ++sequence,
                    f.Source.Clock.Now, default, "event" + sequence, json), deadline.Token);
            }
        }
        finally { await deadline.CancelAsync(); await running; }
    }
}
