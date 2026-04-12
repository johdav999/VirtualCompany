namespace VirtualCompany.Domain.Enums;

public enum WorkflowDefinitionStatus
{
    Draft = 1,
    Active = 2,
    Archived = 3
}

public enum WorkflowTriggerType
{
    Manual = 1,
    Event = 2,
    Schedule = 3,
    Webhook = 4
}

public enum WorkflowInstanceStatus
{
    Started = 1,
    Running = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5,
    Blocked = 6
}

public enum WorkflowExceptionType
{
    Failed = 1,
    Blocked = 2
}

public enum WorkflowExceptionStatus
{
    Open = 1,
    Reviewed = 2,
    Resolved = 3
}

public static class WorkflowDefinitionStatusValues
{
    private static readonly IReadOnlyDictionary<WorkflowDefinitionStatus, string> Values = new Dictionary<WorkflowDefinitionStatus, string>
    {
        [WorkflowDefinitionStatus.Draft] = "draft",
        [WorkflowDefinitionStatus.Active] = "active",
        [WorkflowDefinitionStatus.Archived] = "archived"
    };

    private static readonly IReadOnlyDictionary<string, WorkflowDefinitionStatus> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static WorkflowDefinitionStatus DefaultStatus => WorkflowDefinitionStatus.Active;
    public static IReadOnlyList<string> AllowedValues { get; } = ReverseValues.Keys.OrderBy(x => x).ToArray();

    public static string ToStorageValue(this WorkflowDefinitionStatus status) =>
        Values.TryGetValue(status, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(status), status, BuildValidationMessage());

    public static bool TryParse(string? value, out WorkflowDefinitionStatus status)
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

    public static string BuildValidationMessage(string? attemptedValue = null) =>
        string.IsNullOrWhiteSpace(attemptedValue)
            ? $"Workflow definition status is required. Allowed values: {string.Join(", ", AllowedValues)}."
            : $"Unsupported workflow definition status value '{attemptedValue}'. Allowed values: {string.Join(", ", AllowedValues)}.";
}

public static class WorkflowTriggerTypeValues
{
    private static readonly IReadOnlyDictionary<WorkflowTriggerType, string> Values = new Dictionary<WorkflowTriggerType, string>
    {
        [WorkflowTriggerType.Manual] = "manual",
        [WorkflowTriggerType.Event] = "event",
        [WorkflowTriggerType.Schedule] = "schedule",
        [WorkflowTriggerType.Webhook] = "webhook"
    };

    private static readonly IReadOnlyDictionary<string, WorkflowTriggerType> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static WorkflowTriggerType DefaultType => WorkflowTriggerType.Manual;
    public static IReadOnlyList<string> AllowedValues { get; } = ReverseValues.Keys.OrderBy(x => x).ToArray();

    public static string ToStorageValue(this WorkflowTriggerType triggerType) =>
        Values.TryGetValue(triggerType, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(triggerType), triggerType, BuildValidationMessage());

    public static bool TryParse(string? value, out WorkflowTriggerType triggerType)
    {
        triggerType = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (ReverseValues.TryGetValue(trimmed, out triggerType))
        {
            return true;
        }

        return Enum.TryParse(trimmed, ignoreCase: true, out triggerType) && Values.ContainsKey(triggerType);
    }

    public static WorkflowTriggerType Parse(string value) =>
        TryParse(value, out var triggerType)
            ? triggerType
            : throw new ArgumentOutOfRangeException(nameof(value), value, BuildValidationMessage(value));

    public static string BuildValidationMessage(string? attemptedValue = null) =>
        string.IsNullOrWhiteSpace(attemptedValue)
            ? $"Workflow trigger type is required. Allowed values: {string.Join(", ", AllowedValues)}."
            : $"Unsupported workflow trigger type value '{attemptedValue}'. Allowed values: {string.Join(", ", AllowedValues)}.";
}

public static class WorkflowInstanceStatusValues
{
    private static readonly IReadOnlyDictionary<WorkflowInstanceStatus, string> Values = new Dictionary<WorkflowInstanceStatus, string>
    {
        [WorkflowInstanceStatus.Started] = "started",
        [WorkflowInstanceStatus.Running] = "running",
        [WorkflowInstanceStatus.Completed] = "completed",
        [WorkflowInstanceStatus.Failed] = "failed",
        [WorkflowInstanceStatus.Cancelled] = "cancelled",
        [WorkflowInstanceStatus.Blocked] = "blocked"
    };

    private static readonly IReadOnlyDictionary<string, WorkflowInstanceStatus> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static WorkflowInstanceStatus DefaultStatus => WorkflowInstanceStatus.Started;
    public static IReadOnlyList<string> AllowedValues { get; } = ReverseValues.Keys.OrderBy(x => x).ToArray();

    public static string ToStorageValue(this WorkflowInstanceStatus status) =>
        Values.TryGetValue(status, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(status), status, BuildValidationMessage());

    public static bool TryParse(string? value, out WorkflowInstanceStatus status)
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

    public static string BuildValidationMessage(string? attemptedValue = null) =>
        string.IsNullOrWhiteSpace(attemptedValue)
            ? $"Workflow instance status is required. Allowed values: {string.Join(", ", AllowedValues)}."
            : $"Unsupported workflow instance status value '{attemptedValue}'. Allowed values: {string.Join(", ", AllowedValues)}.";

    public static WorkflowInstanceStatus Parse(string value) =>
        TryParse(value, out var status)
            ? status
            : throw new ArgumentOutOfRangeException(nameof(value), value, BuildValidationMessage(value));
}

public static class WorkflowExceptionTypeValues
{
    private static readonly IReadOnlyDictionary<WorkflowExceptionType, string> Values = new Dictionary<WorkflowExceptionType, string>
    {
        [WorkflowExceptionType.Failed] = "failed",
        [WorkflowExceptionType.Blocked] = "blocked"
    };

    private static readonly IReadOnlyDictionary<string, WorkflowExceptionType> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllowedValues { get; } = ReverseValues.Keys.OrderBy(x => x).ToArray();

    public static string ToStorageValue(this WorkflowExceptionType exceptionType) =>
        Values.TryGetValue(exceptionType, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(exceptionType), exceptionType, BuildValidationMessage());

    public static bool TryParse(string? value, out WorkflowExceptionType exceptionType)
    {
        exceptionType = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (ReverseValues.TryGetValue(trimmed, out exceptionType))
        {
            return true;
        }

        return Enum.TryParse(trimmed, ignoreCase: true, out exceptionType) && Values.ContainsKey(exceptionType);
    }

    public static WorkflowExceptionType Parse(string value) =>
        TryParse(value, out var exceptionType)
            ? exceptionType
            : throw new ArgumentOutOfRangeException(nameof(value), value, BuildValidationMessage(value));

    public static string BuildValidationMessage(string? attemptedValue = null) =>
        string.IsNullOrWhiteSpace(attemptedValue)
            ? $"Workflow exception type is required. Allowed values: {string.Join(", ", AllowedValues)}."
            : $"Unsupported workflow exception type value '{attemptedValue}'. Allowed values: {string.Join(", ", AllowedValues)}.";
}

public static class WorkflowExceptionStatusValues
{
    private static readonly IReadOnlyDictionary<WorkflowExceptionStatus, string> Values = new Dictionary<WorkflowExceptionStatus, string>
    {
        [WorkflowExceptionStatus.Open] = "open",
        [WorkflowExceptionStatus.Reviewed] = "reviewed",
        [WorkflowExceptionStatus.Resolved] = "resolved"
    };

    private static readonly IReadOnlyDictionary<string, WorkflowExceptionStatus> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static WorkflowExceptionStatus DefaultStatus => WorkflowExceptionStatus.Open;
    public static IReadOnlyList<string> AllowedValues { get; } = ReverseValues.Keys.OrderBy(x => x).ToArray();

    public static string ToStorageValue(this WorkflowExceptionStatus status) =>
        Values.TryGetValue(status, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(status), status, BuildValidationMessage());

    public static bool TryParse(string? value, out WorkflowExceptionStatus status)
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

    public static WorkflowExceptionStatus Parse(string value) =>
        TryParse(value, out var status)
            ? status
            : throw new ArgumentOutOfRangeException(nameof(value), value, BuildValidationMessage(value));

    public static string BuildValidationMessage(string? attemptedValue = null) =>
        string.IsNullOrWhiteSpace(attemptedValue)
            ? $"Workflow exception status is required. Allowed values: {string.Join(", ", AllowedValues)}."
            : $"Unsupported workflow exception status value '{attemptedValue}'. Allowed values: {string.Join(", ", AllowedValues)}.";
}
