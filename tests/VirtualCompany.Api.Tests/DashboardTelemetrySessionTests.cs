using System.Linq;
using VirtualCompany.Web.Services;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class DashboardTelemetrySessionTests
{
    [Fact]
    public void First_action_is_recorded_once_per_session()
    {
        var session = new DashboardTelemetrySession("dashboard", Guid.NewGuid());

        session.RecordAction(new DashboardActionTelemetryContext("action-queue", "open-queue", "/queue"));
        var firstAction = session.FirstActionAtUtc;
        session.RecordAction(new DashboardActionTelemetryContext("finance", "open-finance-home", "/finance"));

        Assert.True(session.HasRecordedFirstAction);
        Assert.True(session.SessionStartedUtc <= firstAction);
        Assert.Equal(firstAction, session.FirstActionAtUtc);
        Assert.Equal(firstAction, session.LastActionAtUtc);
        Assert.Equal("open-queue", session.FirstActionContext?.ActionKey);
    }


    [Fact]
    public void Focus_click_preserves_payload_and_records_time_to_first_action_metric()
    {
        var session = new DashboardTelemetrySession("dashboard", Guid.NewGuid());
        var context = new DashboardActionTelemetryContext(
            "today-focus",
            "review",
            "/approvals?filter=pending&status=pending&source=dashboard",
            "approval-1",
            "approval",
            "Review approval",
            2);

        session.RecordFocusItemClick(context);

        Assert.Equal(context, session.LastFocusItemContext);
        Assert.Equal(context, session.FirstActionContext);
        Assert.NotNull(session.TimeToFirstActionMilliseconds);
        Assert.True(session.TimeToFirstActionMilliseconds >= 0);
    }

    [Fact]
    public void First_action_timestamp_is_captured_from_the_same_action_event_that_sets_time_to_first_action()
    {
        var session = new DashboardTelemetrySession("dashboard", Guid.NewGuid());

        session.RecordAction(new DashboardActionTelemetryContext("action-queue", "open-queue", "/queue"));

        Assert.NotNull(session.FirstActionAtUtc);
        Assert.NotNull(session.LastActionAtUtc);
        Assert.Equal(session.FirstActionAtUtc, session.LastActionAtUtc);
        Assert.True(session.FirstActionAtUtc >= session.SessionStartedUtc);
    }

    [Fact]
    public void Scroll_depth_only_moves_forward()
    {
        var session = new DashboardTelemetrySession("dashboard", Guid.NewGuid());

        session.RecordScrollDepth(12);
        session.RecordScrollDepth(30);
        session.RecordScrollDepth(80);
        session.RecordScrollDepth(80);
        session.RecordScrollDepth(100);

        Assert.Equal(100, session.MaxScrollDepthPercentage);
        Assert.Equal(100, session.HighestScrollDepthMilestone);
        Assert.Equal(new[] { 25, 50, 75, 100 }, session.ScrollDepthMilestonesReached.ToArray());
    }
}