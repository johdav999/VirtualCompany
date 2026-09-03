using Bunit;
using Microsoft.AspNetCore.Components;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Components.Dashboard;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class TodayWorkspaceComponentTests
{
    private static readonly Guid CompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime NowUtc = new(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Owner_workspace_renders_stable_multi_lens_layout_and_typed_sections()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();

        var cut = Render(context, CreateOwnerWorkspace());

        Assert.Equal(5, cut.FindAll("[data-testid='today-lens-picker'] button").Count);
        Assert.Equal("true", cut.Find("[data-testid='today-lens-picker'] button").GetAttribute("aria-pressed"));
        Assert.Equal(3, cut.FindAll("[data-testid='today-priority']").Count);
        Assert.Equal(4, cut.FindAll(".today-metric").Count);
        cut.Find("[data-testid='finance-today-section']");
        cut.Find("[data-testid='sales-today-section']");
        cut.Find("[data-testid='marketing-today-section']");
        cut.Find("[data-testid='support-today-section']");
        Assert.Contains("Finley", cut.Find("[data-testid='today-agent-briefings']").TextContent);
        Assert.DoesNotContain("Laura", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sales_manager_uses_same_structure_without_redundant_lens_picker()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var sales = CreateOwnerWorkspace() with
        {
            ActiveLens = "sales",
            AvailableLenses = [new("sales", "Sales", true, "Primary responsibility")],
            Finance = null,
            Marketing = null,
            Support = null
        };

        var cut = Render(context, sales);

        Assert.Empty(cut.FindAll("[data-testid='today-lens-picker']"));
        cut.Find("[data-testid='sales-today-section']");
        Assert.Empty(cut.FindAll("[data-testid='finance-today-section']"));
        Assert.Contains("Sales", cut.Find(".today-section-heading").TextContent);
    }

    [Fact]
    public void Finance_manager_sees_only_the_authorized_finance_section()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var finance = CreateOwnerWorkspace() with
        {
            ActiveLens = "finance",
            AvailableLenses = [new("finance", "Finance", true, "Primary responsibility")],
            Sales = null,
            Marketing = null,
            Support = null
        };

        var cut = Render(context, finance);

        cut.Find("[data-testid='finance-today-section']");
        Assert.Empty(cut.FindAll("[data-testid='sales-today-section']"));
        Assert.Empty(cut.FindAll("[data-testid='today-lens-picker']"));
    }

    [Fact]
    public void Lens_picker_is_keyboard_button_control_and_emits_selected_lens()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        string? selected = null;
        var cut = context.RenderComponent<TodayWorkspace>(parameters => parameters
            .Add(x => x.CompanyId, CompanyId)
            .Add(x => x.Workspace, CreateOwnerWorkspace())
            .Add(x => x.LensChanged, EventCallback.Factory.Create<string>(this, value => selected = value)));

        cut.FindAll("[data-testid='today-lens-picker'] button")[1].Click();

        Assert.Equal("finance", selected);
    }

    [Fact]
    public void Period_picker_marks_today_selected_and_emits_monthly_period()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        string? selected = null;
        var cut = context.RenderComponent<TodayWorkspace>(parameters => parameters
            .Add(x => x.CompanyId, CompanyId)
            .Add(x => x.Workspace, CreateOwnerWorkspace())
            .Add(x => x.PeriodChanged, EventCallback.Factory.Create<string>(this, value => selected = value)));

        var buttons = cut.FindAll("[data-testid='workspace-period-picker'] button");
        Assert.Equal(2, buttons.Count);
        Assert.Equal("true", buttons[0].GetAttribute("aria-pressed"));

        buttons[1].Click();

        Assert.Equal("month", selected);
    }

    [Fact]
    public void Canonical_deep_links_preserve_company_and_work_context()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var cut = Render(context, CreateOwnerWorkspace());

        var route = cut.Find("[data-testid='today-priority'] .today-priority__button").GetAttribute("href");

        Assert.Contains($"companyId={CompanyId:D}", route, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("source=dashboard", route, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("/tasks", route, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Today_route_preserves_company_and_selected_lens()
    {
        var route = DashboardRoutes.BuildTodayPath(CompanyId, "Finance");

        Assert.Equal($"/dashboard?companyId={CompanyId:D}&lens=finance", route);
    }

    [Fact]
    public void Setup_guidance_shows_management_action_only_when_authorized_by_server()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var owner = CreateOwnerWorkspace() with
        {
            ResponsibilitySetup = new(false, true, "Assign responsibility owners.", $"/settings?companyId={CompanyId:D}")
        };
        var member = owner with { ResponsibilitySetup = new(false, false, "Ask an owner or administrator.", string.Empty) };

        var ownerCut = Render(context, owner);
        Assert.Single(ownerCut.FindAll("[data-testid='today-responsibility-setup'] a"));

        var memberCut = Render(context, member);
        Assert.Empty(memberCut.FindAll("[data-testid='today-responsibility-setup'] a"));
        Assert.Contains("Ask an owner", memberCut.Markup);
    }

    [Fact]
    public void Partial_and_stale_data_remain_visible_with_honest_section_state()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var workspace = CreateOwnerWorkspace() with
        {
            IsPartial = true,
            SituationSummary = new("Refresh needed", "Some data is old.", NowUtc.AddHours(-8), "stale", true),
            Finance = CreateOwnerWorkspace().Finance! with
            {
                IsAvailable = false,
                StatusMessage = "Finance data is temporarily unavailable."
            }
        };

        var cut = Render(context, workspace);

        cut.Find("[data-testid='today-partial']");
        cut.Find("[data-testid='today-stale']");
        Assert.Contains("temporarily unavailable", cut.Find("[data-testid='finance-today-section']").TextContent);
        Assert.Equal(3, cut.FindAll("[data-testid='today-priority']").Count);
    }

    [Fact]
    public void Loading_error_empty_and_unauthorized_states_are_announced()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();

        var loading = context.RenderComponent<TodayWorkspace>(p => p
            .Add(x => x.CompanyId, CompanyId)
            .Add(x => x.IsLoading, true));
        Assert.Equal("status", loading.Find("[data-testid='today-loading']").GetAttribute("role"));

        var unauthorized = context.RenderComponent<TodayWorkspace>(p => p
            .Add(x => x.CompanyId, CompanyId)
            .Add(x => x.IsUnauthorized, true));
        Assert.Equal("alert", unauthorized.Find("[data-testid='today-unauthorized']").GetAttribute("role"));

        var error = context.RenderComponent<TodayWorkspace>(p => p
            .Add(x => x.CompanyId, CompanyId)
            .Add(x => x.ErrorMessage, "Temporary failure"));
        Assert.Equal("alert", error.Find("[data-testid='today-error']").GetAttribute("role"));

        var empty = context.RenderComponent<TodayWorkspace>(p => p.Add(x => x.CompanyId, CompanyId));
        Assert.Equal("status", empty.Find("[data-testid='today-empty']").GetAttribute("role"));
    }

    [Fact]
    public void Dashboard_uses_only_the_today_client_for_operational_composition()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "VirtualCompany.Web", "Pages", "Dashboard.razor"));

        Assert.Contains("ITodayWorkspaceApiClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FinanceApiClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AgentStaffOverviewApiClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DashboardSummaryApiClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LAURA", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Responsive_styles_stack_the_context_rail_and_keep_touch_targets()
    {
        var css = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "VirtualCompany.Web", "Components", "Dashboard", "TodayWorkspace.razor.css"));

        Assert.Contains("@media (max-width: 1100px)", css, StringComparison.Ordinal);
        Assert.Contains(".today-rail { grid-template-columns: repeat(2", css, StringComparison.Ordinal);
        Assert.Contains(".today-controls { grid-template-columns: 1fr; }", css, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 560px)", css, StringComparison.Ordinal);
        Assert.Contains(".today-rail { grid-template-columns: 1fr", css, StringComparison.Ordinal);
        Assert.Contains("min-height: 44px", css, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("monitoring", "Monitoring")]
    [InlineData("working", "Working")]
    [InlineData("recommended", "Recommended")]
    [InlineData("needs_user", "Needs you")]
    [InlineData("blocked", "Blocked")]
    [InlineData("completed", "Completed")]
    public void Normalized_agent_states_have_consistent_accessible_presentation(string state, string label)
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var workspace = CreateOwnerWorkspace() with
        {
            AgentUpdates = [new("agent-state", "Agent update", "Safe summary", "Finley", NowUtc,
                "work_task", "/work", "Finance agent", state, RationaleSummary: "Evidence-backed rationale.",
                VisibilityReason: "Shown because you own Finance.")]
        };

        var cut = Render(context, workspace);

        var update = cut.Find("[data-testid='today-agent-briefings'] .today-agent-update");
        Assert.Equal(state, update.GetAttribute("data-state"));
        Assert.Contains(label, update.TextContent);
        Assert.Contains("Evidence-backed rationale", update.TextContent);
        Assert.Contains("Shown because", update.TextContent);
    }

    [Theory]
    [InlineData("queued", "Queued")]
    [InlineData("running", "Running")]
    [InlineData("completed", "Completed")]
    [InlineData("blocked", "Blocked")]
    [InlineData("failed", "Failed")]
    public void Manual_review_progress_and_failure_states_are_visible(string state, string label)
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var workspace = CreateOwnerWorkspace() with
        {
            ManualReview = new(true, null, null, Guid.NewGuid(), null, state, "Durable review state.", NowUtc)
        };

        var cut = Render(context, workspace);

        var review = cut.Find("[data-testid='today-manual-review']");
        Assert.Contains(label, review.TextContent);
        Assert.Contains("Durable review state", review.TextContent);
        if (state is "queued" or "running") Assert.True(review.QuerySelector("button")!.HasAttribute("disabled"));
        if (state is "blocked" or "failed") Assert.NotNull(review.QuerySelector("a[href^='/company-operation']"));
    }

    [Fact]
    public void Manual_review_action_is_absent_when_backend_denies_capability()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var workspace = CreateOwnerWorkspace() with
        {
            ManualReview = new(false, "not_authorized", "Company manager access is required.", null, null,
                "idle", "Company manager access is required.", null)
        };

        var cut = Render(context, workspace);

        Assert.Empty(cut.FindAll("[data-testid='today-manual-review'] button"));
        Assert.Single(cut.FindAll("[data-testid='today-manual-review'] a[href^='/company-operation']"));
    }

    private static IRenderedComponent<TodayWorkspace> Render(TestContext context, TodayWorkspaceViewModel workspace) =>
        context.RenderComponent<TodayWorkspace>(parameters => parameters
            .Add(x => x.CompanyId, CompanyId)
            .Add(x => x.Workspace, workspace));

    private static TodayWorkspaceViewModel CreateOwnerWorkspace()
    {
        var item = new TodayWorkspaceFeatureItemViewModel(
            "item-1", "Review current work", "One item needs attention.", "attention", NowUtc, "/work");
        return new TodayWorkspaceViewModel(
            CompanyId,
            new("Atlas Co.", "Today at Atlas Co.", "What needs attention."),
            "company",
            [
                new("company", "Company", true, "Executive oversight"),
                new("finance", "Finance", false, "Primary responsibility"),
                new("sales", "Sales", false, "Primary responsibility"),
                new("marketing", "Marketing", false, "Primary responsibility"),
                new("customers", "Customers", false, "Primary responsibility")
            ],
            new("Business momentum is steady", "Three priorities need attention.", NowUtc, "fresh", true),
            [
                Priority("priority-1", 1, "company", true, "/tasks?taskId=22222222-2222-2222-2222-222222222222"),
                Priority("priority-2", 2, "sales", false, "/app/sales/pipeline"),
                Priority("priority-3", 3, "customers", false, "/support")
            ],
            [
                Metric("cash", "Available cash", "4.2 MSEK", "attention", "/finance/cash-position"),
                Metric("pipeline", "Pipeline", "18.7 MSEK", "opportunity", "/app/sales/pipeline"),
                Metric("customers", "New customers", "126", "current", "/app/sales"),
                Metric("sla", "SLA at risk", "2", "critical", "/support")
            ],
            new(true, "Finance data is current.", NowUtc, 4200000m, "SEK", 96, "healthy", 2, [item], "/finance"),
            new(true, "Sales pipeline data is current.", NowUtc, 18700000m, "SEK", 12, 4, 2, 2100000m, [item], "/app/sales"),
            new(true, "Customer support data is current.", NowUtc, 14, 1, 0, 2, 0, [item], "/support"),
            new(true, "Marketing plan data is current.", NowUtc, 2, 3, 1, 2, [item], "/marketing"),
            [new("decision-1", "Approve vendor payment", "Review the evidence before approval.", NowUtc, "/approvals")],
            [new("agent-1", "Cash position checked", "Available cash improved today.", "Finley", NowUtc, "finance_activity", "/finance", "Finance agent", "Online")],
            NowUtc,
            null,
            false,
            [],
            new(true, true, string.Empty, $"/settings?companyId={CompanyId:D}"),
            new(true, null, null, null, null, "idle", "Request a fresh company review.", null));
    }

    private static TodayWorkspacePriorityViewModel Priority(string key, int rank, string lens, bool decision, string deepLink) =>
        new(key, rank, lens, "A record needs attention", "It affects today's operating plan.", "Alex Owner", "Finley",
            "Review the evidence and confirm the next step.", NowUtc, "fresh", "task", key, deepLink, decision, null, true, 1m);

    private static TodayWorkspaceMetricViewModel Metric(string key, string label, string value, string status, string deepLink) =>
        new(key, label, null, value, null, status, NowUtc, "dashboard", deepLink);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "VirtualCompany.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
