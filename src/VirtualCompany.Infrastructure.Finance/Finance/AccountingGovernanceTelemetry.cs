using System.Diagnostics.Metrics;

namespace VirtualCompany.Infrastructure.Finance;

internal static class AccountingGovernanceTelemetry
{
    internal const string MeterName = "VirtualCompany.Finance.AccountingGovernance";
    private static readonly Meter Meter = new(MeterName, "1.0.0");
    private static readonly Counter<long> LifecycleChanges = Meter.CreateCounter<long>("accounting_governance.lifecycle_changes");
    private static readonly Counter<long> SeriesPolicyChanges = Meter.CreateCounter<long>("accounting_governance.series_policy_changes");
    private static readonly Counter<long> GapEvidence = Meter.CreateCounter<long>("accounting_governance.gap_evidence");
    private static readonly Counter<long> CommerceEvents = Meter.CreateCounter<long>("accounting_governance.commerce_events");

    internal static void LifecycleChanged(string changeType) =>
        LifecycleChanges.Add(1, new KeyValuePair<string, object?>("change_type", changeType));

    internal static void SeriesPolicyChanged(string seriesKind, string operation) =>
        SeriesPolicyChanges.Add(1, new("series_kind", seriesKind), new("operation", operation));

    internal static void GapExplained() => GapEvidence.Add(1, new KeyValuePair<string, object?>("outcome", "explained"));

    internal static void CommerceEvent(string outcome, string reasonCode) =>
        CommerceEvents.Add(1, new("outcome", outcome), new("reason_code", reasonCode));
}
