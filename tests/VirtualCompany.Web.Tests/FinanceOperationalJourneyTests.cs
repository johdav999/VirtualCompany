using System.Net;
using System.Net.Http.Json;
using System.Web;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Pages.Finance;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class FinanceOperationalJourneyTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static FinanceOperationalReportResponse Report(int read = 1) => new(Company, new(2026, 10, 2), new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc), new(2026, 10, 16), "UTC", "finance-operational/1.0", "Current recorded balances", "Includes overdue balances; no FX conversion", "SEK", "overdue",
        [new(Guid.NewGuid(), "invoice", "Current " + read, " =SUM(1,2)", new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), 31, "past_due_31_60", 80, "SEK", "open", DateTime.UtcNow, "/finance/invoices/" + Guid.NewGuid())], [],
        [new("SEK", 80, 80, 0, 0, null, 80, 0, null)], [], ["Cash evidence unavailable"]);
    [Fact]
    public async Task Typed_client_preserves_filters_scope_and_correlation_and_rejects_empty_company()
    {
        var count = 0; var http = new HttpClient(new Handler(r => { count++; Assert.Equal(Company.ToString(), r.Headers.GetValues("X-Company-Id").Single()); Assert.True(r.Headers.Contains("X-Correlation-Id"));
            var q = HttpUtility.ParseQueryString(r.RequestUri!.Query); Assert.Equal("2026-10-02", q["asOfDate"]); Assert.Equal("14", q["horizonDays"]); Assert.Equal("SEK", q["currency"]); Assert.Equal("overdue", q["bucket"]); return Json(Report()); })) { BaseAddress = new("http://localhost/") };
        var client = new FinanceApiClient(new CompanyApiTransport(http)); Assert.NotNull(await client.GetOperationalReportAsync(Company, new(2026, 10, 2), 14, "SEK", "overdue"));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetOperationalReportAsync(Guid.Empty, new(2026, 10, 2))); Assert.Equal(1, count);
    }
    [Theory][InlineData(null, "fortnox")][InlineData("operational", "operational")]
    public async Task Report_and_record_reads_preserve_configured_or_explicit_operational_source(string? source, string expected)
    {
        var seen = new List<string?>();
        var http = new HttpClient(new Handler(r => { seen.Add(HttpUtility.ParseQueryString(r.RequestUri!.Query)["source"]); return Json(Report()); })) { BaseAddress = new("http://localhost/") };
        var client = new FinanceApiClient(new CompanyApiTransport(http), financeDataSourceFilter: "fortnox");
        await client.GetOperationalReportAsync(Company, new(2026, 10, 2), sourceFilter: source);
        await client.GetInvoiceDetailAsync(Company, Guid.NewGuid(), sourceFilter: source);
        Assert.Equal(new[] { expected, expected }, seen);
        var start = $"http://localhost/finance/invoices/{Guid.NewGuid()}?companyId={Company}&financeSource=operational";
        Assert.Equal("operational", HttpUtility.ParseQueryString(new Uri("http://localhost" + FinanceJourneyRoutes.Build("/finance/transactions", Company, start)).Query)["financeSource"]);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Export_rereads_authorized_scope_and_clears_records_on_revocation(bool revoke)
    {
        using var ctx = new TestContext().AddVirtualCompanyWebPresentationServices(); var reads = 0; var denied = false;
        var http = new HttpClient(new Handler(r => r.RequestUri!.AbsolutePath.Contains("operational-report")
            ? ++reads > 0 && denied ? new(HttpStatusCode.Forbidden) : Json(Report(reads))
            : Json(new CurrentUserContextViewModel { ActiveCompany = new() { CompanyId = Company, CompanyName = "Company", MembershipRole = "owner", Status = "active" },
                Memberships = [new() { CompanyId = Company, CompanyName = "Company", MembershipRole = "owner", Status = "active" }] }))) { BaseAddress = new("http://localhost/") };
        ctx.Services.AddSingleton(new FinanceApiClient(new CompanyApiTransport(http))); ctx.Services.AddSingleton(new OnboardingApiClient(http)); ctx.Services.AddSingleton<FinanceAccessResolver>();
        var module = ctx.JSInterop.SetupModule("./js/reportDownload.js"); module.Mode = JSRuntimeMode.Loose;
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo($"/finance/receivables-aging?companyId={Company}&currency=SEK&bucket=overdue");
        var cut = ctx.RenderComponent<FinanceOperationalReport>(p => p.Add(x => x.CompanyId, Company));
        cut.WaitForAssertion(() => Assert.Contains("Current 1", cut.Markup)); denied = revoke;
        cut.FindAll("button").Single(x => x.TextContent == "Refresh and download CSV").Click();
        cut.WaitForAssertion(() => Assert.Equal(2, reads));
        if (revoke) { cut.WaitForAssertion(() => Assert.Contains("unavailable", cut.Markup)); Assert.DoesNotContain("Current 1", cut.Markup); Assert.Empty(module.Invocations); }
        else { cut.WaitForAssertion(() => Assert.Single(module.Invocations["downloadReport"])); var csv = (string)module.Invocations["downloadReport"].Single().Arguments[1]!; Assert.Contains("Current 2", csv); Assert.DoesNotContain("Current 1", csv); Assert.Contains("' =SUM(1,2)", csv); }
    }
    [Fact]
    public void Durable_report_record_and_overview_returns_survive_reload_and_reject_foreign_or_malformed_origins()
    {
        var overview = DashboardRoutes.BuildMonthlyPath(Company, "finance", 2026, 9);
        var report = DashboardRoutes.EnsureWorkspaceContext($"/finance/receivables-aging?asOfDate=2026-10-02&currency=SEK&bucket=overdue", Company, overview);
        var invoice = FinanceJourneyRoutes.Build("/finance/invoices/" + Guid.NewGuid(), Company, "http://localhost" + report);
        Assert.Equal(report, FinanceJourneyRoutes.Back("http://localhost" + invoice, Company));
        var payment = FinanceJourneyRoutes.Build("/finance/payments?type=outgoing", Company, "http://localhost" + invoice);
        Assert.Equal(invoice, FinanceJourneyRoutes.Back("http://localhost" + payment, Company));
        Assert.Equal(overview, HttpUtility.ParseQueryString(new Uri("http://localhost" + payment).Query)["returnUrl"]);
        Assert.Null(FinanceJourneyRoutes.Normalize(report, Guid.NewGuid())); Assert.Null(FinanceJourneyRoutes.Normalize("//outside.test", Company));
        Assert.Null(FinanceJourneyRoutes.Normalize($"/finance/%2e%2e/work?companyId={Company}", Company));
    }
    [Fact]
    public void Forecast_csv_contains_same_horizon_rows_and_separate_currency_cash_sources()
    {
        var source = Report() with { Receivables = [Report().Receivables[0], Report().Receivables[0] with { Number = "Outside", DueUtc = new(2026, 10, 17) }] };
        var csv = FinanceOperationalCsv.Build(source, "forecast"); Assert.Contains("Expected inflows", csv); Assert.Contains("Current 1", csv); Assert.DoesNotContain("Outside", csv);
        Assert.Contains("2026-10-02T12:00:00.0000000Z", csv); Assert.Contains("Cash evidence unavailable", csv); Assert.Contains("no FX conversion", csv);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Interrupted_and_malformed_report_reads_offer_retry(bool timeout)
    {
        var http = new HttpClient(new Handler(_ => timeout ? throw new TaskCanceledException() : new(HttpStatusCode.OK) { Content = new StringContent("{invalid") })) { BaseAddress = new("http://localhost/") };
        var error = await Assert.ThrowsAsync<FinanceApiException>(() => new FinanceApiClient(http).GetOperationalReportAsync(Company, new(2026, 10, 2)));
        Assert.Contains("Retry", error.Message);
    }
    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> action) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(action(request)); }
}
