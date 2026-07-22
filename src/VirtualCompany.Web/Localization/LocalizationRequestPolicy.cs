using Microsoft.AspNetCore.Localization;

namespace VirtualCompany.Web.Localization;

public static class LocalizationRequestPolicy
{
    public static string NormalizeLocalReturnUrl(string? value)
    {
        var candidate = value?.Trim();
        if (string.IsNullOrWhiteSpace(candidate) ||
            !candidate.StartsWith("/", StringComparison.Ordinal) ||
            candidate.StartsWith("//", StringComparison.Ordinal) ||
            candidate.Contains('\\'))
        {
            return "/";
        }

        return candidate;
    }

    public static bool ShouldSynchronize(string? persistedCulture, string? activeCulture) =>
        SupportedWebCultures.TryNormalize(persistedCulture, out var persisted) &&
        SupportedWebCultures.TryNormalize(activeCulture, out var active) &&
        !string.Equals(persisted, active, StringComparison.OrdinalIgnoreCase);

    public static string CreateCookieValue(string culture)
    {
        if (!SupportedWebCultures.TryNormalize(culture, out var normalized))
        {
            throw new ArgumentException("A supported culture is required.", nameof(culture));
        }

        return CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(normalized));
    }
}
