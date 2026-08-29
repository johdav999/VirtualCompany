using System.Diagnostics.Metrics;

namespace VirtualCompany.Infrastructure.Finance;

internal sealed class TreasuryMovementTelemetry
{
    private static readonly Meter Meter = new("VirtualCompany.Finance.Treasury", "1.0");
    private readonly Counter<long> _created = Meter.CreateCounter<long>("finance.treasury.sources.created");
    private readonly Counter<long> _posted = Meter.CreateCounter<long>("finance.treasury.sources.posted");
    private readonly Counter<long> _reversed = Meter.CreateCounter<long>("finance.treasury.sources.reversed");
    private readonly Counter<long> _blocked = Meter.CreateCounter<long>("finance.treasury.actions.blocked");

    public void Created(string sourceType, string status) => _created.Add(1,
        new KeyValuePair<string, object?>("source_type", sourceType), new("status", status));
    public void Posted(string sourceType) => _posted.Add(1, new KeyValuePair<string, object?>("source_type", sourceType));
    public void Reversed(string sourceType) => _reversed.Add(1, new KeyValuePair<string, object?>("source_type", sourceType));
    public void Blocked(string sourceType, string reasonCode) => _blocked.Add(1,
        new KeyValuePair<string, object?>("source_type", sourceType), new("reason_code", reasonCode));
}
