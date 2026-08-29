using System.Diagnostics.Metrics;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class BankFeedTelemetry
{
    internal const string MeterName = "VirtualCompany.BankFeeds";
    private static readonly Meter Meter = new(MeterName, "1.0.0");
    private static readonly Counter<long> Synchronizations = Meter.CreateCounter<long>("bank.feed.synchronizations");
    private static readonly Counter<long> Pages = Meter.CreateCounter<long>("bank.feed.pages");
    private static readonly Counter<long> Booked = Meter.CreateCounter<long>("bank.feed.booked_transactions");
    private static readonly Counter<long> Pending = Meter.CreateCounter<long>("bank.feed.pending_transactions");
    public void Synchronization(string outcome, string provider, string phase, string? reasonCode) =>
        Synchronizations.Add(1, new("outcome", outcome), new("provider", provider), new("phase", phase), new("reason_code", reasonCode));
    public void Page(string provider, string phase, int booked, int pending, string outcome)
    {
        Pages.Add(1, new("provider", provider), new("phase", phase), new("outcome", outcome));
        if (booked > 0) Booked.Add(booked, [new("provider", provider)]);
        if (pending > 0) Pending.Add(pending, [new("provider", provider)]);
    }
}
