using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Web.Pages;
using VirtualCompany.Web.Services;
using VirtualCompany.Api.Tests;
namespace VirtualCompany.Web.Tests;

public sealed class ExecutionControlJourneyTests
{
    [Fact]
    public void Preview_apply_history_and_scope_return_use_typed_current_state()
    {
        using var context=new TestContext().AddVirtualCompanyWebPresentationServices();var company=Guid.NewGuid();var agent=Guid.NewGuid();var task=Guid.NewGuid();var calls=new List<string>();
        var view=View(company,agent,task);ExecutionControlChange? seen=null;
        context.Services.AddSingleton(new ExecutionControlApiClient(new Transport(async(method,uri,content)=>{
            calls.Add(uri);
            if(uri.EndsWith("/preview")){seen=(await content!.ReadFromJsonAsync<ExecutionControlChange>())!;return Reply(new ExecutionControlPreview(seen,view,"bound-hash","Already admitted steps may complete."));}
            if(uri.EndsWith("/apply")){var applied=(await content!.ReadFromJsonAsync<ExecutionControlApply>())!;Assert.Equal(seen,applied.Change);Assert.Equal("bound-hash",applied.PreviewHash);view=view with{Version=1,Paused=true,History=[new(seen!.CommandId,1,agent,true,"Manager",DateTime.UtcNow,seen.Reason)]};}
            return Reply(method==HttpMethod.Get&&!uri.Contains("agentId")?view with{AgentId=null}:view);
        })));
        var nav=context.Services.GetRequiredService<NavigationManager>();nav.NavigateTo($"/settings/agents/execution?companyId={company}&agentId={agent}");var cut=context.RenderComponent<ExecutionControls>();cut.WaitForAssertion(()=>Assert.Contains("Queued",cut.Markup));
        cut.Find("textarea").Change("Pause during review");cut.FindAll("button").Single(x=>x.TextContent=="Preview impact").Click();cut.WaitForAssertion(()=>Assert.Contains("Apply reviewed pause",cut.Markup));
        cut.FindAll("button").Single(x=>x.TextContent=="Apply reviewed pause").Click();cut.WaitForAssertion(()=>Assert.Contains("Paused new steps",cut.Markup));Assert.Contains("Manager",cut.Markup);
        var link=cut.FindAll("a").Single(x=>x.TextContent=="Review work").GetAttribute("href")!;Assert.Contains(task.ToString(),link);Assert.Contains("executionReturnUrl",link);Assert.Contains("companyId",link);
        cut.Find("#execution-scope").Change("");cut.WaitForAssertion(()=>Assert.DoesNotContain("agentId",nav.Uri));Assert.All(calls,x=>Assert.Contains(company.ToString(),x));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Read_only_or_failed_controls_do_not_offer_a_false_apply(bool failed)
    {
        using var context=new TestContext().AddVirtualCompanyWebPresentationServices();var company=Guid.NewGuid();
        context.Services.AddSingleton(new ExecutionControlApiClient(new Transport((_,_,_)=>Task.FromResult(failed?new HttpResponseMessage(HttpStatusCode.Forbidden):Reply(View(company,Guid.NewGuid(),Guid.NewGuid()) with{AgentId=null,CanManage=false})))));
        context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/settings/agents/execution?companyId={company}");var cut=context.RenderComponent<ExecutionControls>();
        cut.WaitForAssertion(()=>Assert.Contains(failed?"permission":"Manager access",cut.Markup));Assert.DoesNotContain("Apply reviewed",cut.Markup);Assert.Empty(cut.FindAll("textarea"));
    }
    [Fact]
    public async Task Client_rejects_foreign_company_and_offline_mutation()
    {
        var company=Guid.NewGuid();var agent=Guid.NewGuid();var client=new ExecutionControlApiClient(new Transport((_,_,_)=>Task.FromResult(Reply(View(Guid.NewGuid(),agent,Guid.NewGuid())))));
        await Assert.ThrowsAsync<OnboardingApiException>(()=>client.Get(company,agent));
        var offline=new ExecutionControlApiClient(new Transport((_,_,_)=>throw new Exception("Transport must not run")),true);
        await Assert.ThrowsAsync<OnboardingApiException>(()=>offline.Apply(company,new(new(Guid.NewGuid(),agent,true,0,0,"Review"),"hash")));
    }
    private static ExecutionControlView View(Guid company,Guid agent,Guid task)=>new(company,agent,0,2,false,false,false,true,DateTime.UtcNow,[new(agent,"Alex")],[new(task,"task",agent,"Alex","Account review","Queued",DateTime.UtcNow,"Current policies are checked again.")],[]);
    private static HttpResponseMessage Reply<T>(T data)=>new(HttpStatusCode.OK){Content=JsonContent.Create(data)};
    private sealed class Transport(Func<HttpMethod,string,HttpContent?,Task<HttpResponseMessage>> send):ICompanyApiTransport
    {public Uri? BaseAddress=>new("http://localhost/");public Task<HttpResponseMessage> SendAsync(Guid company,HttpMethod method,string uri,HttpContent? content,CancellationToken ct=default)=>send(method,uri,content);}
}
