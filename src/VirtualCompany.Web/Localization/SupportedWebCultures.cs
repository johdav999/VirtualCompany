using System.Globalization;

namespace VirtualCompany.Web.Localization;

public sealed record SupportedWebCulture(string Name, string DisplayName);

public static class SupportedWebCultures
{
    public const string Default = "en-GB";

    public static IReadOnlyList<SupportedWebCulture> All { get; } =
    [
        new(Default, "English (United Kingdom)"),
        new("sv-SE", "Svenska (Sverige)")
    ];

    public static IList<CultureInfo> CultureInfos { get; } =
        All.Select(item => CultureInfo.GetCultureInfo(item.Name)).ToArray();

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            var requested = CultureInfo.GetCultureInfo(value.Trim()).Name;
            var match = All.FirstOrDefault(item =>
                string.Equals(item.Name, requested, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                return false;
            }

            normalized = match.Name;
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }
}
