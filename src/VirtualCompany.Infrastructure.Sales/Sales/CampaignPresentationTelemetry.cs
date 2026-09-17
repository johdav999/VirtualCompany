using System.Diagnostics.Metrics;

namespace VirtualCompany.Infrastructure.Sales;

internal static class CampaignPresentationTelemetry
{
    private static readonly Meter Meter = new("VirtualCompany.Sales.CampaignPresentations", "1.0.0");

    internal static readonly Counter<long> ActivitiesScanned = Meter.CreateCounter<long>("sales.campaign.presentation.activities.scanned");
    internal static readonly Counter<long> SubjectsResolved = Meter.CreateCounter<long>("sales.campaign.presentation.subjects.resolved");
    internal static readonly Counter<long> RunsCreated = Meter.CreateCounter<long>("sales.campaign.presentation.runs.created");
    internal static readonly Counter<long> DuplicateCreationsPrevented = Meter.CreateCounter<long>("sales.campaign.presentation.duplicates.prevented");
    internal static readonly Counter<long> TasksCreated = Meter.CreateCounter<long>("sales.campaign.presentation.tasks.created");
    internal static readonly Counter<long> HandoffsCreated = Meter.CreateCounter<long>("sales.campaign.presentation.handoffs.created");
    internal static readonly Counter<long> RetriesQueued = Meter.CreateCounter<long>("sales.campaign.presentation.retries.queued");
    internal static readonly Counter<long> Failures = Meter.CreateCounter<long>("sales.campaign.presentation.failures");
    internal static readonly Histogram<double> SchedulingLatency = Meter.CreateHistogram<double>("sales.campaign.presentation.scheduling.latency", "ms");
}
