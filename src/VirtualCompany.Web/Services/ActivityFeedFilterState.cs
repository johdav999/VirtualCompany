using System.Collections.Specialized;

namespace VirtualCompany.Web.Services;

public sealed class ActivityFeedFilterState
{
    public const string AllTime = "all";
    public const string LastHour = "lastHour";
    public const string Last24Hours = "last24Hours";
    public const string Last7Days = "last7Days";
    public const string Last30Days = "last30Days";
    public const string Custom = "custom";

    public static readonly IReadOnlyList<ActivityFeedTimeframeOption> TimeframeOptions =
    [
        new(AllTime, "All time"),
        new(LastHour, "Last hour"),
        new(Last24Hours, "Last 24 hours"),
        new(Last7Days, "Last 7 days"),
        new(Last30Days, "Last 30 days"),
        new(Custom, "Custom range")
    ];

    public string? AgentId { get; set; }
    public string? Department { get; set; }
    public string? TaskId { get; set; }
    public string? EventType { get; set; }
    public string? Status { get; set; }
    public string? Timeframe { get; set; } = AllTime;
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }

    public static ActivityFeedFilterState FromQuery(NameValueCollection query) =>
        new ActivityFeedFilterState()
        {
            AgentId = query["agentId"],
            Department = query["department"],
            TaskId = query["task"] ?? query["taskId"],
            EventType = query["eventType"],
            Status = query["status"],
            Timeframe = query["timeframe"],
            FromUtc = ParseDateTime(query["from"]),
            ToUtc = ParseDateTime(query["to"])
        }.Normalize();

    public ActivityFeedFilterState Normalize()
    {
        AgentId = EmptyToNull(AgentId);
        Department = EmptyToNull(Department);
        TaskId = EmptyToNull(TaskId);
        EventType = EmptyToNull(EventType);
        Status = EmptyToNull(Status);
        Timeframe = NormalizeTimeframe(Timeframe);

        if (!string.Equals(Timeframe, Custom, StringComparison.Ordinal))
        {
            FromUtc = null;
            ToUtc = null;
        }

        return this;
    }

    public ActivityFeedFilterViewModel ToApiFilter()
    {
        var normalized = Normalize();
        return new ActivityFeedFilterViewModel
        {
            AgentId = normalized.AgentId,
            Department = normalized.Department,
            TaskId = normalized.TaskId,
            EventType = normalized.EventType,
            Status = normalized.Status,
            Timeframe = normalized.Timeframe,
            FromUtc = normalized.FromUtc,
            ToUtc = normalized.ToUtc
        };
    }

    public IReadOnlyDictionary<string, object?> ToQueryParameters(Guid? companyId)
    {
        var normalized = Normalize();

        // Keep shareable URLs stable by omitting defaults and custom-only date values when unused.
        return new Dictionary<string, object?>
        {
            ["companyId"] = companyId,
            ["agentId"] = normalized.AgentId,
            ["department"] = normalized.Department,
            ["task"] = normalized.TaskId,
            ["taskId"] = null,
            ["eventType"] = normalized.EventType,
            ["status"] = normalized.Status,
            ["timeframe"] = IsDefaultTimeframe(normalized.Timeframe) ? null : normalized.Timeframe,
            ["from"] = string.Equals(normalized.Timeframe, Custom, StringComparison.Ordinal) ? normalized.FromUtc : null,
            ["to"] = string.Equals(normalized.Timeframe, Custom, StringComparison.Ordinal) ? normalized.ToUtc : null
        };
    }

    public static bool IsDefaultTimeframe(string? value) =>
        string.Equals(NormalizeTimeframe(value), AllTime, StringComparison.Ordinal);

    public static string NormalizeTimeframe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return AllTime;
        }

        return value.Trim() switch
        {
            "last-hour" => LastHour,
            "last-24-hours" => Last24Hours,
            "last-7-days" => Last7Days,
            "last-30-days" => Last30Days,
            LastHour => LastHour,
            Last24Hours => Last24Hours,
            Last7Days => Last7Days,
            Last30Days => Last30Days,
            Custom => Custom,
            AllTime => AllTime,
            _ => AllTime
        };
    }

    private static DateTime? ParseDateTime(string? value)
    {
        if (!DateTime.TryParse(value, out var parsed))
        {
            return null;
        }

        return parsed.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : parsed.ToUniversalTime();
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record ActivityFeedTimeframeOption(
    string Value,
    string Label);
