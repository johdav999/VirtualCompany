using System.Net;
using System.Net.Http.Json;
using Bunit;
using VirtualCompany.Api.Tests;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Web.Services;
using VirtualCompany.Web.Components.Dashboard;
namespace VirtualCompany.Web.Tests;
public sealed class StrategicScenarioJourneyTests
{
    private static readonly Guid Company=Guid.NewGuid(),Owner=Guid.NewGuid(),Annual=Guid.NewGuid(),Forecast=Guid.NewGuid(),Scenario=Guid.NewGuid();
    private static StrategicScenarioOptions Options=>new(Company,2026,[],[],[new(Owner,"Recorded owner")]);
    private static StrategicScenarioDocument Doc
    {
        get{var input=new StrategicScenarioInput("Retained sensitivity",Owner,Annual,Forecast,"SEK",new(3,"orders",3,100,80,20,10,10,20,.5m,100,0,0,.5m,.75m,100),[new(1,0,0,"Explicit zeros"),new(2,0,0,"Explicit zeros"),new(3,0,0,"Explicit zeros")],[],"Retained assumptions rationale");
            var source=new ScenarioSource(Annual,1,new string('A',64),"approved",2026,new(2025,12,31,23,0,0,DateTimeKind.Utc),new(2026,12,31,23,0,0,DateTimeKind.Utc),"Europe/Stockholm","SEK",Forecast,"Native forecast","native",new string('B',64),new(2026,10,1,0,0,0,DateTimeKind.Utc),2026,9,4,100,80,["Posted source retained"],[]);
            return new(new(Scenario,Company,Scenario,1,null,null,input.Name,Owner,"Recorded owner",source.SourceAsOfUtc,2026,"SEK"),new(Company,input,"Recorded owner",source,"strategic-scenario.v1",Enumerable.Range(1,3).Select(y=>new ScenarioYearResult(y,100,80,80,20,80,72,10,45,57,0,0,-2,40,18,12)).ToArray(),[],["Negative closing cash"],new string('C',64),"Closing cash=opening+collections−payments−investment+funding","Sensitivity assumptions only"));}
    }
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> a):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)=>a(r,ct);}
    private static HttpResponseMessage Json<T>(T x)=>new(HttpStatusCode.OK){Content=JsonContent.Create(x)};
    private static HttpResponseMessage Route(HttpRequestMessage r)=>r.RequestUri!.AbsolutePath.EndsWith("options")?Json(Options):r.RequestUri.AbsolutePath.EndsWith("open")?Json(Doc):Json(new StrategicScenarioHistoryPage(Company,0,false,[Doc.Summary]));
    private static TestContext Context(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> a){var c=new TestContext().AddVirtualCompanyWebPresentationServices();c.Services.AddSingleton(new StrategicScenarioApiClient(new CompanyApiTransport(new HttpClient(new Handler(a)){BaseAddress=new("http://localhost/")})));return c;}
    [Fact]public void Retained_detail_renders_cash_formulas_native_sources_and_editable_assumptions()
    {
        using var c=Context((r,_)=>Task.FromResult(Route(r)));var cut=c.RenderComponent<StrategicScenarioWorkspace>(p=>p.Add(x=>x.CompanyId,Company).Add(x=>x.FiscalYear,2026).Add(x=>x.ScenarioId,Scenario));
        cut.WaitForAssertion(()=>Assert.Contains("Retained sensitivity",cut.Markup));Assert.Contains("Closing cash",cut.Markup);Assert.Contains("strategic-scenario.v1",cut.Markup);Assert.DoesNotContain("v@h.Revision",cut.Markup);Assert.Contains($"version={Forecast}",cut.Markup);Assert.Contains($"plan={Annual}",cut.Markup);
        cut.FindAll("button").Single(x=>x.TextContent=="Edit assumptions").Click();Assert.Equal("80",cut.Find("input[aria-label='Base capacity units']").GetAttribute("value"));Assert.Contains("Year 3 investment",cut.Markup);
    }
    [Fact]public void Revoked_source_reload_clears_retained_cash_and_actions()
    {
        var deny=false;using var c=Context((r,_)=>Task.FromResult(deny?new(HttpStatusCode.Forbidden):Route(r)));var cut=c.RenderComponent<StrategicScenarioWorkspace>(p=>p.Add(x=>x.CompanyId,Company).Add(x=>x.ScenarioId,Scenario));cut.WaitForAssertion(()=>Assert.Contains("Retained sensitivity",cut.Markup));deny=true;cut.FindAll("button").Single(x=>x.TextContent=="Reload").Click();cut.WaitForAssertion(()=>Assert.Contains("Strategic scenarios are unavailable",cut.Markup));Assert.DoesNotContain("Retained sensitivity",cut.Markup);Assert.DoesNotContain("Duplicate scenario",cut.Markup);
    }
    [Fact]public async Task Late_company_response_cannot_restore_previous_sources()
    {
        var pending=new TaskCompletionSource<HttpResponseMessage>();int calls=0;using var c=Context((_,_)=>++calls==1?pending.Task:Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));var cut=c.RenderComponent<StrategicScenarioWorkspace>(p=>p.Add(x=>x.CompanyId,Company));await cut.InvokeAsync(()=>cut.SetParametersAndRender(p=>p.Add(x=>x.CompanyId,Guid.NewGuid())));pending.SetResult(Json(Options));cut.WaitForAssertion(()=>Assert.Contains("Strategic scenarios are unavailable",cut.Markup));Assert.DoesNotContain("Recorded owner",cut.Markup);
    }
    [Fact]public async Task Company_period_and_calculation_mismatches_fail_closed()
    {
        using var c=Context((_,_)=>Task.FromResult(Json(Options with{CompanyId=Guid.NewGuid()})));await Assert.ThrowsAsync<InvalidDataException>(()=>c.Services.GetRequiredService<StrategicScenarioApiClient>().Options(Company,2026,default));
        using var d=Context((_,_)=>Task.FromResult(Json(Doc with{Scenario=Doc.Scenario with{CalculationVersion="unknown"}})));await Assert.ThrowsAsync<InvalidDataException>(()=>d.Services.GetRequiredService<StrategicScenarioApiClient>().Open(Company,Scenario,default));
        using var missing=Context((_,_)=>Task.FromResult(Json(Doc with{Scenario=Doc.Scenario with{Input=Doc.Scenario.Input with{Drivers=null!}}})));await Assert.ThrowsAsync<InvalidDataException>(()=>missing.Services.GetRequiredService<StrategicScenarioApiClient>().Open(Company,Scenario,default));
    }
    [Theory][InlineData(400)][InlineData(403)][InlineData(404)][InlineData(409)][InlineData(422)][InlineData(503)]public async Task Typed_errors_remain_explicit(int status)
    {
        using var c=Context((_,_)=>Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));var api=c.Services.GetRequiredService<StrategicScenarioApiClient>();if(status==403)await Assert.ThrowsAsync<TodayWorkspaceAccessException>(()=>api.Options(Company,2026,default));else await Assert.ThrowsAsync<InvalidOperationException>(()=>api.Options(Company,2026,default));
    }
}
