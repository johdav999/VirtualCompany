using System.Net;
using System.Net.Http.Json;
using Bunit;
using VirtualCompany.Api.Tests;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Web.Components.Support;
using VirtualCompany.Web.Pages.Support;
using VirtualCompany.Web.Services;
namespace VirtualCompany.Web.Tests;

public sealed class SupportQualityJourneyTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public void Downloads_import_shared_module_and_report_safe_browser_failure(bool proposal)
    {
        var saved=Saved();
        using var c=Context(r=>r.RequestUri!.AbsolutePath.Contains("/export")
            ?Json(new SupportQualityExport("support.csv","text/csv","Case,Hours\nQUALITY-1,12.5",new string('A',64)))
            :r.RequestUri.AbsolutePath.EndsWith("/proposals")?Json(new[]{saved.Summary})
            :proposal?Json(saved):Json(Report()));
        var module=c.JSInterop.SetupModule("./js/reportDownload.js");
        module.SetupVoid("downloadReport",_=>true).SetException(new Microsoft.JSInterop.JSException("private browser stack"));
        if(proposal)c.Services.GetRequiredService<NavigationManager>().NavigateTo($"/support/reports/capacity?companyId={Company}&year=2026&month=9&category=billing&proposal={saved.Summary.Id}");
        IRenderedFragment cut=proposal?c.RenderComponent<SupportCapacity>():c.RenderComponent<SupportQuality>();
        var label=proposal?"Download original proposal CSV":"Refresh and download CSV";
        cut.WaitForAssertion(()=>Assert.Contains(label,cut.Markup));
        cut.FindAll("button").Single(x=>x.TextContent==label).Click();
        cut.WaitForAssertion(()=>Assert.Contains("The CSV could not be downloaded. Try the download again.",cut.Markup));
        Assert.Single(module.Invocations);Assert.DoesNotContain("private browser stack",cut.Markup);
    }
    private static readonly Guid Company=MonthlyReviewJourneyTests.Company,Case=Guid.NewGuid();
    private static readonly DateTime Date=new(2026,9,1,0,0,0,DateTimeKind.Utc);
    private static SupportQualityQuery Query=>new(2026,9,"billing");
    private static SupportQualityReport Report()=>new(Company,Query,"support-quality.v1",Date.AddMonths(1),Date.AddMonths(1),new(Date,Date.AddMonths(1),Date.AddMonths(1),"Europe/Stockholm"),new("Europe/Stockholm",new(8,0),new(17,0),[DayOfWeek.Monday],[]),new(2,1,1,100,1,60,60,1,120),new(0,0,0,null,0,null,null,0,null),[new(Date.AddMonths(1),null,null,1,1)],[new(Case,"QUALITY-1","Recorded billing issue","billing","billing",false,null,Date,true,true,Date.AddHours(2),Date.AddDays(1),60,60,120,Date,true,false,[new(Guid.NewGuid(),"resolved",Date.AddHours(2),"Recorded resolution")],$"/support/cases/{Case}?companyId={Company}")],[new("billing",false,[Case])],true,"Legacy history unavailable.","Waiting remains in elapsed time.",new string('A',64),"Accountable reviewer");
    private static SupportCapacityProposal Saved(){var r=Report();var a=new SupportCapacityAssumptions(2026,10,20,5,30,1,4,80,60,"Retained capacity explanation");return new(new(Guid.NewGuid(),"Saved Support capacity",Guid.NewGuid(),"Accountable reviewer",2026,9,2026,10,Date,null),new(r,a,new(2,20,5,21,9,12.5m,67.2m,134,0,.19m,"Service target is an assumption, not a guarantee."),new string('B',64)),new string('C',64));}
    private static HttpResponseMessage Json<T>(T value)=>new(HttpStatusCode.OK){Content=JsonContent.Create(value)};
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> action):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)=>action(r,ct);}
    private static TestContext Context(Func<HttpRequestMessage,HttpResponseMessage> action)=>Context((r,_)=>Task.FromResult(action(r)));
    private static TestContext Context(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> action){var c=new TestContext().AddVirtualCompanyWebPresentationServices();var h=new HttpClient(new Handler(action)){BaseAddress=new("http://localhost/")};c.Services.AddSingleton(new SupportQualityApiClient(new CompanyApiTransport(h),false));c.Services.AddSingleton(new SupportApiClient(h,true));c.Services.AddSingleton(new AgentApiClient(h,true));c.Services.AddSingleton(new OnboardingApiClient(h,true));c.Services.AddSingleton<IMonthlyReviewApiClient>(new MonthlyReviewJourneyTests.FakeReviews());c.Services.GetRequiredService<NavigationManager>().NavigateTo($"/support/reports/quality?companyId={Company}&year=2026&month=9&category=billing");return c;}
    [Fact] public void Report_distinguishes_unknown_backlog_and_keeps_case_return_context(){using var c=Context(_=>Json(Report()));var cut=c.RenderComponent<SupportQuality>();cut.WaitForAssertion(()=>Assert.Contains("Recorded billing issue",cut.Markup));Assert.Contains("backlog Unavailable",cut.Markup);Assert.Contains("supportReturnUrl=",cut.Markup);Assert.Contains("60.00",cut.Markup);}
    [Fact] public void Restricted_reload_clears_previous_sources_and_export(){var deny=false;using var c=Context(_=>deny?new(HttpStatusCode.Forbidden):Json(Report()));var cut=c.RenderComponent<SupportQuality>();cut.WaitForAssertion(()=>Assert.Contains("Recorded billing issue",cut.Markup));deny=true;cut.FindAll("button").First(x=>x.TextContent=="Reload").Click();cut.WaitForAssertion(()=>Assert.Contains("Support responsibility is required",cut.Markup));Assert.DoesNotContain("Recorded billing issue",cut.Markup);Assert.DoesNotContain("Refresh and download CSV",cut.Markup);}
    [Fact] public void Saved_capacity_is_read_only_and_keeps_original_assumptions(){var saved=Saved();using var c=Context(r=>r.RequestUri!.AbsolutePath.EndsWith("/proposals")?Json(new[]{saved.Summary}):Json(saved));c.Services.GetRequiredService<NavigationManager>().NavigateTo($"/support/reports/capacity?companyId={Company}&year=2026&month=9&category=billing&proposal={saved.Summary.Id}");var cut=c.RenderComponent<SupportCapacity>();cut.WaitForAssertion(()=>Assert.Contains("Retained capacity explanation",cut.Markup));Assert.Contains("Original retained result",cut.Markup);Assert.Contains("12.50",cut.Markup);Assert.DoesNotContain("Save named proposal",cut.Markup);cut.FindAll("button").Single(x=>x.TextContent=="Create a new revision").Click();Assert.Contains("Save named proposal",cut.Markup);Assert.DoesNotContain("Deterministic preview",cut.Markup);}
    [Fact] public void Monthly_review_uses_retained_sources_and_withholds_live_export(){using var c=Context(_=>Json(Report()));var reviews=(MonthlyReviewJourneyTests.FakeReviews)c.Services.GetRequiredService<IMonthlyReviewApiClient>();reviews.Saved=MonthlyReviewJourneyTests.Snapshot(MonthlyReviewJourneyTests.Workspace("customers") with{SupportQuality=Report()});c.Services.GetRequiredService<NavigationManager>().NavigateTo($"/support/reports/quality?companyId={Company}&year=2026&month=9&category=billing&lens=customers&snapshot={reviews.Saved.Summary.Id}");var cut=c.RenderComponent<SupportQuality>();cut.WaitForAssertion(()=>Assert.Contains("Recorded billing issue",cut.Markup));Assert.DoesNotContain("Refresh and download CSV",cut.Markup);}
    [Fact] public void Blank_category_link_reopens_all_category_monthly_evidence(){using var c=Context(_=>Json(Report()));var reviews=(MonthlyReviewJourneyTests.FakeReviews)c.Services.GetRequiredService<IMonthlyReviewApiClient>();reviews.Saved=MonthlyReviewJourneyTests.Snapshot(MonthlyReviewJourneyTests.Workspace("customers") with{SupportQuality=Report() with{Query=new(2026,9)}});c.Services.GetRequiredService<NavigationManager>().NavigateTo($"/support/reports/quality?companyId={Company}&year=2026&month=9&category=&lens=customers&snapshot={reviews.Saved.Summary.Id}");var cut=c.RenderComponent<SupportQuality>();cut.WaitForAssertion(()=>Assert.Contains("Original retained Support evidence",cut.Markup));Assert.Contains("Recorded billing issue",cut.Markup);}
    [Fact] public async Task Late_company_response_cannot_restore_old_case_sources(){var pending=new TaskCompletionSource<HttpResponseMessage>();var foreign=Guid.NewGuid();var count=0;using var c=Context((_,_)=>++count==1?pending.Task:Task.FromResult(Json(Report() with{CompanyId=foreign,Cases=[]})));var cut=c.RenderComponent<SupportQuality>();await cut.InvokeAsync(()=>c.Services.GetRequiredService<NavigationManager>().NavigateTo($"/support/reports/quality?companyId={foreign}&year=2026&month=9&category=billing"));pending.SetResult(Json(Report()));cut.WaitForAssertion(()=>Assert.DoesNotContain("Recorded billing issue",cut.Markup));}
    [Fact] public void Partial_sample_is_explicit_before_metrics(){using var c=Context(_=>Json(Report()));var cut=c.RenderComponent<SupportQualityEvidence>(p=>p.Add(x=>x.Report,Report() with{CompleteSourceCoverage=false}));Assert.Contains("Full company totals are unavailable",cut.Markup);}
    [Theory][InlineData(400)][InlineData(403)][InlineData(404)][InlineData(409)][InlineData(422)][InlineData(503)]public async Task Typed_errors_fail_closed(int status){using var c=Context(_=>new((HttpStatusCode)status));var api=c.Services.GetRequiredService<SupportQualityApiClient>();if(status==403)await Assert.ThrowsAsync<TodayWorkspaceAccessException>(()=>api.Report(Company,Query,default));else await Assert.ThrowsAsync<InvalidOperationException>(()=>api.Report(Company,Query,default));}
    [Fact] public async Task Wrong_period_and_offline_report_are_rejected(){using var c=Context(_=>Json(Report() with{Query=new(2026,8,"billing")}));await Assert.ThrowsAsync<InvalidDataException>(()=>c.Services.GetRequiredService<SupportQualityApiClient>().Report(Company,Query,default));await Assert.ThrowsAsync<InvalidOperationException>(()=>new SupportQualityApiClient(new CompanyApiTransport(new HttpClient()),true).Report(Company,Query,default));}
}
