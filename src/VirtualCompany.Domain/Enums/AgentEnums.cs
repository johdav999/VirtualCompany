namespace VirtualCompany.Domain.Enums;

public enum AgentStatus
{
    Active = 1,
    Paused = 2,
    Restricted = 3,
    Archived = 4
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
    public const string Paused = "paused";
    public const string Restricted = "restricted";
    public const string Archived = "archived";
    public const string Inactive = "inactive";

    private static readonly IReadOnlyDictionary<AgentStatus, string> Values = new Dictionary<AgentStatus, string>
    {
        [AgentStatus.Active] = Active,
        [AgentStatus.Paused] = Paused,
        [AgentStatus.Restricted] = Restricted,
        [AgentStatus.Archived] = Archived
    };

    private static readonly IReadOnlyDictionary<string, AgentStatus> ReverseValues =
        new Dictionary<string, AgentStatus>(StringComparer.OrdinalIgnoreCase)
        {
            [Active] = AgentStatus.Active,
            [Paused] = AgentStatus.Paused,
            [Restricted] = AgentStatus.Restricted,
            [Archived] = AgentStatus.Archived,
            [Inactive] = AgentStatus.Paused
        };

    public static AgentStatus DefaultStatus => AgentStatus.Active;
    public static IReadOnlyList<string> AllowedValues { get; } = [Active, Paused, Restricted, Archived];

    public static string ToStorageValue(this AgentStatus status)
    {
        if (Values.TryGetValue(status, out var value))
        {
            return value;
        }

        throw new ArgumentOutOfRangeException(nameof(status), status, BuildValidationMessage());
    }

    public static bool TryParse(string? value, out AgentStatus status)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            status = default;
            return false;
        }

        var trimmed = value.Trim();
        if (ReverseValues.TryGetValue(trimmed, out status))
        {
            return true;
        }

        return Enum.TryParse(trimmed, ignoreCase: true, out status) && Values.ContainsKey(status);
    }

    public static AgentStatus Parse(string value)
    {
        if (TryParse(value, out var status))
        {
            return status;
        }

        throw new ArgumentOutOfRangeException(nameof(value), value, BuildValidationMessage(value));
    }

    public static void EnsureSupported(AgentStatus status, string paramName)
    {
        _ = status.ToStorageValue();
    }

    public static string BuildValidationMessage(string? attemptedValue = null)
    {
        var allowedValues = string.Join(", ", AllowedValues);
        return string.IsNullOrWhiteSpace(attemptedValue)
            ? $"Agent status is required. Allowed values: {allowedValues}."
            : $"Unsupported agent status value '{attemptedValue}'. Allowed values: {allowedValues}.";
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