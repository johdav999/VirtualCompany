using System.Net;
using System.Net.Http.Json;
using System.Web;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Components.Dashboard;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class WeeklyWorkspaceJourneyTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly DateOnly Week = new(2026, 9, 28);
    [Theory]
    [InlineData("company")][InlineData("sales")][InlineData("marketing")][InlineData("finance")][InlineData("customers")]
    public void Each_role_renders_weekly_meaning_sources_and_exact_return(string lens)
    {
        using var ctx = new TestContext().AddVirtualCompanyWebPresentationServices();
        var w = Workspace(lens);
        var cut = ctx.RenderComponent<WeeklyWorkspace>(p => p.Add(x => x.CompanyId, Company).Add(x => x.Workspace, w));
        Assert.Contains("Week to date", cut.Markup); Assert.Contains("Europe/Stockholm", cut.Markup);
        Assert.Contains("Incomplete history", cut.Markup); Assert.Contains("Current risk", cut.Markup);
        Assert.Equal(6, cut.FindAll("[data-testid=workspace-period-picker] button").Count);
        Assert.Contains("Multi-year",cut.Find("[data-testid=workspace-period-picker]").TextContent);
        var detail = cut.Find(".weekly-metric").GetAttribute("href");
        Assert.Contains("week=2026-09-28", detail); Assert.Contains("lens=" + lens, detail);
        var href = cut.Find(".weekly-commitments a").GetAttribute("href")!;
        var query = HttpUtility.ParseQueryString(new Uri("https://test.local" + href).Query);
        Assert.Equal(DashboardRoutes.BuildWeeklyPath(Company, lens, Week), query["returnUrl"]);
        Assert.Equal(Company.ToString("D"), query["companyId"]);
    }
    [Fact]
    public void Source_details_page_and_prior_sources_preserve_company_role_week_and_metric()
    {
        using var ctx = new TestContext().AddVirtualCompanyWebPresentationServices(); var w = Workspace("sales");
        var metric = w.Contributions[0].Metrics[0] with { Sources = Enumerable.Range(1, 30).Select(i => new WeeklyWorkspaceSourceViewModel(i.ToString(), "Source " + i,
            w.GeneratedAtUtc.AddMinutes(-i), $"/app/sales/deals/{Guid.NewGuid()}?companyId={Company}")).ToList(), ComparisonSources = [new("prior", "Prior source", w.GeneratedAtUtc.AddDays(-7), "/app/sales/activities")] };
        w = w with { Contributions = [w.Contributions[0] with { Metrics = [metric] }] };
        var cut = ctx.RenderComponent<WeeklyWorkspace>(p => p.Add(x => x.CompanyId, Company).Add(x => x.Workspace, w).Add(x => x.Metric, metric.Key).Add(x => x.SourcePage, 2));
        Assert.Equal(6, cut.FindAll("tbody tr").Count); Assert.Contains("30 included source rows", cut.Markup);
        var record = cut.Find("tbody a").GetAttribute("href")!;
        var origin = HttpUtility.ParseQueryString(new Uri("https://test.local" + record).Query)["returnUrl"]!;
        Assert.Contains("sourcePage=2", origin); Assert.Contains("metric=sales.change", origin); Assert.Contains("week=2026-09-28", origin);
        cut.SetParametersAndRender(p => p.Add(x => x.Prior, true)); Assert.Single(cut.FindAll("tbody tr")); Assert.Contains("Prior source", cut.Markup);
    }
    [Fact]
    public void Missing_sample_and_unauthorized_metric_are_explicit_and_foreign_workspace_is_cleared()
    {
        using var ctx = new TestContext().AddVirtualCompanyWebPresentationServices();
        var cut = ctx.RenderComponent<WeeklyWorkspace>(p => p.Add(x => x.CompanyId, Company).Add(x => x.Workspace, Workspace("sales")).Add(x => x.Metric, "finance.cash"));
        Assert.NotEmpty(cut.FindAll("[data-testid=weekly-detail-unavailable]")); Assert.Empty(cut.FindAll("tbody"));
        cut.SetParametersAndRender(p => p.Add(x => x.CompanyId, Guid.NewGuid())); Assert.Single(cut.FindAll("[data-testid=weekly-empty]")); Assert.DoesNotContain("Current risk", cut.Markup);
    }
    [Theory]
    [InlineData("loading")][InlineData("restricted")][InlineData("error")][InlineData("empty")]
    public void Recovery_states_do_not_keep_old_records(string state)
    {
        using var ctx = new TestContext().AddVirtualCompanyWebPresentationServices();
        var cut = ctx.RenderComponent<WeeklyWorkspace>(p => p.Add(x => x.CompanyId, Company)
            .Add(x => x.IsLoading, state == "loading").Add(x => x.IsUnauthorized, state == "restricted")
            .Add(x => x.ErrorMessage, state == "error" ? "Offline; retry" : null));
        Assert.Single(cut.FindAll("[data-testid=weekly-" + state + "]")); Assert.Empty(cut.FindAll(".weekly-metric"));
    }
    [Fact]
    public void Missing_prior_balance_is_unavailable_and_due_schedule_uses_the_full_week()
    {
        using var ctx = new TestContext().AddVirtualCompanyWebPresentationServices(); var w = Workspace("finance");
        var metric = w.Contributions[0].Metrics[0] with { Kind = "balance", ComparisonValue = null, ComparisonSources = [] };
        w = w with { Contributions = [w.Contributions[0] with { Metrics = [metric] }] };
        var cut = ctx.RenderComponent<WeeklyWorkspace>(p => p.Add(x => x.CompanyId, Company).Add(x => x.Workspace, w).Add(x => x.Metric, metric.Key).Add(x => x.Prior, true));
        Assert.Contains("Source history is unavailable", cut.Find("[data-testid=weekly-sources-empty]").TextContent);
        Assert.Contains("Selected balance cutoff", cut.Markup);
        w = w with { Contributions = [w.Contributions[0] with { Metrics = [metric with { Kind = "current_due_schedule" }] }] };
        cut.SetParametersAndRender(p => p.Add(x => x.Workspace, w));
        Assert.Contains("Due-date window", cut.Markup); Assert.Contains("historical schedule comparison unavailable", cut.Markup);
        w = w with { Contributions = [w.Contributions[0] with { Metrics = [metric with { Kind = "balance", Value = null }] }] };
        cut.SetParametersAndRender(p => p.Add(x => x.Workspace, w));
        Assert.Contains("Balance history is unavailable", cut.Markup); Assert.DoesNotContain("Selected balance cutoff", cut.Markup);
    }
    [Fact]
    public void Period_role_and_week_controls_emit_intended_context()
    {
        using var ctx = new TestContext().AddVirtualCompanyWebPresentationServices(); string? period = null; string? lens = null; DateOnly? week = null;
        var cut = ctx.RenderComponent<WeeklyWorkspace>(p => p.Add(x => x.CompanyId, Company).Add(x => x.Workspace, Workspace("sales"))
            .Add(x => x.PeriodChanged, EventCallback.Factory.Create<string>(this, x => period = x))
            .Add(x => x.LensChanged, EventCallback.Factory.Create<string>(this, x => lens = x))
            .Add(x => x.WeekChanged, EventCallback.Factory.Create<DateOnly?>(this, x => week = x)));
        cut.FindAll("[data-testid=workspace-period-picker] button")[2].Click(); Assert.Equal("month", period);
        cut.Find(".weekly-lenses button").Click(); Assert.Equal("sales", lens);
        cut.Find(".weekly-period-nav button").Click(); Assert.Equal(Week.AddDays(-7), week);
    }
    [Fact]
    public async Task Typed_client_enforces_company_lens_offline_and_wire_scope()
    {
        var handler = new Handler(Workspace("sales")); using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local") };
        var client = new WeeklyWorkspaceApiClient(new CompanyApiTransport(http), false);
        var result = await client.GetAsync(Company, "sales", Week); Assert.Equal(Company, result!.CompanyId);
        Assert.Contains("week=2026-09-28", handler.Uri); Assert.Equal(Company.ToString("D"), handler.CompanyHeader);
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetAsync(Guid.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetAsync(Company, "unknown"));
        handler.Workspace = Workspace("sales") with { CompanyId = Guid.NewGuid() };
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(Company, "sales", Week));
        handler.Workspace = Workspace("sales") with { Contributions = [Workspace("finance").Contributions[0]] };
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(Company, "sales", Week));
        handler.Workspace = Workspace("sales");
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(Company, "sales", Week.AddDays(-7)));
        Assert.Null(await new WeeklyWorkspaceApiClient(new CompanyApiTransport(http), true).GetAsync(Company));
        handler.Status = HttpStatusCode.Forbidden; await Assert.ThrowsAsync<TodayWorkspaceAccessException>(() => client.GetAsync(Company));
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public void Campaign_source_opens_the_exact_scoped_record_or_explicitly_refuses_an_unknown_id(bool unknown)
    {
        using var ctx = new TestContext().AddVirtualCompanyWebPresentationServices();
        var campaign = Guid.NewGuid(); var requested = unknown ? Guid.NewGuid() : campaign;
        var handler = new CampaignHandler(campaign); var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        ctx.Services.AddSingleton(new OnboardingApiClient(http)); ctx.Services.AddSingleton(new SalesApiClient(http));
        ctx.Services.AddSingleton(new AgentWorkApiClient(new CompanyApiTransport(http), true));
        ctx.Services.AddSingleton(sp => new SalesPresentationPresetApiClient(new CompanyApiTransport(http), false, sp.GetRequiredService<IApiProblemMessageResolver>()));
        ctx.ComponentFactories.AddStub<VirtualCompany.Web.Components.Work.BusinessAgentWorkPanel>();
        ctx.ComponentFactories.AddStub<VirtualCompany.Web.Components.Sales.SalesAgentPanel>();
        var origin = DashboardRoutes.WithQuery(DashboardRoutes.BuildWeeklyPath(Company, "marketing", Week), ("metric", "marketing.launches"));
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo(DashboardRoutes.EnsureWorkspaceContext($"/app/sales/campaigns?campaignId={requested:D}", Company, origin));
        var cut = ctx.RenderComponent<VirtualCompany.Web.Pages.Sales.SalesCampaigns>();
        if (unknown)
        {
            cut.WaitForAssertion(() => Assert.Contains("unavailable in the selected company", cut.Markup)); Assert.Equal(0, handler.DetailReads);
        }
        else
        {
            cut.WaitForAssertion(() => cut.Find(".sales-campaign-detail")); Assert.Equal(1, handler.DetailReads);
            Assert.Contains("Selected weekly campaign", cut.Markup);
            Assert.Contains(cut.FindAll(".vc-section-tabs a"), x => x.GetAttribute("href") == origin);
        }
    }
    public static WeeklyWorkspaceViewModel Workspace(string lens)
    {
        var now = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        return new(Company, "Weekly company", lens, [new(lens, lens, true, "Assigned responsibility")],
            new(Week, Week.AddDays(6), "Europe/Stockholm", DayOfWeek.Monday, now.AddDays(-3), now.AddDays(4), now,
                now.AddDays(-10), now.AddDays(-7), true, "Sep 28–Oct 4, 2026", "same elapsed interval"),
            [new(lens, "Weekly " + lens, [new(lens + ".change", "Recorded change", 1, "1", 0, "0", "activity", "Recorded events in the selected interval", "Sparse history",
                [new("source", "Recorded source", now.AddHours(-1), "/app/sales/activities")], false)],
                [new("risk", "Current risk", "Open current work", now.AddDays(1), now, "/work?tab=tasks", "Accountable owner", "Recorded agent")], ["History gap"])],
            now, true, [], "Company-local calendar week, independent of fiscal year.");
    }
    private sealed class Handler(WeeklyWorkspaceViewModel workspace) : HttpMessageHandler
    {
        public WeeklyWorkspaceViewModel Workspace = workspace;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string? Uri; public string? CompanyHeader;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Uri = request.RequestUri!.ToString(); CompanyHeader = request.Headers.GetValues("X-Company-Id").Single(); return Task.FromResult(new HttpResponseMessage(Status) { Content = JsonContent.Create(Workspace) }); }
    }
    private sealed class CampaignHandler(Guid campaign) : HttpMessageHandler
    {
        public int DetailReads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath; object? body;
            if (path == "/api/auth/me") body = new CurrentUserContextViewModel { ActiveCompany = new() { CompanyId = Company, CompanyName = "Weekly company", Status = "active" }, Memberships = [new() { CompanyId = Company, Status = "active", MembershipRole = "owner" }] };
            else if (path == "/api/sales/campaigns") body = new[] { new OutboundCampaignSummaryResponse(campaign, "Selected weekly campaign", "draft", 0, 0, 0, 0, DateTime.UtcNow) };
            else if (path == $"/api/sales/campaigns/{campaign:D}")
            {
                DetailReads++; body = new OutboundCampaignDetailResponse(campaign, "Selected weekly campaign", "Recorded source detail", "draft", "existing_contacts", new(false, 0, true), [], [], [], DateTime.UtcNow, DateTime.UtcNow);
            }
            else if (path.EndsWith("/audience-options")) body = new OutboundAudienceOptionsResponse([], []);
            else if (path == "/api/sales/dashboard") body = new { };
            else if (path.EndsWith("/initiative") || path.EndsWith("/readiness") || path.EndsWith("/performance")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            else body = Array.Empty<object>();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });
        }
    }
}
