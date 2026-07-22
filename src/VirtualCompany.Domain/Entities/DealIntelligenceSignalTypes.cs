namespace VirtualCompany.Domain.Entities;
public static class DealIntelligenceSignalTypes
{
    public const string Ghosting = "ghosting";
    public const string PriceResistance = "price_resistance";
    public const string BuyingIntent = "buying_intent";

    public static IReadOnlySet<string> Supported { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Ghosting,
        PriceResistance,
        BuyingIntent
    };

    public static string Normalize(string value)
    {
        var normalized = SalesEntityText.NormalizeRequired(value, nameof(value), 64).ToLowerInvariant();
        return Supported.Contains(normalized)
            ? normalized
            : throw new ArgumentException("Unsupported deal intelligence signal type.", nameof(value));
    }
}

