using Microsoft.Extensions.Options;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class SalesRoomVoiceActivitySegmenterTests
{
    private static readonly Guid Speaker = Guid.NewGuid();

    [Fact]
    public void Silence_and_isolated_false_trigger_are_never_forwarded()
    {
        var detector = new SequenceDetector([false, false, true, false, false, false, false]);
        var segmenter = Create(detector);

        for (var sequence = 0; sequence < 7; sequence++)
            Assert.Null(segmenter.Push(Frame(Speaker, sequence, 1000)).Utterance);

        Assert.Equal(140, segmenter.ReceivedMilliseconds);
        Assert.Equal(0, segmenter.DetectedSpeechMilliseconds);
        Assert.Equal(0, segmenter.ForwardedMilliseconds);
    }

    [Fact]
    public void Confirmed_speech_keeps_pre_roll_and_bounded_trailing_silence()
    {
        var decisions = Enumerable.Repeat(false, 3)
            .Concat(Enumerable.Repeat(true, 4))
            .Concat(Enumerable.Repeat(false, 30)).ToArray();
        var segmenter = Create(new SequenceDetector(decisions));
        SalesRoomDetectedUtterance? utterance = null;

        for (var sequence = 0; sequence < decisions.Length; sequence++)
        {
            var result = segmenter.Push(Frame(Speaker, sequence, (short)(sequence < 3 ? 111 : sequence < 7 ? 1200 : 222)));
            if (sequence == 6) Assert.True(result.SpeechStarted);
            utterance ??= result.Utterance;
        }

        Assert.NotNull(utterance);
        Assert.Equal(80, utterance!.SpeechMilliseconds);
        Assert.Equal(740, utterance.ForwardedMilliseconds);
        Assert.Equal(111, utterance.Samples.Span[0]);
        Assert.Equal(222, utterance.Samples.Span[^1]);
        Assert.Equal(80, segmenter.DetectedSpeechMilliseconds);
        Assert.Equal(740, segmenter.ForwardedMilliseconds);
    }

    [Fact]
    public void Quiet_audio_stays_local_and_track_state_can_be_revoked()
    {
        var options = new SalesRoomAgentOptions();
        var segmenter = new SalesRoomVoiceActivitySegmenter(options,
            new EnergyLocalVoiceActivityDetector(Options.Create(options)));

        for (var sequence = 0; sequence < 20; sequence++)
            Assert.Null(segmenter.Push(Frame(Speaker, sequence, 250)).Utterance);
        segmenter.Clear(Speaker);

        Assert.Equal(400, segmenter.ReceivedMilliseconds);
        Assert.Equal(0, segmenter.ForwardedMilliseconds);
    }

    [Fact]
    public void Concurrent_tracks_mark_the_second_utterance_as_overlapped()
    {
        var other = Guid.NewGuid();
        var segmenter = Create(new SequenceDetector(Enumerable.Repeat(true, 8)
            .Concat(Enumerable.Repeat(false, 30)).ToArray()));

        for (var sequence = 0; sequence < 4; sequence++)
            segmenter.Push(Frame(Speaker, sequence, 1200, "track-a"));
        for (var sequence = 0; sequence < 4; sequence++)
            segmenter.Push(Frame(other, sequence, 1200, "track-b"));

        SalesRoomDetectedUtterance? utterance = null;
        for (var sequence = 4; sequence < 34; sequence++)
            utterance ??= segmenter.Push(Frame(other, sequence, 0, "track-b")).Utterance;

        Assert.NotNull(utterance);
        Assert.True(utterance!.Overlapped);
        Assert.Equal(other, utterance.ParticipantId);
        Assert.Equal("track-b", utterance.TrackId);
    }

    private static SalesRoomVoiceActivitySegmenter Create(ILocalVoiceActivityDetector detector) =>
        new(new SalesRoomAgentOptions(), detector);

    private static SalesRoomAudioFrame Frame(Guid participant, long sequence, short amplitude, string track = "track-1") =>
        new(participant, track, 3, sequence, DateTimeOffset.UnixEpoch.AddMilliseconds(sequence * 20 + 20),
            24_000, Enumerable.Repeat(amplitude, 480).ToArray());

    private sealed class SequenceDetector(IEnumerable<bool> values) : ILocalVoiceActivityDetector
    {
        private readonly Queue<bool> values = new(values);
        public bool IsSpeech(ReadOnlySpan<short> samples, int sampleRate) => values.Count > 0 && values.Dequeue();
    }
}
