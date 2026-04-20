using System.Collections.Specialized;
using System.Globalization;

namespace VirtualCompany.Web.Services;

public sealed class InvoiceReviewFilterState
{
    public string? Status { get; set; }
    public string? Supplier { get; set; }
    public string? RiskLevel { get; set; }
    public string? RecommendationOutcome { get; set; }

    public static InvoiceReviewFilterState FromQuery(NameValueCollection query) =>
        new InvoiceReviewFilterState
        {
            Status = query["status"],
            Supplier = query["supplier"],
            RiskLevel = query["riskLevel"],
            RecommendationOutcome = query["outcome"]
        }.Normalize();

    public InvoiceReviewFilterState Normalize()
    {
        Status = NormalizeToken(Status);
        Supplier = NormalizeText(Supplier);
        RiskLevel = NormalizeToken(RiskLevel);
        RecommendationOutcome = NormalizeToken(RecommendationOutcome);
        return this;
    }

    public IReadOnlyDictionary<string, object?> ToQueryParameters(Guid? companyId)
    {
        var normalized = Normalize();
        return new Dictionary<string, object?>
        {
            [FinanceRoutes.CompanyIdQueryKey] = companyId,
            ["status"] = normalized.Status,
            ["supplier"] = normalized.Supplier,
            ["riskLevel"] = normalized.RiskLevel,
            ["outcome"] = normalized.RecommendationOutcome
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
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };

    private static string? NormalizeText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeToken(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Replace(" ", "_", StringComparison.Ordinal).Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();
}
