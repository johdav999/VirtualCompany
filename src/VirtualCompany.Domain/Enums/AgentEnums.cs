using System.Globalization;

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

public enum AgentAutonomyLevel
{
    Level0 = 0,
    Level1 = 1,
    Level2 = 2,
    Level3 = 3
}

public enum ToolActionType
{
    Read = 1,
    Recommend = 2,
    Execute = 3
}

public enum ToolExecutionStatus
{
    Denied = 1,
    AwaitingApproval = 2,
    Executed = 3,
    Failed = 4
}

public static class AgentStatusValues
{
    public const string Active = "active";
    public const string Paused = "paused";
    public const string Restricted = "restricted";
    public const string Archived = "archived";

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
            [Archived] = AgentStatus.Archived
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

public static class AgentAutonomyLevelValues
{
    private static readonly IReadOnlyDictionary<AgentAutonomyLevel, string> Values = new Dictionary<AgentAutonomyLevel, string>
    {
        [AgentAutonomyLevel.Level0] = "level_0",
        [AgentAutonomyLevel.Level1] = "level_1",
        [AgentAutonomyLevel.Level2] = "level_2",
        [AgentAutonomyLevel.Level3] = "level_3"
    };

    private static readonly IReadOnlyDictionary<string, AgentAutonomyLevel> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<int, AgentAutonomyLevel> NumericValues = new Dictionary<int, AgentAutonomyLevel>
    {
        [0] = AgentAutonomyLevel.Level0,
        [1] = AgentAutonomyLevel.Level1,
        [2] = AgentAutonomyLevel.Level2,
        [3] = AgentAutonomyLevel.Level3
    };

    public static AgentAutonomyLevel DefaultLevel => AgentAutonomyLevel.Level0;
    public static IReadOnlyList<string> AllowedValues { get; } = ReverseValues.Keys.OrderBy(x => x).ToArray();

    public static string ToStorageValue(this AgentAutonomyLevel autonomyLevel)
    {
        if (Values.TryGetValue(autonomyLevel, out var value))
        {
            return value;
        }

        throw new ArgumentOutOfRangeException(nameof(autonomyLevel), autonomyLevel, BuildValidationMessage());
    }

    public static bool TryParse(string? value, out AgentAutonomyLevel autonomyLevel)
    {
        autonomyLevel = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (ReverseValues.TryGetValue(value.Trim(), out autonomyLevel))
        {
            return true;
        }

        if (int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var numericLevel) &&
            NumericValues.TryGetValue(numericLevel, out autonomyLevel))
        {
            return true;
        }

        return Enum.TryParse(value.Trim(), ignoreCase: true, out autonomyLevel) && Values.ContainsKey(autonomyLevel);
    }

    public static AgentAutonomyLevel Parse(string value)
    {
        if (TryParse(value, out var autonomyLevel))
        {
            return autonomyLevel;
        }

        throw new ArgumentOutOfRangeException(nameof(value), value, BuildValidationMessage(value));
    }

    public static void EnsureSupported(AgentAutonomyLevel autonomyLevel, string paramName)
    {
        _ = autonomyLevel.ToStorageValue();
    }

    public static string BuildValidationMessage(string? attemptedValue = null)
    {
        const string allowedValues = "0, 1, 2, 3 (level_0, level_1, level_2, level_3)";
        return string.IsNullOrWhiteSpace(attemptedValue)
            ? $"Agent autonomy level is required. Allowed values: {allowedValues}."
            : $"Unsupported agent autonomy level '{attemptedValue}'. Allowed values: {allowedValues}.";
    }
}

public static class ToolActionTypeValues
{
    private static readonly IReadOnlyDictionary<ToolActionType, string> Values = new Dictionary<ToolActionType, string>
    {
        [ToolActionType.Read] = "read",
        [ToolActionType.Recommend] = "recommend",
        [ToolActionType.Execute] = "execute"
    };

    private static readonly IReadOnlyDictionary<string, ToolActionType> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllowedValues { get; } = ReverseValues.Keys.OrderBy(x => x).ToArray();

    public static string ToStorageValue(this ToolActionType actionType)
    {
        if (Values.TryGetValue(actionType, out var value))
        {
            return value;
        }

        throw new ArgumentOutOfRangeException(nameof(actionType), actionType, BuildValidationMessage());
    }

    public static bool TryParse(string? value, out ToolActionType actionType)
    {
        actionType = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (ReverseValues.TryGetValue(value.Trim(), out actionType))
        {
            return true;
        }

        return Enum.TryParse(value.Trim(), ignoreCase: true, out actionType) && Values.ContainsKey(actionType);
    }

    public static ToolActionType Parse(string value)
    {
        if (TryParse(value, out var actionType))
        {
            return actionType;
        }

        throw new ArgumentOutOfRangeException(nameof(value), value, BuildValidationMessage(value));
    }

    public static void EnsureSupported(ToolActionType actionType, string paramName)
    {
        _ = actionType.ToStorageValue();
    }

    public static string BuildValidationMessage(string? attemptedValue = null)
    {
        var allowedValues = string.Join(", ", AllowedValues);
        return string.IsNullOrWhiteSpace(attemptedValue)
            ? $"Tool action type is required. Allowed values: {allowedValues}."
            : $"Unsupported tool action type '{attemptedValue}'. Allowed values: {allowedValues}.";
    }
}

public static class ToolExecutionStatusValues
{
    private static readonly IReadOnlyDictionary<ToolExecutionStatus, string> Values = new Dictionary<ToolExecutionStatus, string>
    {
        [ToolExecutionStatus.Denied] = "denied",
        [ToolExecutionStatus.AwaitingApproval] = "awaiting_approval",
        [ToolExecutionStatus.Executed] = "executed",
        [ToolExecutionStatus.Failed] = "failed"
    };

    private static readonly IReadOnlyDictionary<string, ToolExecutionStatus> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToStorageValue(this ToolExecutionStatus status) =>
        Values.TryGetValue(status, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported tool execution status.");

    public static ToolExecutionStatus Parse(string value) =>
        ReverseValues.TryGetValue(value.Trim(), out var status)
            ? status
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported tool execution status.");
}

public enum ApprovalRequestStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Expired = 4
}

public static class ApprovalRequestStatusValues
{
    private static readonly IReadOnlyDictionary<ApprovalRequestStatus, string> Values = new Dictionary<ApprovalRequestStatus, string>
    {
        [ApprovalRequestStatus.Pending] = "pending",
        [ApprovalRequestStatus.Approved] = "approved",
        [ApprovalRequestStatus.Rejected] = "rejected",
        [ApprovalRequestStatus.Expired] = "expired"
    };

    private static readonly IReadOnlyDictionary<string, ApprovalRequestStatus> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToStorageValue(this ApprovalRequestStatus status) =>
        Values.TryGetValue(status, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported approval request status.");

    public static ApprovalRequestStatus Parse(string value) =>
        ReverseValues.TryGetValue(value.Trim(), out var status)
            ? status
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported approval request status.");
}