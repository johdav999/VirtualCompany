using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Components.Work;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class BusinessWorkEvidenceJourneyTests
{
    [Theory]
    [InlineData("deal")][InlineData("case")][InlineData("invoice")][InlineData("bill")][InlineData("campaign")][InlineData("brief")]
    public void Shared_panel_preserves_owner_states_and_refreshes_current_decision(string kind)
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices(); var company=Guid.NewGuid(); var record=Guid.NewGuid(); var task=Guid.NewGuid(); var approval=Guid.NewGuid();
        var state="awaiting_approval";var reads=0;var transport=new Transport((_,_,ct)=>{reads++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(Evidence(company,kind,record,task,approval,state))});});
        context.Services.AddSingleton(new AgentWorkApiClient(transport));
        context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo($"/finance/reviews/{record}?companyId={company}");
        var cut=context.RenderComponent<BusinessAgentWorkPanel>(p=>p.Add(x=>x.CompanyId,company).Add(x=>x.RecordKind,kind).Add(x=>x.RecordId,record));
        cut.WaitForAssertion(()=>Assert.Contains("Waiting for approval",cut.Markup));
        Assert.Contains("Retained review policy",cut.Markup); Assert.Contains("Grounding unavailable",cut.Markup);Assert.Contains("Partial evidence",cut.Markup);
        Assert.Contains("Draft artifact; delivery not established",cut.Markup);Assert.DoesNotContain("Approve",cut.FindAll("button").Select(x=>x.TextContent));
        var decision=cut.FindAll("a").Single(x=>x.TextContent=="Open current decision").GetAttribute("href")!;
        Assert.Equal(approval.ToString(),System.Web.HttpUtility.ParseQueryString(new Uri("http://localhost"+decision).Query)["itemId"]);
        Assert.Contains("recordReturnUrl=",decision);Assert.Equal(1,reads);
        state="blocked";cut.Find("button").Click();cut.WaitForAssertion(()=>Assert.Contains("Blocked",cut.Markup));Assert.Equal(2,reads);
        Assert.Contains("Draft artifact; delivery not established",cut.Markup);
    }
    [Fact]
    public void Older_record_response_cannot_replace_new_record_evidence()
    {
        using var context=new TestContext().AddVirtualCompanyWebPresentationServices();var company=Guid.NewGuid();var oldRecord=Guid.NewGuid();var next=Guid.NewGuid();
        var old=new TaskCompletionSource<HttpResponseMessage>();
        context.Services.AddSingleton(new AgentWorkApiClient(new Transport((_,uri,ct)=>uri.Contains(oldRecord.ToString())?old.Task:Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(Evidence(company,"case",next,Guid.NewGuid(),Guid.NewGuid(),"completed"))}))));
        var cut=context.RenderComponent<BusinessAgentWorkPanel>(p=>p.Add(x=>x.CompanyId,company).Add(x=>x.RecordKind,"case").Add(x=>x.RecordId,oldRecord));
        cut.SetParametersAndRender(p=>p.Add(x=>x.RecordId,next));cut.WaitForAssertion(()=>Assert.Contains("Completed",cut.Markup));
        old.SetResult(new(HttpStatusCode.OK){Content=JsonContent.Create(Evidence(company,"case",oldRecord,Guid.NewGuid(),Guid.NewGuid(),"failed"))});
        cut.WaitForAssertion(()=>Assert.Contains("Completed",cut.Markup));Assert.DoesNotContain("Failed",cut.Markup);
    }
    [Fact]
    public void Foreign_record_and_decision_links_fail_closed_and_retry_is_available()
    {
        using var context=new TestContext().AddVirtualCompanyWebPresentationServices();var company=Guid.NewGuid();var record=Guid.NewGuid();
        var bad=Evidence(Guid.NewGuid(),"deal",record,Guid.NewGuid(),Guid.NewGuid(),"planned");
        context.Services.AddSingleton(new AgentWorkApiClient(new Transport((_,_,ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(bad)}))));
        var cut=context.RenderComponent<BusinessAgentWorkPanel>(p=>p.Add(x=>x.CompanyId,company).Add(x=>x.RecordKind,"deal").Add(x=>x.RecordId,record));
        cut.WaitForAssertion(()=>Assert.Contains("mismatched evidence",cut.Find("[role='alert']").TextContent));Assert.Empty(cut.FindAll("a"));Assert.False(cut.Find("button").HasAttribute("disabled"));
    }
    [Fact]
    public void Offline_never_invents_evidence()
    {
        using var context=new TestContext().AddVirtualCompanyWebPresentationServices();
        context.Services.AddSingleton(new AgentWorkApiClient(new Transport((_,_,ct)=>throw new InvalidOperationException("Offline must not send")),true));
        var cut=context.RenderComponent<BusinessAgentWorkPanel>(p=>p.Add(x=>x.CompanyId,Guid.NewGuid()).Add(x=>x.RecordKind,"bill").Add(x=>x.RecordId,Guid.NewGuid()));
        cut.WaitForAssertion(()=>Assert.Contains("backend connection",cut.Find("[role='alert']").TextContent));Assert.Empty(cut.FindAll("a"));
    }
    [Fact]
    public void Business_returns_keep_selected_record_and_reject_foreign_or_untyped_routes()
    {
        using var context=new TestContext().AddVirtualCompanyWebPresentationServices();var company=Guid.NewGuid();var record=Guid.NewGuid();
        var paths=new[]{ $"/app/sales/deals/{record}?companyId={company}&tab=files",$"/support/cases/{record}?companyId={company}",
            $"/finance/reviews/{record}?companyId={company}",$"/finance/bill-inbox/{record}?companyId={company}",
            $"/finance/supplier-bills/{record}?companyId={company}&financeSource=operational",$"/marketing/review?companyId={company}&briefId={record}" };
        foreach(var path in paths){Assert.Equal(path,AgentWorkRoutes.BusinessRecord(path,company));Assert.Null(AgentWorkRoutes.BusinessRecord(path,Guid.NewGuid()));Assert.Null(AgentWorkRoutes.BusinessRecord("https://foreign.example"+path,company));}
        Assert.Null(AgentWorkRoutes.BusinessRecord($"/marketing/review?companyId={company}",company));Assert.Null(AgentWorkRoutes.BusinessRecord($"/support/cases/not-an-id?companyId={company}",company));
        var nav=context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();nav.NavigateTo($"/work?companyId={company}");
        var cut=context.RenderComponent<AgentWorkReturnLink>(p=>p.Add(x=>x.CompanyId,company));Assert.Empty(cut.FindAll("a"));
        nav.NavigateTo($"/work?companyId={company}&tab=approvals&recordReturnUrl={Uri.EscapeDataString(paths[1])}");
        cut.WaitForAssertion(()=>Assert.Equal(paths[1],cut.Find("a").GetAttribute("href")));Assert.Equal("Back to business record",cut.Find("a").TextContent);
    }
    private static BusinessWorkEvidenceDto Evidence(Guid company,string kind,Guid record,Guid task,Guid approval,string state)=>new(company,kind,record,DateTime.UtcNow,
        "Drafting, approval and delivery are separate steps.","Review the current decision and refresh after an outcome.",
        [new(record,"draft","Retained draft","Retained business proposal","draft","needs_review","Draft artifact; delivery not established","Grounding must be reviewed.",null,DateTime.UtcNow,2,$"/support/cases/{record}?companyId={company}",null,["Grounding unavailable"])],
        [new("task",task,"Current internal evidence review","Review the proposal",kind,state,state,"Owner",[],"Review","Human review required",DateTime.UtcNow,DateTime.UtcNow,null,
            $"/agents/work/task/{task}?companyId={company}",$"/work?companyId={company}&tab=tasks&taskId={task}",[],[],[],[])],[],["Some contributions are partial"],true,[new(task,approval,state=="blocked"?"changes_requested":"pending","Retained review policy","Amount limit 1000 SEK","Owner",DateTime.UtcNow.AddDays(1),false,$"/work?companyId={company}&tab=approvals&itemId={approval}")]);
    private sealed class Transport(Func<Guid,string,CancellationToken,Task<HttpResponseMessage>> send):ICompanyApiTransport
    {public Uri? BaseAddress=>new("http://localhost/");public Task<HttpResponseMessage> SendAsync(Guid companyId,HttpMethod method,string uri,HttpContent? content,CancellationToken ct=default)=>send(companyId,uri,ct);}
}
