using System.Net;
using System.Net.Http.Json;
using Bunit;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Components.Sales;
using VirtualCompany.Web.Services;
namespace VirtualCompany.Web.Tests;
public sealed class SalesBrowserMeetingSchedulingTests
{
    [Fact]
    public void Meeting_type_is_separate_from_calendar_and_explains_browser_consent()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices(); string? chosen = null;
        var cut = context.RenderComponent<SalesMeetingTypePicker>(p => p.Add(x => x.BrowserReady, true).Add(x => x.Value, "browser").Add(x => x.CalendarMeetingLabel, "Microsoft Teams").Add(x => x.ValueChanged, value => chosen = value));
        Assert.Contains("audio processing requires consent", cut.Markup); Assert.Contains("Microsoft Teams", cut.Markup);
        var output = Environment.GetEnvironmentVariable("VC_BROWSER_UAT_DIRECTORY"); if (output != null) File.WriteAllText(Path.Combine(output, "picker-rendered.html"), cut.Markup);
        Assert.Equal(3, cut.FindAll("option").Count); cut.Find("select").Change("none"); Assert.Equal("none", chosen);
    }
    [Fact]
    public void Unavailable_browser_choice_keeps_the_calendar_route_selectable()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices(); var cut = context.RenderComponent<SalesMeetingTypePicker>(p => p.Add(x => x.BrowserReady, false).Add(x => x.UnavailableReason, "Browser connection is not ready."));
        Assert.True(cut.Find("option[value=browser]").HasAttribute("disabled")); Assert.False(cut.Find("option[value=calendar_online]").HasAttribute("disabled")); Assert.Contains("Browser connection is not ready", cut.Markup);
    }
    [Fact]
    public async Task Copy_link_uses_company_transport_and_redacts_diagnostics()
    {
        var company = Guid.NewGuid(); var invitation = Guid.NewGuid();
        var client = new SalesBrowserMeetingApiClient(new CompanyApiTransport(new HttpClient(new Handler(request =>
        {
            Assert.Equal(company.ToString("D"), Assert.Single(request.Headers.GetValues("X-Company-Id")));
            Assert.EndsWith($"/invitations/{invitation:D}/link", request.RequestUri!.AbsolutePath);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { url = "https://rooms.example.test/room#invite=secret" }) };
        }))
        { BaseAddress = new("https://api.example.test") }), false);
        var link = await client.LinkAsync(company, invitation); Assert.DoesNotContain("secret", link.ToString());
    }
    [Fact]
    public async Task Offline_and_empty_company_never_send_requests()
    {
        var client = new SalesBrowserMeetingApiClient(new CompanyApiTransport(new HttpClient(new Handler(_ => throw new Exception("Must not send"))) { BaseAddress = new("https://api.example.test") }), true);
        await Assert.ThrowsAsync<ArgumentException>(() => client.ReadinessAsync(Guid.Empty)); await Assert.ThrowsAsync<InvalidOperationException>(() => client.ReadinessAsync(Guid.NewGuid()));
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request)); }
}
