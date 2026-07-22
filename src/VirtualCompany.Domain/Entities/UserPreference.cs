namespace VirtualCompany.Domain.Entities;

public sealed class UserPreference
{
    private UserPreference()
    {
    }

    public UserPreference(Guid userId, string uiCulture, string? formattingCulture = null)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId is required.", nameof(userId));
        }

        UserId = userId;
        UiCulture = NormalizeCulture(uiCulture, nameof(uiCulture), required: true)!;
        FormattingCulture = NormalizeCulture(formattingCulture, nameof(formattingCulture), required: false);
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid UserId { get; private set; }
    public string UiCulture { get; private set; } = null!;
    public string? FormattingCulture { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public User User { get; private set; } = null!;

    public bool Update(string uiCulture, string? formattingCulture)
    {
        var normalizedUiCulture = NormalizeCulture(uiCulture, nameof(uiCulture), required: true)!;
        var normalizedFormattingCulture = NormalizeCulture(formattingCulture, nameof(formattingCulture), required: false);
        if (string.Equals(UiCulture, normalizedUiCulture, StringComparison.Ordinal) &&
            string.Equals(FormattingCulture, normalizedFormattingCulture, StringComparison.Ordinal))
        {
            return false;
        }

        UiCulture = normalizedUiCulture;
        FormattingCulture = normalizedFormattingCulture;
        UpdatedUtc = DateTime.UtcNow;
        return true;
    }

    private static string? NormalizeCulture(string? value, string name, bool required)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                throw new ArgumentException($"{name} is required.", name);
            }

            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > 20)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be 20 characters or fewer.");
        }

        return normalized;
    }
}

public sealed class UserPreferenceChange
{
    private UserPreferenceChange()
    {
    }

    public UserPreferenceChange(
        Guid userId,
        string? previousUiCulture,
        string newUiCulture,
        string? previousFormattingCulture,
        string? newFormattingCulture)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId is required.", nameof(userId));
        }

        Id = Guid.NewGuid();
        UserId = userId;
        PreviousUiCulture = previousUiCulture;
        NewUiCulture = newUiCulture;
        PreviousFormattingCulture = previousFormattingCulture;
        NewFormattingCulture = newFormattingCulture;
        ChangedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string? PreviousUiCulture { get; private set; }
    public string NewUiCulture { get; private set; } = null!;
    public string? PreviousFormattingCulture { get; private set; }
    public string? NewFormattingCulture { get; private set; }
    public DateTime ChangedUtc { get; private set; }
    public User User { get; private set; } = null!;
}
