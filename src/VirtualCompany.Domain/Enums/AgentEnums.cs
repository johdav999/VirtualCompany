namespace VirtualCompany.Domain.Enums;

public enum AgentStatus
{
    Active = 1,
    Inactive = 2
}

public enum AgentSeniority
{
    Junior = 1,
    Mid = 2,
    Senior = 3,
    Lead = 4,
    Executive = 5
}

public static class AgentStatusValues
{
    public const string Active = "active";
    public const string Inactive = "inactive";
    public static AgentStatus DefaultStatus => AgentStatus.Active;

    public static string ToStorageValue(this AgentStatus status) =>
        status switch
        {
            AgentStatus.Active => Active,
            AgentStatus.Inactive => Inactive,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported agent status.")
        };

    public static AgentStatus Parse(string value)
    {
        value = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Agent status is required.", nameof(value));
        }

        return value.ToLowerInvariant() switch
        {
            Active => AgentStatus.Active,
            Inactive => AgentStatus.Inactive,
            _ when Enum.TryParse<AgentStatus>(value, ignoreCase: true, out var legacyStatus) => legacyStatus,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported agent status value.")
        };
    }

    public static void EnsureSupported(AgentStatus status, string paramName)
    {
        _ = status.ToStorageValue();
    }
}

public static class AgentSeniorityValues
{
    private static readonly IReadOnlyDictionary<AgentSeniority, string> Values = new Dictionary<AgentSeniority, string>
    {
        [AgentSeniority.Junior] = "junior",
        [AgentSeniority.Mid] = "mid",
        [AgentSeniority.Senior] = "senior",
        [AgentSeniority.Lead] = "lead",
        [AgentSeniority.Executive] = "executive"
    };

    private static readonly IReadOnlyDictionary<string, AgentSeniority> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllowedValues { get; } = ReverseValues.Keys.OrderBy(x => x).ToArray();

    public static string ToStorageValue(this AgentSeniority seniority)
    {
        if (Values.TryGetValue(seniority, out var value))
        {
            return value;
        }

        throw new ArgumentOutOfRangeException(nameof(seniority), seniority, BuildValidationMessage());
    }

    public static bool TryParse(string? value, out AgentSeniority seniority)
    {
        seniority = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (ReverseValues.TryGetValue(value.Trim(), out seniority))
        {
            return true;
        }

        if (Enum.TryParse<AgentSeniority>(value.Trim(), ignoreCase: true, out var legacy) && Values.ContainsKey(legacy))
        {
            seniority = legacy;
            return true;
        }

        return false;
    }

    public static AgentSeniority Parse(string value)
    {
        if (TryParse(value, out var seniority))
        {
            return seniority;
        }

        throw new ArgumentOutOfRangeException(nameof(value), value, BuildValidationMessage(value));
    }

    public static void EnsureSupported(AgentSeniority seniority, string paramName)
    {
        _ = seniority.ToStorageValue();
    }

    public static string BuildValidationMessage(string? attemptedValue = null)
    {
        var allowedValues = string.Join(", ", AllowedValues);
        return string.IsNullOrWhiteSpace(attemptedValue)
            ? $"Agent seniority is required. Allowed values: {allowedValues}."
            : $"Unsupported agent seniority value '{attemptedValue}'. Allowed values: {allowedValues}.";
    }
}