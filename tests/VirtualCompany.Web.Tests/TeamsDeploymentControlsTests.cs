using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using VirtualCompany.Api.Tests;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Web.Components.Sales;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class TeamsDeploymentControlsTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid Meeting = Guid.NewGuid();
    private static readonly Guid Agent = Guid.NewGuid();
    [Fact]
    public void Presenter_selection_sends_company_bound_version_and_refreshes_parent()
    {
        var saved = false; JsonElement body = default;
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        using var http = new HttpClient(new Handler(async request =>
        {
            Assert.Equal(Company.ToString(), request.Headers.GetValues("X-Company-Id").Single());
            Assert.Contains(Meeting.ToString(), request.RequestUri!.AbsolutePath);
            if (request.Method == HttpMethod.Put) body = await request.Content!.ReadFromJsonAsync<JsonElement>();
            return Json(new TeamsMeetingPresenterViewModel(request.Method == HttpMethod.Put ? Agent : null,
                request.Method == HttpMethod.Put ? 8 : 7, [new(Agent, "Maya", "Marketing")]));
        })) { BaseAddress = new("https://example.test/") };
        context.Services.AddSingleton(new TeamsCallControlApiClient(new CompanyApiTransport(http), false));
        var cut = context.RenderComponent<TeamsMeetingPresenterPicker>(p => p.Add(x => x.CompanyId, Company)
            .Add(x => x.SessionId, Meeting).Add(x => x.OnSaved, () => saved = true));
        cut.WaitForAssertion(() => Assert.Contains("Maya", cut.Markup));
        cut.Find("select").Change(Agent.ToString());
        cut.Find("button").Click();
        cut.WaitForAssertion(() => Assert.True(saved));
        Assert.Equal(Agent, body.GetProperty("agentId").GetGuid());
        Assert.Equal(7, body.GetProperty("expectedVersion").GetInt64());
        CaptureUi("presenter", cut.Markup);
    }
    [Fact]
    public void Empty_presenter_list_explains_permission_requirement()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        using var http = new HttpClient(new Handler(request => Task.FromResult(
            request.Method == HttpMethod.Put ? new HttpResponseMessage(HttpStatusCode.Conflict)
                { Content = JsonContent.Create(new { title = "End the active call before changing the presenter." }) } :
                Json(new TeamsMeetingPresenterViewModel(null, 1, [])))))
            { BaseAddress = new("https://example.test/") };
        context.Services.AddSingleton(new TeamsCallControlApiClient(new CompanyApiTransport(http), false));
        var cut = context.RenderComponent<TeamsMeetingPresenterPicker>(p => p.Add(x => x.CompanyId, Company).Add(x => x.SessionId, Meeting));
        cut.WaitForAssertion(() => Assert.Contains("No assistant has permitted meeting tools", cut.Markup));
        Assert.True(cut.Find("button").HasAttribute("disabled"));
    }
    [Fact]
    public void First_test_validates_scope_then_submits_bounded_grant_and_revoke_version()
    {
        JsonElement grant = default, revoke = default;
        var organizer = Guid.NewGuid();
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        using var http = new HttpClient(new Handler(async request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/revoke")) revoke = await request.Content!.ReadFromJsonAsync<JsonElement>();
                else grant = await request.Content!.ReadFromJsonAsync<JsonElement>();
                return Json(new FirstTeamsUatViewModel(organizer, Meeting, DateTime.UtcNow.AddMinutes(15), "Speech test", 5));
            }
            return Json(new FirstTeamsUatViewModel(null, null, null, null, 4));
        })) { BaseAddress = new("https://example.test/") };
        context.Services.AddSingleton(new TeamsPresenterAdminApiClient(http, false));
        var cut = context.RenderComponent<FirstTeamsUatControls>(p => p.Add(x => x.CompanyId, Company));
        cut.WaitForAssertion(() => Assert.False(cut.FindAll("button")[0].HasAttribute("disabled")));
        cut.FindAll("button")[0].Click();
        Assert.Contains("Enter valid organizer", cut.Markup);
        Assert.Equal(JsonValueKind.Undefined, grant.ValueKind);
        cut.Find("#uat-organizer").Change(organizer.ToString());
        cut.Find("#uat-meeting").Change(Meeting.ToString());
        cut.Find("#uat-minutes").Change("15");
        cut.Find("#uat-reason").Change("Speech test");
        cut.FindAll("button")[0].Click();
        cut.WaitForAssertion(() => Assert.Equal(JsonValueKind.Object, grant.ValueKind));
        Assert.Equal(organizer, grant.GetProperty("organizerUserId").GetGuid());
        Assert.Equal(Meeting, grant.GetProperty("meetingSessionId").GetGuid());
        Assert.Equal(15, grant.GetProperty("durationMinutes").GetInt32());
        Assert.Equal(4, grant.GetProperty("expectedVersion").GetInt64());
        cut.FindAll("button")[1].Click();
        cut.WaitForAssertion(() => Assert.Equal(5, revoke.GetProperty("expectedVersion").GetInt64()));
        Assert.Contains("Production approval stays unchanged", cut.Markup);
        CaptureUi("first-test", cut.Markup);
    }
    [Fact]
    public void Failed_admin_load_keeps_authorization_disabled_and_shows_error()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
            { Content = JsonContent.Create(new { title = "Platform administrator access is required." }) })))
            { BaseAddress = new("https://example.test/") };
        context.Services.AddSingleton(new TeamsPresenterAdminApiClient(http, false));
        var cut = context.RenderComponent<FirstTeamsUatControls>(p => p.Add(x => x.CompanyId, Company));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[role=alert]")));
        Assert.True(cut.FindAll("button")[0].HasAttribute("disabled"));
    }
    private static void CaptureUi(string name, string markup)
    {
        var directory = Environment.GetEnvironmentVariable("VC_TEAMS_UAT_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".html"),
            "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
            "<link rel=\"stylesheet\" href=\"https://localhost:5298/lib/bootstrap/dist/css/bootstrap.min.css\">" +
            "<link rel=\"stylesheet\" href=\"https://localhost:5298/css/app.css\">" +
            "<link rel=\"stylesheet\" href=\"https://localhost:5298/VirtualCompany.Web.styles.css\"></head>" +
            "<body style=\"padding:24px;background:#f4f7fb\"><main style=\"max-width:900px;margin:auto\">" + markup + "</main></body></html>");
    }
    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request);
    }
}
