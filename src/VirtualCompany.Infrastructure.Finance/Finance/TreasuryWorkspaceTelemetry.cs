using System.Diagnostics.Metrics;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class TreasuryWorkspaceTelemetry
{
    private static readonly Meter Meter = new("VirtualCompany.Finance.TreasuryWorkspace", "1.0");
    private readonly Counter<long> _loads = Meter.CreateCounter<long>("finance.treasury_workspace.loads");
    private readonly Counter<long> _failures = Meter.CreateCounter<long>("finance.treasury_workspace.failures");
    private readonly Histogram<double> _duration = Meter.CreateHistogram<double>("finance.treasury_workspace.duration", "ms");
    private readonly Histogram<long> _exceptions = Meter.CreateHistogram<long>("finance.treasury_workspace.exception_count", "items");

    public void Loaded(string riskLevel, bool stale, int exceptionCount, double durationMs)
    {
        _loads.Add(1,
            new KeyValuePair<string, object?>("risk_level", riskLevel),
            new KeyValuePair<string, object?>("stale", stale));
        _exceptions.Record(exceptionCount);
        _duration.Record(durationMs, new KeyValuePair<string, object?>("outcome", "succeeded"));
    }

    public void Failed(double durationMs)
    {
        _failures.Add(1);
        _duration.Record(durationMs, new KeyValuePair<string, object?>("outcome", "failed"));
    }
}
