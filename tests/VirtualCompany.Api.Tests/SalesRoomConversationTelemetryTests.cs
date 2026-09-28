using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using VirtualCompany.Application.Agents;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class SalesRoomConversationTelemetryTests
{
    [Fact]
    public void Timing_is_once_only_and_duplicate_tools_have_no_sensitive_labels()
    {
        var samples = new ConcurrentBag<(string Name, string Tags)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meter) =>
            {
                if (instrument.Meter.Name is SalesRoomBenchmarkTelemetry.MeterName or RealtimeConversationTelemetry.MeterName)
                    meter.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((i, value, tags, _) =>
        {
            Assert.True(value >= 0);
            samples.Add((i.Name, string.Join(",", tags.ToArray().Select(t => $"{t.Key}={t.Value}"))));
        });
        listener.SetMeasurementEventCallback<long>((i, value, tags, _) =>
            samples.Add((i.Name, string.Join(",", tags.ToArray().Select(t => $"{t.Key}={t.Value}")))));
        listener.Start();
        var timer = new SalesRoomConversationTiming(); var turn = Guid.NewGuid();
        timer.Begin("confirmed_input_to_first_answer_frame", turn);
        timer.Complete("confirmed_input_to_first_answer_frame", turn);
        var count = samples.Count;
        timer.Complete("confirmed_input_to_first_answer_frame", turn);
        Assert.Equal(count, samples.Count);
        var state = new OpenAiRealtimeConversationState([new("wait", "Wait", "{}", "read")]);
        state.AddConfirmed(turn, "Private question");
        state.CreateResponse(new(turn, false, KeepProviderContext: true));
        state.ObserveResponse("response_test", turn.ToString("N"));
        Assert.True(state.AcceptTool("response_test", "private_call", "wait", "{}"));
        Assert.False(state.AcceptTool("response_test", "private_call", "wait", "{}"));
        SalesRoomBenchmarkTelemetry.RecordConversationFailure("private error text");
        SalesRoomBenchmarkTelemetry.RecordOutput("bridge", 10, 10, 0);
        Assert.Contains(samples, x => x.Name == "agents.realtime.tool.duplicates" && x.Tags == "");
        Assert.Contains(samples, x => x.Name == "sales.browser_room.conversation.failures" && x.Tags == "reason=other");
        Assert.Contains(samples, x => x.Name == "sales.browser_room.audio.duration" && x.Tags == "stage=generated,purpose=bridge");
        Assert.Contains(samples, x => x.Tags == "operation=confirmed_input_to_first_answer_frame");
        Assert.DoesNotContain(samples, x => x.Tags.Contains("private", StringComparison.OrdinalIgnoreCase) || x.Tags.Contains(turn.ToString()));
    }
}
