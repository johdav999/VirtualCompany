using System.Diagnostics.Metrics;

namespace VirtualCompany.Web.Services;

public sealed record DashboardActionTelemetryContext(
    string Area,
    string ActionKey,
    string Target,
    string? ItemId = null,
    string? SourceType = null,
    string? Label = null,
    int? Position = null);

public sealed class DashboardTelemetrySession
{
    private static readonly Meter DashboardMeter = new("VirtualCompany.Dashboard.Web");
    private static readonly Counter<long> SessionsStarted = DashboardMeter.CreateCounter<long>("dashboard_session_started");
    private static readonly Counter<long> FocusClicks = DashboardMeter.CreateCounter<long>("dashboard_focus_item_clicked");
    private static readonly Counter<long> Actions = DashboardMeter.CreateCounter<long>("dashboard_action_clicked");
    private static readonly Counter<long> FirstActionRecorded = DashboardMeter.CreateCounter<long>("dashboard_first_action_recorded");
    private static readonly Counter<long> ScrollDepthReached = DashboardMeter.CreateCounter<long>("dashboard_scroll_depth_reached");
    private static readonly Histogram<double> ScrollDepth = DashboardMeter.CreateHistogram<double>("dashboard_scroll_depth_pct", unit: "%");
    private static readonly Histogram<double> TimeToFirstAction = DashboardMeter.CreateHistogram<double>("dashboard_time_to_first_action_ms", "ms");
    private static readonly int[] ScrollMilestones = [25, 50, 75, 100];

    private readonly DateTime _sessionStartedUtc = DateTime.UtcNow;
    private readonly HashSet<int> _scrollDepthMilestones = [];
    private double _maxScrollDepthPercentage;

    public DashboardTelemetrySession(string pageName, Guid? companyId)
    {
        PageName = pageName;
        CompanyId = companyId;
        SessionId = Guid.NewGuid().ToString("N");
    }

    public string SessionId { get; }
    public DateTime SessionStartedUtc => _sessionStartedUtc;
    public string PageName { get; }
    public Guid? CompanyId { get; }
    public bool HasStarted { get; private set; }
    public DateTime? FirstActionAtUtc { get; private set; }
    public DateTime? LastActionAtUtc { get; private set; }
    public double? TimeToFirstActionMilliseconds { get; private set; }
    public bool HasRecordedFirstAction => FirstActionAtUtc.HasValue;
    public double MaxScrollDepthPercentage => _maxScrollDepthPercentage;
    public int HighestScrollDepthMilestone { get; private set; }
    public DashboardActionTelemetryContext? FirstActionContext { get; private set; }
    public DashboardActionTelemetryContext? LastActionContext { get; private set; }
    public DashboardActionTelemetryContext? LastFocusItemContext { get; private set; }
    public IReadOnlyCollection<int> ScrollDepthMilestonesReached => _scrollDepthMilestones.OrderBy(value => value).ToArray();

    public bool Matches(string pageName, Guid? companyId) =>
        string.Equals(PageName, pageName, StringComparison.OrdinalIgnoreCase) &&
        CompanyId == companyId;

    public void RecordSessionStarted()
    {
        if (HasStarted)
        {
            return;
        }

        HasStarted = true;
        SessionsStarted.Add(1, Tags("dashboard_session_started", "dashboard", "session-started", $"/{PageName}", null, null, null, null, null));
    }

    public void RecordFocusItemClick(DashboardActionTelemetryContext context)
    {
        var normalizedContext = string.Equals(context.Area, "today-focus", StringComparison.OrdinalIgnoreCase)
            ? context
            : context with { Area = "today-focus" };

        LastFocusItemContext = normalizedContext;
        FocusClicks.Add(1, Tags("dashboard_focus_item_clicked", normalizedContext.Area, normalizedContext.ActionKey, normalizedContext.Target, normalizedContext.ItemId, normalizedContext.SourceType, normalizedContext.Label, normalizedContext.Position, null));
        RecordAction(normalizedContext);
    }

    public void RecordAction(DashboardActionTelemetryContext context)
    {
        LastActionAtUtc = DateTime.UtcNow;
        LastActionContext = context;
        Actions.Add(1, Tags("dashboard_action_clicked", context.Area, context.ActionKey, context.Target, context.ItemId, context.SourceType, context.Label, context.Position, null));

        if (FirstActionAtUtc.HasValue)
        {
            return;
        }

        FirstActionContext = context;
        FirstActionAtUtc = LastActionAtUtc;
        TimeToFirstActionMilliseconds = Math.Max(0, (FirstActionAtUtc.Value - _sessionStartedUtc).TotalMilliseconds);
        TimeToFirstAction.Record(TimeToFirstActionMilliseconds.Value, Tags("dashboard_first_action_recorded", context.Area, context.ActionKey, context.Target, context.ItemId, context.SourceType, context.Label, context.Position, null));
        FirstActionRecorded.Add(1, Tags("dashboard_first_action_recorded", context.Area, context.ActionKey, context.Target, context.ItemId, context.SourceType, context.Label, context.Position, null));
    }

    public void RecordScrollDepth(double depthPercentage)
    {
        var normalizedDepth = Math.Clamp(depthPercentage, 0, 100);
        _maxScrollDepthPercentage = Math.Max(_maxScrollDepthPercentage, normalizedDepth);

        foreach (var milestone in ScrollMilestones)
        {
            if (normalizedDepth < milestone || !_scrollDepthMilestones.Add(milestone))
            {
                continue;
            }

            HighestScrollDepthMilestone = Math.Max(HighestScrollDepthMilestone, milestone);
            ScrollDepthReached.Add(1, Tags("dashboard_scroll_depth_reached", "dashboard", "scroll-depth", string.Empty, null, null, null, null, milestone));
            ScrollDepth.Record(milestone, Tags("dashboard_scroll_depth_reached", "dashboard", "scroll-depth", string.Empty, null, null, null, null, milestone));
        }
    }

    private KeyValuePair<string, object?>[] Tags(
        string eventName,
        string area,
        string actionKey,
        string target,
        string? itemId,
        string? sourceType,
        string? label,
        int? position,
        int? scrollMilestone) =>
    [
        new("event_name", eventName),
        new("session_id", SessionId),
        new("page", PageName),
        new("company_id", CompanyId?.ToString("D")),
        new("area", area),
        new("action_key", actionKey),
        new("target", target),
        new("item_id", itemId),
        new("source_type", sourceType),
        new("label", label),
        new("position", position),
        new("scroll_milestone", scrollMilestone)
    ];
}