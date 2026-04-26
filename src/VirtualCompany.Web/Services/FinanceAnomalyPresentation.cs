using System.Globalization;

namespace VirtualCompany.Web.Services;

public static class FinanceAnomalyPresentation
{
    private static readonly CultureInfo DisplayCulture = CultureInfo.InvariantCulture;
    private static readonly IReadOnlyDictionary<string, string> NaturalLanguageLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["duplicate_vendor_charge"] = "Possible duplicate vendor charge",
        ["unusually_high_amount"] = "Amount is unusually high",
        ["category_mismatch"] = "Category does not match the transaction",
        ["missing_document"] = "Supporting document is missing",
        ["missing_receipt"] = "Receipt is missing",
        ["suspicious_payment_timing"] = "Payment timing looks unusual",
        ["multiple_payments"] = "Multiple payments may refer to the same item",
        ["payment_before_expected_state_transition"] = "Payment was made before the bill or invoice was ready",
        ["baseline"] = "Baseline threshold check",
        ["historical_baseline_deviation"] = "Transaction differs from the historical baseline"
    };

    public static bool HasDeduplicationMetadata(FinanceAnomalyDeduplicationResponse? deduplication) =>
        deduplication is not null &&
        (!string.IsNullOrWhiteSpace(deduplication.Key) ||
         deduplication.WindowStartUtc.HasValue ||
         deduplication.WindowEndUtc.HasValue);

    public static string FormatLabel(string? value)
    {
        var normalized = NormalizeToken(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "n/a";
        }

        return NaturalLanguageLabels.TryGetValue(normalized, out var label)
            ? label
            : FormatTokenAsWords(normalized);
    }

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

    private static string FormatTokenAsWords(string value)
    {
        var label = string.Join(" ", value.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return label.Length == 0 ? "n/a" : char.ToUpperInvariant(label[0]) + label[1..];
    }
}
