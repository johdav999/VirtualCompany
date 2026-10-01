using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Agents;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Api.Tests;

public sealed partial class SalesRoomPlaybackWorkerTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Semantic_worker_streams_without_manual_commits_and_routes_only_retention_authorized_turns(bool retention, bool complete)
    {
        await using var f = await Fixture.Create(hybrid: true, semantic: true);
        f.First.Fail(SalesRoomAgentSpeechStates.Interrupted, "paused", "Fixture pause", f.Source.Clock.Now);
        var floor = await f.Db.SalesRoomFloors.SingleAsync();
        floor.PauseAt(0, floor.TurnGeneration, f.Source.Clock.Now);
        f.Participant.Consent("retained_transcript", retention);
        f.Db.SalesRoomConsents.Add(new(f.Participant, "retained_transcript", retention, "test", f.Source.Clock.Now.AddSeconds(-1)));
        await f.Db.SaveChangesAsync();
        // This stage verifies complete input reaches conversation, not prompt 3's action/speech generation.
        f.Reasoner.Intent = AgentConversationIntent.Acknowledgement;
        f.Reasoner.Complete = complete;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var running = f.Run(deadline.Token);
        try
        {
            await f.Pcm.Connected.Task.WaitAsync(deadline.Token);
            Assert.Equal("low", f.Pcm.Request!.SemanticVadEagerness);
            Assert.False(f.Pcm.Request.ManualInputCommit);
            for (var i = 0; i < 100; i++)
            {
                var sample = i is >= 20 and < 80 ? (short)0 : (short)(i % 2 == 0 ? 1000 : 4000);
                await f.Media.Input.Writer.WriteAsync(new(f.Participant.Id, "rode", 1, i,
                    f.Source.Clock.GetUtcNow().AddMilliseconds((i + 1) * 20), 24000,
                    Enumerable.Repeat(sample, 480).ToArray()), deadline.Token);
            }
            if (retention)
                while (Interlocked.Read(ref f.Pcm.AudioBytes) < 96000) await Task.Delay(20, deadline.Token);
            else await Task.Delay(800, deadline.Token);
            string[] replay = [
                """{"type":"conversation.item.input_audio_transcription.completed","item_id":"one","transcript":"A question, how do you onboard companies?"}""",
                """{"type":"input_audio_buffer.committed","item_id":"one"}""",
                """{"type":"input_audio_buffer.speech_stopped","item_id":"one","audio_end_ms":2000}""",
                """{"type":"input_audio_buffer.speech_started","item_id":"one","audio_start_ms":0}""" ];
            long sequence = 0;
            foreach (var json in replay.Concat(replay))
                await f.Pcm.Events.Writer.WriteAsync(new("test-session", ++sequence, f.Source.Clock.Now,
                    ReadOnlyMemory<byte>.Empty, "event" + sequence, json), deadline.Token);
            if (retention && complete)
                while (f.Conversation.Inputs.Count == 0) await Task.Delay(20, deadline.Token);
            else if (retention) await f.Reasoner.CompletenessEvaluated.Task.WaitAsync(deadline.Token);
            await Task.Delay(400, deadline.Token);
            Assert.DoesNotContain(f.Pcm.ClientEvents, x => x.Contains("input_audio_buffer.commit", StringComparison.Ordinal));
            if (retention && complete) Assert.Equal("A question, how do you onboard companies?", Assert.Single(f.Conversation.Inputs).Text);
            else Assert.Empty(f.Conversation.Inputs);
            if (!retention) Assert.Equal(0, Interlocked.Read(ref f.Pcm.AudioBytes));
            Assert.False(running.IsCompleted); Assert.False(f.Media.Disposed);
            Assert.Equal(0, f.Pcm.Terminations); Assert.Equal(0, f.Media.Completions);
        }
        finally { await deadline.CancelAsync(); await running; }
        // Verification contexts register SQLite functions on the fixture's shared connection.
        // Initialize/read after the worker drains so registration cannot race its database work.
        await using var check = f.OtherDb();
        Assert.Equal(retention ? 1 : 0, await check.SalesRoomAgentTranscripts.CountAsync());
    }

    private sealed class SemanticTestClassifierFactory : ISpeechFrameClassifierFactory
    {
        public ISpeechFrameClassifier Create() => new Classifier();
        private sealed class Classifier : ISpeechFrameClassifier
        {
            public bool IsSpeech(ReadOnlySpan<short> samples, int sampleRate) => samples[0] != 0;
            public void Dispose() { }
        }
    }
}
