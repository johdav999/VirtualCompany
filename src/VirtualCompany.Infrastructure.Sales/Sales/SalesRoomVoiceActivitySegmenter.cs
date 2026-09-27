using VirtualCompany.Application.Sales;
using VirtualCompany.Application.Agents;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesRoomAgentOptions
{
    public const string SectionName = "SalesRoomAgent";
    public bool Enabled { get; set; }
    public bool EmergencyDisabled { get; set; }
    public bool DrainEnabled { get; set; }
    public int LeaseSeconds { get; set; } = 30;
    public int RenewalSeconds { get; set; } = 10;
    public int PreRollMilliseconds { get; set; } = 240;
    public int TrailingSilenceMilliseconds { get; set; } = 600;
    public int MinimumSpeechMilliseconds { get; set; } = 240;
    public int MaximumUtteranceSeconds { get; set; } = 30;
    public int MaximumInputAudioSeconds { get; set; } = 1800;
    public int MaximumOutputAudioSeconds { get; set; } = 1800;
    public int MaximumSessionMinutes { get; set; } = 60;
    public int PlaybackStopAcknowledgementTimeoutMilliseconds { get; set; } = 750;
    public int OrganizerDisconnectGraceSeconds { get; set; } = 10;
    public int ReconciliationIntervalSeconds { get; set; } = 10;
    public int MaximumActiveAgentsGlobal { get; set; } = 50;
    public int MaximumActiveAgentsPerCompany { get; set; } = 2;
    public decimal MaximumInputTokenCostPerMillionUsd { get; set; }
    public decimal MaximumOutputTokenCostPerMillionUsd { get; set; }
    public decimal TranscriptionCostPerMinuteUsd { get; set; }
    public decimal MaximumSpendPerCallUsd { get; set; }
    public decimal MaximumMonthlySpendPerCompanyUsd { get; set; }
    public string CostCurrency { get; set; } = "USD";
    public DateTime ProviderRateCheckedUtc { get; set; }
    public int ProviderRateMaximumAgeDays { get; set; } = 31;
    public string ProviderRateCardReference { get; set; } = "";
}

internal interface ILocalVoiceActivityDetector
{
    bool IsSpeech(ReadOnlySpan<short> samples, int sampleRate);
    bool HasSpeechLikeOnset => true;
    ILocalVoiceActivityDetector CreateForTrack() => this;
    void Dispose() { }
}

internal sealed class SpeechAwareLocalVoiceActivityDetector(ISpeechFrameClassifierFactory factory)
    : ILocalVoiceActivityDetector
{
    public bool IsSpeech(ReadOnlySpan<short> samples, int sampleRate) =>
        throw new InvalidOperationException("A speech detector must be created for a track.");

    public ILocalVoiceActivityDetector CreateForTrack() => new TrackDetector(factory.Create());

    private sealed class TrackDetector(ISpeechFrameClassifier classifier) : ILocalVoiceActivityDetector
    {
        private readonly Queue<double> recentEnergy = new();
        public bool HasSpeechLikeOnset
        {
            get
            {
                // WebRTC VAD can mistake a steady broadband fan for speech. Human speech
                // has a changing syllabic envelope; a stationary motor does not.
                if (recentEnergy.Count < 12) return false;
                var last = recentEnergy.TakeLast(16).ToArray();
                return last.Max() >= (last.Min() + 1) * 2.5;
            }
        }
        public bool IsSpeech(ReadOnlySpan<short> samples, int sampleRate)
        {
            var speech = classifier.IsSpeech(samples, sampleRate);
            for (var offset = 0; offset < samples.Length; offset += 240)
            {
                var chunk = samples.Slice(offset, Math.Min(240, samples.Length - offset));
                double energy = 0;
                foreach (var sample in chunk) energy += (double)sample * sample;
                recentEnergy.Enqueue(Math.Sqrt(energy / chunk.Length));
                while (recentEnergy.Count > 24) recentEnergy.Dequeue();
            }
            return speech;
        }
        public void Dispose() => classifier.Dispose();
    }
}

internal sealed record SalesRoomDetectedUtterance(Guid ParticipantId, string TrackId, long TrackGeneration,
    DateTimeOffset StartedAt, DateTimeOffset EndedAt, bool Overlapped, int SpeechMilliseconds,
    int ForwardedMilliseconds, ReadOnlyMemory<short> Samples);
internal sealed record SalesRoomVadResult(bool SpeechStarted, SalesRoomDetectedUtterance? Utterance);

internal sealed class SalesRoomVoiceActivitySegmenter(
    SalesRoomAgentOptions options,
    ILocalVoiceActivityDetector detector)
{
    private readonly Dictionary<(Guid ParticipantId, string TrackId, long Generation), TrackState> tracks = new();
    private readonly Dictionary<Guid, long> latestGenerations = new();
    private readonly object gate = new();
    public long ReceivedMilliseconds { get; private set; }
    public long DetectedSpeechMilliseconds { get; private set; }
    public long ForwardedMilliseconds { get; private set; }
    public bool HasActiveSpeech { get { lock (gate) return tracks.Values.Any(x => x.Active); } }

    public bool IsCurrentTrack(Guid participantId, string trackId, long generation)
    {
        lock (gate)
            return latestGenerations.TryGetValue(participantId, out var latest) && latest == generation &&
                tracks.ContainsKey((participantId, trackId, generation));
    }

    public SalesRoomVadResult Push(SalesRoomAudioFrame frame)
    {
        lock (gate) return PushCore(frame);
    }

    private SalesRoomVadResult PushCore(SalesRoomAudioFrame frame)
    {
        if (frame.SampleRate != 24_000 || frame.Samples.IsEmpty) throw new SalesRoomVadException("vad_input_invalid");
        var duration = checked((int)Math.Round(frame.Samples.Length * 1000d / frame.SampleRate));
        if (duration is < 1 or > 100) throw new SalesRoomVadException("vad_frame_duration_invalid");
        ReceivedMilliseconds += duration;
        var key = (frame.ParticipantId, frame.TrackId, frame.TrackGeneration);
        if (latestGenerations.TryGetValue(frame.ParticipantId, out var latestGeneration) &&
            frame.TrackGeneration < latestGeneration) return new(false, null);
        latestGenerations[frame.ParticipantId] = frame.TrackGeneration;
        if (!tracks.TryGetValue(key, out var state))
        {
            // A replaced microphone gets a fresh adaptive model, and stale generations
            // cannot retain audio/classifier state for the rest of a long meeting.
            foreach (var stale in tracks.Keys.Where(x => x.ParticipantId == frame.ParticipantId &&
                         x.Generation != frame.TrackGeneration).ToArray())
            {
                tracks[stale].Detector?.Dispose(); tracks.Remove(stale);
            }
            if (tracks.Count >= 32) throw new SalesRoomVadException("vad_track_limit");
            tracks[key] = state = new TrackState
                { Detector = detector.CreateForTrack(), Onset = new SpeechOnsetPolicy(options.MinimumSpeechMilliseconds) };
        }
        if (state.LastSequence is long lastSequence && frame.Sequence <= lastSequence)
            return new(false, null);
        if (!state.Active && state.LastReceivedAt is { } lastReceived &&
            (frame.ReceivedAt - lastReceived > TimeSpan.FromMilliseconds(Math.Max(100, duration * 2)) ||
             frame.Sequence != state.LastSequence + 1))
        {
            // DTX, mute and packet gaps must not combine isolated noises into a speech onset.
            state.Onset!.Reset();
            state.PreRoll.Clear(); state.PreRollMilliseconds = 0;
        }
        state.LastSequence = frame.Sequence;
        state.LastReceivedAt = frame.ReceivedAt;
        bool speech;
        try { speech = state.Detector!.IsSpeech(frame.Samples.Span, frame.SampleRate); }
        catch (SalesRoomVadException) { throw; }
        catch { throw new SalesRoomVadException("vad_detector_failed"); }

        if (!state.Active)
        {
            state.PreRoll.Enqueue(new(frame, duration, speech));
            state.PreRollMilliseconds += duration;
            while (state.PreRollMilliseconds > Math.Max(options.PreRollMilliseconds, options.MinimumSpeechMilliseconds) && state.PreRoll.TryDequeue(out var old))
                state.PreRollMilliseconds -= old.DurationMilliseconds;
            if (!state.Onset!.Observe(speech, duration))
                return new(false, null);
            if (!state.Detector!.HasSpeechLikeOnset)
            {
                state.Onset.Reset();
                return new(false, null);
            }
            state.Active = true; state.Overlapped = tracks.Values.Any(x => !ReferenceEquals(x, state) && x.Active);
            state.Frames.AddRange(state.PreRoll); state.PreRoll.Clear(); state.PreRollMilliseconds = 0;
            state.SpeechMilliseconds = state.Frames.Where(x => x.Speech).Sum(x => x.DurationMilliseconds);
            state.TrailingMilliseconds = speech ? 0 : duration;
            return new(true, CompleteIfBounded(key, state));
        }

        var buffered = new BufferedFrame(frame, duration, speech);
        state.Frames.Add(buffered);
        if (speech) { state.SpeechMilliseconds += duration; state.TrailingMilliseconds = 0; }
        else state.TrailingMilliseconds += duration;
        if (state.TrailingMilliseconds >= options.TrailingSilenceMilliseconds ||
            state.Frames.Sum(x => x.DurationMilliseconds) >= options.MaximumUtteranceSeconds * 1000)
            return new(false, Complete(key, state));
        return new(false, null);
    }

    public IReadOnlyList<SalesRoomDetectedUtterance> FlushExpired(DateTimeOffset now)
    {
        lock (gate)
        {
            var expired = tracks.Where(x => x.Value.Active && x.Value.Frames.Count > 0 &&
                    now - x.Value.Frames[^1].Frame.ReceivedAt >= TimeSpan.FromMilliseconds(options.TrailingSilenceMilliseconds))
                .Select(x => x.Key).ToArray();
            return expired.Select(key => Complete(key, tracks[key])).Where(x => x is not null).ToArray()!;
        }
    }
    public void Clear(Guid participantId)
    {
        lock (gate)
        {
            latestGenerations.Remove(participantId);
            foreach (var key in tracks.Keys.Where(x => x.ParticipantId == participantId).ToArray())
            {
                tracks[key].Detector?.Dispose(); tracks.Remove(key);
            }
        }
    }
    public void ClearAll() { lock (gate) { foreach (var track in tracks.Values) track.Detector?.Dispose(); tracks.Clear(); latestGenerations.Clear(); } }

    private SalesRoomDetectedUtterance? CompleteIfBounded((Guid ParticipantId, string TrackId, long Generation) key, TrackState state) =>
        state.Frames.Sum(x => x.DurationMilliseconds) >= options.MaximumUtteranceSeconds * 1000 ? Complete(key, state) : null;
    private SalesRoomDetectedUtterance? Complete((Guid ParticipantId, string TrackId, long Generation) key, TrackState state)
    {
        var frames = state.Frames.ToArray();
        tracks[key] = new TrackState
        {
            Detector = state.Detector,
            Onset = new SpeechOnsetPolicy(options.MinimumSpeechMilliseconds),
            LastSequence = state.LastSequence, LastReceivedAt = state.LastReceivedAt
        };
        if (state.SpeechMilliseconds < options.MinimumSpeechMilliseconds || frames.Length == 0) return null;
        var samples = new short[frames.Sum(x => x.Frame.Samples.Length)];
        var offset = 0;
        foreach (var frame in frames) { frame.Frame.Samples.Span.CopyTo(samples.AsSpan(offset)); offset += frame.Frame.Samples.Length; }
        var forwarded = frames.Sum(x => x.DurationMilliseconds);
        DetectedSpeechMilliseconds += state.SpeechMilliseconds; ForwardedMilliseconds += forwarded;
        return new(key.ParticipantId, key.TrackId, key.Generation,
            frames[0].Frame.ReceivedAt - TimeSpan.FromMilliseconds(frames[0].DurationMilliseconds),
            frames[^1].Frame.ReceivedAt, state.Overlapped, state.SpeechMilliseconds, forwarded, samples);
    }

    private sealed record BufferedFrame(SalesRoomAudioFrame Frame, int DurationMilliseconds, bool Speech);
    private sealed class TrackState
    {
        public Queue<BufferedFrame> PreRoll { get; } = new();
        public List<BufferedFrame> Frames { get; } = [];
        public int PreRollMilliseconds, SpeechMilliseconds, TrailingMilliseconds;
        public bool Active, Overlapped;
        public long? LastSequence;
        public DateTimeOffset? LastReceivedAt;
        public ILocalVoiceActivityDetector? Detector;
        public SpeechOnsetPolicy? Onset;
    }
}

internal sealed class SalesRoomVadException(string code) : Exception("Local speech detection failed.")
{ public string Code { get; } = code; }
