namespace VirtualCompany.Domain.Enums;

public enum ApprovalTargetEntityType
{
    Task = 1,
    Workflow = 2,
    Action = 3
}

public static class ApprovalTargetEntityTypeValues
{
    private static readonly IReadOnlyDictionary<ApprovalTargetEntityType, string> Values = new Dictionary<ApprovalTargetEntityType, string>
    {
        [ApprovalTargetEntityType.Task] = "task",
        [ApprovalTargetEntityType.Workflow] = "workflow",
        [ApprovalTargetEntityType.Action] = "action"
    };

    private static readonly IReadOnlyDictionary<string, ApprovalTargetEntityType> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllowedValues { get; } = ReverseValues.Keys.OrderBy(x => x).ToArray();

    public static string ToStorageValue(this ApprovalTargetEntityType type) =>
        Values.TryGetValue(type, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported approval target entity type.");

    public static bool TryParse(string? value, out ApprovalTargetEntityType type)
    {
        type = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (ReverseValues.TryGetValue(trimmed, out type))
        {
            return true;
        }

        return Enum.TryParse(trimmed, ignoreCase: true, out type) && Values.ContainsKey(type);
    }

    public static ApprovalTargetEntityType Parse(string value) =>
        TryParse(value, out var type)
            ? type
            : throw new ArgumentOutOfRangeException(nameof(value), value, $"Unsupported approval target entity type. Allowed values: {string.Join(", ", AllowedValues)}.");
}

public enum ApprovalStepApproverType
{
    Role = 1,
    User = 2
}

public static class ApprovalStepApproverTypeValues
{
    private static readonly IReadOnlyDictionary<ApprovalStepApproverType, string> Values = new Dictionary<ApprovalStepApproverType, string>
    {
        [ApprovalStepApproverType.Role] = "role",
        [ApprovalStepApproverType.User] = "user"
    };

    private static readonly IReadOnlyDictionary<string, ApprovalStepApproverType> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllowedValues { get; } = ReverseValues.Keys.OrderBy(x => x).ToArray();

    public static string ToStorageValue(this ApprovalStepApproverType type) =>
        Values.TryGetValue(type, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported approval step approver type.");

    public static ApprovalStepApproverType Parse(string value) =>
        !string.IsNullOrWhiteSpace(value) && ReverseValues.TryGetValue(value.Trim(), out var type)
            ? type
            : throw new ArgumentOutOfRangeException(nameof(value), value, $"Unsupported approval step approver type. Allowed values: {string.Join(", ", AllowedValues)}.");
}

public enum ApprovalStepStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Skipped = 4
}

public static class ApprovalStepStatusValues
{
    private static readonly IReadOnlyDictionary<ApprovalStepStatus, string> Values = new Dictionary<ApprovalStepStatus, string>
    {
        [ApprovalStepStatus.Pending] = "pending",
        [ApprovalStepStatus.Approved] = "approved",
        [ApprovalStepStatus.Rejected] = "rejected",
        [ApprovalStepStatus.Skipped] = "skipped"
    };

    private static readonly IReadOnlyDictionary<string, ApprovalStepStatus> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToStorageValue(this ApprovalStepStatus status) =>
        Values.TryGetValue(status, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported approval step status.");

    public static ApprovalStepStatus Parse(string value) =>
        !string.IsNullOrWhiteSpace(value) && ReverseValues.TryGetValue(value.Trim(), out var status)
            ? status
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported approval step status.");
}