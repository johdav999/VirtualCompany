using System.Diagnostics.Metrics;

namespace VirtualCompany.Web.Services;

public sealed class SalesPresentationPreparationTelemetry
{
    private static readonly Meter Meter = new("VirtualCompany.Web.SalesPresentationPreparation", "1.0.0");
    private static readonly Counter<long> UploadsAccepted =
        Meter.CreateCounter<long>("sales.presentation.uploads.accepted");
    private static readonly Histogram<double> ProcessingDuration =
        Meter.CreateHistogram<double>("sales.presentation.processing.observed_duration", "ms");
    private static readonly Counter<long> ProcessingFailures =
        Meter.CreateCounter<long>("sales.presentation.processing.failures");
    private static readonly Counter<long> RetriesRequested =
        Meter.CreateCounter<long>("sales.presentation.retries.requested");
    private static readonly Counter<long> ActivationOutcomes =
        Meter.CreateCounter<long>("sales.presentation.activations");

    public void UploadAccepted() => UploadsAccepted.Add(1);

    public void ProcessingObserved(TimeSpan duration, string outcome) =>
        ProcessingDuration.Record(
            Math.Max(0, duration.TotalMilliseconds),
            new KeyValuePair<string, object?>("outcome", NormalizeOutcome(outcome)));

    public void ProcessingFailed(string? failureCode) =>
        ProcessingFailures.Add(
            1,
            new KeyValuePair<string, object?>("category", FailureCategory(failureCode)));

    public void RetryRequested() => RetriesRequested.Add(1);

    public void ActivationCompleted(string outcome) =>
        ActivationOutcomes.Add(
            1,
            new KeyValuePair<string, object?>("outcome", NormalizeOutcome(outcome)));

    private static string NormalizeOutcome(string value) => value switch
    {
        "succeeded" => "succeeded",
        "failed" => "failed",
        "conflict" => "conflict",
        "cancelled" => "cancelled",
        "timeout" => "timeout",
        _ => "unknown"
    };

    private static string FailureCategory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unspecified";
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Contains("password", StringComparison.Ordinal)) return "protected_file";
        if (normalized.Contains("format", StringComparison.Ordinal) ||
            normalized.Contains("package", StringComparison.Ordinal) ||
            normalized.Contains("invalid", StringComparison.Ordinal)) return "invalid_content";
        if (normalized.Contains("render", StringComparison.Ordinal)) return "renderer";
        if (normalized.Contains("storage", StringComparison.Ordinal)) return "storage";
        if (normalized.Contains("timeout", StringComparison.Ordinal)) return "timeout";
        return "processing";
    }
}
