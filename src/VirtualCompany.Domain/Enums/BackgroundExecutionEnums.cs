namespace VirtualCompany.Domain.Enums;

public enum BackgroundExecutionType
{
    ScheduledWorkflow = 1,
    WorkflowProgression = 2,
    Retry = 3,
    LongRunningTask = 4,
    OutboxDispatch = 5
}

public enum BackgroundExecutionStatus
{
    Pending = 1,
    InProgress = 2,
    Succeeded = 3,
    RetryScheduled = 4,
    Failed = 5,
    Escalated = 6,
    Blocked = 7
}

public enum BackgroundExecutionFailureCategory
{
    Unknown = 0,
    TransientInfrastructure = 1,
    LockContention = 2,
    ExternalDependencyTimeout = 3,
    ExternalDependencyUnavailable = 4,
    PermanentBusinessRule = 5,
    PermanentPolicy = 6,
    Validation = 7,
    Configuration = 8,
    RateLimited = 9,
    ApprovalRequired = 10
}

public enum CompanyOutboxMessageStatus
{
    Pending = 1,
    InProgress = 2,
    Dispatched = 3,
    Failed = 4,
    RetryScheduled = 5
}

public static class BackgroundExecutionTypeValues
{
    private static readonly IReadOnlyDictionary<BackgroundExecutionType, string> Values = new Dictionary<BackgroundExecutionType, string>
    {
        [BackgroundExecutionType.ScheduledWorkflow] = "scheduled_workflow",
        [BackgroundExecutionType.WorkflowProgression] = "workflow_progression",
        [BackgroundExecutionType.Retry] = "retry",
        [BackgroundExecutionType.LongRunningTask] = "long_running_task",
        [BackgroundExecutionType.OutboxDispatch] = "outbox_dispatch"
    };

    private static readonly IReadOnlyDictionary<string, BackgroundExecutionType> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToStorageValue(this BackgroundExecutionType type) =>
        Values.TryGetValue(type, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported background execution type.");

    public static BackgroundExecutionType Parse(string value) =>
        TryParse(value, out var type)
            ? type
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported background execution type value.");

    public static bool TryParse(string? value, out BackgroundExecutionType type)
    {
        type = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return ReverseValues.TryGetValue(trimmed, out type) ||
            Enum.TryParse(trimmed, ignoreCase: true, out type) && Values.ContainsKey(type);
    }
}

public static class BackgroundExecutionStatusValues
{
    private static readonly IReadOnlyDictionary<BackgroundExecutionStatus, string> Values = new Dictionary<BackgroundExecutionStatus, string>
    {
        [BackgroundExecutionStatus.Pending] = "pending",
        [BackgroundExecutionStatus.InProgress] = "in_progress",
        [BackgroundExecutionStatus.Succeeded] = "succeeded",
        [BackgroundExecutionStatus.RetryScheduled] = "retry_scheduled",
        [BackgroundExecutionStatus.Failed] = "failed",
        [BackgroundExecutionStatus.Escalated] = "escalated",
        [BackgroundExecutionStatus.Blocked] = "blocked"
    };

    private static readonly IReadOnlyDictionary<string, BackgroundExecutionStatus> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static BackgroundExecutionStatus DefaultStatus => BackgroundExecutionStatus.Pending;

    public static string ToStorageValue(this BackgroundExecutionStatus status) =>
        Values.TryGetValue(status, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported background execution status.");

    public static BackgroundExecutionStatus Parse(string value) =>
        TryParse(value, out var status)
            ? status
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported background execution status value.");

    public static bool TryParse(string? value, out BackgroundExecutionStatus status)
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

public static class BackgroundExecutionFailureCategoryValues
{
    private static readonly IReadOnlyDictionary<BackgroundExecutionFailureCategory, string> Values = new Dictionary<BackgroundExecutionFailureCategory, string>
    {
        [BackgroundExecutionFailureCategory.Unknown] = "unknown",
        [BackgroundExecutionFailureCategory.TransientInfrastructure] = "transient_infrastructure",
        [BackgroundExecutionFailureCategory.LockContention] = "lock_contention",
        [BackgroundExecutionFailureCategory.ExternalDependencyTimeout] = "external_dependency_timeout",
        [BackgroundExecutionFailureCategory.ExternalDependencyUnavailable] = "external_dependency_unavailable",
        [BackgroundExecutionFailureCategory.PermanentBusinessRule] = "permanent_business_rule",
        [BackgroundExecutionFailureCategory.PermanentPolicy] = "permanent_policy",
        [BackgroundExecutionFailureCategory.Validation] = "validation",
        [BackgroundExecutionFailureCategory.Configuration] = "configuration",
        [BackgroundExecutionFailureCategory.RateLimited] = "rate_limited",
        [BackgroundExecutionFailureCategory.ApprovalRequired] = "approval_required"
    };

    private static readonly IReadOnlyDictionary<string, BackgroundExecutionFailureCategory> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToStorageValue(this BackgroundExecutionFailureCategory category) =>
        Values.TryGetValue(category, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(category), category, "Unsupported background execution failure category.");

    public static BackgroundExecutionFailureCategory Parse(string value) =>
        TryParse(value, out var category)
            ? category
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported background execution failure category value.");

    public static bool TryParse(string? value, out BackgroundExecutionFailureCategory category)
    {
        category = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return ReverseValues.TryGetValue(trimmed, out category) ||
            Enum.TryParse(trimmed, ignoreCase: true, out category) && Values.ContainsKey(category);
    }
}

public static class CompanyOutboxMessageStatusValues
{
    private static readonly IReadOnlyDictionary<CompanyOutboxMessageStatus, string> Values = new Dictionary<CompanyOutboxMessageStatus, string>
    {
        [CompanyOutboxMessageStatus.Pending] = "pending",
        [CompanyOutboxMessageStatus.InProgress] = "in_progress",
        [CompanyOutboxMessageStatus.Dispatched] = "dispatched",
        [CompanyOutboxMessageStatus.Failed] = "failed",
        [CompanyOutboxMessageStatus.RetryScheduled] = "retry_scheduled"
    };

    private static readonly IReadOnlyDictionary<string, CompanyOutboxMessageStatus> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static CompanyOutboxMessageStatus DefaultStatus => CompanyOutboxMessageStatus.Pending;

    public static string ToStorageValue(this CompanyOutboxMessageStatus status) =>
        Values.TryGetValue(status, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported company outbox message status.");

    public static CompanyOutboxMessageStatus Parse(string value) =>
        TryParse(value, out var status)
            ? status
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported company outbox message status value.");

    public static bool TryParse(string? value, out CompanyOutboxMessageStatus status)
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