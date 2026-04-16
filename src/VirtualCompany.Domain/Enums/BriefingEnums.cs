namespace VirtualCompany.Domain.Enums;

public enum CompanyBriefingType
{
    Daily = 1,
    Weekly = 2
}

public enum CompanyBriefingStatus
{
    Generated = 1,
    Failed = 2
}

public enum CompanyBriefingUpdateJobTriggerType
{
    EventDriven = 1,
    Daily = 2,
    Weekly = 3
}

public enum CompanyBriefingUpdateJobStatus
{
    Pending = 1,
    Processing = 2,
    Retrying = 3,
    Completed = 4,
    Failed = 5
}

public enum CompanyNotificationChannel
{
    InApp = 1,
    Mobile = 2
}

public enum CompanyNotificationStatus
{
    Unread = 1,
    Read = 2,
    Actioned = 3,
    Suppressed = 4
}

public enum CompanyNotificationType
{
    ApprovalRequested = 1,
    Escalation = 2,
    WorkflowFailure = 3,
    BriefingAvailable = 4,
    ProactiveMessage = 5
}

public enum CompanyNotificationPriority
{
    Low = 1,
    Normal = 2,
    High = 3,
    Critical = 4
}

public enum BriefingSectionPriorityCategory
{
    Informational = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum BriefingDeliveryFrequency
{
    Daily = 1,
    Weekly = 2,
    DailyAndWeekly = 3
}

public enum BriefingPreferenceSource
{
    User = 1,
    TenantDefault = 2,
    SystemDefault = 3
}

public enum BriefingLinkedEntityType
{
    Task = 1,
    WorkflowInstance = 2,
    Approval = 3
}

public enum BriefingLinkedEntityResolutionState
{
    Available = 1,
    Deleted = 2,
    Inaccessible = 3,
    Unknown = 4
}

public enum BriefingLinkedEntityPlaceholderReason
{
    None = 0,
    DeletedOrInaccessible = 1
}

public enum BriefingSeverityRuleStatus
{
    Active = 1,
    Disabled = 2
}

public static class CompanyNotificationTypeValues
{
    public static string ToStorageValue(this CompanyNotificationType type) =>
        type switch
        {
            CompanyNotificationType.ApprovalRequested => "approval_requested",
            CompanyNotificationType.Escalation => "escalation",
            CompanyNotificationType.WorkflowFailure => "workflow_failure",
            CompanyNotificationType.BriefingAvailable => "briefing_available",
            CompanyNotificationType.ProactiveMessage => "proactive_message",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported notification type.")
        };
}

public static class CompanyBriefingTypeValues
{
    public const string Daily = "daily_briefing";
    public const string Weekly = "weekly_summary";

    private static readonly IReadOnlyDictionary<CompanyBriefingType, string> Values = new Dictionary<CompanyBriefingType, string>
    {
        [CompanyBriefingType.Daily] = Daily,
        [CompanyBriefingType.Weekly] = Weekly
    };

    private static readonly IReadOnlyDictionary<string, CompanyBriefingType> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllowedValues { get; } = [Daily, Weekly];

    public static string ToStorageValue(this CompanyBriefingType type) =>
        Values.TryGetValue(type, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(type), type, BuildValidationMessage());

    public static bool TryParse(string? value, out CompanyBriefingType type)
    {
        type = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return ReverseValues.TryGetValue(trimmed, out type) ||
               string.Equals(trimmed, "daily", StringComparison.OrdinalIgnoreCase) && (type = CompanyBriefingType.Daily) == CompanyBriefingType.Daily ||
               string.Equals(trimmed, "weekly", StringComparison.OrdinalIgnoreCase) && (type = CompanyBriefingType.Weekly) == CompanyBriefingType.Weekly
            || Enum.TryParse(trimmed, ignoreCase: true, out type) && Values.ContainsKey(type);
    }

    public static CompanyBriefingType Parse(string value) =>
        TryParse(value, out var type)
            ? type
            : throw new ArgumentOutOfRangeException(nameof(value), value, BuildValidationMessage(value));

    public static string BuildValidationMessage(string? attemptedValue = null)
    {
        var allowedValues = string.Join(", ", AllowedValues);
        return string.IsNullOrWhiteSpace(attemptedValue)
            ? $"Briefing type is required. Allowed values: {allowedValues}."
            : $"Unsupported briefing type value '{attemptedValue}'. Allowed values: {allowedValues}.";
    }
}

public static class BriefingPreferenceErrorCodes
{
    public const string InvalidDeliveryFrequency = "briefing_preferences.invalid_delivery_frequency";
    public const string UnsupportedFocusArea = "briefing_preferences.unsupported_focus_area";
    public const string InvalidPriorityThreshold = "briefing_preferences.invalid_priority_threshold";
}

public static class BriefingFocusAreaValues
{
    public const string Alerts = "alerts";
    public const string PendingApprovals = "pending_approvals";
    public const string KpiHighlights = "kpi_highlights";
    public const string Anomalies = "anomalies";
    public const string NotableAgentUpdates = "notable_agent_updates";

    private static readonly IReadOnlySet<string> Supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Alerts,
        PendingApprovals,
        KpiHighlights,
        Anomalies,
        NotableAgentUpdates
    };

    public static IReadOnlyList<string> AllowedValues { get; } =
        [Alerts, PendingApprovals, KpiHighlights, Anomalies, NotableAgentUpdates];

    public static IReadOnlyList<string> NormalizeOrThrow(IEnumerable<string>? focusAreas)
    {
        var normalized = (focusAreas ?? AllowedValues)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
        {
            return AllowedValues;
        }

        var unsupported = normalized.Where(value => !Supported.Contains(value)).ToList();
        if (unsupported.Count > 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(focusAreas),
                string.Join(", ", unsupported),
                BriefingPreferenceErrorCodes.UnsupportedFocusArea);
        }

        return normalized;
    }
}

public static class BriefingDeliveryFrequencyValues
{
    private static readonly IReadOnlyDictionary<BriefingDeliveryFrequency, string> Values = new Dictionary<BriefingDeliveryFrequency, string>
    {
        [BriefingDeliveryFrequency.Daily] = "daily",
        [BriefingDeliveryFrequency.Weekly] = "weekly",
        [BriefingDeliveryFrequency.DailyAndWeekly] = "daily_and_weekly"
    };

    private static readonly IReadOnlyDictionary<string, BriefingDeliveryFrequency> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllowedValues { get; } = ["daily", "weekly", "daily_and_weekly"];

    public static string ToStorageValue(this BriefingDeliveryFrequency frequency) =>
        Values.TryGetValue(frequency, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(frequency), frequency, BriefingPreferenceErrorCodes.InvalidDeliveryFrequency);

    public static BriefingDeliveryFrequency Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, BriefingPreferenceErrorCodes.InvalidDeliveryFrequency);
        }

        var trimmed = value.Trim();
        return ReverseValues.TryGetValue(trimmed, out var frequency)
            ? frequency
            : throw new ArgumentOutOfRangeException(nameof(value), value, BriefingPreferenceErrorCodes.InvalidDeliveryFrequency);
    }
}

public static class BriefingPreferenceSourceValues
{
    public static string ToStorageValue(this BriefingPreferenceSource source) =>
        source == BriefingPreferenceSource.User ? "user" :
        source == BriefingPreferenceSource.TenantDefault ? "tenant_default" :
        source == BriefingPreferenceSource.SystemDefault ? "system_default" :
        throw new ArgumentOutOfRangeException(nameof(source), source, "Unsupported briefing preference source.");
}

public static class CompanyBriefingStatusValues
{
    public static string ToStorageValue(this CompanyBriefingStatus status) =>
        status switch
        {
            CompanyBriefingStatus.Generated => "generated",
            CompanyBriefingStatus.Failed => "failed",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported briefing status.")
        };

    public static CompanyBriefingStatus Parse(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "generated" => CompanyBriefingStatus.Generated,
            "failed" => CompanyBriefingStatus.Failed,
            _ when Enum.TryParse<CompanyBriefingStatus>(value.Trim(), ignoreCase: true, out var parsed) => parsed,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported briefing status value.")
        };
}

public static class CompanyBriefingUpdateJobTriggerTypeValues
{
    public const string EventDriven = "event_driven";
    public const string Daily = "daily";
    public const string Weekly = "weekly";

    private static readonly IReadOnlyDictionary<CompanyBriefingUpdateJobTriggerType, string> Values = new Dictionary<CompanyBriefingUpdateJobTriggerType, string>
    {
        [CompanyBriefingUpdateJobTriggerType.EventDriven] = EventDriven,
        [CompanyBriefingUpdateJobTriggerType.Daily] = Daily,
        [CompanyBriefingUpdateJobTriggerType.Weekly] = Weekly
    };

    private static readonly IReadOnlyDictionary<string, CompanyBriefingUpdateJobTriggerType> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToStorageValue(this CompanyBriefingUpdateJobTriggerType triggerType) =>
        Values.TryGetValue(triggerType, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(triggerType), triggerType, "Unsupported briefing update job trigger type.");

    public static CompanyBriefingUpdateJobTriggerType Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Briefing update job trigger type is required.", nameof(value));
        }

        var trimmed = value.Trim();
        return ReverseValues.TryGetValue(trimmed, out var triggerType) ||
               string.Equals(trimmed, "event", StringComparison.OrdinalIgnoreCase) && (triggerType = CompanyBriefingUpdateJobTriggerType.EventDriven) == CompanyBriefingUpdateJobTriggerType.EventDriven ||
               Enum.TryParse(trimmed, ignoreCase: true, out triggerType) && Values.ContainsKey(triggerType)
            ? triggerType
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported briefing update job trigger type value.");
    }
}

public static class CompanyBriefingUpdateJobStatusValues
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Retrying = "retrying";
    public const string Completed = "completed";
    public const string Failed = "failed";

    private static readonly IReadOnlyDictionary<CompanyBriefingUpdateJobStatus, string> Values = new Dictionary<CompanyBriefingUpdateJobStatus, string>
    {
        [CompanyBriefingUpdateJobStatus.Pending] = Pending,
        [CompanyBriefingUpdateJobStatus.Processing] = Processing,
        [CompanyBriefingUpdateJobStatus.Retrying] = Retrying,
        [CompanyBriefingUpdateJobStatus.Completed] = Completed,
        [CompanyBriefingUpdateJobStatus.Failed] = Failed
    };

    private static readonly IReadOnlyDictionary<string, CompanyBriefingUpdateJobStatus> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToStorageValue(this CompanyBriefingUpdateJobStatus status) =>
        Values.TryGetValue(status, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported briefing update job status.");

    public static CompanyBriefingUpdateJobStatus Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Briefing update job status is required.", nameof(value));
        }

        var trimmed = value.Trim();
        return ReverseValues.TryGetValue(trimmed, out var status) ||
               Enum.TryParse(trimmed, ignoreCase: true, out status) && Values.ContainsKey(status)
            ? status
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported briefing update job status value.");
    }
}

public static class CompanyNotificationChannelValues
{
    public static string ToStorageValue(this CompanyNotificationChannel channel) =>
        channel == CompanyNotificationChannel.InApp ? "in_app" :
        channel == CompanyNotificationChannel.Mobile ? "mobile" :
        throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unsupported notification channel.");

    public static CompanyNotificationChannel Parse(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "in_app" => CompanyNotificationChannel.InApp,
            "mobile" => CompanyNotificationChannel.Mobile,
            _ when Enum.TryParse<CompanyNotificationChannel>(value.Trim(), ignoreCase: true, out var parsed) => parsed,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported notification channel value.")
        };
}

public static class CompanyNotificationStatusValues
{
    public static string ToStorageValue(this CompanyNotificationStatus status) =>
        status switch
        {
            CompanyNotificationStatus.Unread => "unread",
            CompanyNotificationStatus.Read => "read",
            CompanyNotificationStatus.Actioned => "actioned",
            CompanyNotificationStatus.Suppressed => "suppressed",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported notification status.")
        };

    public static CompanyNotificationStatus Parse(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "pending" or "delivered" or "unread" => CompanyNotificationStatus.Unread,
            "read" => CompanyNotificationStatus.Read,
            "actioned" => CompanyNotificationStatus.Actioned,
            "suppressed" => CompanyNotificationStatus.Suppressed,
            _ when Enum.TryParse<CompanyNotificationStatus>(value.Trim(), ignoreCase: true, out var parsed) => parsed,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported notification status value.")
        };
}

public static class CompanyNotificationPriorityValues
{
    public static string ToStorageValue(this CompanyNotificationPriority priority) =>
        priority.ToString().ToLowerInvariant();

    public static CompanyNotificationPriority Parse(string value) =>
        Enum.TryParse<CompanyNotificationPriority>(value.Trim(), ignoreCase: true, out var parsed) ? parsed : CompanyNotificationPriority.Normal;
}

public static class BriefingSectionPriorityCategoryValues
{
    private static readonly IReadOnlyDictionary<BriefingSectionPriorityCategory, string> Values = new Dictionary<BriefingSectionPriorityCategory, string>
    {
        [BriefingSectionPriorityCategory.Informational] = "informational",
        [BriefingSectionPriorityCategory.Medium] = "medium",
        [BriefingSectionPriorityCategory.High] = "high",
        [BriefingSectionPriorityCategory.Critical] = "critical"
    };

    private static readonly IReadOnlyDictionary<string, BriefingSectionPriorityCategory> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToStorageValue(this BriefingSectionPriorityCategory category) =>
        Values.TryGetValue(category, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(category), category, "Unsupported briefing section priority category.");

    public static BriefingSectionPriorityCategory Parse(string value) =>
        !string.IsNullOrWhiteSpace(value) && ReverseValues.TryGetValue(value.Trim(), out var category)
            ? category
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported briefing section priority category.");
}

public static class BriefingLinkedEntityTypeValues
{
    private static readonly IReadOnlyDictionary<BriefingLinkedEntityType, string> Values = new Dictionary<BriefingLinkedEntityType, string>
    {
        [BriefingLinkedEntityType.Task] = "task",
        [BriefingLinkedEntityType.WorkflowInstance] = "workflow_instance",
        [BriefingLinkedEntityType.Approval] = "approval"
    };

    private static readonly IReadOnlyDictionary<string, BriefingLinkedEntityType> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToStorageValue(this BriefingLinkedEntityType entityType) =>
        Values.TryGetValue(entityType, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(entityType), entityType, "Unsupported briefing linked entity type.");

    public static bool TryParse(string? value, out BriefingLinkedEntityType entityType)
    {
        entityType = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return ReverseValues.TryGetValue(value.Trim(), out entityType);
    }
}

public static class BriefingLinkedEntityResolutionStateValues
{
    public static string ToStorageValue(this BriefingLinkedEntityResolutionState state) =>
        state switch
        {
            BriefingLinkedEntityResolutionState.Available => "available",
            BriefingLinkedEntityResolutionState.Deleted => "deleted",
            BriefingLinkedEntityResolutionState.Inaccessible => "inaccessible",
            BriefingLinkedEntityResolutionState.Unknown => "unknown",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unsupported briefing linked entity resolution state.")
        };
}

public static class BriefingLinkedEntityPlaceholderReasonValues
{
    public static string? ToStorageValue(this BriefingLinkedEntityPlaceholderReason reason) =>
        reason switch
        {
            BriefingLinkedEntityPlaceholderReason.None => null,
            BriefingLinkedEntityPlaceholderReason.DeletedOrInaccessible => "deleted_or_inaccessible",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unsupported briefing linked entity placeholder reason.")
        };
}

public static class BriefingSeverityRuleStatusValues
{
    private static readonly IReadOnlyDictionary<BriefingSeverityRuleStatus, string> Values = new Dictionary<BriefingSeverityRuleStatus, string>
    {
        [BriefingSeverityRuleStatus.Active] = "active",
        [BriefingSeverityRuleStatus.Disabled] = "disabled"
    };

    private static readonly IReadOnlyDictionary<string, BriefingSeverityRuleStatus> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToStorageValue(this BriefingSeverityRuleStatus status) =>
        Values.TryGetValue(status, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported briefing severity rule status.");

    public static BriefingSeverityRuleStatus Parse(string value) =>
        !string.IsNullOrWhiteSpace(value) && ReverseValues.TryGetValue(value.Trim(), out var status)
            ? status
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported briefing severity rule status.");
}