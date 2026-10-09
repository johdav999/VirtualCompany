using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Pages;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class TaskPolicyJourneyTests
{
    [Fact]
    public void Saved_policy_preview_activation_history_and_exact_observed_work_are_coherent()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var company = Guid.NewGuid(); var agent = Guid.NewGuid(); var record = Guid.NewGuid(); var goal = Guid.NewGuid(); var task = Guid.NewGuid();
        var view = View(company, agent); TaskPolicyPreview? preview = null;
        context.Services.AddSingleton(new AgentWorkApiClient(new Transport((_,_,_) => Task.FromResult(Reply(Authority(company, agent))))));
        context.Services.AddSingleton(new TaskTypePolicyApiClient(new Transport(async (_, uri, body) =>
        {
            if(uri.EndsWith("catalogue")) return Reply(new[] { Entry });
            if(uri.Contains("/records")) return Reply(new TaskPolicyQueueContext([new(record,"Renewal account")],[new(goal,"Retain renewal evidence")]));
            if(uri.EndsWith("preview"))
            {
                var change = (await body!.ReadFromJsonAsync<TaskPolicyChange>())!;
                Assert.Equal(1, change.ExpectedVersion); Assert.Equal("automatic", change.Mode);
                preview = new(change, Entry, view, 2, new("Policy","available","allowed","Bounded internal work only",false), true,"hash","authority",1,"No content or delivery is generated.");
                return Reply(preview);
            }
            if(uri.EndsWith("apply")) { var applied = (await body!.ReadFromJsonAsync<TaskPolicyApply>())!; Assert.Equal(preview!.Change, applied.Change); view = view with { Version=2,History=[new(2,"automatic",3,DateTime.UtcNow,DateTime.UtcNow.AddDays(1),Guid.NewGuid(),"Reviewed renewal limits","hash","Policy manager"),..view.History] }; }
            if(uri.EndsWith("queue")) { var queued=(await body!.ReadFromJsonAsync<QueueTaskPolicyWork>())!;Assert.Equal(2,queued.ExpectedVersion);Assert.Equal(record,queued.RecordId);Assert.Equal(goal,queued.GoalId);return Reply(task); }
            return Reply(view);
        })));
        var nav=context.Services.GetRequiredService<NavigationManager>(); nav.NavigateTo($"/settings/agents/task-policies?companyId={company}&agentId={agent}");
        var cut=context.RenderComponent<TaskTypePolicies>();
        cut.WaitForAssertion(()=>Assert.Equal("3",cut.Find("#policy-limit").GetAttribute("value")));
        Assert.Equal("automatic",cut.Find("input[checked]").GetAttribute("value"));
        Assert.True(Button(cut,"Queue internal work").HasAttribute("disabled"));
        cut.Find("#policy-record").Change(record.ToString());cut.Find("#policy-goal").Change(goal.ToString());cut.Find("textarea").Change("Reviewed renewal limits");
        Button(cut,"Preview changes").Click();cut.WaitForAssertion(()=>Assert.Contains("Version 1 → 2",cut.Markup));
        Button(cut,"Apply reviewed change").Click();cut.WaitForAssertion(()=>Assert.Contains("Current policy record version 2",cut.Markup));
        Assert.Contains("Policy manager",cut.Markup);Button(cut,"Queue internal work").Click();cut.WaitForAssertion(()=>Assert.Contains($"/agents/work/task/{task}",nav.Uri));
    }
    [Theory][InlineData("disabled",false)][InlineData("automatic",true)]
    public void Revoked_or_expired_policy_never_offers_queue(string mode,bool expired)
    {
        using var c=new TestContext().AddVirtualCompanyWebPresentationServices();var company=Guid.NewGuid();var agent=Guid.NewGuid();
        c.Services.AddSingleton(new AgentWorkApiClient(new Transport((_,_,_)=>Task.FromResult(Reply(Authority(company,agent))))));
        c.Services.AddSingleton(new TaskTypePolicyApiClient(new Transport((_,uri,_)=>Task.FromResult(uri.EndsWith("catalogue")?Reply(new[]{Entry}):uri.Contains("records")?Reply(new TaskPolicyQueueContext([],[])):Reply(View(company,agent) with {History=[new(1,mode,3,DateTime.UtcNow.AddDays(-2),expired?DateTime.UtcNow.AddDays(-1):DateTime.UtcNow.AddDays(1),Guid.NewGuid(),"Restricted","hash")]})))));
        c.Services.GetRequiredService<NavigationManager>().NavigateTo($"/settings/agents/task-policies?companyId={company}");var cut=c.RenderComponent<TaskTypePolicies>();
        cut.WaitForAssertion(()=>Assert.Contains("Current policy record version 1",cut.Markup));Assert.True(Button(cut,"Queue internal work").HasAttribute("disabled"));
    }
    [Fact]
    public async Task Typed_mutation_rejects_foreign_applied_policy_empty_work_and_offline_results()
    {
        var company=Guid.NewGuid();var agent=Guid.NewGuid();var change=new TaskPolicyChange(agent,Entry.Code,"automatic",1,DateTime.UtcNow.AddDays(1),"Reviewed");
        var foreign=new TaskTypePolicyApiClient(new Transport((_,_,_)=>Task.FromResult(Reply(View(Guid.NewGuid(),agent)))));
        await Assert.ThrowsAsync<OnboardingApiException>(()=>foreign.Apply(company,new(change,"hash")));
        var empty=new TaskTypePolicyApiClient(new Transport((_,_,_)=>Task.FromResult(Reply(Guid.Empty))));
        await Assert.ThrowsAsync<OnboardingApiException>(()=>empty.Queue(company,new(agent,Entry.Code,Guid.NewGuid(),1)));
        var offline=new TaskTypePolicyApiClient(new Transport((_,_,_)=>throw new InvalidOperationException("No transport")),true);
        await Assert.ThrowsAsync<OnboardingApiException>(()=>offline.Preview(company,change));
    }
    private static readonly TaskTypePolicyCatalogueEntry Entry=new("sales.account_research","Sales account research","Sales","sales.research_prospect","sales.research_prospect","recommend","prospectId");
    private static TaskPolicyView View(Guid company,Guid agent)=>new(company,agent,Entry.Code,1,[new(1,"automatic",3,DateTime.UtcNow,DateTime.UtcNow.AddDays(1),Guid.NewGuid(),"Existing bounds","hash","Policy manager")]);
    private static AuthorityExplanationDto Authority(Guid company,Guid agent)=>new(company,null,null,"","controlled_execution",1,[],new("Policy","available","allowed","Current limits",false),[],[new(agent,"Alex")],[],"authority-explanation-v1","hash",DateTime.UtcNow);
    private static AngleSharp.Dom.IElement Button(IRenderedComponent<TaskTypePolicies> c,string text)=>c.FindAll("button").Single(x=>x.TextContent==text);
    private static HttpResponseMessage Reply<T>(T value)=>new(HttpStatusCode.OK){Content=JsonContent.Create(value)};
    private sealed class Transport(Func<Guid,string,HttpContent?,Task<HttpResponseMessage>> send):ICompanyApiTransport
    {public Uri? BaseAddress=>new("http://localhost/");public Task<HttpResponseMessage> SendAsync(Guid company,HttpMethod method,string uri,HttpContent? body,CancellationToken ct=default)=>send(company,uri,body);}
}
