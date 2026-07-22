using Microsoft.Extensions.Localization;

namespace VirtualCompany.Web.Localization.Shared;

public static class SharedValidationPresenter
{
    private static readonly IReadOnlyDictionary<string, string> ResourceKeys =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["email_invalid"] = "EmailInvalid",
            ["field_invalid"] = "FieldInvalid",
            ["field_required"] = "FieldRequired",
            ["maximum_length"] = "MaximumLength",
            ["minimum_length"] = "MinimumLength",
            ["range"] = "Range"
        };

    public static string Format(
        string? code,
        IStringLocalizer<ValidationResources> localizer,
        params object[] arguments)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        var normalized = code?.Trim().Replace('-', '_').ToLowerInvariant();
        if (normalized is null || !ResourceKeys.TryGetValue(normalized, out var resourceKey))
        {
            return localizer["ValidationSummary"];
        }

        return localizer[resourceKey, arguments];
    }
}
