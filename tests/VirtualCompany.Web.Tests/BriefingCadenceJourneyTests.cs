using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Components;
using VirtualCompany.Web.Services;
namespace VirtualCompany.Web.Tests;
public sealed class BriefingCadenceJourneyTests
{
    private static readonly Guid Company = Guid.NewGuid(), User = Guid.NewGuid();
    private static BriefingCadenceContext Context => new(Company, User, true, new() { Role = "sales", Timezone = "Europe/Stockholm", WorkStart = new(8,0), WorkEnd = new(18,0), QuietStart = new(20,0), QuietEnd = new(7,0), Workdays = [1,2,3,4,5], FocusAreas = ["sales"], Schedules = new[] {"morning","end_of_day","shift_handover","weekly","monthly","quarterly","annual"}.Select(k => new BriefingCadenceRow { Kind = k, Enabled = k == "morning", LocalTime = new(8,30) }).ToArray() }, [], ["sales"], ["in_app"]);
    private static BriefingCadencePreview Preview => new(Company, User, User, "self", "Europe/Stockholm", DateTime.UtcNow, "2026-10-07 08:30 +02:00", DateTime.UtcNow, ["morning"], [], []);
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> act) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Task.FromResult(act(r)); }
    private sealed class AsyncHandler(Func<HttpRequestMessage,Task<HttpResponseMessage>> act) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => act(r); }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    private static TestContext Setup(Func<HttpRequestMessage,HttpResponseMessage> act) { var c = new TestContext().AddVirtualCompanyWebPresentationServices(); c.Services.AddSingleton(new BriefingCadenceApiClient(new CompanyApiTransport(new HttpClient(new Handler(act)) { BaseAddress = new("http://localhost/") }))); return c; }
    [Fact] public void All_cadences_save_and_preview_shows_next_local_time_and_truthful_empty_state()
    { var saved = false; using var c = Setup(r => { if (r.Method == HttpMethod.Put) saved = true; return r.RequestUri!.AbsolutePath.EndsWith("preview") ? Json(Preview) : Json(Context); }); var cut = c.RenderComponent<BriefingCadencePanel>(p => p.Add(x => x.CompanyId, Company));
        cut.WaitForAssertion(() => Assert.Contains("Quarterly", cut.Markup)); Assert.NotNull(cut.Find("select[aria-label='Role defaults']")); cut.FindAll("button").Single(x => x.TextContent == "Save briefing preferences").Click(); cut.WaitForAssertion(() => Assert.True(saved)); Assert.Contains("Schedules and routing are active", cut.Markup);
        cut.FindAll("button").Single(x => x.TextContent == "Preview").Click(); cut.WaitForAssertion(() => Assert.Contains("2026-10-07 08:30 +02:00", cut.Markup)); Assert.Contains("No authorized work items", cut.Markup); Assert.Contains("No scheduled delivery has run", cut.Markup); }
    [Fact] public void Save_failure_preserves_inputs_and_absence_shows_no_authority_grant()
    { using var c = Setup(r => r.Method == HttpMethod.Put ? new(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { detail = "Choose an eligible fallback" }) } : Json(Context)); var cut = c.RenderComponent<BriefingCadencePanel>(p => p.Add(x => x.CompanyId, Company)); cut.WaitForAssertion(() => Assert.Contains("Timezone", cut.Markup));
        cut.FindAll("button").Single(x => x.TextContent == "Save briefing preferences").Click(); cut.WaitForAssertion(() => Assert.Contains("Choose an eligible fallback", cut.Markup)); Assert.Contains("Europe/Stockholm", cut.Markup);
        cut.FindAll("button").Single(x => x.TextContent == "Absence routing").Click(); Assert.Contains("Routing grants no record access or approval authority", cut.Markup); Assert.NotNull(cut.Find("select[aria-label='Eligible delegate']")); Assert.Contains("No other eligible member", cut.Markup); }
    [Fact] public void Authorized_retained_source_renders_its_numeric_version_and_native_route()
    {
        var task = Guid.NewGuid(); var source = $"/work/source?companyId={Company:D}&taskId={task:D}";
        var data = Preview with { Items = [new(task, "Capacity commitment", "new", "sales", "normal", null, DateTime.UtcNow, $"/work?companyId={Company:D}&taskId={task:D}", source, "scenario", 1)] };
        using var c = Setup(r => r.RequestUri!.AbsolutePath.EndsWith("preview") ? Json(data) : Json(Context));
        var cut = c.RenderComponent<BriefingCadencePanel>(p => p.Add(x => x.CompanyId, Company));
        cut.WaitForAssertion(() => Assert.Contains("Timezone", cut.Markup)); cut.FindAll("button").Single(x => x.TextContent == "Preview").Click();
        cut.WaitForAssertion(() => Assert.Equal(source, cut.FindAll("a").Single(x => x.TextContent == "Retained scenario source · v1").GetAttribute("href")));
    }
    [Fact] public async Task Client_rejects_foreign_company_malformed_preview_and_forged_source_routes()
    { foreach (var invalid in new[] { Preview with { CompanyId = Guid.NewGuid() }, Preview with { Items = null! }, Preview with { Items = [new(Guid.NewGuid(), "Secret", "new", "sales", "normal", null, DateTime.UtcNow, "https://evil.test", null, null, null)] } }) { using var c = Setup(_ => Json(invalid)); await Assert.ThrowsAsync<InvalidDataException>(() => c.Services.GetRequiredService<BriefingCadenceApiClient>().Preview(Company, default)); } }
    [Theory][InlineData(403)][InlineData(404)][InlineData(500)] public void Failed_load_has_visible_error_without_preview_content(int status)
    { using var c = Setup(_ => new((HttpStatusCode)status)); var cut = c.RenderComponent<BriefingCadencePanel>(p => p.Add(x => x.CompanyId, Company)); cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[role=alert]"))); Assert.DoesNotContain("Included work items", cut.Markup); }
    [Fact] public async Task Late_previous_company_response_cannot_restore_settings_or_preview()
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>(); var calls = 0; using var c = new TestContext().AddVirtualCompanyWebPresentationServices();
        c.Services.AddSingleton(new BriefingCadenceApiClient(new CompanyApiTransport(new HttpClient(new AsyncHandler(_ => ++calls == 1 ? pending.Task : Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)))) { BaseAddress = new("http://localhost/") })));
        var cut = c.RenderComponent<BriefingCadencePanel>(p => p.Add(x => x.CompanyId, Company)); cut.SetParametersAndRender(p => p.Add(x => x.CompanyId, Guid.NewGuid()));
        pending.SetResult(Json(Context)); await Task.Yield(); cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[role=alert]"))); Assert.DoesNotContain("Europe/Stockholm", cut.Markup);
    }
}
