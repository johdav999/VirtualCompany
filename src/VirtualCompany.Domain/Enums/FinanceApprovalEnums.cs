namespace VirtualCompany.Domain.Enums;

public enum ApprovalTargetType
{
    Bill = 1,
    Payment = 2,
    Exception = 3
}

public static class ApprovalTargetTypeValues
{
    private static readonly IReadOnlyDictionary<ApprovalTargetType, string> Values = new Dictionary<ApprovalTargetType, string>
    {
        [ApprovalTargetType.Bill] = "bill",
        [ApprovalTargetType.Payment] = "payment",
        [ApprovalTargetType.Exception] = "exception"
    };

    private static readonly IReadOnlyDictionary<string, ApprovalTargetType> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllowedValues { get; } = ReverseValues.Keys.OrderBy(x => x).ToArray();

    public static string ToStorageValue(this ApprovalTargetType targetType) =>
        Values.TryGetValue(targetType, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(targetType), targetType, "Unsupported approval target type.");

    public static ApprovalTargetType Parse(string value) =>
        TryParse(value, out var targetType)
            ? targetType
            : throw new ArgumentOutOfRangeException(nameof(value), value, $"Unsupported approval target type. Allowed values: {string.Join(", ", AllowedValues)}.");

    public static bool TryParse(string? value, out ApprovalTargetType targetType)
    {
        targetType = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return ReverseValues.TryGetValue(trimmed, out targetType) ||
            Enum.TryParse(trimmed, ignoreCase: true, out targetType) && Values.ContainsKey(targetType);
    }

    public static void EnsureSupported(ApprovalTargetType targetType, string? paramName = null)
    {
        if (!Values.ContainsKey(targetType))
        {
            throw new ArgumentOutOfRangeException(paramName ?? nameof(targetType), targetType, "Unsupported approval target type.");
        }
    }

    public static string BuildCheckConstraintSql(string columnName)
    {
        var allowedValues = string.Join(", ", AllowedValues.Select(value => $"'{value}'"));
        return $"{columnName} IN ({allowedValues})";
    }
}

public enum ApprovalTaskStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Escalated = 4
}

public static class ApprovalTaskStatusValues
{
    private static readonly IReadOnlyDictionary<ApprovalTaskStatus, string> Values = new Dictionary<ApprovalTaskStatus, string>
    {
        [ApprovalTaskStatus.Pending] = "pending",
        [ApprovalTaskStatus.Approved] = "approved",
        [ApprovalTaskStatus.Rejected] = "rejected",
        [ApprovalTaskStatus.Escalated] = "escalated"
    };

    private static readonly IReadOnlyDictionary<string, ApprovalTaskStatus> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllowedValues { get; } = ReverseValues.Keys.OrderBy(x => x).ToArray();

    public static string ToStorageValue(this ApprovalTaskStatus status) =>
        Values.TryGetValue(status, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported approval task status.");

    public static ApprovalTaskStatus Parse(string value) =>
        TryParse(value, out var status)
            ? status
            : throw new ArgumentOutOfRangeException(nameof(value), value, $"Unsupported approval task status. Allowed values: {string.Join(", ", AllowedValues)}.");

    public static bool TryParse(string? value, out ApprovalTaskStatus status)
    {
        status = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return ReverseValues.TryGetValue(trimmed, out status) ||
            Enum.TryParse(trimmed, ignoreCase: true, out status) && Values.ContainsKey(status);
    }

    public static void EnsureSupported(ApprovalTaskStatus status, string? paramName = null)
    {
        if (!Values.ContainsKey(status))
        {
            throw new ArgumentOutOfRangeException(paramName ?? nameof(status), status, "Unsupported approval task status.");
        }
    }

    public static string BuildCheckConstraintSql(string columnName)
    {
        var allowedValues = string.Join(", ", AllowedValues.Select(value => $"'{value}'"));
        return $"{columnName} IN ({allowedValues})";
    }
}