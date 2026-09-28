using System.Diagnostics.Metrics;

namespace VirtualCompany.Infrastructure.Companies;

public static class RealtimeConversationTelemetry
{
    public const string MeterName = "VirtualCompany.Agents.RealtimeConversation";
    private static readonly Meter Meter = new(MeterName, "1.0.0");
    private static readonly Counter<long> DuplicateTools = Meter.CreateCounter<long>("agents.realtime.tool.duplicates");

    internal static void RecordDuplicateTool() => DuplicateTools.Add(1);
}
