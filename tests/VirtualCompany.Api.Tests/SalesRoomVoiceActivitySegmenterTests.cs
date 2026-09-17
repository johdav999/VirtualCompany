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
    public void Active_speech_completes_when_the_transport_sends_no_trailing_silence()
    {
        var segmenter = Create(new SequenceDetector(Enumerable.Repeat(true, 4)));

        for (var sequence = 0; sequence < 4; sequence++)
            segmenter.Push(Frame(Speaker, sequence, 1200));

        Assert.Empty(segmenter.FlushExpired(DateTimeOffset.UnixEpoch.AddMilliseconds(679)));
        var utterance = Assert.Single(segmenter.FlushExpired(DateTimeOffset.UnixEpoch.AddMilliseconds(680)));
        Assert.Equal(80, utterance.SpeechMilliseconds);
        Assert.Equal(80, utterance.ForwardedMilliseconds);
        Assert.Empty(segmenter.FlushExpired(DateTimeOffset.UnixEpoch.AddSeconds(2)));
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

    [Fact]
    public void Dc_bias_and_quiet_noise_do_not_interrupt_with_the_real_detector()
    {
        var options = new SalesRoomAgentOptions();
        var segmenter = new SalesRoomVoiceActivitySegmenter(options,
            new EnergyLocalVoiceActivityDetector(Options.Create(options)));
        for (var i = 0; i < 100; i++)
        {
            var samples = Enumerable.Range(0, 480).Select(n => (short)(4000 + (n % 2 == 0 ? 100 : -100))).ToArray();
            var result = segmenter.Push(Frame(Speaker, i, 0) with { Samples = samples });
            Assert.False(result.SpeechStarted);
            Assert.Null(result.Utterance);
        }
        Assert.Equal(0, segmenter.ForwardedMilliseconds);
    }

    [Fact]
    public void Brief_noise_burst_does_not_interrupt_but_sustained_audio_does()
    {
        var options = new SalesRoomAgentOptions();
        var segmenter = new SalesRoomVoiceActivitySegmenter(options,
            new EnergyLocalVoiceActivityDetector(Options.Create(options)));
        var wave = Enumerable.Range(0, 480).Select(n => (short)(2000 * Math.Sin(2 * Math.PI * 200 * n / 24000))).ToArray();
        for (var i = 0; i < 40; i++)
        {
            // 100 ms burst, silence, then a sustained speech-like waveform.
            var result = segmenter.Push(Frame(Speaker, i, 0) with { Samples = i < 5 || i >= 20 ? wave : new short[480] });
            Assert.Equal(i == 31, result.SpeechStarted);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Separated_noise_bursts_never_accumulate_into_speech(bool timestampGap)
    {
        var segmenter = Create(new SequenceDetector(Enumerable.Repeat(true, 12)));
        for (var i = 0; i < 12; i++)
        {
            var frame = Frame(Speaker, timestampGap ? i : i * 2, 1200);
            if (timestampGap) frame = frame with { ReceivedAt = DateTimeOffset.UnixEpoch.AddSeconds(i) };
            Assert.False(segmenter.Push(frame).SpeechStarted);
        }
        Assert.Equal(0, segmenter.ForwardedMilliseconds);
    }

    [Fact]
    public void Duplicate_frames_cannot_confirm_speech()
    {
        var segmenter = Create(new SequenceDetector(Enumerable.Repeat(true, 12)));
        for (var i = 0; i < 12; i++) Assert.False(segmenter.Push(Frame(Speaker, 0, 1200)).SpeechStarted);
    }

    [Fact]
    public void Continuous_fan_noise_does_not_interrupt_but_speech_above_it_does()
    {
        var options = new SalesRoomAgentOptions();
        var segmenter = new SalesRoomVoiceActivitySegmenter(options,
            new EnergyLocalVoiceActivityDetector(Options.Create(options)));
        var random = new Random(42);
        for (var i = 0; i < 120; i++)
        {
            // Fan noise is deliberately above both old absolute energy thresholds.
            var samples = Enumerable.Range(0, 480).Select(n => (short)(random.Next(-1800, 1801) +
                (i >= 100 ? 7000 * Math.Sin(2 * Math.PI * 200 * n / 24000) : 0))).ToArray();
            var result = segmenter.Push(Frame(Speaker, i, 0) with { Samples = samples });
            Assert.Equal(i == 111, result.SpeechStarted);
        }
    }

    [Fact]
    public void Noisy_microphone_does_not_raise_another_participants_noise_floor()
    {
        var options = new SalesRoomAgentOptions();
        var segmenter = new SalesRoomVoiceActivitySegmenter(options,
            new EnergyLocalVoiceActivityDetector(Options.Create(options)));
        var other = Guid.NewGuid();
        var noise = Enumerable.Range(0, 480).Select(n => (short)(n % 2 == 0 ? 3000 : -3000)).ToArray();
        var voice = Enumerable.Range(0, 480).Select(n => (short)(2000 * Math.Sin(2 * Math.PI * 200 * n / 24000))).ToArray();
        for (var i = 0; i < 40; i++)
        {
            Assert.False(segmenter.Push(Frame(Speaker, i, 0) with { Samples = noise }).SpeechStarted);
            var result = segmenter.Push(Frame(other, i, 0) with { Samples = i >= 20 ? voice : new short[480] });
            Assert.Equal(i == 31, result.SpeechStarted);
        }
    }

    [Fact]
    public void Ambient_calibration_survives_completed_utterances()
    {
        var options = new SalesRoomAgentOptions();
        var segmenter = new SalesRoomVoiceActivitySegmenter(options,
            new EnergyLocalVoiceActivityDetector(Options.Create(options)));
        var voice = Enumerable.Range(0, 480).Select(n => (short)(2000 * Math.Sin(2 * Math.PI * 200 * n / 24000))).ToArray();
        var starts = new List<int>();
        for (var i = 0; i < 90; i++)
        {
            var result = segmenter.Push(Frame(Speaker, i, 0) with
            { Samples = i is >= 20 and < 35 or >= 70 ? voice : new short[480] });
            if (result.SpeechStarted) starts.Add(i);
        }
        Assert.Equal(new[] { 31, 81 }, starts);
    }

    private static SalesRoomVoiceActivitySegmenter Create(ILocalVoiceActivityDetector detector) =>
        new(new SalesRoomAgentOptions { MinimumSpeechMilliseconds = 80 }, detector);

    private static SalesRoomAudioFrame Frame(Guid participant, long sequence, short amplitude, string track = "track-1") =>
        new(participant, track, 3, sequence, DateTimeOffset.UnixEpoch.AddMilliseconds(sequence * 20 + 20),
            24_000, Enumerable.Repeat(amplitude, 480).ToArray());

    private sealed class SequenceDetector(IEnumerable<bool> values) : ILocalVoiceActivityDetector
    {
        private readonly Queue<bool> values = new(values);
        public bool IsSpeech(ReadOnlySpan<short> samples, int sampleRate) => values.Count > 0 && values.Dequeue();
    }
}
