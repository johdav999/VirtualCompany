using System.Net;
using System.Net.Http.Json;
using System.Web;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Components.Marketing;
using VirtualCompany.Web.Pages.Marketing;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class MarketingManagementJourneyTests
{
    private static readonly Guid Company=MonthlyReviewJourneyTests.Company,Campaign=Guid.NewGuid(),Model=Guid.NewGuid(),Segment=Guid.NewGuid();
    private static readonly DateTime Now=new(2026,10,1,12,0,0,DateTimeKind.Utc);
    private static MarketingManagementQuery Query=>new(2026,9,"SEK",ModelId:Model);
    private static MarketingManagementReport Report()=>new(Company,Query,"Europe/Stockholm",Now.AddMonths(-1),Now,Now,"marketing-management.v1",
        [new("email","SEK",100,1,0,1,[new("leads",5,20)],"Recorded attribution; no causal evidence."),new("social","SEK",null,1,1,1,[new("leads",5,null)],"Missing cost; economics unavailable.")],[],[],[new(Model,"Even recorded","even",1,"{}","Configured attribution, no causal uplift",30)],
        [new(Guid.NewGuid(),"Guarded experiment",Campaign,"Recorded hypothesis","leads","complaints",100,Now.AddMonths(-1),Now,new(Guid.NewGuid(),Guid.NewGuid(),"stop_guardrail_breach",100,0,true,false,"{\"source\":\"guardrail\"}","No causal uplift established"),100,"Recorded experiment only")],
        [new(Campaign,"Recorded campaign","SEK",200,[new(Guid.NewGuid(),Segment,2,"Current recorded association")])],[],["Economics = known cost divided by credited units; incomplete sources remain unavailable."]);
    private static MarketingBudgetProposal Saved()=>new(new(Guid.NewGuid(),Guid.NewGuid(),1,null,Guid.NewGuid(),Now,Now,2026,9,"SEK"),Report(),new("SEK",300,500,[new(Campaign,300,"Recorded impact assumption")],"Original planning notes"),new("SEK",300,200,100,[new(Campaign,"Recorded campaign",200,300,100,"Recorded impact assumption")],["No spend authority"]),new string('a',64),"Private retained evidence");
    private static HttpResponseMessage Json<T>(T value)=>new(HttpStatusCode.OK){Content=JsonContent.Create(value)};
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> action):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)=>action(r,ct);}
    private static TestContext Context(Func<HttpRequestMessage,HttpResponseMessage> action)=>Context((r,_)=>Task.FromResult(action(r)));
    private static TestContext Context(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> action)
    {
        var ctx=new TestContext().AddVirtualCompanyWebPresentationServices();var http=new HttpClient(new Handler(action)){BaseAddress=new("http://localhost/")};ctx.Services.AddSingleton(new MarketingManagementApiClient(new CompanyApiTransport(http),false));ctx.Services.AddSingleton(new OnboardingApiClient(http,useOfflineMode:true));ctx.Services.AddSingleton<IMonthlyReviewApiClient>(new MonthlyReviewJourneyTests.FakeReviews());ctx.Services.AddSingleton<IMonthlyWorkspaceApiClient>(new MonthlyWorkspaceApiClient(new CompanyApiTransport(http),false));
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo($"/app/marketing/reports/management?companyId={Company}&"+MarketingManagementApiClient.Parameters(Query));return ctx;
    }
    [Fact]
    public void Evidence_discloses_cost_gaps_denominators_guardrails_and_exact_campaign_segment_returns()
    {
        using var ctx=Context(_=>Json(Report()));var origin=$"http://localhost/app/marketing/reports/management?companyId={Company}&year=2026&month=9&snapshot={Guid.NewGuid()}";var cut=ctx.RenderComponent<MarketingManagementEvidence>(p=>p.Add(x=>x.Report,Report()).Add(x=>x.ReturnUrl,origin));
        Assert.Contains("Unavailable SEK / leads",cut.Markup);Assert.Contains("1 unknown costs",cut.Markup);Assert.Contains("Guardrail breached Yes",cut.Markup);Assert.Contains("Causal eligibility Not established",cut.Markup);Assert.Contains("divided by credited units",cut.Markup);
        foreach(var link in cut.FindAll("a").Take(2)){var q=HttpUtility.ParseQueryString(new Uri("http://localhost"+link.GetAttribute("href")).Query);Assert.Equal(Company.ToString(),q["companyId"]);Assert.Contains("snapshot=",q["recordReturnUrl"]);Assert.Contains("month=9",q["recordReturnUrl"]);}
        Assert.Contains("segmentVersionId="+Segment,cut.Markup);
    }
    [Fact]
    public void Report_links_budget_and_monthly_and_restricted_reload_removes_previous_results()
    {
        var denied=false;using var ctx=Context(_=>denied?new(HttpStatusCode.Forbidden):Json(Report()));var cut=ctx.RenderComponent<MarketingManagement>();cut.WaitForAssertion(()=>Assert.Contains("Recorded campaign",cut.Markup));Assert.Contains("/marketing/reports/budget",cut.Markup);Assert.Contains("period=month",cut.Markup);
        denied=true;cut.FindAll("button").Single(x=>x.TextContent=="Reload").Click();cut.WaitForAssertion(()=>Assert.Contains("access changed",cut.Markup));Assert.DoesNotContain("Recorded campaign",cut.Markup);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void CSV_handoff_refreshes_authorization_and_uses_native_download_function(bool revoke)
    {
        var exports=0;using var ctx=Context(r=>{if(r.RequestUri!.AbsolutePath.EndsWith("/export")){exports++;return revoke?new(HttpStatusCode.Forbidden):Json(new MarketingManagementExport("recorded.csv","fresh,authorized,source"));}return Json(Report());});var module=ctx.JSInterop.SetupModule("./js/reportDownload.js");module.Mode=JSRuntimeMode.Loose;var cut=ctx.RenderComponent<MarketingManagement>();cut.WaitForAssertion(()=>Assert.Contains("Recorded campaign",cut.Markup));cut.FindAll("button").Single(x=>x.TextContent=="Refresh and download CSV").Click();cut.WaitForAssertion(()=>Assert.Equal(1,exports));
        if(revoke){cut.WaitForAssertion(()=>Assert.Contains("access changed",cut.Markup));Assert.DoesNotContain("Recorded campaign",cut.Markup);Assert.Empty(module.Invocations);}else{cut.WaitForAssertion(()=>Assert.Single(module.Invocations["downloadReport"]));Assert.Equal("fresh,authorized,source",module.Invocations["downloadReport"].Single().Arguments[1]);}
    }
    [Fact]
    public void Proposal_retry_keeps_exact_request_identity_and_allocation_assumptions()
    {
        var saved=Saved();var bodies=new List<string>();var fail=true;using var ctx=Context(r=>{if(r.Method==HttpMethod.Post){bodies.Add(r.Content!.ReadAsStringAsync().GetAwaiter().GetResult());return fail?new(HttpStatusCode.ServiceUnavailable):Json(saved);}return r.RequestUri!.AbsolutePath.EndsWith("/proposals")?Json(Array.Empty<MarketingBudgetProposalSummary>()):Json(Report());});ctx.Services.GetRequiredService<NavigationManager>().NavigateTo($"/app/marketing/reports/budget?companyId={Company}&"+MarketingManagementApiClient.Parameters(Query));var cut=ctx.RenderComponent<MarketingBudget>();cut.WaitForAssertion(()=>Assert.Contains("Save proposal",cut.Markup));cut.Find("select").Change(Campaign.ToString());cut.FindAll("input")[1].Change("300");cut.FindAll("input")[3].Change("300");cut.FindAll("input")[4].Change("Recorded assumption");cut.FindAll("button").Single(x=>x.TextContent=="Save proposal").Click();cut.WaitForAssertion(()=>Assert.Contains("request failed",cut.Markup));fail=false;cut.FindAll("button").Single(x=>x.TextContent=="Retry").Click();cut.WaitForAssertion(()=>Assert.Equal(2,bodies.Count));Assert.Equal(bodies[0],bodies[1]);Assert.Contains("proposal=",ctx.Services.GetRequiredService<NavigationManager>().Uri);
    }
    [Fact]
    public void Reopened_proposal_shows_original_owner_allocations_notes_revision_and_retained_report_link()
    {
        var saved=Saved();using var ctx=Context(r=>r.RequestUri!.AbsolutePath.EndsWith("/proposals")?Json(new[]{saved.Summary}):Json(saved));ctx.Services.GetRequiredService<NavigationManager>().NavigateTo($"/app/marketing/reports/budget?companyId={Company}&year=2026&month=9&currency=SEK&proposal={saved.Summary.Id}");var cut=ctx.RenderComponent<MarketingBudget>();cut.WaitForAssertion(()=>Assert.Contains("Retained revision 1",cut.Markup));Assert.Contains("Original planning notes",cut.Markup);Assert.Contains("Recorded impact assumption",cut.Markup);Assert.Contains(saved.Summary.AccountableUserId.ToString(),cut.Markup);Assert.Contains("Refresh as new revision",cut.Markup);Assert.Contains("proposalEvidence="+saved.Summary.Id,cut.Markup);
    }
    [Fact]
    public async Task Late_report_cannot_restore_results_after_company_switch()
    {
        var pending=new TaskCompletionSource<HttpResponseMessage>();var other=Guid.NewGuid();var calls=0;using var ctx=Context((_,_)=>++calls==1?pending.Task:Task.FromResult(Json(Report() with{CompanyId=other,Campaigns=[]})));var cut=ctx.RenderComponent<MarketingManagement>();await cut.InvokeAsync(()=>ctx.Services.GetRequiredService<NavigationManager>().NavigateTo($"/app/marketing/reports/management?companyId={other}&"+MarketingManagementApiClient.Parameters(Query)));pending.SetResult(Json(Report()));cut.WaitForAssertion(()=>Assert.DoesNotContain("Recorded campaign",cut.Markup));
    }
    [Fact]
    public void Retained_monthly_report_rejects_conflicting_filters_and_cannot_export_as_current()
    {
        using var ctx=Context(_=>Json(Report()));var reviews=(MonthlyReviewJourneyTests.FakeReviews)ctx.Services.GetRequiredService<IMonthlyReviewApiClient>();reviews.Saved=MonthlyReviewJourneyTests.Snapshot(MonthlyReviewJourneyTests.Workspace("marketing") with{MarketingManagement=Report()});ctx.Services.GetRequiredService<NavigationManager>().NavigateTo($"/app/marketing/reports/management?companyId={Company}&year=2026&month=9&lens=marketing&snapshot={reviews.Saved.Summary.Id}");var cut=ctx.RenderComponent<MarketingManagement>();cut.WaitForAssertion(()=>Assert.Contains("Retained report snapshot",cut.Markup));Assert.DoesNotContain("Refresh and download CSV",cut.Markup);
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo($"/app/marketing/reports/management?companyId={Company}&year=2026&month=9&lens=marketing&currency=EUR&snapshot={reviews.Saved.Summary.Id}");cut.WaitForAssertion(()=>Assert.Contains("filters differ",cut.Markup));Assert.DoesNotContain("Recorded campaign",cut.Markup);
    }
    [Theory][InlineData(403)][InlineData(400)][InlineData(404)][InlineData(409)][InlineData(422)][InlineData(503)]
    public async Task Typed_client_rejects_failed_payloads(int code)
    {
        using var ctx=Context(_=>new((HttpStatusCode)code));var client=ctx.Services.GetRequiredService<MarketingManagementApiClient>();if(code==403)await Assert.ThrowsAsync<TodayWorkspaceAccessException>(()=>client.Report(Company,Query));else await Assert.ThrowsAsync<InvalidOperationException>(()=>client.Report(Company,Query));
    }
    [Fact]
    public async Task Typed_client_carries_company_and_filters_rejects_mismatch_and_offline_results()
    {
        HttpRequestMessage? request=null;using var ctx=Context(r=>{request=r;return Json(Report() with{CompanyId=Guid.NewGuid()});});var client=ctx.Services.GetRequiredService<MarketingManagementApiClient>();await Assert.ThrowsAsync<InvalidDataException>(()=>client.Report(Company,Query));Assert.Equal(Company.ToString(),request!.Headers.GetValues("X-Company-Id").Single());Assert.Contains("modelId="+Model,request.RequestUri!.Query);await Assert.ThrowsAsync<ArgumentException>(()=>client.Report(Guid.Empty,Query));await Assert.ThrowsAsync<InvalidOperationException>(()=>new MarketingManagementApiClient(new CompanyApiTransport(new HttpClient()),true).Report(Company,Query));
    }
}
