using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Components.Sales;
using VirtualCompany.Web.Pages.Sales;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class SalesOperationalJourneyTests
{
    private static readonly Guid Company = Guid.NewGuid(), Deal = Guid.NewGuid();

    [Fact]
    public void Inner_navigation_survives_reload_and_preserves_exact_month_filters_and_company()
    {
        var overview = DashboardRoutes.BuildMonthlyPath(Company, "sales", 2026, 9);
        var report = $"/app/sales/forecast?companyId={Company}&days=60&currency=SEK&returnUrl={Uri.EscapeDataString(overview)}";
        var detail = SalesJourneyRoutes.Build($"/app/sales/deals/{Deal}", Company, "https://example.test" + report);
        var contact = SalesJourneyRoutes.Build("/app/sales/contacts/" + Guid.NewGuid(), Company, "https://example.test" + detail);
        Assert.Equal(detail, SalesJourneyRoutes.Back("https://example.test" + contact, Company));
        Assert.Equal(report, SalesJourneyRoutes.Back("https://example.test" + detail, Company));
        Assert.Equal(overview, HttpUtility.ParseQueryString(new Uri("https://example.test" + contact).Query)["returnUrl"]);
        Assert.Null(SalesJourneyRoutes.Normalize(report, Guid.NewGuid()));
        Assert.Null(SalesJourneyRoutes.Normalize("//outside.test/?companyId=" + Company, Company));
    }

    [Fact]
    public void Meeting_preset_and_closing_approval_links_keep_the_exact_record_return()
    {
        var origin = $"/app/sales/meeting-invitations/{Guid.NewGuid()}/prepare?companyId={Company}&returnUrl={Uri.EscapeDataString(DashboardRoutes.BuildTodayPath(Company, "sales"))}";
        var library = SalesJourneyRoutes.Build("/app/sales/presentation-presets", Company, "https://example.test" + origin);
        Assert.Equal(origin, SalesJourneyRoutes.Back("https://example.test" + library, Company));
        var closing = $"/app/sales/rooms/{Guid.NewGuid()}/review?companyId={Company}";
        var work = SalesJourneyRoutes.Build("/work?tab=approvals&itemId=" + Guid.NewGuid(), Company, "https://example.test" + closing);
        Assert.Equal(closing, HttpUtility.ParseQueryString(new Uri("https://example.test" + work).Query)["recordReturnUrl"]);
    }

    [Fact]
    public async Task Typed_report_client_carries_context_filters_correlation_and_never_sends_empty_company()
    {
        var requests = 0;
        var client = new SalesOperationalApiClient(new CompanyApiTransport(new HttpClient(new Handler(request => {
            requests++; Assert.Equal(Company.ToString(), request.Headers.GetValues("X-Company-Id").Single());
            Assert.True(request.Headers.Contains("X-Correlation-Id"));
            Assert.Equal("90", HttpUtility.ParseQueryString(request.RequestUri!.Query)["days"]);
            Assert.Equal("SEK", HttpUtility.ParseQueryString(request.RequestUri.Query)["currency"]);
            return Json(Report());
        })) { BaseAddress = new("https://example.test/") }), false);
        var result = await client.OpportunitiesAsync(Company, true, 90, "SEK", null);
        Assert.Equal(4050, result.Totals.Single().ExpectedAmount);
        await Assert.ThrowsAsync<ArgumentException>(() => client.OpportunitiesAsync(Guid.Empty, true, 90, null, null));
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task Uncertain_write_and_stale_review_are_explicit_and_csv_matches_exact_included_rows()
    {
        var failing = new SalesOperationalApiClient(new CompanyApiTransport(new HttpClient(new Handler(_ => throw new HttpRequestException())) { BaseAddress = new("https://example.test/") }), false);
        var uncertain = await Assert.ThrowsAsync<SalesOperationalApiException>(() => failing.RecordAsync(Company, Deal, Guid.NewGuid(), "Next step", DateTime.UtcNow));
        Assert.True(uncertain.IsUncertain);
        var conflict = new SalesOperationalApiClient(new CompanyApiTransport(new HttpClient(new Handler(_ => new(HttpStatusCode.Conflict))) { BaseAddress = new("https://example.test/") }), false);
        Assert.Equal(HttpStatusCode.Conflict, (await Assert.ThrowsAsync<SalesOperationalApiException>(() => conflict.ReviewAsync(Company, Commitment()))).Status);
        var report = Report(); var csv = SalesOperationalCsv.Export(report);
        Assert.Contains("\"12000\",\"4050\"", csv); Assert.Contains("\"' =SUM(1,2)\"", csv);
        Assert.Contains("stage-risk-v1", csv); Assert.Contains("2026-10-01T12:00:00.0000000Z", csv);
        Assert.Equal(report.Rows.Count + 3, csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void Commitment_component_blocks_blind_retry_then_reloads_persisted_history()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices(); var writes = 0;
        context.Services.AddSingleton(new SalesOperationalApiClient(new CompanyApiTransport(new HttpClient(new Handler(request => {
            if (request.Method == HttpMethod.Post) { writes++; throw new HttpRequestException(); }
            return Json(new SalesActivityReportViewModel(Company, DateTime.UtcNow, "all", Deal, [], []));
        })) { BaseAddress = new("https://example.test/") }), false));
        var cut = context.RenderComponent<SalesCommitmentPanel>(p => p.Add(x => x.CompanyId, Company).Add(x => x.DealId, Deal));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("textarea")));
        cut.Find("textarea").Input("Review proposal"); cut.Find("input").Input("2026-10-02T13:00");
        cut.FindAll("button")[0].Click();
        cut.WaitForAssertion(() => Assert.Contains("result is uncertain", cut.Markup));
        Assert.True(cut.FindAll("button")[0].HasAttribute("disabled")); Assert.Equal(1, writes);
        cut.FindAll("button").Single(x => x.TextContent.Contains("Reload activity history")).Click();
        cut.WaitForAssertion(() => Assert.False(cut.FindAll("button")[0].HasAttribute("disabled")));
    }

    [Fact]
    public void Revoked_activity_access_clears_record_rows()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices(); var restricted = false;
        context.Services.AddSingleton(new SalesOperationalApiClient(new CompanyApiTransport(new HttpClient(new Handler(_ => restricted ? new(HttpStatusCode.Forbidden) :
            Json(new SalesActivityReportViewModel(Company, DateTime.UtcNow, "all", Deal, [Commitment()], [])))) { BaseAddress = new("https://example.test/") }), false));
        var cut = context.RenderComponent<SalesCommitmentPanel>(p => p.Add(x => x.CompanyId, Company).Add(x => x.DealId, Deal));
        cut.WaitForAssertion(() => Assert.Contains("Private next step", cut.Markup)); restricted = true;
        cut.FindAll("button").Single(x => x.TextContent.Contains("Reload activity history")).Click();
        cut.WaitForAssertion(() => { Assert.DoesNotContain("Private next step", cut.Markup); Assert.Contains("restricted", cut.Markup); });
    }

    private static SalesCommitmentViewModel Commitment() => new(Guid.NewGuid(), Deal, "Renewal", "Private next step", "pending", DateTime.UtcNow, DateTime.UtcNow);
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Download_refreshes_authorization_and_passes_only_current_rows_to_browser(bool revoke)
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var reads = 0; var restricted = false;
        var client = new HttpClient { BaseAddress = new("http://localhost/") };
        context.Services.AddSingleton(new OnboardingApiClient(client, useOfflineMode: true));
        context.Services.AddSingleton(new SalesApiClient(new HttpClient(new Handler(_ => Json(new SalesPipelineResponse([])))) { BaseAddress = new("http://localhost/") }));
        context.Services.AddSingleton(new AgentApiClient(client, useOfflineMode: true));
        context.Services.AddSingleton(new SalesOperationalApiClient(new CompanyApiTransport(new HttpClient(new Handler(_ => {
            reads++;
            return restricted ? new(HttpStatusCode.Forbidden) : Json(Report() with {
                Rows = [Report().Rows.Single() with { Title = "Current rows " + reads }]
            });
        })) { BaseAddress = new("http://localhost/") }), false));
        var module = context.JSInterop.SetupModule("/js/reportDownload.js");
        module.Mode = JSRuntimeMode.Loose;
        context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/app/sales/forecast?companyId={Company}");
        var cut = context.RenderComponent<SalesOperationalReport>();
        cut.WaitForAssertion(() => Assert.Contains("Current rows 1", cut.Markup));
        cut.FindAll("button").Single(x => x.TextContent.Contains("Refresh and prepare CSV")).Click();
        cut.WaitForAssertion(() => Assert.Contains("Current rows 2", cut.Markup));
        restricted = revoke;
        cut.FindAll("button").Single(x => x.TextContent == "Export CSV").Click();
        cut.WaitForAssertion(() => Assert.Equal(3, reads));
        if (revoke)
        {
            cut.WaitForAssertion(() => Assert.Contains("restricted", cut.Markup));
            Assert.DoesNotContain("Current rows", cut.Markup);
            Assert.Empty(module.Invocations);
        }
        else
        {
            cut.WaitForAssertion(() => Assert.Single(module.Invocations["downloadReport"]));
            var invocation = module.Invocations["downloadReport"].Single();
            Assert.Equal("sales-operational-report.csv", invocation.Arguments[0]);
            Assert.Contains("Current rows 3", (string)invocation.Arguments[1]!);
            Assert.DoesNotContain("Current rows 2", (string)invocation.Arguments[1]!);
        }
    }
    private static SalesOpportunityReportViewModel Report() => new(Company, new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc), true, 30, "SEK", null, "stage-risk-v1",
        [new(Deal, " =SUM(1,2)", Guid.NewGuid(), "Qualified", "SEK", 12000, DateTime.UtcNow, DateTime.UtcNow, .45m, .5m, null, 4050)], [new("SEK", 1, 12000, 4050)], ["SEK"]);
    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> action) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(action(request)); }
}
