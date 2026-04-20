using Bunit;
using VirtualCompany.Web.Components;
using VirtualCompany.Web.Services;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class BusinessSignalsPanelTests
{
    [Fact]
    public void Business_signals_panel_renders_signals_with_severity_classes_icons_and_no_legacy_kpis()
    {
        using var context = new TestContext();
        var detectedAtUtc = new DateTime(2026, 4, 19, 12, 0, 0, DateTimeKind.Utc);

        var signals = new List<BusinessSignalViewModel>
        {
            new()
            {
                Type = "operationalLoad",
                Severity = "warning",
                Title = "Operational load is building",
                Summary = "Open work is rising faster than available capacity.",
                MetricValue = 2.5m,
                MetricLabel = "open_tasks_per_agent",
                ActionLabel = "Open tasks",
                ActionUrl = "/tasks?companyId=11111111-1111-1111-1111-111111111111",
                DetectedAtUtc = detectedAtUtc
            },
            new()
            {
                Type = "approval-bottleneck",
                Severity = "critical",
                Title = "Approvals are waiting on decisions",
                Summary = "5 approval(s) are pending.",
                ActionLabel = "Review approvals",
                ActionUrl = "/approvals?companyId=11111111-1111-1111-1111-111111111111&status=pending",
                DetectedAtUtc = detectedAtUtc
            }
        };

        var cut = context.RenderComponent<BusinessSignalsPanel>(parameters => parameters.Add(component => component.Signals, signals));

        var warningSignal = cut.Find("[data-testid='business-signal-operationalload']");
        Assert.Contains("business-signal--warning", warningSignal.ClassName);
        Assert.Equal("~", warningSignal.QuerySelector(".business-signal__icon")!.TextContent.Trim());
        Assert.Contains("Operational load", warningSignal.TextContent);

        var criticalSignal = cut.Find("[data-testid='business-signal-approval-bottleneck']");
        Assert.Contains("business-signal--critical", criticalSignal.ClassName);
        Assert.Equal("!", criticalSignal.QuerySelector(".business-signal__icon")!.TextContent.Trim());
        Assert.Contains("Approval bottleneck", criticalSignal.TextContent);

        Assert.Empty(cut.FindAll(".kpi-tile"));
        Assert.DoesNotContain("workflow exceptions", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }
}