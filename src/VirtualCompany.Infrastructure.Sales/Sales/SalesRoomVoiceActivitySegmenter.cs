using VirtualCompany.Application.Sales;

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
    public int MinimumSpeechMilliseconds { get; set; } = 80;
    public int MaximumUtteranceSeconds { get; set; } = 30;
    public int MaximumInputAudioSeconds { get; set; } = 1800;
    public int MaximumOutputAudioSeconds { get; set; } = 1800;
    public int MaximumSessionMinutes { get; set; } = 60;
    public int PlaybackStopAcknowledgementTimeoutMilliseconds { get; set; } = 750;
    public int OrganizerDisconnectGraceSeconds { get; set; } = 10;
    public int ReconciliationIntervalSeconds { get; set; } = 10;
    public int MaximumActiveAgentsGlobal { get; set; } = 50;
    public int MaximumActiveAgentsPerCompany { get; set; } = 2;
    public int SpeechRmsThreshold { get; set; } = 320;
    public int SpeechPeakThreshold { get; set; } = 700;
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
}

internal sealed class EnergyLocalVoiceActivityDetector(Microsoft.Extensions.Options.IOptions<SalesRoomAgentOptions> configured)
    : ILocalVoiceActivityDetector
{
    public bool IsSpeech(ReadOnlySpan<short> samples, int sampleRate)
    {
        if (sampleRate != 24_000 || samples.IsEmpty) throw new SalesRoomVadException("vad_input_invalid");
        double sum = 0; var peak = 0;
        foreach (var sample in samples)
        {
            var value = Math.Abs((int)sample); peak = Math.Max(peak, value); sum += (double)value * value;
        }
        var rms = Math.Sqrt(sum / samples.Length);
        return peak >= configured.Value.SpeechPeakThreshold && rms >= configured.Value.SpeechRmsThreshold;
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
    public long ReceivedMilliseconds { get; private set; }
    public long DetectedSpeechMilliseconds { get; private set; }
    public long ForwardedMilliseconds { get; private set; }

    public SalesRoomVadResult Push(SalesRoomAudioFrame frame)
    {
        if (frame.SampleRate != 24_000 || frame.Samples.IsEmpty) throw new SalesRoomVadException("vad_input_invalid");
        var duration = checked((int)Math.Round(frame.Samples.Length * 1000d / frame.SampleRate));
        if (duration is < 1 or > 100) throw new SalesRoomVadException("vad_frame_duration_invalid");
        ReceivedMilliseconds += duration;
        var key = (frame.ParticipantId, frame.TrackId, frame.TrackGeneration);
        if (!tracks.TryGetValue(key, out var state)) tracks[key] = state = new TrackState();
        bool speech;
        try { speech = detector.IsSpeech(frame.Samples.Span, frame.SampleRate); }
        catch (SalesRoomVadException) { throw; }
        catch { throw new SalesRoomVadException("vad_detector_failed"); }

        if (!state.Active)
        {
            state.PreRoll.Enqueue(new(frame, duration, speech));
            state.PreRollMilliseconds += duration;
            while (state.PreRollMilliseconds > options.PreRollMilliseconds && state.PreRoll.TryDequeue(out var old))
                state.PreRollMilliseconds -= old.DurationMilliseconds;
            state.ConsecutiveSpeechMilliseconds = speech ? state.ConsecutiveSpeechMilliseconds + duration : 0;
            if (state.ConsecutiveSpeechMilliseconds < options.MinimumSpeechMilliseconds)
                return new(false, null);
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

    public void Clear(Guid participantId)
    {
        foreach (var key in tracks.Keys.Where(x => x.ParticipantId == participantId).ToArray()) tracks.Remove(key);
    }
    public void ClearAll() => tracks.Clear();

    private SalesRoomDetectedUtterance? CompleteIfBounded((Guid ParticipantId, string TrackId, long Generation) key, TrackState state) =>
        state.Frames.Sum(x => x.DurationMilliseconds) >= options.MaximumUtteranceSeconds * 1000 ? Complete(key, state) : null;
    private SalesRoomDetectedUtterance? Complete((Guid ParticipantId, string TrackId, long Generation) key, TrackState state)
    {
        var frames = state.Frames.ToArray(); tracks.Remove(key);
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
        public int PreRollMilliseconds, ConsecutiveSpeechMilliseconds, SpeechMilliseconds, TrailingMilliseconds;
        public bool Active, Overlapped;
    }
}

internal sealed class SalesRoomVadException(string code) : Exception("Local speech detection failed.")
{ public string Code { get; } = code; }
