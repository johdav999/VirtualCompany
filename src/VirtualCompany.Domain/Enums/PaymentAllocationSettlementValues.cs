namespace VirtualCompany.Domain.Enums;

public static class PaymentAllocationSettlementStatuses
{
    public const string LegacyUnavailable = "legacy_unavailable";
    public const string Posted = "posted";
    public const string Reversed = "reversed";

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        Posted => Posted,
        Reversed => Reversed,
        _ => LegacyUnavailable
    };

    public static bool IsSupported(string value) =>
        value is LegacyUnavailable or Posted or Reversed;
}
