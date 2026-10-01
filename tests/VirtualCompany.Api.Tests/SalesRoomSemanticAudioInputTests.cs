using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class SalesRoomSemanticAudioInputTests
{
    [Theory]
    [InlineData("TR_microphone")]
    [InlineData("TR_replacement")]
    public void Pinned_sdk_remote_track_with_empty_sid_uses_publication_identity(string sid)
    {
        // The SDK creates this track with only a native handle; Sid stays empty.
        // No native call or external service is needed to recreate that object shape.
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var track = (LiveKit.Rtc.RemoteAudioTrack)typeof(LiveKit.Rtc.RemoteAudioTrack)
            .GetConstructors(flags).Single().Invoke([null]);
        var publication = (LiveKit.Rtc.RemoteTrackPublication)typeof(LiveKit.Rtc.RemoteTrackPublication)
            .GetConstructors(flags).Single().Invoke([null, new LiveKit.Proto.TrackPublicationInfo { Sid = sid }, null]);
        Assert.Equal("", track.Sid);
        Assert.Equal(sid, LiveKitSalesRoomMediaConnection.InputTrackId(publication));
        var scope = Scope() with { TrackId = LiveKitSalesRoomMediaConnection.InputTrackId(publication)! };
        using var input = new SalesRoomSemanticAudioInput(new(), new Detector());
        Speak(input, scope);
        Assert.Equal(sid, Assert.IsType<SalesRoomSemanticTurn>(Complete(input)).Scope.TrackId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Missing_publication_identity_is_not_fabricated(string sid)
    {
        var publication = (LiveKit.Rtc.RemoteTrackPublication)typeof(LiveKit.Rtc.RemoteTrackPublication)
            .GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Single().Invoke([null, new LiveKit.Proto.TrackPublicationInfo { Sid = sid }, null]);
        Assert.Null(LiveKitSalesRoomMediaConnection.InputTrackId(publication));
    }
    private static readonly DateTimeOffset Start = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
    private static SalesRoomSemanticScope Scope() => new(Guid.NewGuid(), "rode", 1, 1, 1, true, 1, 1, true);
    private static RealtimeAgentEvent Event(string type, string id = "item1", int? start = null, int? end = null, string? text = null) =>
        new(Guid.NewGuid().ToString(), 1, type, Text: text, ItemId: id, AudioStartMilliseconds: start, AudioEndMilliseconds: end);
    private static SalesRoomAudioFrame Frame(SalesRoomSemanticScope scope, int sequence, bool speech = true) =>
        new(scope.ParticipantId, scope.TrackId, scope.TrackGeneration, sequence, Start.AddMilliseconds((sequence + 1) * 20),
            24000, Enumerable.Repeat((short)(speech ? 1000 : 0), 480).ToArray());
    private static void Speak(SalesRoomSemanticAudioInput input, SalesRoomSemanticScope scope, int from = 0, int to = 20)
    { for (var i = from; i < to; i++) input.Push(Frame(scope, i), scope); }
    private static SalesRoomSemanticTurn? Complete(SalesRoomSemanticAudioInput input, string id = "item1", int start = 0, int end = 400)
    {
        input.Observe(Event(RealtimeAgentEventTypes.ParticipantSpeechStarted, id, start: start), Start.UtcDateTime);
        input.Observe(Event(RealtimeAgentEventTypes.ParticipantSpeechStopped, id, end: end), Start.UtcDateTime);
        input.Observe(Event(RealtimeAgentEventTypes.InputCommitted, id), Start.UtcDateTime);
        return input.Observe(Event(RealtimeAgentEventTypes.ParticipantTranscriptCompleted, id, text: "How do you onboard companies?"), Start.UtcDateTime);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pause_remains_continuous_and_out_of_order_events_complete_exactly_once(bool reverse)
    {
        using var input = new SalesRoomSemanticAudioInput(new(), new Detector());
        var scope = Scope(); Speak(input, scope);
        for (var i = 20; i < 80; i++)
        {
            var silence = input.Push(Frame(scope, i, false), scope);
            Assert.Equal(480, silence.Samples.Length); Assert.False(silence.ClearBuffer);
        }
        Speak(input, scope, 80, 100);
        Assert.Equal(2000, input.ForwardedMilliseconds);
        var events = new[] {
            Event(RealtimeAgentEventTypes.ParticipantSpeechStarted, start: 0),
            Event(RealtimeAgentEventTypes.ParticipantSpeechStopped, end: 2000),
            Event(RealtimeAgentEventTypes.InputCommitted),
            Event(RealtimeAgentEventTypes.ParticipantTranscriptCompleted, text: "A question, how do you onboard companies?") };
        var ordered = reverse ? events.Reverse().ToArray() : events;
        foreach (var e in ordered.Take(3)) Assert.Null(input.Observe(e, Start.UtcDateTime));
        var turn = Assert.IsType<SalesRoomSemanticTurn>(input.Observe(ordered[3], Start.UtcDateTime));
        Assert.Equal(scope, turn.Scope); Assert.Equal("A question, how do you onboard companies?", turn.Text);
        Assert.Equal(2000, (turn.Utterance.EndedAt - turn.Utterance.StartedAt).TotalMilliseconds);
        Assert.Equal(0, input.PendingCount);
        foreach (var e in events) Assert.Null(input.Observe(e, Start.UtcDateTime));
        Speak(input, scope, 100, 120);
        Assert.NotNull(Complete(input, "next", 2000, 2400));
    }

    [Fact]
    public void Local_audio_after_completed_item_does_not_block_but_new_provider_speech_does()
    {
        using var input = new SalesRoomSemanticAudioInput(new(), new Detector());
        var scope = Scope(); Speak(input, scope, 0, 40);
        Assert.NotNull(Complete(input, end: 400));
        Assert.False(input.HasPendingSpeech);
        input.Observe(Event(RealtimeAgentEventTypes.ParticipantSpeechStarted, "next", start: 400), Start.UtcDateTime);
        Assert.True(input.HasPendingSpeech);
        Assert.NotNull(Complete(input, "next", 400, 800));
        Assert.False(input.HasPendingSpeech);
    }

    [Fact]
    public void Local_noise_and_provider_speech_start_alone_never_release_a_turn()
    {
        using var input = new SalesRoomSemanticAudioInput(new(), new Detector());
        var scope = Scope();
        for (var i = 0; i < 100; i++) Assert.True(input.Push(Frame(scope, i, false), scope).Samples.IsEmpty);
        Assert.Equal(0, input.ForwardedMilliseconds);
        Assert.Null(input.Observe(Event(RealtimeAgentEventTypes.ParticipantSpeechStarted, start: 0), Start.UtcDateTime));
        Assert.Null(Complete(input)); // No trusted local audio window to bind provider text to.
    }

    [Theory]
    [InlineData("track")]
    [InlineData("consent")]
    [InlineData("participant_generation")]
    [InlineData("room_turn")]
    [InlineData("gap")]
    [InlineData("overlap")]
    public void Changed_provenance_or_overlap_invalidates_old_turn(string boundary)
    {
        using var input = new SalesRoomSemanticAudioInput(new(), new Detector());
        var scope = Scope(); Speak(input, scope);
        var next = boundary switch {
            "track" => scope with { TrackId = "new-mic", TrackGeneration = 2 },
            "consent" => scope with { ConsentVersion = 2 },
            "participant_generation" => scope with { ParticipantGeneration = 2 },
            "room_turn" => scope with { TurnGeneration = 2 },
            "overlap" => Scope(), _ => scope };
        var cleared = false;
        for (var i = 21; i < 40; i++) cleared |= input.Push(Frame(next, i), next).ClearBuffer;
        Assert.True(cleared); Assert.Null(Complete(input));
        if (boundary == "track") Assert.False(input.IsCurrentTrack(scope.ParticipantId, scope.TrackId, scope.TrackGeneration));
    }

    [Fact]
    public void Deck_advancement_does_not_cut_a_human_sentence()
    {
        using var input = new SalesRoomSemanticAudioInput(new(), new Detector());
        var scope = Scope(); Speak(input, scope);
        var output = input.Push(Frame(scope, 20), scope with { ResponseGeneration = 2 });
        Assert.False(output.ClearBuffer); Assert.Equal(480, output.Samples.Length);
        Assert.Equal(scope, Assert.IsType<SalesRoomSemanticTurn>(Complete(input, end: 420)).Scope);
    }

    [Fact]
    public void Revoked_input_rejects_late_provider_completion()
    {
        using var input = new SalesRoomSemanticAudioInput(new(), new Detector());
        var scope = Scope(); Speak(input, scope);
        Assert.True(input.Revoke(scope.ParticipantId)); Assert.Null(Complete(input));
        Assert.True(input.Tick(Start.AddSeconds(1)).Samples.IsEmpty);
    }

    [Fact]
    public void Dtx_silence_is_accounted_timeout_recovers_without_committing_audio()
    {
        using var input = new SalesRoomSemanticAudioInput(new(), new Detector());
        var scope = Scope(); Speak(input, scope);
        Assert.Equal(4800, input.Tick(Start.AddMilliseconds(600)).Samples.Length);
        Assert.Equal(600, input.ForwardedMilliseconds);
        input.Observe(Event(RealtimeAgentEventTypes.ParticipantSpeechStarted, start: 0), Start.UtcDateTime);
        Assert.Equal(1, input.Expire(Start.UtcDateTime.AddSeconds(43)));
        Assert.True(input.Tick(Start.AddSeconds(43)).ClearBuffer);
        Assert.Equal(0, input.PendingCount);
        Speak(input, scope, 2200, 2220);
        Assert.NotNull(Complete(input, "new", 600, 1000));
    }

    [Fact]
    public void Failed_transcription_is_not_released_and_next_item_can_complete()
    {
        using var input = new SalesRoomSemanticAudioInput(new(), new Detector());
        var scope = Scope(); Speak(input, scope);
        Assert.Null(input.Observe(Event(RealtimeAgentEventTypes.ParticipantTranscriptFailed), Start.UtcDateTime));
        Assert.Null(Complete(input));
        Speak(input, scope, 20, 40);
        Assert.NotNull(Complete(input, "next", 400, 800));
    }

    private sealed class Detector : ILocalVoiceActivityDetector
    {
        public bool IsSpeech(ReadOnlySpan<short> samples, int sampleRate) => samples[0] != 0;
        public ILocalVoiceActivityDetector CreateForTrack() => new Detector();
    }
}
