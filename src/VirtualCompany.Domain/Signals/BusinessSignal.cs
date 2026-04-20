namespace VirtualCompany.Domain.Signals;

public sealed record BusinessSignal(
    BusinessSignalType Type,
    BusinessSignalSeverity Severity,
    string Title,
    string Summary,
    decimal? MetricValue,
    string? MetricLabel,
    string? ActionLabel,
    string? ActionUrl,
    DateTime DetectedAtUtc);
