namespace VirtualCompany.Domain.Enums;

public enum MemoryType
{
    Preference = 1,
    DecisionPattern = 2,
    Summary = 3,
    RoleMemory = 4,
    CompanyMemory = 5
}

public static class MemoryTypeValues
{
    public const string Preference = "preference";
    public const string DecisionPattern = "decision_pattern";
    public const string Summary = "summary";
    public const string RoleMemory = "role_memory";
    public const string CompanyMemory = "company_memory";

    private static readonly IReadOnlyDictionary<MemoryType, string> Values = new Dictionary<MemoryType, string>
    {
        [MemoryType.Preference] = Preference,
        [MemoryType.DecisionPattern] = DecisionPattern,
        [MemoryType.Summary] = Summary,
        [MemoryType.RoleMemory] = RoleMemory,
        [MemoryType.CompanyMemory] = CompanyMemory
    };

    private static readonly IReadOnlyDictionary<string, MemoryType> LegacyAliases =
        new Dictionary<string, MemoryType>(StringComparer.OrdinalIgnoreCase)
        {
            [BuildAliasKey(Preference)] = MemoryType.Preference,
            [BuildAliasKey(DecisionPattern)] = MemoryType.DecisionPattern,
            [BuildAliasKey(Summary)] = MemoryType.Summary,
            [BuildAliasKey(RoleMemory)] = MemoryType.RoleMemory,
            [BuildAliasKey(CompanyMemory)] = MemoryType.CompanyMemory
        };

    private static readonly IReadOnlyDictionary<string, MemoryType> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllowedValues { get; } = ReverseValues.Keys.OrderBy(x => x).ToArray();

    public static string ToStorageValue(this MemoryType value) =>
        Values.TryGetValue(value, out var storageValue)
            ? storageValue
            : throw new ArgumentOutOfRangeException(nameof(value), value, BuildValidationMessage());

    public static bool TryParse(string? value, out MemoryType memoryType)
    {
        memoryType = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (ReverseValues.TryGetValue(trimmed, out memoryType))
        {
            return true;
        }

        if (LegacyAliases.TryGetValue(BuildAliasKey(trimmed), out memoryType))
        {
            return true;
        }

        return Enum.TryParse(trimmed, ignoreCase: true, out memoryType) && Values.ContainsKey(memoryType);
    }

    public static string BuildCheckConstraintSql(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            throw new ArgumentException("Column name is required.", nameof(columnName));
        }

        var allowedValues = string.Join(", ", AllowedValues.Select(value => $"'{value}'"));
        return $"{columnName} IN ({allowedValues})";
    }

    private static string BuildAliasKey(string value) =>
        new(value.Where(char.IsLetterOrDigit).ToArray());

    public static MemoryType Parse(string value)
    {
        if (TryParse(value, out var memoryType))
        {
            return memoryType;
        }

        throw new ArgumentOutOfRangeException(nameof(value), value, BuildValidationMessage(value));
    }

    public static void EnsureSupported(MemoryType value, string paramName)
    {
        _ = value.ToStorageValue();
    }

    public static string BuildValidationMessage(string? attemptedValue = null)
    {
        var allowedValues = string.Join(", ", AllowedValues);
        return string.IsNullOrWhiteSpace(attemptedValue)
            ? $"Memory type is required. Allowed values: {allowedValues}."
            : $"Unsupported memory type '{attemptedValue}'. Allowed values: {allowedValues}.";
    }
}

public enum MemoryScope
{
    All = 1,
    CompanyWide = 2,
    AgentSpecific = 3,
    CombinedForAgent = 4
}

public static class MemoryScopeValues
{
    public const string All = "all";
    public const string CompanyWide = "company_wide";
    public const string AgentSpecific = "agent_specific";
    public const string CombinedForAgent = "combined_for_agent";

    private static readonly IReadOnlyDictionary<MemoryScope, string> Values = new Dictionary<MemoryScope, string>
    {
        [MemoryScope.All] = All,
        [MemoryScope.CompanyWide] = CompanyWide,
        [MemoryScope.AgentSpecific] = AgentSpecific,
        [MemoryScope.CombinedForAgent] = CombinedForAgent
    };

    private static readonly IReadOnlyDictionary<string, MemoryScope> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllowedValues { get; } = ReverseValues.Keys.OrderBy(x => x).ToArray();

    public static string ToStorageValue(this MemoryScope value) =>
        Values.TryGetValue(value, out var storageValue)
            ? storageValue
            : throw new ArgumentOutOfRangeException(nameof(value), value, BuildValidationMessage());

    public static bool TryParse(string? value, out MemoryScope scope)
    {
        scope = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return ReverseValues.TryGetValue(value.Trim(), out scope);
    }

    public static MemoryScope Parse(string value) =>
        TryParse(value, out var scope)
            ? scope
            : throw new ArgumentOutOfRangeException(nameof(value), value, BuildValidationMessage(value));

    public static string BuildValidationMessage(string? attemptedValue = null)
    {
        var allowedValues = string.Join(", ", AllowedValues);
        return string.IsNullOrWhiteSpace(attemptedValue)
            ? $"Memory scope is required. Allowed values: {allowedValues}."
            : $"Unsupported memory scope '{attemptedValue}'. Allowed values: {allowedValues}.";
    }
}
