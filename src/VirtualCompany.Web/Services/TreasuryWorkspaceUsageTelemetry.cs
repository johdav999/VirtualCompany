using System.Diagnostics.Metrics;

namespace VirtualCompany.Web.Services;

public sealed class TreasuryWorkspaceUsageTelemetry
{
    private static readonly Meter Meter = new("VirtualCompany.Web.TreasuryWorkspace", "1.0");
    private readonly Counter<long> _views = Meter.CreateCounter<long>("finance.treasury_workspace.views");
    private readonly Counter<long> _actions = Meter.CreateCounter<long>("finance.treasury_workspace.actions");

    public void Viewed(string riskLevel, bool stale, string locale) => _views.Add(1,
        new KeyValuePair<string, object?>("risk_level", riskLevel),
        new KeyValuePair<string, object?>("stale", stale),
        new KeyValuePair<string, object?>("locale", locale));

    public void ActionOpened(string action, string reasonCode) => _actions.Add(1,
        new KeyValuePair<string, object?>("action", action),
        new KeyValuePair<string, object?>("reason_code", reasonCode));
}
