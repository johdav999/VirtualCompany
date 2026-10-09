using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Layout;
using VirtualCompany.Web.Pages;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class WorkspaceShellTests
{
    private static readonly Guid A = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid B = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Inactive = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void Dashboard_uses_server_default_and_keeps_only_available_responsibilities()
    {
        using var context = CreateContext(new TodayClient((id, lens) => Task.FromResult<TodayWorkspaceViewModel?>(Today(id, lens == "marketing" ? "marketing" : "sales", ["sales", "marketing"]))));
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(DashboardRoutes.BuildTodayPath(A, "finance"));
        var cut = context.RenderComponent<Dashboard>();
        cut.WaitForAssertion(() =>
        {
            Assert.Equal(2, cut.FindAll("[data-testid='today-lens-picker'] button").Count);
            Assert.DoesNotContain("finance", cut.Find("[data-testid='today-lens-picker']").TextContent, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(DashboardRoutes.BuildTodayPath(A, "sales"), LocalUri(context));
        });
        cut.FindAll("[data-testid='today-lens-picker'] button")[1].Click();
        Assert.Equal(DashboardRoutes.BuildTodayPath(A, "marketing"), LocalUri(context));
    }

    [Fact]
    public async Task Older_company_response_cannot_replace_newer_company_workspace()
    {
        var oldResponse = new TaskCompletionSource<TodayWorkspaceViewModel?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var context = CreateContext(new TodayClient((id, _) => id == A ? oldResponse.Task : Task.FromResult<TodayWorkspaceViewModel?>(Today(id))));
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(DashboardRoutes.BuildTodayPath(A, "company"));
        var cut = context.RenderComponent<Dashboard>();
        cut.Find("[data-testid='today-loading']");
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(DashboardRoutes.BuildTodayPath(B, "company"));
        cut.WaitForAssertion(() => Assert.Contains("Company B", cut.Find("[data-testid='today-workspace']").TextContent));
        oldResponse.SetResult(Today(A));
        await cut.InvokeAsync(() => Task.CompletedTask);
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Company B", cut.Markup);
            Assert.DoesNotContain("Company A", cut.Markup);
            Assert.Empty(cut.FindAll("[data-testid='today-loading']"));
        });
    }

    [Fact]
    public void Inactive_or_unknown_company_never_loads_workspace_records()
    {
        var calls = 0;
        using var context = CreateContext(new TodayClient((id, _) => { calls++; return Task.FromResult<TodayWorkspaceViewModel?>(Today(id)); }));
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(DashboardRoutes.BuildTodayPath(Inactive));
        var cut = context.RenderComponent<Dashboard>();
        cut.WaitForAssertion(() => cut.Find("[data-testid='today-unauthorized']"));
        Assert.Equal(0, calls);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(DashboardRoutes.BuildTodayPath(Guid.NewGuid()));
        cut.WaitForAssertion(() => cut.Find("[data-testid='today-unauthorized']"));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Monthly_switching_keeps_the_selected_month_and_return_origin()
    {
        using var context = CreateContext(new TodayClient((id, _) => Task.FromResult<TodayWorkspaceViewModel?>(Today(id))));
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(DashboardRoutes.BuildMonthlyPath(A, "sales", 2026, 8));
        var cut = context.RenderComponent<Dashboard>();
        cut.WaitForAssertion(() => cut.Find("[data-testid='monthly-workspace']"));
        var link = cut.Find(".monthly-result").GetAttribute("href")!;
        Assert.Equal(DashboardRoutes.BuildMonthlyPath(A, "sales", 2026, 8),
            System.Web.HttpUtility.ParseQueryString(new Uri("http://localhost" + link).Query)["returnUrl"]);
        cut.FindAll("[data-testid='monthly-lens-picker'] button")[1].Click();
        Assert.Equal(DashboardRoutes.BuildMonthlyPath(A, "marketing", 2026, 8), LocalUri(context));
    }

    [Fact]
    public async Task Departing_dashboard_does_not_canonicalize_over_business_navigation()
    {
        var calls = 0;
        using var context = CreateContext(new TodayClient((id, lens) =>
        {
            calls++;
            return Task.FromResult<TodayWorkspaceViewModel?>(Today(id, lens ?? "company"));
        }));
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(DashboardRoutes.BuildTodayPath(A, "sales"));
        var cut = context.RenderComponent<Dashboard>();
        cut.WaitForAssertion(() => cut.Find("[data-testid='today-workspace']"));
        var target = DashboardRoutes.EnsureWorkspaceContext("/app/sales", A, DashboardRoutes.BuildTodayPath(A, "sales"));
        navigation.NavigateTo(target);
        await cut.InvokeAsync(() => Task.CompletedTask);
        Assert.Equal(target, LocalUri(context));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Invalid_monthly_link_shows_a_recoverable_error()
    {
        using var context = CreateContext(new TodayClient((id, _) => Task.FromResult<TodayWorkspaceViewModel?>(Today(id))));
        context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/dashboard?companyId={A:D}&period=month&month=15");
        var cut = context.RenderComponent<Dashboard>();
        cut.WaitForAssertion(() => Assert.Contains("This month link is invalid", cut.Find("[data-testid='monthly-error']").TextContent));
        cut.Find("[data-testid='monthly-error'] button").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='monthly-workspace']"));
        Assert.Equal(DashboardRoutes.BuildMonthlyPath(A, "sales", 2026, 8), LocalUri(context));
    }

    [Fact]
    public void Company_picker_excludes_inactive_memberships_and_starts_new_company_at_its_default()
    {
        using var context = CreateContext(new TodayClient((id, _) => Task.FromResult<TodayWorkspaceViewModel?>(Today(id))));
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(DashboardRoutes.BuildMonthlyPath(A, "sales", 2026, 8));
        var cut = context.RenderComponent<NavMenu>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("#workspace-company option").Count));
        Assert.DoesNotContain("Inactive", cut.Find("#workspace-company").TextContent);
        cut.Find("#workspace-company").Change(B.ToString("D"));
        Assert.Equal(DashboardRoutes.BuildTodayPath(B), LocalUri(context));
        cut.WaitForAssertion(() =>
        {
            Assert.Equal(B.ToString("D"), cut.Find("#workspace-company option[selected]").GetAttribute("value"));
            Assert.DoesNotContain("period=month", cut.Find("a.navbar-brand").GetAttribute("href"));
        });
    }

    [Fact]
    public void Company_context_label_does_not_reuse_the_active_company_on_a_foreign_link()
    {
        using var context = CreateContext(new TodayClient((id, _) => Task.FromResult<TodayWorkspaceViewModel?>(Today(id))));
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(DashboardRoutes.BuildTodayPath(Inactive));
        var cut = context.RenderComponent<NavMenu>();
        cut.WaitForAssertion(() => Assert.Equal("Company", cut.FindComponent<VirtualCompany.Web.Components.ContextualAgentSidebarCard>().Instance.AgentName));
    }

    [Fact]
    public void Sidebar_overview_keeps_monthly_context_after_opening_a_business_record()
    {
        using var context = CreateContext(new TodayClient((id, _) => Task.FromResult<TodayWorkspaceViewModel?>(Today(id))));
        var origin = DashboardRoutes.BuildMonthlyPath(A, "sales", 2026, 8);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(
            DashboardRoutes.EnsureWorkspaceContext("/app/sales/deals/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", A, origin));
        var cut = context.RenderComponent<NavMenu>();
        cut.WaitForAssertion(() => Assert.Equal(origin, cut.Find("a.navbar-brand").GetAttribute("href")));
        context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/app/sales/pipeline?companyId={A:D}");
        cut.WaitForAssertion(() => Assert.Equal(origin, cut.Find("a.navbar-brand").GetAttribute("href")));
    }

    private static string LocalUri(TestContext context) => new Uri(context.Services.GetRequiredService<NavigationManager>().Uri).PathAndQuery;

    [Fact]
    public async Task Weekly_company_switch_ignores_a_late_response_from_the_previous_company()
    {
        var old = new TaskCompletionSource<WeeklyWorkspaceViewModel?>();
        using var context = CreateContext(new TodayClient((id, _) => Task.FromResult<TodayWorkspaceViewModel?>(Today(id))));
        context.Services.AddSingleton<IWeeklyWorkspaceApiClient>(new WeeklyClient((id, _, _) => id == A ? old.Task : Task.FromResult<WeeklyWorkspaceViewModel?>(Weekly(id))));
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(DashboardRoutes.BuildWeeklyPath(A, "sales", new(2026, 9, 28)));
        var cut = context.RenderComponent<Dashboard>();
        cut.WaitForAssertion(() => cut.Find("[data-testid=weekly-loading]"));
        navigation.NavigateTo(DashboardRoutes.BuildWeeklyPath(B, "sales", new(2026, 9, 28)));
        cut.WaitForAssertion(() => Assert.Contains("Company B", cut.Find("[data-testid=weekly-workspace]").TextContent));
        old.SetResult(Weekly(A)); await cut.InvokeAsync(() => Task.CompletedTask);
        cut.WaitForAssertion(() => Assert.DoesNotContain("Company A", cut.Markup));
        Assert.Contains(B.ToString("D"), LocalUri(context));
    }

    [Fact]
    public void Weekly_canonicalization_and_sidebar_return_retain_source_detail_context()
    {
        using var context = CreateContext(new TodayClient((id, _) => Task.FromResult<TodayWorkspaceViewModel?>(Today(id))));
        context.Services.AddSingleton<IWeeklyWorkspaceApiClient>(new WeeklyClient((id, _, _) => Task.FromResult<WeeklyWorkspaceViewModel?>(Weekly(id))));
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(DashboardRoutes.WithQuery(DashboardRoutes.BuildWeeklyPath(A, "sales", new(2026, 9, 30)),
            ("metric", "sales.change"), ("prior", "true"), ("sourcePage", "2")));
        var cut = context.RenderComponent<Dashboard>();
        cut.WaitForAssertion(() => cut.Find("[data-testid=weekly-detail]"));
        var origin = LocalUri(context);
        Assert.Contains("week=2026-09-28", origin); Assert.Contains("metric=sales.change", origin);
        Assert.Contains("prior=true", origin); Assert.Contains("sourcePage=2", origin);
        navigation.NavigateTo(DashboardRoutes.EnsureWorkspaceContext("/app/sales/activities", A, origin));
        var nav = context.RenderComponent<NavMenu>();
        nav.WaitForAssertion(() => Assert.Equal(origin, nav.Find("a.navbar-brand").GetAttribute("href")));
    }

    [Fact]
    public void Weekly_retry_retains_the_selected_historical_period()
    {
        var requests = new List<DateOnly?>();
        using var context = CreateContext(new TodayClient((id, _) => Task.FromResult<TodayWorkspaceViewModel?>(Today(id))));
        context.Services.AddSingleton<IWeeklyWorkspaceApiClient>(new WeeklyClient((id, _, week) =>
        {
            requests.Add(week);
            return requests.Count == 1 ? Task.FromException<WeeklyWorkspaceViewModel?>(new HttpRequestException("Transient")) : Task.FromResult<WeeklyWorkspaceViewModel?>(Weekly(id));
        }));
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(DashboardRoutes.BuildWeeklyPath(A, "sales", new(2026, 9, 28)));
        var cut = context.RenderComponent<Dashboard>();
        cut.WaitForAssertion(() => cut.Find("[data-testid=weekly-error]"));
        cut.Find("[data-testid=weekly-error] button").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid=weekly-workspace]"));
        Assert.Equal(new DateOnly(2026, 9, 28), requests[0]); Assert.Equal(requests[0], requests[1]);
    }

    private static WeeklyWorkspaceViewModel Weekly(Guid id) => WeeklyWorkspaceJourneyTests.Workspace("sales") with
    { CompanyId = id, CompanyName = id == A ? "Company A" : "Company B" };
    private sealed class WeeklyClient(Func<Guid, string?, DateOnly?, Task<WeeklyWorkspaceViewModel?>> get) : IWeeklyWorkspaceApiClient
    {
        public Task<WeeklyWorkspaceViewModel?> GetAsync(Guid id, string? lens = null, DateOnly? week = null, CancellationToken token = default) => get(id, lens, week);
    }

    private static TestContext CreateContext(ITodayWorkspaceApiClient today)
    {
        var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddLogging();
        context.Services.AddScoped<IDashboardInteractionService, DashboardInteractionService>();
        context.Services.AddSingleton<OverviewNavigationContext>();
        context.Services.AddSingleton(new FinanceAccessResolver());
        var http = new HttpClient(new ShellHandler()) { BaseAddress = new Uri("http://localhost/") };
        context.Services.AddSingleton(new OnboardingApiClient(http));
        context.Services.AddSingleton(new FinanceIntegrationApplicationApiClient(http));
        context.Services.AddSingleton(today);
        context.Services.AddSingleton<IMonthlyWorkspaceApiClient>(new MonthlyClient());
        return context;
    }

    internal static TodayWorkspaceViewModel Today(Guid id, string lens = "company", string[]? lenses = null) => new(
        id, new(id == A ? "Company A" : "Company B", "Today", "Your work"), lens,
        (lenses ?? ["company", "finance", "sales", "marketing", "customers"]).Select(x => new TodayWorkspaceLensViewModel(x, x, x == lens, "Assigned responsibility")).ToArray(),
        new("Current work", "Source-linked work", DateTime.UtcNow, "current", true),
        [new("priority", 1, lens, "Review work", "A deadline is approaching", "Assigned owner", null, "Open record",
            DateTime.UtcNow, "current", "work_task", null, "/work?tab=tasks", false, null, true, 1m)],
        [], null, null, null, null, [], [], DateTime.UtcNow, null, false, []);

    private sealed class TodayClient(Func<Guid, string?, Task<TodayWorkspaceViewModel?>> get) : ITodayWorkspaceApiClient
    {
        public Task<TodayWorkspaceViewModel?> GetAsync(Guid id, string? lens = null, CancellationToken cancellationToken = default) => get(id, lens);
        public Task<TodayWorkspaceManualReviewViewModel> RequestReviewAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class MonthlyClient : IMonthlyWorkspaceApiClient
    {
        public Task<MonthlyWorkspaceViewModel?> GetAsync(Guid id, string? lens = null, int? year = null, int? month = null, CancellationToken cancellationToken = default)
        {
            var today = Today(id, lens ?? "sales", ["sales", "marketing"]);
            return Task.FromResult<MonthlyWorkspaceViewModel?>(new(id, today.Header, today.ActiveLens, today.AvailableLenses,
                new(year ?? 2026, month ?? 8, "UTC", DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, "August", "July"),
                new("Monthly review", "Recorded results", "Complete test sources", true),
                [new("result", "Recorded result", 1, "1", 0, "0", "count", "current", DateTime.UtcNow, "work_task", "/work")],
                [], [], [], [], [], DateTime.UtcNow, null, false, []));
        }
    }

    private sealed class ShellHandler : HttpMessageHandler
    {
        private Guid activeCompany = A;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            object? body = null;
            if (path == "/api/auth/select-company")
            {
                var selection = await request.Content!.ReadFromJsonAsync<SelectCompanyRequest>(cancellationToken);
                activeCompany = selection!.CompanyId;
                body = new CompanySelectionViewModel { CompanyId = activeCompany };
            }
            else if (path == "/api/auth/me")
                body = new CurrentUserContextViewModel
                {
                    User = new() { Id = A, UiCulture = "en-GB" },
                    ActiveCompany = new() { CompanyId = activeCompany, CompanyName = activeCompany == A ? "Company A" : "Company B", Status = "active" },
                    Memberships = [
                        new() { CompanyId = A, CompanyName = "Company A", Status = "active", MembershipRole = "owner" },
                        new() { CompanyId = B, CompanyName = "Company B", Status = "active", MembershipRole = "employee" },
                        new() { CompanyId = Inactive, CompanyName = "Inactive", Status = "revoked", MembershipRole = "owner" }]
                };
            else if (path == "/api/onboarding/progress") body = new OnboardingProgressViewModel { Status = "completed", IsCompleted = true };
            else if (path.EndsWith("/access")) body = new CompanyAccessViewModel { CompanyId = activeCompany, Status = "active", MembershipRole = "owner" };
            else if (path.EndsWith("/dashboard-entry")) body = new CompanyDashboardEntryViewModel { CompanyId = activeCompany };
            else if (path == "/api/platform/finance-integration-applications") return new(HttpStatusCode.Forbidden);
            else if (path.EndsWith("/agents")) body = Array.Empty<CompanyAgentSummaryViewModel>();
            return new(body is null ? HttpStatusCode.NotFound : HttpStatusCode.OK) { Content = JsonContent.Create(body) };
        }
    }
}
