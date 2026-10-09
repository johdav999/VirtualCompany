using System.Net;
using System.Net.Http.Json;
using System.Web;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Pages.Marketing;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class MarketingOperationalJourneyTests
{
    private static readonly Guid Company = Guid.NewGuid(), Campaign = Guid.NewGuid();
    [Fact]
    public async Task Report_client_preserves_filters_company_and_correlation_and_rejects_empty_context()
    {
        var calls = 0;
        var client = new MarketingOperationalApiClient(new CompanyApiTransport(new HttpClient(new Handler(request => {
            calls++; Assert.Equal(Company.ToString(), request.Headers.GetValues("X-Company-Id").Single()); Assert.True(request.Headers.Contains("X-Correlation-Id"));
            var query = HttpUtility.ParseQueryString(request.RequestUri!.Query); Assert.Equal("SEK", query["currency"]); Assert.Equal(Campaign.ToString(), query["campaignId"]);
            return Json(Report());
        })) { BaseAddress = new("http://localhost/") }), false);
        await client.ReportAsync(Company, Report().Filter);
        await Assert.ThrowsAsync<ArgumentException>(() => client.ReportAsync(Guid.Empty, Report().Filter)); Assert.Equal(1, calls);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Interrupted_or_malformed_reads_are_recoverable(bool timeout)
    {
        var client = new MarketingOperationalApiClient(new CompanyApiTransport(new HttpClient(new Handler(_ =>
            timeout ? throw new TaskCanceledException() : new(HttpStatusCode.OK) { Content = new StringContent("{invalid") })) { BaseAddress = new("http://localhost/") }), false);
        var error = await Assert.ThrowsAsync<MarketingApiException>(() => client.ReportAsync(Company, Report().Filter));
        Assert.Contains("Retry", error.Message);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Download_refreshes_scope_and_current_rows_before_JS_handoff(bool revoke)
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices(); var reads = 0; var restricted = false;
        context.Services.AddSingleton(new MarketingOperationalApiClient(new CompanyApiTransport(new HttpClient(new Handler(_ => {
            reads++; return restricted ? new(HttpStatusCode.Forbidden) : Json(Report() with { Campaigns = [Report().Campaigns.Single() with { Name = "Current " + reads }] });
        })) { BaseAddress = new("http://localhost/") }), false));
        var module = context.JSInterop.SetupModule("./js/reportDownload.js"); module.Mode = JSRuntimeMode.Loose;
        context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/marketing/reports/spend?companyId={Company}");
        var cut = context.RenderComponent<MarketingOperationalReport>(p => p.Add(x => x.Kind, "spend"));
        cut.WaitForAssertion(() => Assert.Contains("Current 1", cut.Markup));
        cut.FindAll("button").Single(x => x.TextContent == "Refresh and prepare CSV").Click();
        cut.WaitForAssertion(() => Assert.Contains("Current 2", cut.Markup)); restricted = revoke;
        cut.FindAll("button").Single(x => x.TextContent == "Download current CSV").Click();
        cut.WaitForAssertion(() => Assert.Equal(3, reads));
        if (revoke) { cut.WaitForAssertion(() => Assert.Contains("restricted", cut.Markup)); Assert.DoesNotContain("Current", cut.Markup); Assert.Empty(module.Invocations); }
        else { cut.WaitForAssertion(() => Assert.Single(module.Invocations["downloadReport"])); var csv = (string)module.Invocations["downloadReport"].Single().Arguments[1]!; Assert.Contains("Current 3", csv); Assert.DoesNotContain("Current 2", csv); }
    }
    [Fact]
    public void CSV_has_included_rows_unknowns_source_dates_and_formula_escaping()
    {
        var csv = MarketingReportCsv.Create(Report(), "spend"); Assert.Contains("\"120\"", csv); Assert.Contains("\"' =SUM(1,2)\"", csv);
        Assert.Contains("marketing-operational-v1", csv); Assert.Contains("2026-10-01T12:00:00.0000000Z", csv); Assert.Contains("Unknown spend coverage", csv);
        Assert.Equal(5, csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
    }
    [Fact]
    public void Navigation_preserves_month_and_selected_records_and_rejects_foreign_returns()
    {
        var origin = DashboardRoutes.BuildMonthlyPath(Company, "marketing", 2026, 9); var brief = Guid.NewGuid(); var version = Guid.NewGuid(); var asset = Guid.NewGuid();
        var path = MarketingJourneyRoutes.Review(Company, Campaign, brief, origin, version, asset); var query = HttpUtility.ParseQueryString(new Uri("http://local.test" + path).Query);
        Assert.Equal(origin, query["returnUrl"]); Assert.Equal(brief.ToString(), query["briefId"]); Assert.Equal(version.ToString(), query["variantId"]); Assert.Equal(asset.ToString(), query["assetId"]);
        Assert.Equal(path, MarketingJourneyRoutes.NormalizeRecordReturn(path, Company)); Assert.Null(MarketingJourneyRoutes.NormalizeRecordReturn(path, Guid.NewGuid()));
        Assert.Null(MarketingJourneyRoutes.NormalizeRecordReturn("//outside.test/", Company));
    }

    [Fact]
    public void Campaign_report_retains_campaign_evidence_and_exact_record_return()
    {
        var origin = DashboardRoutes.BuildMonthlyPath(Company, "marketing", 2026, 9);
        var health = DashboardRoutes.BuildHealthPath(Company, "marketing", origin);
        var review = DashboardRoutes.WithQuery(MarketingJourneyRoutes.Review(Company, Campaign, null, origin),
            ("healthReturnUrl", health));
        var report = MarketingJourneyRoutes.Report(Company, "delivery", origin, Campaign, "http://localhost" + review);
        var query = HttpUtility.ParseQueryString(new Uri("http://localhost" + report).Query);
        Assert.Equal(Campaign.ToString(), query["campaignId"]);
        Assert.Equal(review, query["recordReturnUrl"]);
        Assert.Equal(origin, query["returnUrl"]);
        Assert.Null(query["priorityReturnUrl"]);
        Assert.Equal(health, query["healthReturnUrl"]);
        var spend = MarketingJourneyRoutes.Build($"/marketing/reports/spend?campaignId={Campaign}&currency=SEK", Company, "http://localhost" + report);
        var spendQuery = HttpUtility.ParseQueryString(new Uri("http://localhost" + spend).Query);
        Assert.Equal(review, spendQuery["recordReturnUrl"]);
        Assert.Null(spendQuery["priorityReturnUrl"]);
        Assert.Equal("SEK", spendQuery["currency"]);
    }

    [Fact]
    public void Report_tabs_and_drilldown_preserve_return_after_reload()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var origin = DashboardRoutes.BuildMonthlyPath(Company, "marketing", 2026, 9);
        var review = MarketingJourneyRoutes.Review(Company, Campaign, null, origin);
        var report = MarketingJourneyRoutes.Report(Company, "spend", origin, Campaign, "http://localhost" + review);
        context.Services.AddSingleton(new MarketingOperationalApiClient(new CompanyApiTransport(new HttpClient(new Handler(_ => Json(Report()))) { BaseAddress = new("http://localhost/") }), false));
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(report);
        var cut = context.RenderComponent<MarketingOperationalReport>(p => p.Add(x => x.Kind, "spend"));
        cut.WaitForAssertion(() => Assert.Contains("Known spend", cut.Markup));
        Assert.Equal(review, cut.FindAll("a").Single(x => x.TextContent == "Back to previous view").GetAttribute("href"));
        var tab = cut.FindAll("a").Single(x => x.TextContent == "Available lead results").GetAttribute("href")!;
        Assert.Equal(review, HttpUtility.ParseQueryString(new Uri("http://localhost" + tab).Query)["recordReturnUrl"]);
        var drilldown = cut.FindAll("a").Single(x => x.TextContent.Contains("=SUM")).GetAttribute("href")!;
        Assert.Equal(report, HttpUtility.ParseQueryString(new Uri("http://localhost" + drilldown).Query)["recordReturnUrl"]);
    }

    [Fact]
    public void Report_navigation_drops_foreign_evidence_and_bounds_repeated_round_trips()
    {
        var foreign = Guid.NewGuid();
        var path = DashboardRoutes.WithQuery(MarketingJourneyRoutes.Review(Company, Campaign, null, null),
            ("recordReturnUrl", MarketingJourneyRoutes.Review(foreign, Campaign, null, null)),
            ("priorityReturnUrl", $"/dashboard/priorities?companyId={foreign}&lens=marketing&key=risk"),
            ("healthReturnUrl", DashboardRoutes.BuildHealthPath(foreign)));
        var cleared = MarketingJourneyRoutes.Build("/marketing/reports/spend", Company, "http://localhost" + path);
        var query = HttpUtility.ParseQueryString(new Uri("http://localhost" + cleared).Query);
        Assert.Null(query["recordReturnUrl"]); Assert.Null(query["priorityReturnUrl"]); Assert.Null(query["healthReturnUrl"]);
        for (var i = 0; i < 12; i++)
            path = MarketingJourneyRoutes.Build(i % 2 == 0 ? "/marketing/reports/spend" : "/marketing/review", Company, "http://localhost" + path, true);
        var depth = 0;
        while ((path = HttpUtility.ParseQueryString(new Uri("http://localhost" + path).Query)["recordReturnUrl"]!) is not null) depth++;
        Assert.InRange(depth, 1, 3);
    }
    private static MarketingOperationalReportViewModel Report() => new(Company, new(2026,10,1,12,0,0,DateTimeKind.Utc),
        new(new(2026,9,1,0,0,0,DateTimeKind.Utc), new(2026,10,2,0,0,0,DateTimeKind.Utc), Campaign, "SEK"), "marketing-operational-v1",
        [new(Campaign, " =SUM(1,2)", "planning", "b2b", null, DateTime.UtcNow, 100, "SEK", 120, 1, null, 0, 0, ["Unknown spend coverage"])], [], [], [], ["Delivery meaning", "Known immutable costs", "No causal attribution"]);
    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> action) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(action(request)); }
}
