using System.Globalization;

namespace VirtualCompany.Web.Localization.Formatting;

public interface ILocalDateTimeFormatter
{
    string Date(DateOnly value);
    string Time(TimeOnly value);
    string DateTime(DateTime valueUtc, string? timeZoneId = null);
    string DateTime(DateTimeOffset value, string? timeZoneId = null);
    string Optional(DateTime? valueUtc, string? timeZoneId = null, string empty = "-");
    string Relative(DateTime valueUtc, DateTime? nowUtc = null);
}

public interface ICompanyPresentationContext
{
    string? TimeZoneId { get; }
    string? CurrencyCode { get; }
    CultureInfo FormattingCulture { get; }
    void SetActiveCompany(string? timeZoneId, string? currencyCode);
    void SetFormattingCulture(string? cultureName);
}

public sealed class CompanyPresentationContext : ICompanyPresentationContext
{
    public string? TimeZoneId { get; private set; }
    public string? CurrencyCode { get; private set; }
    public CultureInfo FormattingCulture { get; private set; } = CultureInfo.CurrentCulture;

    public void SetActiveCompany(string? timeZoneId, string? currencyCode)
    {
        TimeZoneId = string.IsNullOrWhiteSpace(timeZoneId) ? null : timeZoneId.Trim();
        CurrencyCode = string.IsNullOrWhiteSpace(currencyCode) ? null : currencyCode.Trim().ToUpperInvariant();
    }

    public void SetFormattingCulture(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
        {
            FormattingCulture = CultureInfo.CurrentCulture;
            return;
        }

        try { FormattingCulture = CultureInfo.GetCultureInfo(cultureName.Trim()); }
        catch (CultureNotFoundException) { FormattingCulture = CultureInfo.CurrentCulture; }
    }
}

public interface INumberFormatter
{
    string Integer(long value);
    string Decimal(decimal value, int decimalPlaces = 2);
    string Percentage(decimal value, int decimalPlaces = 0);
}

public interface IMoneyFormatter
{
    string Format(decimal amount, string currencyCode);
}

public sealed class LocalDateTimeFormatter : ILocalDateTimeFormatter
{
    private readonly ICompanyPresentationContext? _context;

    public LocalDateTimeFormatter(ICompanyPresentationContext? context = null) => _context = context;

    private CultureInfo Culture => _context?.FormattingCulture ?? CultureInfo.CurrentCulture;

    public string Date(DateOnly value) => value.ToString("d", Culture);
    public string Time(TimeOnly value) => value.ToString("t", Culture);

    public string DateTime(DateTime valueUtc, string? timeZoneId = null) =>
        Convert(new DateTimeOffset(System.DateTime.SpecifyKind(valueUtc, DateTimeKind.Utc)), ResolveTimeZone(timeZoneId))
            .ToString("g", Culture);

    public string DateTime(DateTimeOffset value, string? timeZoneId = null) =>
        Convert(value.ToUniversalTime(), ResolveTimeZone(timeZoneId)).ToString("g", Culture);

    public string Optional(DateTime? valueUtc, string? timeZoneId = null, string empty = "-") =>
        valueUtc.HasValue ? DateTime(valueUtc.Value, timeZoneId) : empty;

    public string Relative(DateTime valueUtc, DateTime? nowUtc = null)
    {
        var elapsed = (nowUtc ?? System.DateTime.UtcNow) - System.DateTime.SpecifyKind(valueUtc, DateTimeKind.Utc);
        var future = elapsed < TimeSpan.Zero;
        var absolute = elapsed.Duration();
        var quantity = absolute.TotalDays >= 1 ? (int)absolute.TotalDays : absolute.TotalHours >= 1 ? (int)absolute.TotalHours : Math.Max(1, (int)absolute.TotalMinutes);
        var unit = absolute.TotalDays >= 1 ? "day" : absolute.TotalHours >= 1 ? "hour" : "minute";
        if (Culture.Name.StartsWith("sv", StringComparison.OrdinalIgnoreCase))
            return future ? $"om {quantity} {SwedishUnit(unit, quantity)}" : $"för {quantity} {SwedishUnit(unit, quantity)} sedan";
        return future ? $"in {quantity} {EnglishUnit(unit, quantity)}" : $"{quantity} {EnglishUnit(unit, quantity)} ago";
    }

    private string? ResolveTimeZone(string? explicitTimeZoneId) =>
        string.IsNullOrWhiteSpace(explicitTimeZoneId) ? _context?.TimeZoneId : explicitTimeZoneId;

    private static string EnglishUnit(string unit, int quantity) => quantity == 1 ? unit : $"{unit}s";
    private static string SwedishUnit(string unit, int quantity) => unit switch
    {
        "day" => quantity == 1 ? "dag" : "dagar",
        "hour" => quantity == 1 ? "timme" : "timmar",
        _ => quantity == 1 ? "minut" : "minuter"
    };

    private static DateTimeOffset Convert(DateTimeOffset valueUtc, string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) return valueUtc;
        try { return TimeZoneInfo.ConvertTime(valueUtc, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)); }
        catch (TimeZoneNotFoundException) { return valueUtc; }
        catch (InvalidTimeZoneException) { return valueUtc; }
    }
}

public sealed class NumberFormatter : INumberFormatter
{
    private readonly ICompanyPresentationContext? _context;
    public NumberFormatter(ICompanyPresentationContext? context = null) => _context = context;
    private CultureInfo Culture => _context?.FormattingCulture ?? CultureInfo.CurrentCulture;
    public string Integer(long value) => value.ToString("N0", Culture);
    public string Decimal(decimal value, int decimalPlaces = 2) => value.ToString($"N{Math.Clamp(decimalPlaces, 0, 28)}", Culture);
    public string Percentage(decimal value, int decimalPlaces = 0) => value.ToString($"P{Math.Clamp(decimalPlaces, 0, 28)}", Culture);
}

public sealed class MoneyFormatter : IMoneyFormatter
{
    private readonly ICompanyPresentationContext? _context;
    public MoneyFormatter(ICompanyPresentationContext? context = null) => _context = context;

    public string Format(decimal amount, string currencyCode)
    {
        var code = NormalizeCurrency(currencyCode);
        var culture = _context?.FormattingCulture ?? CultureInfo.CurrentCulture;
        var number = amount.ToString("N2", culture);
        return culture.Name.StartsWith("sv", StringComparison.OrdinalIgnoreCase)
            ? $"{number} {code}"
            : $"{code} {number}";
    }

    private static string NormalizeCurrency(string currencyCode)
    {
        var code = currencyCode?.Trim().ToUpperInvariant() ?? string.Empty;
        return code.Length == 3 && code.All(char.IsAsciiLetterUpper) ? code : "---";
    }
}
