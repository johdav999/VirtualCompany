namespace VirtualCompany.Domain.Entities;

public sealed class CompanyCurrencyDefinition : ICompanyOwnedEntity
{
    private CompanyCurrencyDefinition() { }

    public CompanyCurrencyDefinition(Guid id, Guid companyId, string code, string name,
        int minorUnitPrecision, bool isEnabled, DateTime createdUtc)
    {
        Id = ExchangeRateText.Id(id, nameof(id));
        CompanyId = ExchangeRateText.Id(companyId, nameof(companyId));
        Code = ExchangeRateText.Currency(code, nameof(code));
        Name = ExchangeRateText.Required(name, 100, nameof(name));
        if (minorUnitPrecision is < 0 or > 6)
            throw new ArgumentOutOfRangeException(nameof(minorUnitPrecision), "Currency precision must be between 0 and 6.");
        MinorUnitPrecision = minorUnitPrecision;
        IsEnabled = isEnabled;
        CreatedUtc = UpdatedUtc = ExchangeRateText.Utc(createdUtc);
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public int MinorUnitPrecision { get; private set; }
    public bool IsEnabled { get; private set; }
    public long Version { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }

    public void Configure(string name, int minorUnitPrecision, bool isEnabled, long expectedVersion, DateTime nowUtc)
    {
        EnsureVersion(expectedVersion);
        if (minorUnitPrecision is < 0 or > 6)
            throw new ArgumentOutOfRangeException(nameof(minorUnitPrecision), "Currency precision must be between 0 and 6.");
        Name = ExchangeRateText.Required(name, 100, nameof(name));
        MinorUnitPrecision = minorUnitPrecision;
        IsEnabled = isEnabled;
        UpdatedUtc = ExchangeRateText.Utc(nowUtc);
        Version++;
    }

    public void EnsureVersion(long expectedVersion)
    {
        if (Version != expectedVersion)
            throw new InvalidOperationException("The currency definition changed after it was loaded.");
    }
}

internal static class ExchangeRateText
{
    public static Guid Id(Guid value, string name) =>
        value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;

    public static string Currency(string value, string name)
    {
        var normalized = Required(value, 3, name).ToUpperInvariant();
        if (normalized.Length != 3 || normalized.Any(character => character is < 'A' or > 'Z'))
            throw new ArgumentException("Currency must be a three-letter alphabetic code.", name);
        return normalized;
    }

    public static string Token(string value, int maxLength, string name) =>
        Required(value, maxLength, name).Replace('-', '_').ToLowerInvariant();

    public static string Required(string value, int maxLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
    }

    public static string? Optional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    public static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
