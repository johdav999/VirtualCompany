using System.Globalization;

namespace VirtualCompany.Web.Services;

public static class FinanceAnomalyPresentation
{
    private static readonly CultureInfo DisplayCulture = CultureInfo.InvariantCulture;

    public static bool HasDeduplicationMetadata(FinanceAnomalyDeduplicationResponse? deduplication) =>
        deduplication is not null &&
        (!string.IsNullOrWhiteSpace(deduplication.Key) ||
         deduplication.WindowStartUtc.HasValue ||
         deduplication.WindowEndUtc.HasValue);

    public static string FormatLabel(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "n/a"
            : string.Join(" ", value.Trim().Replace("-", "_", StringComparison.Ordinal).Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public static string FormatConfidence(decimal confidence) =>
        $"{Math.Clamp(confidence, 0m, 1m):P0}";

    public static string FormatDateTime(DateTime? value) =>
        value.HasValue ? value.Value.ToString("yyyy-MM-dd HH:mm 'UTC'", DisplayCulture) : "n/a";

    public static string FormatCurrency(decimal amount, string currency) =>
        $"{currency} {amount.ToString("N2", DisplayCulture)}";

    public static string FormatWindow(DateTime? startUtc, DateTime? endUtc)
    {
        if (startUtc.HasValue && endUtc.HasValue)
        {
            return $"{FormatDateTime(startUtc)} to {FormatDateTime(endUtc)}";
        }

        return startUtc.HasValue ? FormatDateTime(startUtc) : FormatDateTime(endUtc);
    }

    public static string ResolveStatusPillClass(string? status) =>
        NormalizeToken(status) switch
        {
            "completed" or "resolved" or "closed" => "finance-risk-pill finance-risk-pill--positive",
            "failed" or "blocked" => "finance-risk-pill finance-risk-pill--critical",
            "awaiting_approval" => "finance-risk-pill finance-risk-pill--warning",
            "in_progress" or "open" or "acknowledged" => "finance-risk-pill finance-risk-pill--medium",
            _ => "finance-risk-pill finance-risk-pill--neutral"
        };

    private static string? NormalizeToken(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim()
                .Replace(" ", "_", StringComparison.Ordinal)
                .Replace("-", "_", StringComparison.Ordinal)
                .ToLowerInvariant();
}