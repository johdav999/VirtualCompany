namespace VirtualCompany.Shared;

public sealed class FinanceSeedingStateDiagnosticsResponse
{
    public string? PersistedState { get; set; }
    public string? MetadataState { get; set; }
    public bool MetadataPresent { get; set; }
    public bool MetadataIndicatesComplete { get; set; }
    public bool UsedFastPath { get; set; }
    public string Reason { get; set; } = string.Empty;
    public bool HasAccounts { get; set; }
    public bool HasCounterparties { get; set; }
    public bool HasTransactions { get; set; }
    public bool HasBalances { get; set; }
    public bool HasPolicyConfiguration { get; set; }
    public bool HasInvoices { get; set; }
    public bool HasBills { get; set; }
}

public sealed class FinanceSeedingStateResponse
{
    public Guid CompanyId { get; set; }
    public string SeedingState { get; set; } = string.Empty;
    public string DerivedFrom { get; set; } = string.Empty;
    public DateTime CheckedAtUtc { get; set; }
    public FinanceSeedingStateDiagnosticsResponse Diagnostics { get; set; } = new();
}

public sealed class FinanceEntryInitializationResponse
{
    public Guid CompanyId { get; set; }
    public string InitializationStatus { get; set; } = string.Empty;
    public string ProgressState { get; set; } = string.Empty;
    public string SeedingState { get; set; } = string.Empty;
    public bool SeedJobEnqueued { get; set; }
    public bool SeedJobActive { get; set; }
    public bool CanRetry { get; set; }
    public bool CanRefresh { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime CheckedAtUtc { get; set; }
    public DateTime? SeededAtUtc { get; set; }
    public DateTime? LastAttemptedUtc { get; set; }
    public DateTime? LastCompletedUtc { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }
    public string? JobStatus { get; set; }
    public string? CorrelationId { get; set; }
    public string? IdempotencyKey { get; set; }
    public bool DataAlreadyExists { get; set; }
    public string SeedMode { get; set; } = string.Empty;
    public bool CanGenerate { get; set; } = true;
    public string RecommendedAction { get; set; } = FinanceRecommendedActionContractValues.Generate;
    public IReadOnlyList<string> SupportedModes { get; set; } = [FinanceManualSeedModes.Replace];
    public string SeedOperation { get; set; } = FinanceSeedOperationContractValues.None;
    public bool ConfirmationRequired { get; set; }
    public bool FallbackTriggered { get; set; }
    public string? StatusEndpoint { get; set; }
    public string? SeedEndpoint { get; set; }
    public string? ConfirmationMessage { get; set; }
}

public static class FinanceManualSeedModes
{
    public const string Replace = "replace";
    public const string Append = "append";

    private static readonly HashSet<string> AllowedModes = new(StringComparer.OrdinalIgnoreCase)
    {
        Replace,
        Append
    };

    public static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();

    public static bool IsSupported(string? value) =>
        AllowedModes.Contains(Normalize(value));
}

public sealed class FinanceManualSeedRequest
{
    public string Mode { get; set; } = FinanceManualSeedModes.Replace;
    public bool ConfirmReplace { get; set; }
}

public sealed class FinanceInitializationProblemResponse
{
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public int Status { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Guid CompanyId { get; set; }
    public string Domain { get; set; } = FinanceInitializationDomainValues.Finance;
    public string Module { get; set; } = FinanceInitializationDomainValues.Finance;
    public bool CanTriggerSeed { get; set; }
    public bool CanGenerate { get; set; }
    public string RecommendedAction { get; set; } = FinanceRecommendedActionContractValues.Generate;
    public IReadOnlyList<string> SupportedModes { get; set; } = [FinanceManualSeedModes.Replace];
    public bool FallbackTriggered { get; set; }
    public bool SeedRequested { get; set; }
    public bool SeedJobActive { get; set; }
    public bool ConfirmationRequired { get; set; }
    public string ProgressState { get; set; } = string.Empty;
    public string SeedingState { get; set; } = string.Empty;
    public string InitializationStatus { get; set; } = string.Empty;
    public string? JobStatus { get; set; }
    public string? CorrelationId { get; set; }
    public string? StatusEndpoint { get; set; }
    public string? SeedEndpoint { get; set; }
    public string? ConfirmationMessage { get; set; }
}

public static class FinanceInitializationProblemCodeValues
{
    public const string NotInitialized = "not_initialized";
}

public static class FinanceInitializationDomainValues
{
    public const string Finance = "finance";
}

public static class FinanceRecommendedActionContractValues
{
    public const string Generate = "generate";
    public const string Regenerate = "regenerate";
}

public static class FinanceSeedingStateContractValues
{
    public const string Seeding = "seeding";
    public const string Seeded = "seeded";
    public const string Failed = "failed";
    public const string NotSeeded = "not_seeded";
    public const string PartiallySeeded = Seeding;
    public const string FullySeeded = Seeded;
}

public static class FinanceEntryProgressStateContractValues
{
    public const string NotSeeded = "not_seeded";
    public const string SeedingRequested = "seeding_requested";
    public const string InProgress = "in_progress";
    public const string Seeded = "seeded";
    public const string Failed = "failed";
}

public static class FinanceEntryInitializationContractValues
{
    public const string Ready = "ready";
    public const string Initializing = "initializing";
    public const string Failed = "failed";
}

public static class FinanceSeedOperationContractValues
{
    public const string None = "none";
    public const string Started = "started";
    public const string Reused = "reused";
    public const string Skipped = "skipped";
    public const string Rejected = "rejected";
}