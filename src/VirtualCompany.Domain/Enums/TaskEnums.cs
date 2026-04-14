namespace VirtualCompany.Domain.Enums;

public enum WorkTaskStatus
{
    New = 1,
    InProgress = 2,
    Blocked = 3,
    AwaitingApproval = 4,
    Completed = 5,
    Failed = 6
}

public enum WorkTaskPriority
{
    Low = 1,
    Normal = 2,
    High = 3,
    Critical = 4
}

public static class WorkTaskStatusValues
{
    public const string New = "new";
    public const string InProgress = "in_progress";
    public const string Blocked = "blocked";
    public const string AwaitingApproval = "awaiting_approval";
    public const string Completed = "completed";
    public const string Failed = "failed";

    private static readonly IReadOnlyDictionary<WorkTaskStatus, string> Values = new Dictionary<WorkTaskStatus, string>
    {
        [WorkTaskStatus.New] = New,
        [WorkTaskStatus.InProgress] = InProgress,
        [WorkTaskStatus.Blocked] = Blocked,
        [WorkTaskStatus.AwaitingApproval] = AwaitingApproval,
        [WorkTaskStatus.Completed] = Completed,
        [WorkTaskStatus.Failed] = Failed
    };

    private static readonly IReadOnlyDictionary<string, WorkTaskStatus> ReverseValues =
        new Dictionary<string, WorkTaskStatus>(StringComparer.OrdinalIgnoreCase)
        {
            [New] = WorkTaskStatus.New,
            [InProgress] = WorkTaskStatus.InProgress,
            [Blocked] = WorkTaskStatus.Blocked,
            [AwaitingApproval] = WorkTaskStatus.AwaitingApproval,
            [Completed] = WorkTaskStatus.Completed,
            [Failed] = WorkTaskStatus.Failed
        };

    public static WorkTaskStatus DefaultStatus => WorkTaskStatus.New;
    public static IReadOnlyList<string> AllowedValues { get; } = [New, InProgress, Blocked, AwaitingApproval, Completed, Failed];

    public static string ToStorageValue(this WorkTaskStatus status)
    {
        if (Values.TryGetValue(status, out var value))
        {
            return value;
        }

        throw new ArgumentOutOfRangeException(nameof(status), status, BuildValidationMessage());
    }

    public static bool TryParse(string? value, out WorkTaskStatus status)
    {
        status = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (ReverseValues.TryGetValue(trimmed, out status))
        {
            return true;
        }

        return Enum.TryParse(trimmed, ignoreCase: true, out status) && Values.ContainsKey(status);
    }

    public static WorkTaskStatus Parse(string value)
    {
        if (TryParse(value, out var status))
        {
            return status;
        }

        throw new ArgumentOutOfRangeException(nameof(value), value, BuildValidationMessage(value));
    }

    public static void EnsureSupported(WorkTaskStatus status, string paramName)
    {
        _ = status.ToStorageValue();
    }

    public static string BuildValidationMessage(string? attemptedValue = null)
    {
        var allowedValues = string.Join(", ", AllowedValues);
        return string.IsNullOrWhiteSpace(attemptedValue)
            ? $"Task status is required. Allowed values: {allowedValues}."
            : $"Unsupported task status value '{attemptedValue}'. Allowed values: {allowedValues}.";
    }
}

public static class WorkTaskPriorityValues
{
    private static readonly IReadOnlyDictionary<WorkTaskPriority, string> Values = new Dictionary<WorkTaskPriority, string>
    {
        [WorkTaskPriority.Low] = "low",
        [WorkTaskPriority.Normal] = "normal",
        [WorkTaskPriority.High] = "high",
        [WorkTaskPriority.Critical] = "critical"
    };

    private static readonly IReadOnlyDictionary<string, WorkTaskPriority> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static WorkTaskPriority DefaultPriority => WorkTaskPriority.Normal;
    public static IReadOnlyList<string> AllowedValues { get; } = ReverseValues.Keys.OrderBy(x => x).ToArray();

    public static string ToStorageValue(this WorkTaskPriority priority) =>
        Values.TryGetValue(priority, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(priority), priority, BuildValidationMessage());

    public static bool TryParse(string? value, out WorkTaskPriority priority)
    {
        priority = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (ReverseValues.TryGetValue(trimmed, out priority))
        {
            return true;
        }

        return Enum.TryParse(trimmed, ignoreCase: true, out priority) && Values.ContainsKey(priority);
    }

    public static WorkTaskPriority Parse(string value) =>
        TryParse(value, out var priority)
            ? priority
            : throw new ArgumentOutOfRangeException(nameof(value), value, BuildValidationMessage(value));

    public static string BuildValidationMessage(string? attemptedValue = null) =>
        string.IsNullOrWhiteSpace(attemptedValue)
            ? $"Task priority is required. Allowed values: {string.Join(", ", AllowedValues)}."
            : $"Unsupported task priority value '{attemptedValue}'. Allowed values: {string.Join(", ", AllowedValues)}.";
}

public static class WorkTaskSourceTypes
{
    public const string User = "user";
    public const string Agent = "agent";
}
