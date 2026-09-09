using System.Diagnostics.Metrics;

namespace VirtualCompany.Infrastructure.Sales;

internal static class SalesMeetingVoiceTelemetry
{
    private static readonly Meter Meter = new("VirtualCompany.Sales.MeetingVoice", "1.0.0");
    internal static readonly Counter<long> SessionsStarted = Meter.CreateCounter<long>("sales.meeting.voice.sessions.started");
    internal static readonly Counter<long> SessionsFailed = Meter.CreateCounter<long>("sales.meeting.voice.sessions.failed");
    internal static readonly Counter<long> EventsProcessed = Meter.CreateCounter<long>("sales.meeting.voice.events.processed");
    internal static readonly Counter<long> EventsDeduplicated = Meter.CreateCounter<long>("sales.meeting.voice.events.deduplicated");
    internal static readonly Counter<long> ToolsRejected = Meter.CreateCounter<long>("sales.meeting.voice.tools.rejected");
    internal static readonly Counter<long> Fallbacks = Meter.CreateCounter<long>("sales.meeting.voice.fallbacks");
}
