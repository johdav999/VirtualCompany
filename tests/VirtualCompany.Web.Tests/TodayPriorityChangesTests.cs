using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class TodayPriorityChangesTests
{
    [Fact]
    public void Retired_priority_detail_screen_is_not_registered()
    {
        var routes = typeof(VirtualCompany.Web.Pages.Dashboard).Assembly.GetTypes()
            .SelectMany(type => type.GetCustomAttributes(typeof(Microsoft.AspNetCore.Components.RouteAttribute), true))
            .Cast<Microsoft.AspNetCore.Components.RouteAttribute>();
        Assert.DoesNotContain(routes, route => route.Template == "/dashboard/priorities");
    }
    private static readonly Guid Company = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    internal static TodayWorkspaceViewModel Workspace(Guid? company = null) => new(company ?? Company,
        new("North", "Today", "Priorities"), "sales", [new("sales", "Sales", true, "Assigned")],
        new("Review", "Review today's priorities", Now, "current", false),
        [new("deal:1", 1, "sales", "Renewal needs attention", "12,000 SEK is at risk", "Johan", "Alex", "Review the opportunity",
            Now.AddDays(-2), "stale", "sales_deal", "record-1", "/app/sales/deals/44444444-4444-4444-4444-444444444444",
            false, Now.AddHours(-1), true, 1, "Assigned to you", "Overdue work comes first", "open")],
        [], null, null, null, null, [], [], Now, null, true, [new("finance", "unavailable", "Finance data is unavailable.")]);

    [Fact]
    public void Changes_are_scoped_to_company_lens_and_session_and_detect_resolution()
    {
        var tracker = new TodayPriorityChanges(); var first = Workspace();
        Assert.Null(tracker.Observe(first).PreviousCheck);
        Assert.Equal(0, tracker.Observe(first).Changed);
        Assert.Equal(1, tracker.Observe(first with { Priorities = [first.Priorities[0] with { SourceState = "in_progress" }] }).Changed);
        Assert.Equal(1, tracker.Observe(first with { Priorities = [] }, updateBaseline: false).Removed);
        Assert.Equal(1, tracker.Observe(first with { Priorities = [] }).Removed);
        Assert.Null(tracker.Observe(first with { CompanyId = Guid.NewGuid() }).PreviousCheck);
        Assert.Null(tracker.Observe(first with { ActiveLens = "marketing" }).PreviousCheck);
        Assert.Null(tracker.Observe(first with { GeneratedAtUtc = Now.AddDays(1) }).PreviousCheck);
    }
}
