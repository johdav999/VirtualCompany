using System.Diagnostics.Metrics;

namespace VirtualCompany.Infrastructure.Sales;

internal static class SalesMeetingChangeTelemetry
{
    private static readonly Meter Meter = new("VirtualCompany.SalesMeetingChanges", "1.0.0");
    private static readonly Counter<long> Transitions = Meter.CreateCounter<long>("sales.meeting_change.transitions");
    private static readonly Counter<long> DeliveryOutcomes = Meter.CreateCounter<long>("sales.meeting.change_delivery.outcomes");

    internal static void RecordTransition(string action, string outcome, string status) =>
        Transitions.Add(1, new KeyValuePair<string, object?>("action", action),
            new KeyValuePair<string, object?>("outcome", outcome),
            new KeyValuePair<string, object?>("status", status));

    internal static void RecordDelivery(string channel, string outcome) =>
        DeliveryOutcomes.Add(1, new KeyValuePair<string, object?>("channel", channel),
            new KeyValuePair<string, object?>("outcome", outcome));
}
