namespace VirtualCompany.Domain.Enums;

public enum ExecutionExceptionKind
{
    Blocked = 1,
    Failed = 2
}

public enum ExecutionExceptionSeverity
{
    Warning = 1,
    Error = 2,
    Critical = 3
}

public enum ExecutionExceptionStatus
{
    Open = 1,
    Acknowledged = 2,
    Resolved = 3
}

public enum ExecutionExceptionSourceType
{
    BackgroundExecution = 1,
    WorkflowInstance = 2,
    WorkTask = 3,
    OutboxMessage = 4,
    Schedule = 5
}

public static class ExecutionExceptionKindValues
{
    private static readonly IReadOnlyDictionary<ExecutionExceptionKind, string> Values = new Dictionary<ExecutionExceptionKind, string>
    {
        [ExecutionExceptionKind.Blocked] = "blocked",
        [ExecutionExceptionKind.Failed] = "failed"
    };

    private static readonly IReadOnlyDictionary<string, ExecutionExceptionKind> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToStorageValue(this ExecutionExceptionKind kind) =>
        Values.TryGetValue(kind, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported execution exception kind.");

    public static ExecutionExceptionKind Parse(string value) =>
        TryParse(value, out var kind)
            ? kind
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported execution exception kind value.");

    public static bool TryParse(string? value, out ExecutionExceptionKind kind)
    {
        kind = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return ReverseValues.TryGetValue(trimmed, out kind) ||
            Enum.TryParse(trimmed, ignoreCase: true, out kind) && Values.ContainsKey(kind);
    }
}

public static class ExecutionExceptionSeverityValues
{
    private static readonly IReadOnlyDictionary<ExecutionExceptionSeverity, string> Values = new Dictionary<ExecutionExceptionSeverity, string>
    {
        [ExecutionExceptionSeverity.Warning] = "warning",
        [ExecutionExceptionSeverity.Error] = "error",
        [ExecutionExceptionSeverity.Critical] = "critical"
    };

    private static readonly IReadOnlyDictionary<string, ExecutionExceptionSeverity> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToStorageValue(this ExecutionExceptionSeverity severity) =>
        Values.TryGetValue(severity, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(severity), severity, "Unsupported execution exception severity.");

    public static ExecutionExceptionSeverity Parse(string value) =>
        TryParse(value, out var severity)
            ? severity
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported execution exception severity value.");

    public static bool TryParse(string? value, out ExecutionExceptionSeverity severity)
    {
        severity = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return ReverseValues.TryGetValue(trimmed, out severity) ||
            Enum.TryParse(trimmed, ignoreCase: true, out severity) && Values.ContainsKey(severity);
    }
}

public static class ExecutionExceptionStatusValues
{
    private static readonly IReadOnlyDictionary<ExecutionExceptionStatus, string> Values = new Dictionary<ExecutionExceptionStatus, string>
    {
        [ExecutionExceptionStatus.Open] = "open",
        [ExecutionExceptionStatus.Acknowledged] = "acknowledged",
        [ExecutionExceptionStatus.Resolved] = "resolved"
    };

    private static readonly IReadOnlyDictionary<string, ExecutionExceptionStatus> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static ExecutionExceptionStatus DefaultStatus => ExecutionExceptionStatus.Open;

    public static string ToStorageValue(this ExecutionExceptionStatus status) =>
        Values.TryGetValue(status, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported execution exception status.");

    public static ExecutionExceptionStatus Parse(string value) =>
        TryParse(value, out var status)
            ? status
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported execution exception status value.");

    public static bool TryParse(string? value, out ExecutionExceptionStatus status)
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
}

public static class ExecutionExceptionSourceTypeValues
{
    private static readonly IReadOnlyDictionary<ExecutionExceptionSourceType, string> Values = new Dictionary<ExecutionExceptionSourceType, string>
    {
        [ExecutionExceptionSourceType.BackgroundExecution] = "background_execution",
        [ExecutionExceptionSourceType.WorkflowInstance] = "workflow_instance",
        [ExecutionExceptionSourceType.WorkTask] = "work_task",
        [ExecutionExceptionSourceType.OutboxMessage] = "outbox_message",
        [ExecutionExceptionSourceType.Schedule] = "schedule"
    };

    private static readonly IReadOnlyDictionary<string, ExecutionExceptionSourceType> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToStorageValue(this ExecutionExceptionSourceType sourceType) =>
        Values.TryGetValue(sourceType, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(sourceType), sourceType, "Unsupported execution exception source type.");

    public static ExecutionExceptionSourceType Parse(string value) =>
        TryParse(value, out var sourceType)
            ? sourceType
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported execution exception source type value.");

    public static bool TryParse(string? value, out ExecutionExceptionSourceType sourceType)
    {
        sourceType = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return ReverseValues.TryGetValue(trimmed, out sourceType) ||
            Enum.TryParse(trimmed, ignoreCase: true, out sourceType) && Values.ContainsKey(sourceType);
    }
}