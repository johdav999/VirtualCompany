namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceInitializationOptions
{
    public const string SectionName = "FinanceInitialization";

    public string MissingDatasetBehavior { get; set; } = FinanceMissingDatasetBehaviorValues.TriggerSeed;

    public bool ShouldTriggerSeedFallback() =>
        string.Equals(
            FinanceMissingDatasetBehaviorValues.Normalize(MissingDatasetBehavior),
            FinanceMissingDatasetBehaviorValues.TriggerSeed,
            StringComparison.OrdinalIgnoreCase);
}

public static class FinanceMissingDatasetBehaviorValues
{
    public const string ReturnNotInitialized = "return_not_initialized";
    public const string TriggerSeed = "trigger_seed";

    public static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? TriggerSeed
            : value.Trim().Replace('-', '_').Replace(' ', '_').ToLowerInvariant();

    public static bool IsSupported(string? value) =>
        string.Equals(Normalize(value), ReturnNotInitialized, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Normalize(value), TriggerSeed, StringComparison.OrdinalIgnoreCase);
}