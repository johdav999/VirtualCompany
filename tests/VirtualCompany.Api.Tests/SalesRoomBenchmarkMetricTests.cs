using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class SalesRoomBenchmarkMetricTests
{
    [Fact]
    public void Metrics_balance_sessions_and_separate_audio_stages_without_tenant_tags()
    {
        var values = new ConcurrentBag<(string Name, long Value, Dictionary<string, object?> Tags)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meter) =>
            {
                if (instrument.Meter.Name == SalesRoomBenchmarkTelemetry.MeterName)
                    meter.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            values.Add((instrument.Name, value, CopyTags(tags))));
        listener.Start();

        using (SalesRoomBenchmarkTelemetry.BeginSession())
        {
            SalesRoomBenchmarkTelemetry.RecordInput(100, 30, 50);
            SalesRoomBenchmarkTelemetry.RecordProvider(40, 2, 3);
            SalesRoomBenchmarkTelemetry.RecordCache(true);
            SalesRoomBenchmarkTelemetry.RecordGeneration(4, 5, 60, "completed");
            SalesRoomBenchmarkTelemetry.RecordOutput("answer", 80, 50, 30);
            SalesRoomBenchmarkTelemetry.RecordMedia(2, 1);
            SalesRoomBenchmarkTelemetry.RecordFailure("agent_worker");
            SalesRoomBenchmarkTelemetry.RecordAdmission("accepted");
            SalesRoomBenchmarkTelemetry.RecordLifecycle("emergency_disabled");
            SalesRoomBenchmarkTelemetry.RecordOwnership("reconcile_fenced");
            SalesRoomBenchmarkTelemetry.RecordQuota("call_spend");
            SalesRoomBenchmarkTelemetry.RecordEstimatedSpend(1.25m);
            SalesRoomBenchmarkTelemetry.RecordLatency("voice_connect", TimeSpan.FromMilliseconds(42));
            SalesRoomBenchmarkTelemetry.QueueDepth.Record(7);
        }

        Assert.Equal(0, values.Where(x => x.Name == "sales.browser_room.sessions.active").Sum(x => x.Value));
        Assert.Contains(values, x => x.Name == "sales.browser_room.audio.duration" &&
            Equals(x.Tags.GetValueOrDefault("stage"), "forwarded") && x.Value == 50);
        Assert.Contains(values, x => x.Name == "sales.browser_room.audio.duration" &&
            Equals(x.Tags.GetValueOrDefault("stage"), "cancelled") && x.Value == 30);
        Assert.Contains(values, x => x.Name == "sales.browser_room.narration.cache" &&
            Equals(x.Tags.GetValueOrDefault("result"), "hit"));
        Assert.Contains(values, x => x.Name == "sales.browser_room.media.events" &&
            Equals(x.Tags.GetValueOrDefault("event"), "frame_dropped") && x.Value == 2);
        Assert.Contains(values, x => x.Name == "sales.browser_room.admission" &&
            Equals(x.Tags.GetValueOrDefault("result"), "accepted"));
        Assert.Contains(values, x => x.Name == "sales.browser_room.agent.ownership" &&
            Equals(x.Tags.GetValueOrDefault("result"), "reconcile_fenced"));
        Assert.Contains(values, x => x.Name == "sales.browser_room.quota" &&
            Equals(x.Tags.GetValueOrDefault("kind"), "call_spend"));
        Assert.DoesNotContain(values.SelectMany(x => x.Tags.Keys), key =>
            key.Contains("company", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("room", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("user", StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, object?> CopyTags(
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var result = new Dictionary<string, object?>(tags.Length, StringComparer.Ordinal);
        foreach (var tag in tags)
            result[tag.Key] = tag.Value;

        return result;
    }
}
