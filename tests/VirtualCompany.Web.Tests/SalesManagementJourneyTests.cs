using System.Net;
using System.Net.Http.Json;
using System.Web;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Components.Sales;
using VirtualCompany.Web.Pages.Sales;
using VirtualCompany.Web.Services;
namespace VirtualCompany.Web.Tests;
public sealed class SalesManagementJourneyTests
{
    private static readonly Guid Company=Guid.NewGuid(),Deal=Guid.NewGuid();private static readonly DateTime Now=new(2026,10,1,12,0,0,DateTimeKind.Utc);
    private static SalesManagementReport Report()=>new(Company,2026,9,"Europe/Stockholm",Now.AddMonths(-1),Now,Now,null,"sales-management.v1",
        new("selected",1,1,0,0,100,100,10,1,1),new("prior",0,0,0,0,null,null,null,0,0),null,
        [new(Deal,"Included native opportunity","SEK",Now.AddDays(-15),"selected","won",Now.AddDays(-5),null,10,false,[],"/unused")],
        [new(Deal,"Current forecast opportunity",Guid.NewGuid(),"Qualified","SEK",1000,Now.AddDays(1),Now,.45m,.5m,null,337.5m)],
        [new(30,"SEK",1,1000,337.5m)],[Deal],[],null,"Created cohort; won / created; prior comparable duration; stage-risk-v1.","Owner and territory unavailable. Planning assumptions only.","Missing reasons are not inferred. Deleted history excluded.");
    private static SalesCapacityProposal Saved(SalesManagementReport? report=null)=>new(new(Guid.NewGuid(),Guid.NewGuid(),1,null,Guid.NewGuid(),Now,2026,9,null),report??Report(),new(160,8,[new("North",100)],"Recorded notes"),new(20,1,8,152,[new("North",100,160)]),new string('a',64),"Immutable private revisions; current Sales scope.");
    private static HttpResponseMessage Json<T>(T value)=>new(HttpStatusCode.OK){Content=JsonContent.Create(value)};
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> action):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)=>action(r,ct);}
    private static TestContext Context(Func<HttpRequestMessage,HttpResponseMessage> action)=>Context((r,_)=>Task.FromResult(action(r)));
    private static TestContext Context(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> action)
    {
        var ctx=new TestContext().AddVirtualCompanyWebPresentationServices();var http=new HttpClient(new Handler(action)){BaseAddress=new("http://localhost/")};
        ctx.Services.AddSingleton(new SalesManagementApiClient(new CompanyApiTransport(http),false));ctx.Services.AddSingleton(new OnboardingApiClient(new HttpClient{BaseAddress=new("http://localhost/")},useOfflineMode:true));
        ctx.Services.AddSingleton(new SalesApiClient(http));ctx.Services.AddSingleton(new AgentApiClient(http));
        ctx.Services.AddSingleton<IMonthlyReviewApiClient>(new MonthlyReviewJourneyTests.FakeReviews());ctx.Services.AddSingleton<IMonthlyWorkspaceApiClient>(new MonthlyWorkspaceApiClient(new CompanyApiTransport(http),false));
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo($"/app/sales/reports/management?companyId={Company}&year=2026&month=9");return ctx;
    }
    [Fact]
    public void Evidence_renders_denominators_missing_reasons_separate_forecast_and_exact_opportunity_return()
    {
        using var ctx=Context(_=>Json(Report()));var origin=$"http://localhost/app/sales/reports/management?companyId={Company}&year=2026&month=9&currency=SEK&snapshot={Guid.NewGuid()}";
        var cut=ctx.RenderComponent<SalesManagementEvidence>(p=>p.Add(x=>x.Report,Report()).Add(x=>x.ReturnUrl,origin));
        Assert.Contains("Missing; no explanation inferred",cut.Markup);Assert.Contains("Owner and territory unavailable",cut.Markup);Assert.Contains("Unavailable",cut.Markup);Assert.Contains("Default risk 0.5",cut.Markup);
        var link=cut.FindAll("a")[0].GetAttribute("href")!;var query=HttpUtility.ParseQueryString(new Uri("http://localhost"+link).Query);Assert.Contains("month=9",query["salesReturnUrl"]);Assert.Contains("snapshot=",query["salesReturnUrl"]);Assert.Equal(Company.ToString(),query["companyId"]);
    }
    [Fact]
    public void Report_page_links_planning_and_monthly_review_and_restricted_reload_clears_results()
    {
        var denied=false;using var ctx=Context(_=>denied?new(HttpStatusCode.Forbidden):Json(Report()));var cut=ctx.RenderComponent<SalesManagement>();
        cut.WaitForAssertion(()=>Assert.Contains("Included native opportunity",cut.Markup));Assert.Contains("/app/sales/reports/capacity",cut.Markup);Assert.Contains("period=month",cut.Markup);
        denied=true;cut.FindAll("button").Single(x=>x.TextContent=="Reload").Click();cut.WaitForAssertion(()=>Assert.Contains("access changed",cut.Markup));Assert.DoesNotContain("Included native opportunity",cut.Markup);
    }
    [Fact]
    public void Capacity_save_retains_request_on_conflict_and_reopened_results_and_revision_action_are_visible()
    {
        var value=Saved();var bodies=new List<string>();var conflict=true;
        using var ctx=Context(r=>{if(r.Method==HttpMethod.Post){bodies.Add(r.Content!.ReadAsStringAsync().GetAwaiter().GetResult());return conflict?new(HttpStatusCode.Conflict):Json(value);}return r.RequestUri!.AbsolutePath.EndsWith("/proposals")?Json(Array.Empty<SalesCapacityProposalSummary>()):r.RequestUri.AbsolutePath.Contains("/proposals/")?Json(value):Json(Report());});
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo($"/app/sales/reports/capacity?companyId={Company}&year=2026&month=9");
        var cut=ctx.RenderComponent<SalesCapacity>();
        cut.WaitForAssertion(()=>Assert.Contains("Save proposal",cut.Markup));cut.FindAll("input")[0].Change("160");cut.FindAll("input")[1].Change("8");cut.FindAll("button").Single(x=>x.TextContent=="Save proposal").Click();cut.WaitForAssertion(()=>Assert.Contains("changed",cut.Markup));
        conflict=false;cut.FindAll("button").Single(x=>x.TextContent=="Retry").Click();cut.WaitForAssertion(()=>Assert.Equal(2,bodies.Count));Assert.Equal(bodies[0],bodies[1]);Assert.Contains("proposal=",ctx.Services.GetRequiredService<NavigationManager>().Uri);
    }
    [Fact]
    public void Saved_proposal_displays_original_hours_notes_owner_results_and_refresh_as_new_revision()
    {
        var value=Saved();using var ctx=Context(r=>r.RequestUri!.AbsolutePath.EndsWith("/proposals")?Json(new[]{value.Summary}):Json(value));
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo($"/app/sales/reports/capacity?companyId={Company}&year=2026&month=9&proposal={value.Summary.Id}");
        var cut=ctx.RenderComponent<SalesCapacity>();
        cut.WaitForAssertion(()=>Assert.Contains("Retained revision 1",cut.Markup));Assert.Contains("Recorded notes",cut.Markup);Assert.Contains("capacity gap 152",cut.Markup);Assert.Contains(value.Summary.AccountableUserId.ToString(),cut.Markup);Assert.Contains("Refresh as new revision",cut.Markup);
    }
    [Fact]
    public async Task Late_report_after_company_switch_cannot_restore_foreign_results()
    {
        var pending=new TaskCompletionSource<HttpResponseMessage>();var other=Guid.NewGuid();var calls=0;
        using var ctx=Context((_,ct)=>++calls==1?pending.Task:Task.FromResult(Json(Report() with{CompanyId=other,Opportunities=[]})));
        var cut=ctx.RenderComponent<SalesManagement>();
        await cut.InvokeAsync(()=>ctx.Services.GetRequiredService<NavigationManager>().NavigateTo($"/app/sales/reports/management?companyId={other}&year=2026&month=9"));pending.SetResult(Json(Report()));
        cut.WaitForAssertion(()=>Assert.DoesNotContain("Included native opportunity",cut.Markup));
    }
    [Theory][InlineData(403)][InlineData(409)][InlineData(422)][InlineData(503)]
    public async Task Typed_client_handles_failure_without_returning_failed_payload(int code)
    {
        var http=new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage((HttpStatusCode)code)))){BaseAddress=new("http://localhost/")};var client=new SalesManagementApiClient(new CompanyApiTransport(http),false);
        if(code==403)await Assert.ThrowsAsync<TodayWorkspaceAccessException>(()=>client.Report(Company,new(2026,9)));else await Assert.ThrowsAsync<InvalidOperationException>(()=>client.Report(Company,new(2026,9)));
    }
    [Fact]
    public async Task Typed_client_rejects_company_and_period_mismatch_and_carries_company_header()
    {
        HttpRequestMessage? request=null;var bad=Report() with{CompanyId=Guid.NewGuid()};var http=new HttpClient(new Handler((r,_)=>{request=r;return Task.FromResult(Json(bad));})){BaseAddress=new("http://localhost/")};var client=new SalesManagementApiClient(new CompanyApiTransport(http),false);
        await Assert.ThrowsAsync<InvalidDataException>(()=>client.Report(Company,new(2026,9)));Assert.Equal(Company.ToString(),request!.Headers.GetValues("X-Company-Id").Single());bad=Report() with{Month=8};await Assert.ThrowsAsync<InvalidDataException>(()=>client.Report(Company,new(2026,9)));await Assert.ThrowsAsync<ArgumentException>(()=>client.Report(Guid.Empty,new(2026,9)));
    }
}


