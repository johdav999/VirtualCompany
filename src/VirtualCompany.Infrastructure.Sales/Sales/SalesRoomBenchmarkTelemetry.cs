using System.Diagnostics.Metrics;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Sales;

public static class SalesRoomBenchmarkTelemetry
{
    public const string MeterName = "VirtualCompany.Sales.BrowserRoom";
    private static readonly Meter Meter = new(MeterName, "1.0.0");
    internal static readonly UpDownCounter<long> ActiveSessions = Meter.CreateUpDownCounter<long>("sales.browser_room.sessions.active");
    internal static readonly Counter<long> Sessions = Meter.CreateCounter<long>("sales.browser_room.sessions");
    internal static readonly Counter<long> AudioMilliseconds = Meter.CreateCounter<long>("sales.browser_room.audio.duration", "ms");
    internal static readonly Counter<long> Tokens = Meter.CreateCounter<long>("sales.browser_room.provider.tokens", "tokens");
    internal static readonly Counter<long> NarrationCache = Meter.CreateCounter<long>("sales.browser_room.narration.cache");
    internal static readonly Counter<long> NarrationGeneration = Meter.CreateCounter<long>("sales.browser_room.narration.generation");
    internal static readonly Counter<long> MediaEvents = Meter.CreateCounter<long>("sales.browser_room.media.events");
    internal static readonly Counter<long> Failures = Meter.CreateCounter<long>("sales.browser_room.failures");
    internal static readonly Histogram<long> QueueDepth = Meter.CreateHistogram<long>("sales.browser_room.queue.depth", "items");
    internal static readonly Counter<long> Admissions = Meter.CreateCounter<long>("sales.browser_room.admission");
    internal static readonly Counter<long> Lifecycle = Meter.CreateCounter<long>("sales.browser_room.lifecycle");
    internal static readonly Counter<long> Ownership = Meter.CreateCounter<long>("sales.browser_room.agent.ownership");
    internal static readonly Counter<long> Quotas = Meter.CreateCounter<long>("sales.browser_room.quota");
    internal static readonly Histogram<double> Latency = Meter.CreateHistogram<double>("sales.browser_room.latency", "ms");
    internal static readonly Histogram<long> EstimatedSpend = Meter.CreateHistogram<long>("sales.browser_room.estimated_spend", "usd-micro");

    internal static IDisposable BeginSession()
    {
        ActiveSessions.Add(1);
        Sessions.Add(1, new KeyValuePair<string, object?>("outcome", "started"));
        return new Session();
    }

    internal static void RecordInput(long received, long detected, long forwarded)
    {
        AddAudio(received, "received", "human_input");
        AddAudio(detected, "detected", "human_input");
        AddAudio(forwarded, "forwarded", "human_input");
    }

    internal static void RecordProvider(long billedMilliseconds, int inputTokens, int outputTokens)
    {
        AddAudio(billedMilliseconds, "billed", "transcription");
        if (inputTokens > 0) Tokens.Add(inputTokens, new KeyValuePair<string, object?>("direction", "input"));
        if (outputTokens > 0) Tokens.Add(outputTokens, new KeyValuePair<string, object?>("direction", "output"));
    }

    internal static void RecordCache(bool hit) =>
        NarrationCache.Add(1, new KeyValuePair<string, object?>("result", hit ? "hit" : "miss"));

    internal static void RecordGeneration(int inputTokens, int outputTokens, int milliseconds, string outcome)
    {
        NarrationGeneration.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        AddAudio(milliseconds, "generated", "narration");
        if (inputTokens > 0) Tokens.Add(inputTokens, new("direction", "input"), new("purpose", "narration"));
        if (outputTokens > 0) Tokens.Add(outputTokens, new("direction", "output"), new("purpose", "narration"));
    }

    internal static void RecordOutput(string kind, int generated, int played, int cancelled)
    {
        if (kind == SalesRoomAgentSpeechKinds.Answer) AddAudio(generated, "generated", kind);
        AddAudio(played, "played", kind);
        AddAudio(cancelled, "cancelled", kind);
    }

    internal static void RecordMedia(long droppedFrames, int reconnects)
    {
        if (droppedFrames > 0) MediaEvents.Add(droppedFrames,
            new KeyValuePair<string, object?>("event", "frame_dropped"));
        if (reconnects > 0) MediaEvents.Add(reconnects,
            new KeyValuePair<string, object?>("event", "reconnect"));
    }

    internal static void RecordFailure(string component) =>
        Failures.Add(1, new KeyValuePair<string, object?>("component", component));

    internal static void RecordAdmission(string result) =>
        Admissions.Add(1, new KeyValuePair<string, object?>("result", result));

    internal static void RecordLifecycle(string state) =>
        Lifecycle.Add(1, new KeyValuePair<string, object?>("state", state));

    internal static void RecordOwnership(string result) =>
        Ownership.Add(1, new KeyValuePair<string, object?>("result", result));

    internal static void RecordQuota(string kind) =>
        Quotas.Add(1, new KeyValuePair<string, object?>("kind", kind));

    internal static void RecordLatency(string operation, TimeSpan elapsed) =>
        Latency.Record(elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("operation", operation));

    internal static void RecordEstimatedSpend(decimal amount) =>
        EstimatedSpend.Record(decimal.ToInt64(decimal.Round(Math.Max(0, amount) * 1_000_000m, 0)),
            new KeyValuePair<string, object?>("currency", "USD"));

    private static void AddAudio(long value, string stage, string purpose)
    {
        if (value > 0) AudioMilliseconds.Add(value, new("stage", stage), new("purpose", purpose));
    }

    private sealed class Session : IDisposable
    {
        private int disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            ActiveSessions.Add(-1);
            Sessions.Add(1, new KeyValuePair<string, object?>("outcome", "ended"));
        }
    }
}
