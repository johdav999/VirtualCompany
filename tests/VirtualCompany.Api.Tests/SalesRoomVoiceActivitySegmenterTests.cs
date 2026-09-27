using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Sales;
using VirtualCompany.Infrastructure.Platform;
using VirtualCompany.Application.Agents;
using Xunit.Abstractions;

namespace VirtualCompany.Api.Tests;

public sealed class SalesRoomVoiceActivitySegmenterTests(ITestOutputHelper output)
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
            new SpeechAwareLocalVoiceActivityDetector(new WebRtcSpeechFrameClassifierFactory()));

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
    public void Queued_audio_waits_for_active_human_speech_to_end()
    {
        var segmenter = Create(new SequenceDetector(
            Enumerable.Repeat(true, 4).Concat(Enumerable.Repeat(false, 30))));
        Assert.False(segmenter.HasActiveSpeech);
        for (var sequence = 0; sequence < 4; sequence++)
            segmenter.Push(Frame(Speaker, sequence, 1200));
        Assert.True(segmenter.HasActiveSpeech);
        for (var sequence = 4; sequence < 34; sequence++)
            segmenter.Push(Frame(Speaker, sequence, 0));
        Assert.False(segmenter.HasActiveSpeech);
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
    public void Replacing_a_microphone_disposes_the_old_track_classifier()
    {
        var detector = new TrackingDetector();
        var segmenter = Create(detector);
        segmenter.Push(Frame(Speaker, 0, 0));
        segmenter.Push(Frame(Speaker, 1, 0) with { TrackGeneration = 4 });
        Assert.Equal(2, detector.Created);
        Assert.Equal(1, detector.Disposed);
        Assert.False(segmenter.Push(Frame(Speaker, 2, 0)).SpeechStarted);
        Assert.Equal(2, detector.Created);
        segmenter.ClearAll();
        Assert.Equal(2, detector.Disposed);
    }

    [Fact]
    public void Dc_bias_and_quiet_noise_do_not_interrupt_with_the_real_detector()
    {
        var options = new SalesRoomAgentOptions();
        var segmenter = new SalesRoomVoiceActivitySegmenter(options,
            new SpeechAwareLocalVoiceActivityDetector(new WebRtcSpeechFrameClassifierFactory()));
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
    public void Brief_noise_burst_and_sustained_tone_do_not_interrupt()
    {
        var options = new SalesRoomAgentOptions();
        var segmenter = new SalesRoomVoiceActivitySegmenter(options,
            new SpeechAwareLocalVoiceActivityDetector(new WebRtcSpeechFrameClassifierFactory()));
        var wave = Enumerable.Range(0, 480).Select(n => (short)(2000 * Math.Sin(2 * Math.PI * 200 * n / 24000))).ToArray();
        for (var i = 0; i < 40; i++)
        {
            // 100 ms burst, silence, then a non-speech steady tone.
            var result = segmenter.Push(Frame(Speaker, i, 0) with { Samples = i < 5 || i >= 20 ? wave : new short[480] });
            Assert.False(result.SpeechStarted);
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

    private static SalesRoomVoiceActivitySegmenter Create(ILocalVoiceActivityDetector detector) =>
        new(new SalesRoomAgentOptions { MinimumSpeechMilliseconds = 80 }, detector);

    [Fact]
    public void Replaced_microphone_track_cannot_confirm_an_older_utterance()
    {
        var segmenter = Create(new SequenceDetector(Enumerable.Repeat(false, 4)));
        segmenter.Push(Frame(Speaker, 0, 0));
        Assert.True(segmenter.IsCurrentTrack(Speaker, "track-1", 3));
        segmenter.Push(Frame(Speaker, 1, 0, "track-2") with { TrackGeneration = 4 });
        Assert.False(segmenter.IsCurrentTrack(Speaker, "track-1", 3));
        Assert.True(segmenter.IsCurrentTrack(Speaker, "track-2", 4));
        segmenter.ClearAll();
    }

    private static SalesRoomAudioFrame Frame(Guid participant, long sequence, short amplitude, string track = "track-1") =>
        new(participant, track, 3, sequence, DateTimeOffset.UnixEpoch.AddMilliseconds(sequence * 20 + 20),
            24_000, Enumerable.Repeat(amplitude, 480).ToArray());

    private sealed class SequenceDetector(IEnumerable<bool> values) : ILocalVoiceActivityDetector
    {
        private readonly Queue<bool> values = new(values);
        public bool IsSpeech(ReadOnlySpan<short> samples, int sampleRate) => values.Count > 0 && values.Dequeue();
    }

    [Fact]
    public void Production_classifier_rejects_continuous_fan_like_noise()
    {
        var segmenter = new SalesRoomVoiceActivitySegmenter(new SalesRoomAgentOptions(),
            new SpeechAwareLocalVoiceActivityDetector(new WebRtcSpeechFrameClassifierFactory()));
        var random = new Random(42);
        for (var i = 0; i < 100; i++)
        {
            var samples = Enumerable.Range(0, 480)
                .Select(n => (short)(random.Next(-900, 901) + 550 * Math.Sin(2 * Math.PI * 110 * n / 24_000)))
                .ToArray();
            Assert.False(segmenter.Push(Frame(Speaker, i, 0) with { Samples = samples }).SpeechStarted);
        }
        Assert.Equal(0, segmenter.ForwardedMilliseconds);
        segmenter.ClearAll();
    }

    [Fact]
    public void Stationary_fan_cannot_interrupt_even_when_the_vad_misclassifies_every_frame()
    {
        var segmenter = new SalesRoomVoiceActivitySegmenter(new SalesRoomAgentOptions(),
            new SpeechAwareLocalVoiceActivityDetector(new AlwaysSpeechClassifierFactory()));
        var random = new Random(123);
        for (var i = 0; i < 100; i++)
        {
            var samples = Enumerable.Range(0, 480)
                .Select(n => (short)(random.Next(-5_000, 5_001) +
                    1_000 * Math.Sin(2 * Math.PI * 120 * (i * 480 + n) / 24_000)))
                .ToArray();
            var result = segmenter.Push(Frame(Speaker, i, 0) with { Samples = samples });
            Assert.False(result.SpeechStarted);
            Assert.Null(result.Utterance);
        }
        Assert.Equal(0, segmenter.ForwardedMilliseconds);
        segmenter.ClearAll();
    }

    [Fact]
    public void Fan_spin_up_cannot_interrupt_even_when_the_vad_misclassifies_every_frame()
    {
        var segmenter = new SalesRoomVoiceActivitySegmenter(new SalesRoomAgentOptions(),
            new SpeechAwareLocalVoiceActivityDetector(new AlwaysSpeechClassifierFactory()));
        var random = new Random(456);
        for (var i = 0; i < 100; i++)
        {
            var amplitude = Math.Min(5_000, 200 + i * 250);
            var samples = Enumerable.Range(0, 480)
                .Select(_ => (short)random.Next(-amplitude, amplitude + 1)).ToArray();
            Assert.False(segmenter.Push(Frame(Speaker, i, 0) with { Samples = samples }).SpeechStarted);
        }
        segmenter.ClearAll();
    }

    [Fact]
    public void Spoken_interruption_still_confirms_over_a_running_fan()
    {
        var speech = ReadFixture("stop-alex-en.wav");
        var segmenter = new SalesRoomVoiceActivitySegmenter(new SalesRoomAgentOptions(),
            new SpeechAwareLocalVoiceActivityDetector(new WebRtcSpeechFrameClassifierFactory()));
        var random = new Random(789);
        var confirmed = false;
        for (var i = 0; i < 90; i++)
        {
            var samples = Enumerable.Range(0, 480).Select(n =>
            {
                var sample = random.Next(-900, 901);
                var speechOffset = (i - 20) * 480 + n;
                if (speechOffset >= 0 && speechOffset < speech.Length) sample += speech[speechOffset];
                return (short)Math.Clamp(sample, short.MinValue, short.MaxValue);
            }).ToArray();
            var result = segmenter.Push(Frame(Speaker, i, 0) with { Samples = samples });
            if (i < 20) Assert.False(result.SpeechStarted);
            confirmed |= result.SpeechStarted;
        }
        Assert.True(confirmed);
        segmenter.ClearAll();
    }

    [Fact]
    public void Production_classifier_rejects_keyboard_clicks_and_steady_tone()
    {
        var segmenter = new SalesRoomVoiceActivitySegmenter(new SalesRoomAgentOptions(),
            new SpeechAwareLocalVoiceActivityDetector(new WebRtcSpeechFrameClassifierFactory()));
        for (var i = 0; i < 100; i++)
        {
            var samples = i < 20 ? new short[480] : Enumerable.Range(0, 480)
                .Select(n => (short)(1100 * Math.Sin(2 * Math.PI * 200 * n / 24_000) +
                    (i % 25 == 0 && n < 8 ? 9000 : 0))).ToArray();
            Assert.False(segmenter.Push(Frame(Speaker, i, 0) with { Samples = samples }).SpeechStarted);
        }
        segmenter.ClearAll();
    }

    [Theory]
    [InlineData("stop-alex-en.wav")]
    [InlineData("stanna-alex-sv.wav")]
    public void Production_classifier_confirms_short_spoken_interruption(string fixture)
    {
        var pcm = ReadFixture(fixture);
        var segmenter = new SalesRoomVoiceActivitySegmenter(new SalesRoomAgentOptions(),
            new SpeechAwareLocalVoiceActivityDetector(new WebRtcSpeechFrameClassifierFactory()));
        var confirmed = false;
        var firstAudibleFrame = -1;
        var confirmedFrame = -1;
        var sequence = 0;
        for (var offset = 0; offset + 480 <= pcm.Length; offset += 480)
        {
            var frame = pcm[offset..(offset + 480)];
            if (firstAudibleFrame < 0 && frame.Any(sample => Math.Abs((int)sample) >= 200))
                firstAudibleFrame = sequence;
            var result = segmenter.Push(Frame(Speaker, sequence++, 0) with { Samples = frame });
            confirmed |= result.SpeechStarted;
            if (result.SpeechStarted) confirmedFrame = sequence - 1;
        }
        Assert.True(confirmed);
        Assert.True(firstAudibleFrame >= 0);
        var onsetMilliseconds = (confirmedFrame - firstAudibleFrame) * 20;
        output.WriteLine($"{fixture}: controlled onset-to-confirmed-command {onsetMilliseconds} ms");
        Assert.InRange(onsetMilliseconds, 0, 300);
        segmenter.ClearAll();
    }

    private static short[] ReadFixture(string name)
    {
        using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "SpeechInterruption", name));
        using var reader = new BinaryReader(file);
        Assert.Equal("RIFF", new string(reader.ReadChars(4)));
        _ = reader.ReadInt32();
        Assert.Equal("WAVE", new string(reader.ReadChars(4)));
        while (file.Position < file.Length)
        {
            var chunk = new string(reader.ReadChars(4));
            var bytes = reader.ReadInt32();
            if (chunk == "data")
            {
                Assert.Equal(0, bytes % 2);
                var samples = new short[bytes / 2];
                for (var i = 0; i < samples.Length; i++) samples[i] = reader.ReadInt16();
                return samples;
            }
            file.Position += bytes + (bytes & 1);
        }
        throw new InvalidDataException("Missing PCM data in speech fixture.");
    }

    private sealed class TrackingDetector : ILocalVoiceActivityDetector
    {
        public int Created, Disposed;
        public bool IsSpeech(ReadOnlySpan<short> samples, int sampleRate) => false;
        public ILocalVoiceActivityDetector CreateForTrack() { Created++; return new Track(this); }
        private sealed class Track(TrackingDetector owner) : ILocalVoiceActivityDetector
        {
            public bool IsSpeech(ReadOnlySpan<short> samples, int sampleRate) => false;
            public void Dispose() => owner.Disposed++;
        }
    }

    private sealed class AlwaysSpeechClassifierFactory : ISpeechFrameClassifierFactory
    {
        public ISpeechFrameClassifier Create() => new AlwaysSpeechClassifier();
        private sealed class AlwaysSpeechClassifier : ISpeechFrameClassifier
        {
            public bool IsSpeech(ReadOnlySpan<short> samples, int sampleRate) => true;
            public void Dispose() { }
        }
    }
}
