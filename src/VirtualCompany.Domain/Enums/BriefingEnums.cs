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
    BriefingAvailable = 4
}

public enum CompanyNotificationPriority
{
    Low = 1,
    Normal = 2,
    High = 3,
    Critical = 4
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