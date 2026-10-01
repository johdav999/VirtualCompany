using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Agents;

namespace VirtualCompany.Infrastructure.Sales;

internal sealed record SalesRoomSemanticScope(Guid ParticipantId, string TrackId, long TrackGeneration,
    long ParticipantGeneration, long ConsentVersion, bool Retain, long TurnGeneration,
    long ResponseGeneration, bool BeganDuringPresentation);
internal sealed record SalesRoomSemanticAudio(bool ClearBuffer, ReadOnlyMemory<short> Samples);
internal sealed record SalesRoomSemanticTurn(SalesRoomSemanticScope Scope, SalesRoomDetectedUtterance Utterance, string Text);

// One serialized input lane per room worker. Provider offsets refer to ALL submitted audio
// in the session, including silence. A completed item must fit wholly inside one trusted
// provenance window; no FIFO assignment of server commits to local utterances is allowed.
internal sealed class SalesRoomSemanticAudioInput(SalesRoomAgentOptions options, ILocalVoiceActivityDetector detector)
    : IDisposable
{
    private sealed class Track(ILocalVoiceActivityDetector detector, int minimum)
    {
        public ILocalVoiceActivityDetector Detector = detector;
        public SpeechOnsetPolicy Onset = new(minimum);
        public Queue<SalesRoomAudioFrame> PreRoll = new();
        public long Sequence = -1;
        public DateTimeOffset LastAt;
        public SalesRoomSemanticScope? Scope;
    }
    private sealed class Window(SalesRoomSemanticScope scope, long start, DateTimeOffset at)
    {
        public SalesRoomSemanticScope Scope = scope;
        public long Start = start, End = start;
        public DateTimeOffset At = at, LastAt = at, LastSpeech = at;
        public bool Valid = true;
        public int SpeechMilliseconds;
    }
    private sealed class Item(DateTime at)
    {
        public DateTime At = at;
        public long? Start, End;
        public bool Committed;
        public string? Text;
    }
    private readonly Dictionary<(Guid, string, long), Track> tracks = [];
    private readonly List<Window> windows = [];
    private readonly Dictionary<string, Item> items = new(StringComparer.Ordinal);
    private readonly HashSet<string> finished = new(StringComparer.Ordinal);
    private Window? active;
    private long submittedSamples;
    public long ReceivedMilliseconds { get; private set; }
    public long DetectedSpeechMilliseconds { get; private set; }
    public long ForwardedMilliseconds => submittedSamples / 24;
    public int PendingCount => items.Count;
    public long Discontinuities { get; private set; }
    public string ObservationReason { get; private set; } = "waiting";
    public SalesRoomSemanticScope? ActiveScope => active?.Scope;
    // Local VAD keeps a provenance window open, including noise and trailing audio.
    // Only provider-confirmed speech may fence a semantically complete command.
    public bool HasPendingSpeech => items.Values.Any(x => x.Start.HasValue && !x.End.HasValue);
    public bool IsCurrentTrack(Guid participant, string track, long generation) =>
        tracks.ContainsKey((participant, track, generation));

    public SalesRoomSemanticAudio Push(SalesRoomAudioFrame frame, SalesRoomSemanticScope scope)
    {
        if (frame.SampleRate != 24000 || frame.Samples.Length is < 24 or > 2400)
            throw new SalesRoomVadException("vad_frame_duration_invalid");
        if (frame.ParticipantId != scope.ParticipantId || frame.TrackId != scope.TrackId || frame.TrackGeneration != scope.TrackGeneration ||
            frame.ParticipantId == Guid.Empty || string.IsNullOrWhiteSpace(frame.TrackId) || frame.TrackId.Length > 200 ||
            frame.TrackGeneration < 1 || frame.Sequence < 0 || !scope.Retain)
            throw new SalesRoomVadException("semantic_provenance_invalid");
        var duration = frame.Samples.Length / 24;
        ReceivedMilliseconds += duration;
        if (active is not null && scope with { ResponseGeneration = active.Scope.ResponseGeneration,
                BeganDuringPresentation = active.Scope.BeganDuringPresentation } == active.Scope)
            scope = active.Scope; // Approved narration may advance while the same human turn is speaking.
        var key = (frame.ParticipantId, frame.TrackId, frame.TrackGeneration);
        if (tracks.Keys.Any(x => x.Item1 == frame.ParticipantId && x.Item3 > frame.TrackGeneration)) return new(false, default);
        var clear = false;
        foreach (var old in tracks.Keys.Where(x => x.Item1 == frame.ParticipantId && x != key).ToArray())
        { tracks[old].Detector.Dispose(); tracks.Remove(old); clear |= Reset(); }
        if (!tracks.TryGetValue(key, out var track))
        {
            if (tracks.Count >= 32) throw new SalesRoomVadException("vad_track_limit");
            tracks[key] = track = new(detector.CreateForTrack(), options.MinimumSpeechMilliseconds);
        }
        if (frame.Sequence <= track.Sequence) return new(clear, default);
        if (track.Scope != scope || track.Sequence >= 0 &&
            (frame.Sequence != track.Sequence + 1 || active is null && frame.ReceivedAt - track.LastAt > TimeSpan.FromMilliseconds(250)))
        {
            if (track.Sequence >= 0) Discontinuities++;
            if (active?.Scope.ParticipantId == scope.ParticipantId) clear |= Reset();
            track.Onset.Reset(); track.PreRoll.Clear();
        }
        track.Sequence = frame.Sequence; track.LastAt = frame.ReceivedAt; track.Scope = scope;
        var speech = track.Detector.IsSpeech(frame.Samples.Span, frame.SampleRate);
        if (speech) DetectedSpeechMilliseconds += duration;
        if (active?.Scope == scope)
        {
            if (speech) { active.LastSpeech = frame.ReceivedAt; active.SpeechMilliseconds += duration; }
            active.LastAt = frame.ReceivedAt;
            return new(clear, Forward(frame.Samples)); // Includes intra-sentence silence unchanged.
        }
        track.PreRoll.Enqueue(frame with { Samples = frame.Samples.ToArray() });
        while (track.PreRoll.Sum(x => x.Samples.Length) > Math.Max(500, options.PreRollMilliseconds + options.MinimumSpeechMilliseconds) * 24)
            track.PreRoll.Dequeue();
        if (!track.Onset.Observe(speech, duration)) return new(clear, default);
        track.Onset.Reset();
        if (!track.Detector.HasSpeechLikeOnset) return new(clear, default);
        if (active is not null)
        {
            // Two active speakers cannot be attributed safely. Discard the mixed turn,
            // keep listening and require a new onset. Never forward the second speaker.
            clear |= Reset(); track.PreRoll.Clear(); return new(clear, default);
        }
        var frames = track.PreRoll.ToArray(); track.PreRoll.Clear();
        var samples = frames.SelectMany(x => x.Samples.ToArray()).ToArray();
        var started = frames[0].ReceivedAt - TimeSpan.FromMilliseconds(frames[0].Samples.Length / 24d);
        active = new(scope, submittedSamples, started) { LastAt = frame.ReceivedAt, LastSpeech = frame.ReceivedAt,
            SpeechMilliseconds = options.MinimumSpeechMilliseconds };
        windows.Add(active);
        if (windows.Count > 128) windows.RemoveAt(0);
        return new(clear, Forward(samples));
    }

    // DTX may suppress silent frames. Supply bounded silence so provider-owned completion
    // can finish; it is billed/forwarded input, never speech evidence or a manual commit.
    public SalesRoomSemanticAudio Tick(DateTimeOffset now)
    {
        if (active is null) return new(false, default);
        if (now - active.At > TimeSpan.FromSeconds(options.MaximumUtteranceSeconds + 12))
            return new(Reset(), default);
        var gap = now - active.LastAt;
        if (gap < TimeSpan.FromMilliseconds(100)) return new(false, default);
        var count = (int)Math.Min(4800, gap.TotalMilliseconds * 24);
        active.LastAt = active.LastAt.AddMilliseconds(count / 24d);
        return new(false, Forward(new short[count]));
    }

    public bool Reset()
    {
        var hadInput = active is not null;
        if (active is not null) active.Valid = false;
        active = null;
        foreach (var track in tracks.Values) { track.PreRoll.Clear(); track.Onset.Reset(); }
        return hadInput;
    }
    public bool Revoke(Guid participant)
    {
        var reset = active?.Scope.ParticipantId == participant && Reset();
        foreach (var window in windows.Where(x => x.Scope.ParticipantId == participant)) window.Valid = false;
        foreach (var key in tracks.Keys.Where(x => x.Item1 == participant).ToArray())
        { tracks[key].Detector.Dispose(); tracks.Remove(key); }
        return reset;
    }

    public SalesRoomSemanticTurn? Observe(RealtimeAgentEvent value, DateTime now)
    {
        ObservationReason = "waiting";
        if (value.ItemId is not { Length: > 0 and <= 200 } id || finished.Contains(id)) return null;
        if (value.Type is not (RealtimeAgentEventTypes.ParticipantSpeechStarted or RealtimeAgentEventTypes.ParticipantSpeechStopped or
            RealtimeAgentEventTypes.InputCommitted or RealtimeAgentEventTypes.ParticipantTranscriptCompleted or
            RealtimeAgentEventTypes.ParticipantTranscriptFailed)) return null;
        if (finished.Count >= 4096 || items.Count >= 32 && !items.ContainsKey(id))
            throw new SalesRoomVadException("semantic_input_backpressure");
        if (!items.TryGetValue(id, out var item)) items[id] = item = new(now);
        if (value.Type == RealtimeAgentEventTypes.ParticipantTranscriptFailed) { ObservationReason = "provider_failed"; Finish(id); return null; }
        if (value.Type == RealtimeAgentEventTypes.ParticipantSpeechStarted) item.Start ??= value.AudioStartMilliseconds * 24L;
        if (value.Type == RealtimeAgentEventTypes.ParticipantSpeechStopped) item.End ??= value.AudioEndMilliseconds * 24L;
        if (value.Type == RealtimeAgentEventTypes.InputCommitted) item.Committed = true;
        if (value.Type == RealtimeAgentEventTypes.ParticipantTranscriptCompleted) item.Text ??= value.Text ?? "";
        if (!item.Committed || item.Start is not long start || item.End is not long end || item.Text is null) return null;
        Finish(id);
        var window = windows.LastOrDefault(x => x.Valid && x.Start <= start && x.End >= end && end > start);
        if (window is null) { ObservationReason = "no_valid_provenance_window"; return null; }
        var began = window.At.AddMilliseconds((start - window.Start) / 24d);
        var ended = window.At.AddMilliseconds((end - window.Start) / 24d);
        if (active == window && active.LastSpeech <= ended) active = null;
        if (string.IsNullOrWhiteSpace(item.Text) || item.Text.Length > 2000 || window.SpeechMilliseconds < options.MinimumSpeechMilliseconds)
        { ObservationReason = "invalid_transcript_or_insufficient_speech"; return null; }
        // Provider item completion alone never grants interruption; routing checks completeness,
        // consent, current generation and the allowed action after this correlation succeeds.
        ObservationReason = "correlated";
        return new(window.Scope, new(window.Scope.ParticipantId, window.Scope.TrackId, window.Scope.TrackGeneration,
            began, ended, false, window.SpeechMilliseconds, (int)((end - start) / 24), ReadOnlyMemory<short>.Empty), item.Text);
    }

    public int Expire(DateTime now)
    {
        var old = items.Where(x => now - x.Value.At > TimeSpan.FromSeconds(options.MaximumUtteranceSeconds + 12)).Select(x => x.Key).ToArray();
        foreach (var id in old) Finish(id);
        return old.Length;
    }
    private void Finish(string id) { items.Remove(id); finished.Add(id); }
    private ReadOnlyMemory<short> Forward(ReadOnlyMemory<short> samples)
    { submittedSamples += samples.Length; active!.End = submittedSamples; return samples; }
    public void Dispose() { foreach (var track in tracks.Values) track.Detector.Dispose(); tracks.Clear(); windows.Clear(); items.Clear(); finished.Clear(); active = null; }
}
