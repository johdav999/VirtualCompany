using System.Net;
using System.Net.Http.Json;
using System.Web;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Services;
using ReportPage = VirtualCompany.Web.Pages.Support.SupportOperationalReport;

namespace VirtualCompany.Web.Tests;

public sealed class SupportOperationalJourneyTests
{
    private static readonly Guid Company = Guid.NewGuid(), Case = Guid.NewGuid();
    [Fact]
    public async Task Typed_report_and_knowledge_client_preserves_scope_filters_and_correlation_rejects_empty_context()
    {
        var calls = 0;
        var client = Client(request => {
            calls++; Assert.Equal(Company.ToString(), request.Headers.GetValues("X-Company-Id").Single()); Assert.True(request.Headers.Contains("X-Correlation-Id"));
            var query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
            Assert.Equal("waiting_internal",query["status"]); Assert.Equal("7-30d",query["ageBucket"]); Assert.Equal(Case.ToString(),query["contactId"]);
            return Json(Report());
        });
        await client.GetOperationalReportAsync(Company, new(Status:"waiting_internal",ContactId:Case,AgeBucket:"7-30d"));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetOperationalReportAsync(Guid.Empty,new())); Assert.Equal(1,calls);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Malformed_and_timed_out_reads_have_recovery_feedback(bool timeout)
    {
        var client = Client(_ => timeout ? throw new TaskCanceledException() : new(HttpStatusCode.OK) { Content = new StringContent("{invalid") });
        var error = await Assert.ThrowsAsync<SupportApiException>(() => client.GetOperationalReportAsync(Company,new()));
        Assert.Contains("Retry",error.Message);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Export_rereads_authorized_rows_and_never_downloads_revoked_cached_data(bool revoke)
    {
        using var context = Context(); var reads=0;
        context.Services.AddSingleton(Client(_ => { reads++; return reads>1 && revoke ? new(HttpStatusCode.Forbidden) : Json(Report() with { Cases=[Report().Cases.Single() with { Owner="Current "+reads }] }); }));
        var module=context.JSInterop.SetupModule("./js/reportDownload.js"); module.Mode=JSRuntimeMode.Loose;
        context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/support/reports?companyId={Company}&view=sla");
        var cut=context.RenderComponent<ReportPage>(); cut.WaitForAssertion(()=>Assert.Contains("Current 1",cut.Markup));
        cut.FindAll("button").Single(x=>x.TextContent=="Export CSV").Click();
        cut.WaitForAssertion(()=>Assert.Equal(2,reads));
        if(revoke) { cut.WaitForAssertion(()=>Assert.Contains("failed",cut.Markup)); Assert.DoesNotContain("Current 1",cut.Markup); Assert.Empty(module.Invocations); }
        else { cut.WaitForAssertion(()=>Assert.Single(module.Invocations["downloadReport"])); Assert.Contains("Current 2",(string)module.Invocations["downloadReport"].Single().Arguments[1]!); }
    }
    [Fact]
    public void CSV_preserves_case_denominator_deadline_asof_owner_and_formula_escaping()
    {
        var report=Report(); var csv=SupportReportCsv.Build(report);
        Assert.Contains("' =SUM(1,2)",csv); Assert.Contains("Specialist",csv); Assert.Contains(report.AsOfUtc.ToString("O"),csv);
        Assert.Equal(report.Cases.Count+1,csv.Split('\n',StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("Missing target",csv);
    }
    [Fact]
    public void Navigation_keeps_exact_month_report_filters_and_bounded_case_parent_rejects_foreign_or_external_returns()
    {
        var overview=DashboardRoutes.BuildMonthlyPath(Company,"customers",2026,9);
        var parent=SupportJourneyRoutes.Build("/support/reports?view=aging&status=waiting_internal&ageBucket=7-30d",Company,overview);
        var path=SupportJourneyRoutes.Record($"/support/cases/{Case}",Company,"http://local.test"+parent);
        var query=HttpUtility.ParseQueryString(new Uri("http://local.test"+path).Query);
        Assert.Equal(parent,query["supportReturnUrl"]); Assert.Equal(overview,query["returnUrl"]);
        var sibling=SupportJourneyRoutes.Record("/support/knowledge",Company,"http://local.test"+path);
        Assert.Equal(path,HttpUtility.ParseQueryString(new Uri("http://local.test"+sibling).Query)["supportReturnUrl"]);
        Assert.Null(SupportJourneyRoutes.NormalizeReturn(parent,Guid.NewGuid())); Assert.Null(SupportJourneyRoutes.NormalizeReturn("//evil.test/",Company));
        Assert.Null(SupportJourneyRoutes.NormalizeReturn($"/supporters?companyId={Company}",Company));
        var priority=DashboardRoutes.BuildPriorityPath(Company,"customers","support-case:"+Case.ToString("N"),overview);
        var source=path+"&priorityReturnUrl="+Uri.EscapeDataString(priority);
        Assert.Equal(priority,HttpUtility.ParseQueryString(new Uri("http://local.test"+SupportJourneyRoutes.Record("/support/knowledge",Company,"http://local.test"+source)).Query)["priorityReturnUrl"]);
        var queue=$"http://local.test/support?companyId={Company}&status=waiting_internal&priority=high&category=technical_issue&owner=mine&q=access";
        var reportScope=HttpUtility.ParseQueryString(new Uri("http://local.test"+SupportJourneyRoutes.Record("/support/reports?view=aging",Company,queue)).Query);
        Assert.Equal("waiting_internal",reportScope["status"]);Assert.Equal("high",reportScope["priority"]);Assert.Equal("technical_issue",reportScope["category"]);Assert.Equal("mine",reportScope["owner"]);Assert.Equal("access",reportScope["q"]);
    }
    [Fact]
    public void Report_empty_and_error_states_offer_recovery_without_stale_rows()
    {
        using var context=Context();context.Services.AddSingleton(Client(_=>Json(Report() with {Cases=[]})));
        context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/support/reports?companyId={Company}&view=aging");
        var cut=context.RenderComponent<ReportPage>();cut.WaitForAssertion(()=>Assert.Contains("No cases match this scope",cut.Markup));
        Assert.Contains("paused owners",cut.Markup);Assert.Contains("Back to support",cut.Markup);
    }
    private static TestContext Context()
    {
        var context=new TestContext().AddVirtualCompanyWebPresentationServices();
        context.Services.AddSingleton(new OnboardingApiClient(new HttpClient {BaseAddress=new("http://localhost/")},useOfflineMode:true));
        return context;
    }
    [Fact]
    public void Unsaved_reply_cannot_be_approved_and_rejection_remains_in_durable_history()
    {
        using var context=Context();
        var draft=new SupportReplyDraftDto(Guid.NewGuid(),Case,"Recorded reply","Helpful","needs_review","Needs review",.6m,.7m,null,"[]",null,null,null,null,null,null,DateTime.UtcNow,DateTime.UtcNow);
        var saved=false;
        context.Services.AddSingleton(Client(request=>{
            var path=request.RequestUri!.AbsolutePath;
            if(request.Method==HttpMethod.Put) { draft=draft with {DraftBody="Reviewed reply"}; saved=true; return Json(draft); }
            if(path.EndsWith("/reject")) { draft=draft with {Status="rejected",StatusLabel="Rejected"}; return Json(draft); }
            if(path.EndsWith("/knowledge")) return Json(new SupportKnowledgeContext(Case,[],[],[],0,"No trusted evidence"));
            if(path.EndsWith("/assignees")) return Json(Array.Empty<SupportAssigneeOptionDto>());
            return Json(new {id=Case,caseNumber="SUP-REVIEW",subject="Review case",summary="Customer question",status="new",statusLabel="New",priority="normal",priorityLabel="Normal",category="general_question",categoryLabel="General question",source="manual",assignedAgentId=Guid.NewGuid(),allowedActions=new[]{"assign"},createdUtc=DateTime.UtcNow,updatedUtc=DateTime.UtcNow,messages=Array.Empty<object>(),events=Array.Empty<object>(),replyDrafts=new[]{draft},refundRequests=Array.Empty<object>(),knowledgeGaps=Array.Empty<object>(),context=new SupportCaseContextSummary(Case,null,null,null,[],0,"Unmatched")});
        }));
        context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/support/cases/{Case}?companyId={Company}");
        var cut=context.RenderComponent<VirtualCompany.Web.Pages.Support.SupportCaseDetail>(p=>p.Add(x=>x.CaseId,Case));
        cut.WaitForAssertion(()=>Assert.Contains("Recorded reply",cut.Markup));
        cut.Find($"#support-draft-{draft.Id:D}").Input("Reviewed reply");
        Assert.True(cut.FindAll("button").Single(x=>x.TextContent=="Approve").HasAttribute("disabled"));
        cut.FindAll("button").Single(x=>x.TextContent=="Save draft").Click();
        cut.WaitForAssertion(()=>Assert.True(saved));
        cut.FindAll("button").Single(x=>x.TextContent=="Reject").Click();
        cut.WaitForAssertion(()=>Assert.Contains("Rejected",cut.Markup));
        Assert.Contains("Reviewed reply",cut.Markup); Assert.DoesNotContain("Request delivery",cut.Markup);
    }
    private static SupportApiClient Client(Func<HttpRequestMessage,HttpResponseMessage> action) => new(new HttpClient(new Handler(action)){BaseAddress=new("http://localhost/")});
    private static SupportOperationalReport Report() => new(Company,"sla",new(2026,10,2,12,0,0,DateTimeKind.Utc),
        new("Europe/Stockholm",new(8,0),new(17,0),[DayOfWeek.Monday],[]),"Current unresolved cases","Waiting and paused owners do not pause targets.",
        [new(new(Case,"SUP-1"," =SUM(1,2)","waiting_internal","Waiting internally","high","High","general_question","General question","manual","Customer",null,null,null,null,new(2026,9,20,12,0,0,DateTimeKind.Utc),new(2026,10,2,11,0,0,DateTimeKind.Utc),null,new(2026,10,2,13,0,0,DateTimeKind.Utc),true,false,false,false),"Specialist",288,"7-30d",new(2026,10,2,13,0,0,DateTimeKind.Utc),true,true)],1,0,1,0,0,1,[new("7-30d","7–30 days",1)]);
    private static HttpResponseMessage Json<T>(T data)=>new(HttpStatusCode.OK){Content=JsonContent.Create(data)};
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> action):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(action(request));}
}
