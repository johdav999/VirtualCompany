using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Components.Dashboard;
using VirtualCompany.Web.Pages;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class PriorityEvidenceTests
{
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
    public void Evidence_discloses_and_uses_the_active_company_timezone()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        context.Services.GetRequiredService<VirtualCompany.Web.Localization.Formatting.ICompanyPresentationContext>()
            .SetActiveCompany("Europe/Stockholm", "SEK");
        var cut = context.RenderComponent<PriorityEvidenceDetail>(p => p.Add(x => x.CompanyId, Company)
            .Add(x => x.PriorityKey, "deal:1").Add(x => x.Workspace, Workspace()));
        Assert.Contains("Europe/Stockholm", cut.Markup);
        Assert.Contains("2:00 PM", cut.Markup); // The source timestamp is 12:00 UTC, Stockholm summer time is UTC+2.
    }

    [Fact]
    public void Evidence_has_matching_source_context_and_read_only_next_action()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var cut = context.RenderComponent<PriorityEvidenceDetail>(p => p.Add(x => x.CompanyId, Company)
            .Add(x => x.PriorityKey, "deal:1").Add(x => x.Workspace, Workspace()));
        Assert.Contains("Sales opportunity", cut.Find("[data-testid='priority-evidence']").TextContent);
        Assert.Contains("Renewal needs attention", cut.Find("[data-testid='priority-evidence']").TextContent);
        Assert.Contains("Overdue work comes first", cut.Find("[data-testid='priority-ordering']").TextContent);
        Assert.Contains("Finance data is unavailable", cut.Find("[data-testid='priority-partial']").TextContent);
        cut.Find("[data-testid='priority-stale']");
        Assert.Contains("does not approve or execute", cut.Markup);
        var action = new Uri("http://localhost" + cut.Find("[data-testid='priority-next-action']").GetAttribute("href"));
        Assert.Equal("/app/sales/deals/44444444-4444-4444-4444-444444444444", action.AbsolutePath);
        var query = System.Web.HttpUtility.ParseQueryString(action.Query);
        Assert.Equal(Company.ToString("D"), query["companyId"]);
        Assert.Equal(DashboardRoutes.BuildTodayPath(Company, "sales"), query["returnUrl"]);
        Assert.NotNull(DashboardRoutes.NormalizePriorityPath(query["priorityReturnUrl"], Company));
        Assert.Empty(cut.FindAll("button[data-testid='approve']"));
    }

    [Theory]
    [InlineData("task", "Work task")]
    [InlineData("approval", "Approval request")]
    [InlineData("finance_insight", "Finance insight")]
    [InlineData("support_case", "Customer support case")]
    [InlineData("marketing_sales_handoff", "Marketing to Sales handoff")]
    [InlineData("marketing_content_brief", "Marketing content brief")]
    [InlineData("marketing_experiment", "Marketing experiment")]
    public void Contributor_evidence_uses_business_labels_and_handles_missing_timestamp(string source, string label)
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var workspace = Workspace();
        workspace = workspace with { Priorities = [workspace.Priorities[0] with { EvidenceSourceType = source, ObservedAtUtc = DateTime.MinValue, Freshness = "unknown", EvidenceSourceId = null }] };
        var cut = context.RenderComponent<PriorityEvidenceDetail>(p => p.Add(x => x.CompanyId, Company).Add(x => x.PriorityKey, "deal:1").Add(x => x.Workspace, workspace));
        Assert.Contains(label, cut.Find("[data-testid='priority-evidence']").TextContent);
        Assert.Contains("Source timestamp unavailable", cut.Markup);
        Assert.Contains("aggregate observation", cut.Markup);
        Assert.DoesNotContain("0001", cut.Markup);
    }

    [Fact]
    public void Missing_or_foreign_priority_retains_no_source_title_or_primary_action()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var cut = context.RenderComponent<PriorityEvidenceDetail>(p => p.Add(x => x.CompanyId, Company).Add(x => x.PriorityKey, "foreign")
            .Add(x => x.Workspace, Workspace()).Add(x => x.ReturnUrl, "https://unsafe.example/"));
        cut.Find("[data-testid='priority-unavailable']");
        Assert.DoesNotContain("Renewal", cut.Markup);
        Assert.Empty(cut.FindAll("[data-testid='priority-next-action']"));
        cut.SetParametersAndRender(p => p.Add(x => x.PriorityKey, "deal:1").Add(x => x.Workspace, Workspace(Guid.NewGuid())));
        Assert.DoesNotContain("Renewal", cut.Markup);
    }

    [Fact]
    public void Linked_task_and_approval_keep_exact_record_ids()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var task = Guid.NewGuid(); var approval = Guid.NewGuid(); var workspace = Workspace();
        workspace = workspace with { Priorities = [workspace.Priorities[0] with { RelatedTaskId = task, RelatedApprovalId = approval, SourceState = "awaiting_approval" }] };
        var cut = context.RenderComponent<PriorityEvidenceDetail>(p => p.Add(x => x.CompanyId, Company).Add(x => x.PriorityKey, "deal:1").Add(x => x.Workspace, workspace));
        Assert.Contains("Waiting for review", cut.Find("[data-testid='priority-work-state']").TextContent);
        Assert.Contains(task.ToString("D"), cut.FindAll(".priority-detail__related")[0].GetAttribute("href"));
        Assert.Contains(approval.ToString("D"), cut.FindAll(".priority-detail__related")[1].GetAttribute("href"));
    }

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

    [Fact]
    public async Task Detail_discards_an_old_company_response_and_refreshes_without_mutation()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var old = new TaskCompletionSource<TodayWorkspaceViewModel?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = Guid.NewGuid();
        var client = new ReadOnlyClient(id => id == Company ? old.Task : Task.FromResult<TodayWorkspaceViewModel?>(Workspace(second) with { Priorities = [] }));
        context.Services.AddSingleton<ITodayWorkspaceApiClient>(client);
        context.Services.AddSingleton(new OnboardingApiClient(new HttpClient { BaseAddress = new Uri("http://localhost/") }, useOfflineMode: true));
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(DashboardRoutes.BuildPriorityPath(Company, "sales", "deal:1"));
        var cut = context.RenderComponent<PriorityDetails>();
        navigation.NavigateTo(DashboardRoutes.BuildPriorityPath(second, "sales", "deal:1"));
        cut.WaitForAssertion(() => cut.Find("[data-testid='priority-unavailable']"));
        old.SetResult(Workspace());
        await cut.InvokeAsync(() => Task.CompletedTask);
        cut.WaitForAssertion(() => Assert.DoesNotContain("Renewal", cut.Markup));
        Assert.True(client.Refreshes >= 2);
    }

    private sealed class ReadOnlyClient(Func<Guid, Task<TodayWorkspaceViewModel?>> read) : ITodayWorkspaceApiClient
    {
        public int Refreshes;
        public Task<TodayWorkspaceViewModel?> GetAsync(Guid companyId, string? lens = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Detail must refresh.");
        public Task<TodayWorkspaceViewModel?> RefreshAsync(Guid companyId, string? lens = null, CancellationToken cancellationToken = default) { Refreshes++; return read(companyId); }
        public Task<TodayWorkspaceManualReviewViewModel> RequestReviewAsync(Guid companyId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Opening detail cannot command work.");
    }
}
