using System.Diagnostics;

namespace VirtualCompany.Infrastructure.Sales;

// Per worker lifetime; never stores text, audio, identities in metric tags, or unbounded history.
// These are server transport timings, NOT proof that a browser loudspeaker was audible.
internal sealed class SalesRoomConversationTiming
{
    private readonly object gate = new();
    private readonly Dictionary<(string Stage, Guid Turn), long> pending = [];

    internal void Begin(string stage, Guid turn)
    {
        lock (gate)
        {
            foreach (var key in pending.Where(x => Stopwatch.GetElapsedTime(x.Value) > TimeSpan.FromMinutes(2))
                         .Select(x => x.Key).ToArray()) pending.Remove(key);
            if (pending.Count >= 32) pending.Remove(pending.MinBy(x => x.Value).Key);
            pending.TryAdd((stage, turn), Stopwatch.GetTimestamp());
        }
    }

    internal void Complete(string stage, Guid turn)
    {
        lock (gate)
            if (pending.Remove((stage, turn), out var started))
            {
                var elapsed = Stopwatch.GetElapsedTime(started);
                if (elapsed <= TimeSpan.FromMinutes(2)) SalesRoomBenchmarkTelemetry.RecordLatency(stage, elapsed);
            }
    }
}
