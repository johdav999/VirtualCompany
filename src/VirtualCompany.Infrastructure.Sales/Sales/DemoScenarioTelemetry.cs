using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace VirtualCompany.Infrastructure.Sales;

internal static class DemoScenarioTelemetry
{
    internal static readonly ActivitySource ActivitySource = new("VirtualCompany.Sales.DemoScenarios");
    private static readonly Meter Meter = new("VirtualCompany.Sales.DemoScenarios");
    internal static readonly Counter<long> Provisions = Meter.CreateCounter<long>("demo_scenario_provisions");
    internal static readonly Counter<long> Resets = Meter.CreateCounter<long>("demo_scenario_resets");
    internal static readonly Counter<long> Commands = Meter.CreateCounter<long>("demo_scenario_commands");
    internal static readonly Counter<long> Rejections = Meter.CreateCounter<long>("demo_scenario_rejections");
    internal static readonly Counter<long> BlockedSideEffects = Meter.CreateCounter<long>("demo_scenario_external_side_effects_blocked");
}

