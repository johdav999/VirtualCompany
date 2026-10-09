using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Components.Work;
using VirtualCompany.Web.Pages;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class AuthorityExplanationJourneyTests
{
    [Fact]
    public void Settings_agent_selection_survives_reload_in_the_url_without_editing_authority()
    {
        using var context=new TestContext().AddVirtualCompanyWebPresentationServices();var company=Guid.NewGuid();var second=Guid.NewGuid();
        var first=Evidence(company,"recommend").Agents.Single().Id;
        context.Services.AddSingleton(new AgentWorkApiClient(new Transport((_,uri,_)=>{
            var dto=Evidence(company,"recommend");var agent=dto.Agents.Single() with {Id=uri.Contains(second.ToString())?second:first,Name=uri.Contains(second.ToString())?"Agent B":"Agent A"};
            return Reply(dto with {Agents=[agent],AvailableAgents=[new(first,"Agent A"),new(second,"Agent B")]});})));
        var navigation=context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/settings/agents/authority?companyId={company}");
        var page=context.RenderComponent<AgentAuthoritySettings>();page.WaitForAssertion(()=>Assert.Equal(2,page.FindAll(".authority-selector option").Count));
        page.Find(".authority-selector select").Change(second.ToString());page.WaitForAssertion(()=>Assert.Contains("agentId="+second,navigation.Uri));
        page.WaitForAssertion(()=>Assert.Contains("Agent B · agent capability",page.Markup));page.Dispose();
        var reloaded=context.RenderComponent<AgentAuthoritySettings>();reloaded.WaitForAssertion(()=>Assert.Contains("Agent B · agent capability",reloaded.Markup));
    }
    [Fact]
    public void Work_and_settings_pass_the_actual_work_kind_into_shared_authority_reads()
    {
        using var context=new TestContext().AddVirtualCompanyWebPresentationServices();var company=Guid.NewGuid();var task=Guid.NewGuid();
        var calls=new List<string>();
        var evidence=Evidence(company,"controlled_execution") with {WorkKind="task",WorkId=task};
        var item=new AgentWorkItemDto("task",task,"Authority task","Review limits","finance","planned","planned","Owner",[],"Review task",
            "No outstanding dependency",DateTime.UtcNow,DateTime.UtcNow,null,$"/agents/work/task/{task}?companyId={company}",null,[],[],[],[]);
        context.Services.AddSingleton(new AgentWorkApiClient(new Transport((_,uri,_)=>{
            calls.Add(uri);return uri.Contains("authority-explanation")?Reply(evidence):Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(item)});})));
        var navigation=context.Services.GetRequiredService<NavigationManager>();
        var original=$"/agents/work/task/{task}?companyId={company}&boardReturnUrl={Uri.EscapeDataString($"/agents/staff?companyId={company}&state=planned")}";
        navigation.NavigateTo(original);
        var detail=context.RenderComponent<AgentWorkDetail>(p=>p.Add(x=>x.Id,task).Add(x=>x.Kind,"task"));
        detail.WaitForAssertion(()=>Assert.Equal(4,detail.FindAll("input[type=radio]").Count));
        var settingsHref=detail.FindAll("a").Single(x=>x.TextContent.Contains("View authority settings")).GetAttribute("href")!;
        detail.Dispose();
        var beforeSettings=calls.Count;
        navigation.NavigateTo(settingsHref);
        var settings=context.RenderComponent<AgentAuthoritySettings>();
        settings.WaitForAssertion(()=>Assert.Equal(4,settings.FindAll("input[type=radio]").Count));
        Assert.Equal(original,settings.FindAll("a").Single(x=>x.TextContent=="Back to work").GetAttribute("href"));
        Assert.Contains(calls.Skip(beforeSettings),x=>x.Contains("authority-explanation"));
        Assert.All(calls.Where(x=>x.Contains("authority-explanation")),x=> {Assert.Contains("workKind=task",x);Assert.Contains(task.ToString(),x);});
    }
    [Theory]
    [InlineData("recommend","Advise")][InlineData("organize","Organize")]
    [InlineData("operate_internally","Do internal work")][InlineData("controlled_execution","Act within limits")]
    public void Four_positions_are_labeled_read_only_and_have_explicit_consequences(string intent,string label)
    {
        using var context=new TestContext().AddVirtualCompanyWebPresentationServices();var company=Guid.NewGuid();
        context.Services.AddSingleton(new AgentWorkApiClient(new Transport((_,_,_)=>Reply(Evidence(company,intent)))));
        var cut=context.RenderComponent<AuthorityExplanationPanel>(p=>p.Add(x=>x.CompanyId,company));
        cut.WaitForAssertion(()=>Assert.Equal(4,cut.FindAll("input[type=radio]").Count));
        Assert.All(cut.FindAll("input[type=radio]"),radio=>Assert.True(radio.HasAttribute("disabled")));
        Assert.Contains(label,cut.Find("input[checked]").ParentElement!.TextContent);Assert.Contains("read only",cut.Find("legend").TextContent);
        Assert.All(cut.FindAll(".authority-position"),x=>Assert.False(string.IsNullOrWhiteSpace(x.QuerySelector("small")!.TextContent)));
        Assert.Contains("Human review required",cut.Markup);Assert.Contains("Integration unavailable",cut.Markup);Assert.Contains("Unsupported",cut.Markup);
        Assert.Contains("expired",cut.Markup);Assert.Contains("cannot change authority",cut.Markup);Assert.Empty(cut.FindAll("form"));
        Assert.Contains("100.0",cut.Markup);Assert.DoesNotContain("100 0",cut.Markup);
    }
    [Fact]
    public void Refresh_replaces_current_evidence_without_any_mutation()
    {
        using var context=new TestContext().AddVirtualCompanyWebPresentationServices();var company=Guid.NewGuid();var calls=0;
        context.Services.AddSingleton(new AgentWorkApiClient(new Transport((_,_,_)=>Reply(Evidence(company,calls++==0?"recommend":"controlled_execution")))));
        var cut=context.RenderComponent<AuthorityExplanationPanel>(p=>p.Add(x=>x.CompanyId,company));
        cut.WaitForAssertion(()=>Assert.Equal("recommend",cut.Find("input[checked]").GetAttribute("value")));
        cut.Find("button").Click();cut.WaitForAssertion(()=>Assert.Equal("controlled_execution",cut.Find("input[checked]").GetAttribute("value")));Assert.Equal(2,calls);
    }
    [Fact]
    public void Late_company_response_and_mismatched_evidence_never_replace_current_authority()
    {
        using var context=new TestContext().AddVirtualCompanyWebPresentationServices();var old=Guid.NewGuid();var next=Guid.NewGuid();
        var pending=new TaskCompletionSource<HttpResponseMessage>();
        context.Services.AddSingleton(new AgentWorkApiClient(new Transport((company,_,_)=>company==old?pending.Task:Reply(Evidence(next,"organize")))));
        var cut=context.RenderComponent<AuthorityExplanationPanel>(p=>p.Add(x=>x.CompanyId,old));
        cut.SetParametersAndRender(p=>p.Add(x=>x.CompanyId,next));cut.WaitForAssertion(()=>Assert.Equal("organize",cut.Find("input[checked]").GetAttribute("value")));
        pending.SetResult(new(HttpStatusCode.OK){Content=JsonContent.Create(Evidence(old,"controlled_execution"))});
        cut.WaitForAssertion(()=>Assert.Equal("organize",cut.Find("input[checked]").GetAttribute("value")));
    }
    [Fact]
    public async Task Typed_client_refuses_foreign_or_unsupported_projection_and_offline_state()
    {
        var company=Guid.NewGuid();var client=new AgentWorkApiClient(new Transport((_,_,_)=>Reply(Evidence(Guid.NewGuid(),"recommend"))));
        await Assert.ThrowsAsync<OnboardingApiException>(()=>client.AuthorityAsync(company));
        client=new(new Transport((_,_,_)=>Reply(Evidence(company,"recommend") with {ProjectionVersion="unknown"})));
        await Assert.ThrowsAsync<OnboardingApiException>(()=>client.AuthorityAsync(company));
        client=new(new Transport((_,_,_)=>throw new InvalidOperationException("No offline network")),true);
        await Assert.ThrowsAsync<OnboardingApiException>(()=>client.AuthorityAsync(company));
    }
    [Theory]
    [InlineData("available","Passed this check")][InlineData("evaluation_failed","Evaluation unavailable")]
    [InlineData("permission_denied","Permission denied")][InlineData("not_implemented","Unsupported")]
    public void Presenter_keeps_restrictions_and_model_semantics_explicit(string state,string label)
    {
        Assert.Equal(label,AgentAuthorityTransparencyPresenter.CheckLabel(state));
        Assert.Equal("Guided",AgentAuthorityTransparencyPresenter.ProfileLabel("level_2"));
        Assert.Equal("Supervised internal execution",AgentAuthorityTransparencyPresenter.GrantLabel("supervised_internal_execute"));
        Assert.Contains("human review",AgentAuthorityTransparencyPresenter.CompanyPositions[3].Consequence);
        Assert.Equal("Profile unavailable",AgentAuthorityTransparencyPresenter.ProfileLabel("controlled_execution"));
    }
    private static Task<HttpResponseMessage> Reply(AuthorityExplanationDto value)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(value)});
    private static AuthorityExplanationDto Evidence(Guid company,string intent)=>new(company,null,null,"finance_review",intent,7,
        [new("Tasks per day","20"),new("Mandatory review","External work requires review.")],new("Dispatch","approval_required","approval_required","Human review is required for this task.",true),
        [new(Guid.Parse("14141414-1414-1414-1414-141414141414"),"Laura","level_2","v1","hash",
            [new("read_cash","read","finance","available","ready","finance.view",new("Tool policy","available","allowed","Read passed this check.",false)),
            new("prepare_draft","recommend","finance","approval_required","ready","finance.view",new("Policy","approval_required","review","Review the proposed draft.",true)),
            new("calendar","execute","finance","integration_unavailable","unavailable","",new("Capability","integration_unavailable","missing","Reconnect the integration.",false)),
            new("removed_tool","read","finance","not_implemented","not_available","",new("Capability","not_implemented","unsupported","This tool is not implemented.",false))],
            [new("daily_cash","read_monitor",1,DateTime.UtcNow.AddDays(-2),DateTime.UtcNow.AddDays(-1),[new("Records per run","10"),new("Amount per run","100.0")],new("Finance grant","permission_denied","expired","The grant has expired.",false))])],[],[],"authority-explanation-v1","hash",DateTime.UtcNow);
    private sealed class Transport(Func<Guid,string,CancellationToken,Task<HttpResponseMessage>> send):ICompanyApiTransport
    {
        public Uri? BaseAddress=>new("http://localhost/");
        public Task<HttpResponseMessage> SendAsync(Guid companyId,HttpMethod method,string uri,HttpContent? content,CancellationToken ct=default)
        {Assert.Equal(HttpMethod.Get,method);Assert.Null(content);return send(companyId,uri,ct);}
    }
}
