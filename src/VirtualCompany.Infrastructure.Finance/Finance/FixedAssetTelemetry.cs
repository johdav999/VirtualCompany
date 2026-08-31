using System.Diagnostics.Metrics;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FixedAssetTelemetry
{
    public const string MeterName = "VirtualCompany.Finance.FixedAssets";
    private readonly Counter<long> _registrations;
    private readonly Counter<long> _bookEvents;
    private readonly Counter<long> _depreciationItems;
    private readonly Counter<long> _exceptions;
    private readonly Histogram<double> _depreciationAmounts;
    public FixedAssetTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _registrations = meter.CreateCounter<long>("fixed_asset.registrations");
        _bookEvents = meter.CreateCounter<long>("fixed_asset.book_events");
        _depreciationItems = meter.CreateCounter<long>("fixed_asset.depreciation_items");
        _exceptions = meter.CreateCounter<long>("fixed_asset.exceptions");
        _depreciationAmounts = meter.CreateHistogram<double>("fixed_asset.depreciation.amount");
    }
    public void Registered() => _registrations.Add(1);
    public void BookEvent(string eventType) => _bookEvents.Add(1, new KeyValuePair<string, object?>("event_type", eventType));
    public void DepreciationPosted(decimal amount) { _depreciationItems.Add(1); _depreciationAmounts.Record((double)amount); }
    public void Exception(string reasonCode) => _exceptions.Add(1, new KeyValuePair<string, object?>("reason_code", reasonCode));
}
