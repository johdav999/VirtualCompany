using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class ExchangeRateTelemetry(ILogger<ExchangeRateTelemetry> logger)
{
    internal const string MeterName = "VirtualCompany.ExchangeRates";
    private static readonly Meter Meter = new(MeterName, "1.0.0");
    private static readonly Counter<long> Imports = Meter.CreateCounter<long>("exchange_rate.imports");
    private static readonly Counter<long> Lookups = Meter.CreateCounter<long>("exchange_rate.lookups");
    private static readonly Counter<long> Refreshes = Meter.CreateCounter<long>("exchange_rate.refreshes");

    public void Import(string sourceKey, string outcome, int observations, string? reasonCode)
    {
        Imports.Add(1, new("source", sourceKey), new("outcome", outcome), new("reason_code", reasonCode));
        logger.LogInformation("Exchange-rate import for source {SourceKey} completed with {Outcome}; {ObservationCount} observations. ReasonCode={ReasonCode}.",
            sourceKey, outcome, observations, reasonCode);
    }

    public void Lookup(string purpose, string outcome, int legs, string? reasonCode)
    {
        Lookups.Add(1, new("purpose", purpose), new("outcome", outcome), new("reason_code", reasonCode));
        logger.LogInformation("Exchange-rate lookup for purpose {Purpose} completed with {Outcome}; {LegCount} leg(s). ReasonCode={ReasonCode}.",
            purpose, outcome, legs, reasonCode);
    }

    public void Refresh(string sourceKey, string outcome, string? reasonCode)
    {
        Refreshes.Add(1, new("source", sourceKey), new("outcome", outcome), new("reason_code", reasonCode));
        logger.LogInformation("Exchange-rate refresh for source {SourceKey} completed with {Outcome}. ReasonCode={ReasonCode}.",
            sourceKey, outcome, reasonCode);
    }
}
