using System.Globalization;

namespace VirtualCompany.Application.Auth;

public static class SupportedUserCultures
{
    public const string Default = "en-GB";
    public static IReadOnlyList<string> All { get; } = [Default, "sv-SE"];

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(value.Trim());
            var supported = All.FirstOrDefault(candidate =>
                string.Equals(candidate, culture.Name, StringComparison.OrdinalIgnoreCase));
            if (supported is null)
            {
                return false;
            }

            normalized = supported;
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }
}

public static class UserPreferenceErrorCodes
{
    public const string UnsupportedUiCulture = "preferences.ui_culture_unsupported";
    public const string UnsupportedFormattingCulture = "preferences.formatting_culture_unsupported";
    public const string CurrentUserRequired = "preferences.current_user_required";
}

public sealed record UserPreferenceDto(
    string UiCulture,
    string? FormattingCulture,
    DateTime? UpdatedUtc);

public sealed record UpdateUserPreferenceCommand(
    string UiCulture,
    string? FormattingCulture);

public interface IUserPreferenceService
{
    Task<UserPreferenceDto> GetCurrentAsync(CancellationToken cancellationToken);
    Task<UserPreferenceDto> UpdateCurrentAsync(UpdateUserPreferenceCommand command, CancellationToken cancellationToken);
}

public sealed class UserPreferenceValidationException : Exception
{
    public UserPreferenceValidationException(string code, string field, string message)
        : base(message)
    {
        Code = code;
        Errors = new Dictionary<string, string[]> { [field] = [message] };
    }

    public string Code { get; }
    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
