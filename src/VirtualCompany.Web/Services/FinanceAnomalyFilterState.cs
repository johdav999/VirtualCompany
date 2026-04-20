using System.Globalization;

namespace VirtualCompany.Web.Services;

public sealed class FinanceAnomalyFilterState
{
    public string? AnomalyType { get; set; }
    public string? Status { get; set; }
    public decimal? ConfidenceMin { get; set; }
    public decimal? ConfidenceMax { get; set; }
    public string? Supplier { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;

    public FinanceAnomalyFilterState Normalize()
    {
        AnomalyType = NormalizeToken(AnomalyType);
        Status = NormalizeToken(Status);
        Supplier = NormalizeText(Supplier);
        ConfidenceMin = NormalizeConfidence(ConfidenceMin);
        ConfidenceMax = NormalizeConfidence(ConfidenceMax);
        DateFrom = DateFrom?.Date;
        DateTo = DateTo?.Date;
        Page = Page <= 0 ? 1 : Page;
        PageSize = PageSize switch
        {
            25 or 50 or 100 => PageSize,
            _ => 50
        };

        if (ConfidenceMin.HasValue && ConfidenceMax.HasValue && ConfidenceMin > ConfidenceMax)
        {
            (ConfidenceMin, ConfidenceMax) = (ConfidenceMax, ConfidenceMin);
        }

        return this;
    }

    public IReadOnlyDictionary<string, object?> ToQueryParameters(Guid? companyId)
    {
        var normalized = Normalize();
        return new Dictionary<string, object?>
        {
            [FinanceRoutes.CompanyIdQueryKey] = companyId,
            ["type"] = normalized.AnomalyType,
            ["status"] = normalized.Status,
            ["confidenceMin"] = normalized.ConfidenceMin,
            ["confidenceMax"] = normalized.ConfidenceMax,
            ["supplier"] = normalized.Supplier,
            ["dateFrom"] = normalized.DateFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["dateTo"] = normalized.DateTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["page"] = normalized.Page,
            ["pageSize"] = normalized.PageSize
        };
    }

    public string ToQueryString(Guid? companyId)
    {
        var values = ToQueryParameters(companyId);
        return string.Join("&", values
            .Where(x => x.Value is not null)
            .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(FormatQueryValue(x.Value!))}"));
    }

    private static string FormatQueryValue(object value) =>
        value switch
        {
            Guid guid => guid.ToString("D"),
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            decimal amount => amount.ToString("0.##", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };

    private static string? NormalizeText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeToken(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim()
                .Replace(" ", "_", StringComparison.Ordinal)
                .Replace("-", "_", StringComparison.Ordinal)
                .ToLowerInvariant();

    private static decimal? NormalizeConfidence(decimal? value) =>
        value.HasValue ? Math.Clamp(value.Value, 0m, 1m) : null;
}