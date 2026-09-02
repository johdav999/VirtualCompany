using Bunit;
using Microsoft.AspNetCore.Components;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Components.Dashboard;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class MonthlyWorkspaceComponentTests
{
    private static readonly Guid CompanyId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime Now = new(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Owner_review_renders_period_results_priorities_sections_decisions_and_agent_outcomes()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var cut = Render(context, CreateWorkspace());

        Assert.Equal(2, cut.FindAll("[data-testid='workspace-period-picker'] button").Count);
        Assert.Equal(5, cut.FindAll("[data-testid='monthly-lens-picker'] button").Count);
        Assert.Equal(2, cut.FindAll(".monthly-result").Count);
        Assert.Equal(2, cut.FindAll(".monthly-result__icon").Count);
        Assert.Equal(2, cut.FindAll(".monthly-priority").Count);
        Assert.Equal(2, cut.FindAll(".monthly-priority__agent img").Count);
        Assert.Equal(3, cut.FindAll(".monthly-section").Count);
        Assert.Contains("Decision", cut.Find("[data-testid='monthly-decisions']").TextContent);
        Assert.Contains("Sales Agent", cut.Find("[data-testid='monthly-agent-outcomes']").TextContent);
        Assert.Contains("Completed", cut.Find("[data-testid='monthly-agent-outcomes']").TextContent);
    }

    [Fact]
    public void Sales_manager_variant_does_not_render_unauthorized_finance_section()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var sales = CreateWorkspace() with
        {
            ActiveLens = "sales",
            AvailableLenses = [new("sales", "Sales", true, "Primary responsibility")],
            Sections = CreateWorkspace().Sections.Where(x => x.Lens == "sales").ToList(),
            Results = CreateWorkspace().Results.Where(x => x.Key.StartsWith("sales", StringComparison.Ordinal)).ToList()
        };

        var cut = Render(context, sales);

        Assert.Empty(cut.FindAll("[data-testid='monthly-lens-picker']"));
        Assert.Single(cut.FindAll(".monthly-section"));
        Assert.DoesNotContain("Finance result", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Period_lens_and_month_controls_emit_requested_context()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        string? period = null; string? lens = null; (int Year, int Month) selected = default;
        var cut = context.RenderComponent<MonthlyWorkspace>(p => p.Add(x => x.CompanyId, CompanyId)
            .Add(x => x.Workspace, CreateWorkspace())
            .Add(x => x.PeriodChanged, EventCallback.Factory.Create<string>(this, x => period = x))
            .Add(x => x.LensChanged, EventCallback.Factory.Create<string>(this, x => lens = x))
            .Add(x => x.MonthChanged, EventCallback.Factory.Create<(int Year, int Month)>(this, x => selected = x)));

        cut.FindAll("[data-testid='workspace-period-picker'] button")[0].Click();
        cut.FindAll("[data-testid='monthly-lens-picker'] button")[2].Click();
        cut.FindAll(".monthly-month-nav button")[0].Click();

        Assert.Equal("today", period);
        Assert.Equal("sales", lens);
        Assert.Equal((2026, 7), selected);
    }

    [Fact]
    public void Unavailable_and_partial_data_are_truthful_and_actionable()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var workspace = CreateWorkspace() with { IsPartial = true };
        var cut = Render(context, workspace);

        cut.Find("[data-testid='monthly-partial']");
        Assert.Contains("Unavailable", cut.Markup);
        Assert.Contains("Set up source", cut.Markup);
    }

    [Fact]
    public void Monthly_route_and_styles_preserve_period_context_and_mobile_stacking()
    {
        var route = DashboardRoutes.BuildMonthlyPath(CompanyId, "Sales", 2026, 8);
        var css = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "VirtualCompany.Web", "Components", "Dashboard", "MonthlyWorkspace.razor.css"));

        Assert.Equal($"/dashboard?companyId={CompanyId:D}&period=month&lens=sales&year=2026&month=8", route);
        Assert.Contains("@media(max-width:560px)", css);
        Assert.Contains(".monthly-rail { grid-template-columns:1fr; }", css);
        Assert.Contains("min-height:44px", css);
    }

    [Fact]
    public void Loading_error_empty_and_unauthorized_states_are_announced()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        Assert.Equal("status", context.RenderComponent<MonthlyWorkspace>(p => p.Add(x => x.CompanyId, CompanyId).Add(x => x.IsLoading, true)).Find("[data-testid='monthly-loading']").GetAttribute("role"));
        Assert.Equal("alert", context.RenderComponent<MonthlyWorkspace>(p => p.Add(x => x.CompanyId, CompanyId).Add(x => x.IsUnauthorized, true)).Find("[data-testid='monthly-unauthorized']").GetAttribute("role"));
        Assert.Equal("alert", context.RenderComponent<MonthlyWorkspace>(p => p.Add(x => x.CompanyId, CompanyId).Add(x => x.ErrorMessage, "Failure")).Find("[data-testid='monthly-error']").GetAttribute("role"));
        Assert.Equal("status", context.RenderComponent<MonthlyWorkspace>(p => p.Add(x => x.CompanyId, CompanyId)).Find("[data-testid='monthly-empty']").GetAttribute("role"));
    }

    private static IRenderedComponent<MonthlyWorkspace> Render(TestContext context, MonthlyWorkspaceViewModel workspace) =>
        context.RenderComponent<MonthlyWorkspace>(p => p.Add(x => x.CompanyId, CompanyId).Add(x => x.Workspace, workspace));

    private static MonthlyWorkspaceViewModel CreateWorkspace()
    {
        var period = new MonthlyWorkspacePeriodViewModel(2026, 8, "UTC", new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), "August 1–31, 2026", "July 2026");
        var priority = new TodayWorkspacePriorityViewModel("p", 1, "sales", "Pipeline changed", "It affects next month", "Owner", "Sales Agent", "Review", Now, "current", "sales_activity", "p", "/sales", false, null, true, 1);
        return new(CompanyId, new("Atlas", "Monthly review", "Summary"), "company",
            [new("company", "Company", true, "Owner"), new("finance", "Finance", false, "Oversight"), new("sales", "Sales", false, "Oversight"), new("marketing", "Marketing", false, "Oversight"), new("customers", "Customers", false, "Oversight")],
            period, new("One area needs attention", "Deterministic summary", "1 of 2 sources current", true),
            [new("finance.net", "Finance result", 10, "10 SEK", 8, "8 SEK", "SEK", "positive", Now, "finance", "/finance"),
             new("sales.moves", "Pipeline movement", 2, "2", 1, "1", "moves", "positive", Now, "sales", "/sales")],
            [priority, priority with { Key = "p2", Rank = 2, WhatHappened = "Follow-up required" }],
            [new("finance", "Finance", "Positive result", "healthy", Now, [new("Net result", "10 SEK")], [], "/finance", "Current"),
             new("sales", "Sales", "Movement recorded", "current", Now, [new("Stage moves", "2")], [], "/sales", "Current"),
             new("marketing", "Marketing", "Outcomes unavailable", "unavailable", Now, [new("Outcomes", "Unavailable", "unavailable")], [], "/marketing", "Missing source", true, "/marketing")],
            [new("d", "Decision", "Approve next step", Now, "/approvals")],
            [new("a", "Outcome", "Completed work", "Sales Agent", Now, "work_task", "/tasks", "Sales agent", "completed")],
            [new("finance", "Finance", "current", Now, "Current"), new("marketing", "Marketing", "unavailable", null, "Missing", "/marketing")],
            Now, null, true, []);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
