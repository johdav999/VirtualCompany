using System.Globalization;
using VirtualCompany.Web.Localization.Formatting;

namespace VirtualCompany.Web.Tests;

public sealed class PresentationFormatterTests
{
    [Theory]
    [InlineData("en-GB", "SEK 22,000.00")]
    [InlineData("sv-SE", "22 000,00 SEK")]
    public void Money_PreservesIsoCurrencyAndUsesCulture(string cultureName, string expected)
    {
        using var _ = new CultureScope(cultureName);
        Assert.Equal(NormalizeDisplaySpacing(expected), NormalizeDisplaySpacing(new MoneyFormatter().Format(22000m, "sek")));
    }

    [Fact]
    public void Money_UsesSafeCodeForInvalidCurrency()
    {
        using var _ = new CultureScope("en-GB");
        Assert.Equal("--- 1.00", new MoneyFormatter().Format(1m, "not-a-code"));
    }

    [Fact]
    public void DateTime_ConvertsUtcAcrossDstAndInvalidZoneFallsBackToUtc()
    {
        using var _ = new CultureScope("en-GB");
        var formatter = new LocalDateTimeFormatter();
        var utc = new DateTime(2026, 7, 16, 12, 0, 0, DateTimeKind.Utc);

        Assert.Contains("14:00", formatter.DateTime(utc, "Europe/Stockholm"));
        Assert.Contains("12:00", formatter.DateTime(utc, "Invalid/Zone"));
    }

    [Theory]
    [InlineData("en-GB", "12.5%")]
    [InlineData("sv-SE", "12,5 %")]
    public void Percentage_UsesCulture(string cultureName, string expected)
    {
        using var _ = new CultureScope(cultureName);
        Assert.Equal(expected, new NumberFormatter().Percentage(0.125m, 1));
    }

    [Fact]
    public void CompanyContext_ControlsTimezoneAndFormattingCultureIndependentlyOfUiCulture()
    {
        using var _ = new CultureScope("en-GB");
        var context = new CompanyPresentationContext();
        context.SetActiveCompany("Europe/Stockholm", "SEK");
        context.SetFormattingCulture("sv-SE");
        var instant = new DateTime(2026, 12, 16, 12, 0, 0, DateTimeKind.Utc);

        Assert.Contains("13:00", new LocalDateTimeFormatter(context).DateTime(instant));
        Assert.Equal("1\u00A0234,50", new NumberFormatter(context).Decimal(1234.5m));
        Assert.Equal("1\u00A0234,50 SEK", new MoneyFormatter(context).Format(1234.5m, "SEK"));
    }

    [Fact]
    public void Formatter_HandlesDstInvalidTimezoneNegativeValuesAndRelativeTime()
    {
        using var _ = new CultureScope("en-GB");
        var formatter = new LocalDateTimeFormatter();

        Assert.Contains("14:00", formatter.DateTime(new DateTime(2026, 7, 16, 12, 0, 0, DateTimeKind.Utc), "Europe/Stockholm"));
        Assert.Contains("13:00", formatter.DateTime(new DateTime(2026, 12, 16, 12, 0, 0, DateTimeKind.Utc), "Europe/Stockholm"));
        Assert.Contains("12:00", formatter.DateTime(new DateTime(2026, 7, 16, 12, 0, 0, DateTimeKind.Utc), "Bad/Timezone"));
        Assert.Equal("2 hours ago", formatter.Relative(new DateTime(2026, 7, 16, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 7, 16, 12, 0, 0, DateTimeKind.Utc)));
        Assert.Equal("SEK -1,234.50", new MoneyFormatter().Format(-1234.5m, "SEK"));
        Assert.Equal("0", new NumberFormatter().Integer(0));
    }

    [Fact]
    public void InvalidFormattingCulture_FallsBackWithoutThrowing()
    {
        using var _ = new CultureScope("en-GB");
        var context = new CompanyPresentationContext();
        context.SetFormattingCulture("not-a-culture");

        Assert.Equal("1,234.50", new NumberFormatter(context).Decimal(1234.5m));
    }

    private static string NormalizeDisplaySpacing(string value) =>
        value.Replace("\u00C2", string.Empty, StringComparison.Ordinal).Replace('\u00A0', ' ');

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo original = CultureInfo.CurrentCulture;
        private readonly CultureInfo originalUi = CultureInfo.CurrentUICulture;

        public CultureScope(string name)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = original;
            CultureInfo.CurrentUICulture = originalUi;
        }
    }
}
