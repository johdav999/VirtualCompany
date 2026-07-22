using System.Globalization;
using System.Text.RegularExpressions;

namespace VirtualCompany.Domain.ValueObjects;

public static partial class CommunicationLanguageTag
{
    public const int MaxLength = 20;

    public static string? NormalizeOptional(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var candidate = value.Trim();
        if (candidate.Length > MaxLength || !LanguageTagPattern().IsMatch(candidate))
        {
            throw new ArgumentException("Use a valid BCP 47 language tag, such as en-GB or sv-SE.", parameterName);
        }

        try
        {
            return CultureInfo.GetCultureInfo(candidate).Name;
        }
        catch (CultureNotFoundException exception)
        {
            throw new ArgumentException("Use a recognized BCP 47 language tag, such as en-GB or sv-SE.", parameterName, exception);
        }
    }

    public static bool TryNormalize(string? value, out string? normalized)
    {
        try
        {
            normalized = NormalizeOptional(value, nameof(value));
            return normalized is not null;
        }
        catch (ArgumentException)
        {
            normalized = null;
            return false;
        }
    }

    [GeneratedRegex("^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageTagPattern();
}
