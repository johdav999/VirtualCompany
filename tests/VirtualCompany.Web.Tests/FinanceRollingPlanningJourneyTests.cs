using System.Net;
using System.Net.Http.Json;
using Bunit;
using VirtualCompany.Api.Tests;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Web.Components.Finance;
using VirtualCompany.Web.Pages.Finance;
using VirtualCompany.Web.Services;
using ComparisonPage=VirtualCompany.Web.Pages.Finance.FinanceForecastComparison;
namespace VirtualCompany.Web.Tests;

public sealed class FinanceRollingPlanningJourneyTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public void Downloads_import_shared_module_after_fresh_authorization(bool compare)
    {
        var first=Saved();var later=Saved();var deny=false;
        using var c=Context(r=>r.RequestUri!.AbsolutePath.Contains("/export")
            ?deny?new(HttpStatusCode.Forbidden):Json(new VirtualCompany.Application.Finance.FinancePlanningExport("finance.csv","Account,Value\nRevenue,-1300",new string('A',64)))
            :r.RequestUri.AbsolutePath.EndsWith("/versions")?Json(new[]{first.Summary,later.Summary})
            :compare?Json(new VirtualCompany.Application.Finance.FinanceForecastComparison(first,later,[])):Json(Report()));
        var module=c.JSInterop.SetupModule("./js/reportDownload.js");
        module.SetupVoid("downloadReport",_=>true).SetVoidResult();
        if(compare)c.Services.GetRequiredService<NavigationManager>().NavigateTo($"/finance/reports/forecast-comparison?companyId={Company}&earlier={first.Summary.Id}&later={later.Summary.Id}");
        IRenderedFragment cut=compare?c.RenderComponent<ComparisonPage>():c.RenderComponent<FinanceVariance>();
        var label=compare?"Download comparison CSV":"Download CSV";
        cut.WaitForAssertion(()=>Assert.Contains(label,cut.Markup));
        cut.FindAll("button").Single(x=>x.TextContent==label).Click();
        cut.WaitForAssertion(()=>Assert.Single(module.Invocations));
        Assert.Equal("finance.csv",module.Invocations.Single().Arguments[0]);
        deny=true;cut.FindAll("button").Single(x=>x.TextContent==label).Click();
        cut.WaitForAssertion(()=>Assert.Contains("Finance access changed",cut.Markup));
        Assert.Single(module.Invocations);Assert.DoesNotContain(label,cut.Markup);
    }
    private static readonly Guid Company=MonthlyReviewJourneyTests.Company,Account=Guid.NewGuid();private static readonly DateTime Date=new(2026,9,1,0,0,0,DateTimeKind.Utc);
    private static FinancePlanningQuery Query=>new(2026,9,4,"approved",null,"SEK",Account);
    private static FinancePlanningReport Report()=>new(Company,Query,Date,"finance-rolling-planning.v1",new string('A',64),
        [new(Date,Account,"3000","Recorded revenue",null,"SEK",-1000,-1100,null,-900,100,-9.09m),new(Date.AddMonths(2),Account,"3000","Recorded revenue",null,"SEK",null,-1200,null,null,null,null)],
        [new(Guid.NewGuid(),"actual",Account,"3000","Recorded revenue",Date,null,-1000,"SEK",null,Date,"Posted native journal",$"/finance/accounting/journals?companyId={Company}&journalId={Guid.NewGuid()}")],
        ["approved"],[],[new(Account,"3000 · Recorded revenue")],[],[],["No cash timing or opening balance conversion."],[]);
    private static FinanceForecastRevision Saved(){var r=Report();var input=new PreviewFinanceForecast(Query,Date.AddMonths(1),[new(Date.AddMonths(2),Account,null,"SEK",-1300,"Retained assumption")],"Original forecast explanation");return new(new(Guid.NewGuid(),"Saved outlook","saved-native",null,Guid.NewGuid(),Date,Date),new(r,input,[new(Date,Account,null,"SEK",-1000,-1100,null,"Actual retained"),new(Date.AddMonths(2),Account,null,"SEK",null,-1200,-1300,"Retained assumption")],new string('B',64)),new string('C',64));}
    private static HttpResponseMessage Json<T>(T value)=>new(HttpStatusCode.OK){Content=JsonContent.Create(value)};
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> action):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)=>action(r,ct);}
    private static TestContext Context(Func<HttpRequestMessage,HttpResponseMessage> action)=>Context((r,_)=>Task.FromResult(action(r)));
    private static TestContext Context(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> action){var c=new TestContext().AddVirtualCompanyWebPresentationServices();var http=new HttpClient(new Handler(action)){BaseAddress=new("http://localhost/")};c.Services.AddSingleton(new FinanceRollingPlanningApiClient(new CompanyApiTransport(http),false));c.Services.AddSingleton(new OnboardingApiClient(http,useOfflineMode:true));c.Services.AddSingleton<IMonthlyReviewApiClient>(new MonthlyReviewJourneyTests.FakeReviews());c.Services.GetRequiredService<NavigationManager>().NavigateTo($"/finance/reports/variance?companyId={Company}&"+FinanceRollingPlanningApiClient.Parameters(Query));return c;}
    [Fact]public void Evidence_keeps_unavailable_actuals_and_distinct_signed_values(){using var c=Context(_=>Json(Report()));var cut=c.RenderComponent<FinancePlanningEvidence>(p=>p.Add(x=>x.Report,Report()));Assert.Contains("Unavailable",cut.Markup);Assert.Contains((-1000m).ToString("N2"),cut.Markup);Assert.Contains("Prior year",cut.Markup);Assert.Contains("Scenario: unavailable",cut.Markup);}
    [Fact]public void Source_drilldown_and_note_preserve_report_return_context(){using var c=Context(_=>Json(Report()));var cut=c.RenderComponent<FinanceVariance>();cut.WaitForAssertion(()=>Assert.Contains("Recorded revenue",cut.Markup));cut.FindAll("button").First(x=>x.TextContent=="Inspect sources").Click();Assert.Contains("Posted native journal",cut.Markup);Assert.Contains("financeReturnUrl=",cut.Markup);Assert.Contains("Save explanation",cut.Markup);}
    [Fact]public void Restricted_reload_withholds_previous_rows_and_export(){var deny=false;using var c=Context(_=>deny?new(HttpStatusCode.Forbidden):Json(Report()));var cut=c.RenderComponent<FinanceVariance>();cut.WaitForAssertion(()=>Assert.Contains("Recorded revenue",cut.Markup));deny=true;cut.FindAll("button").First(x=>x.TextContent=="Refresh").Click();cut.WaitForAssertion(()=>Assert.Contains("Finance access changed",cut.Markup));Assert.DoesNotContain("Recorded revenue",cut.Markup);Assert.DoesNotContain("Download CSV",cut.Markup);}
    [Fact]public void Reopened_forecast_keeps_original_values_and_assumptions_read_only(){var saved=Saved();using var c=Context(r=>r.RequestUri!.AbsolutePath.EndsWith("/versions")?Json(new[]{saved.Summary}):Json(saved));c.Services.GetRequiredService<NavigationManager>().NavigateTo($"/finance/reports/rolling-forecast?companyId={Company}&version={saved.Summary.Id}");var cut=c.RenderComponent<FinanceRollingForecast>();cut.WaitForAssertion(()=>Assert.Contains("Original retained preview",cut.Markup));Assert.Contains("Original forecast explanation",cut.Markup);Assert.Contains("Retained assumption",cut.Markup);Assert.Contains((-1300m).ToString("N2"),cut.Markup);Assert.DoesNotContain("Save named version",cut.Markup);Assert.Contains("Create a new revision",cut.Markup);}
    [Fact]public async Task A_late_company_response_cannot_restore_old_evidence(){var pending=new TaskCompletionSource<HttpResponseMessage>();var foreign=Guid.NewGuid();var count=0;using var c=Context((_,_)=>++count==1?pending.Task:Task.FromResult(Json(Report() with{CompanyId=foreign,Rows=[],Accounts=[],Sources=[]})));var cut=c.RenderComponent<FinanceVariance>();await cut.InvokeAsync(()=>c.Services.GetRequiredService<NavigationManager>().NavigateTo($"/finance/reports/variance?companyId={foreign}&"+FinanceRollingPlanningApiClient.Parameters(Query)));pending.SetResult(Json(Report()));cut.WaitForAssertion(()=>Assert.DoesNotContain("Recorded revenue",cut.Markup));}
    [Fact]public void Snapshot_report_is_retained_and_cannot_export_current_rows(){using var c=Context(_=>Json(Report()));var reviews=(MonthlyReviewJourneyTests.FakeReviews)c.Services.GetRequiredService<IMonthlyReviewApiClient>();reviews.Saved=MonthlyReviewJourneyTests.Snapshot(MonthlyReviewJourneyTests.Workspace("finance") with{FinancePlanning=Report()});var uri=$"/finance/reports/variance?companyId={Company}&year=2026&month=9&lens=finance&snapshot={reviews.Saved.Summary.Id}";c.Services.GetRequiredService<NavigationManager>().NavigateTo(uri);var cut=c.RenderComponent<FinanceVariance>();cut.WaitForAssertion(()=>Assert.Contains("Original monthly review evidence",cut.Markup));Assert.DoesNotContain("Download CSV",cut.Markup);Assert.EndsWith(uri,cut.Find("[aria-current='page']").GetAttribute("href"));Assert.All(cut.FindAll("form input,form select,form button"),x=>Assert.True(x.HasAttribute("disabled")));}
    [Fact]public void Empty_comparison_explains_absent_baselines_and_retains_all_coverage()
    {
        var q=Query with{BudgetVersion=null,ForecastVersion=null};var r=Report() with{Query=q,Rows=[],Sources=[],Coverage=["Posted ledger has no matching rows.","No approved baseline is recorded."]};
        using var c=Context(_=>Json(r));c.Services.GetRequiredService<NavigationManager>().NavigateTo($"/finance/reports/variance?companyId={Company}&"+FinanceRollingPlanningApiClient.Parameters(q));
        var cut=c.RenderComponent<FinanceVariance>();cut.WaitForAssertion(()=>Assert.Contains("No matching sources",cut.Markup));
        Assert.Equal(2,cut.FindAll(".variance-summary dd").Count(x=>x.TextContent=="No version selected"));
        Assert.Empty(cut.FindAll("table"));Assert.Contains("Select a recorded budget to calculate variance",cut.Markup);
        Assert.Contains(r.Coverage[0],cut.Find(".variance-coverage details").TextContent);Assert.Contains(r.Coverage[1],cut.Find(".variance-coverage details").TextContent);
        Assert.Equal("#budget-version",cut.Find(".planning-empty-actions a:last-child").GetAttribute("href"));
        Assert.DoesNotContain("Laura",cut.Markup);
    }
    [Fact]public void Applied_filter_context_is_distinct_from_pending_changes_and_survives_navigation()
    {
        var q=Query with{Months=2,Currency="EUR",BudgetVersion=null};using var c=Context(r=>Json(Report() with{Query=r.RequestUri!.Query.Contains("months=2")?q:Query}));
        var cut=c.RenderComponent<FinanceVariance>();cut.WaitForAssertion(()=>Assert.Contains("approved",cut.Find(".variance-summary").TextContent));
        cut.Find("#budget-version").Change("");cut.Find("input[maxlength='3']").Change("eur");cut.FindAll("input[type='number']")[1].Change("2");
        Assert.Contains("approved",cut.Find(".variance-summary").TextContent);
        cut.Find("form").Submit();cut.WaitForAssertion(()=>Assert.Contains("No version selected",cut.Find(".variance-summary").TextContent));
        var uri=c.Services.GetRequiredService<NavigationManager>().Uri;Assert.Contains("months=2",uri);Assert.Contains("currency=EUR",uri);Assert.DoesNotContain("budgetVersion=",uri);
        Assert.Contains(Date.ToString("MMM yyyy"),cut.Find(".variance-summary").TextContent);Assert.Contains(Date.AddMonths(1).ToString("MMM yyyy"),cut.Find(".variance-summary").TextContent);
    }
    [Fact]public void Populated_comparison_exposes_signed_measures_and_accessible_source_actions()
    {
        using var c=Context(_=>Json(Report()));var cut=c.RenderComponent<FinanceVariance>();cut.WaitForAssertion(()=>Assert.Equal(2,cut.FindAll("table tbody tr").Count));
        Assert.Equal(11,cut.FindAll("thead th[scope='col']").Count);Assert.Equal(2,cut.FindAll("tbody th[scope='row']").Count);
        Assert.Contains((-9.09m).ToString("N2")+"%",cut.Find("table").TextContent);Assert.Contains("Unavailable",cut.FindAll("tbody tr")[1].TextContent);
        Assert.Equal("0",cut.Find(".planning-scroll").GetAttribute("tabindex"));cut.FindAll("button").First(x=>x.TextContent=="Inspect sources").Click();
        Assert.Contains("Posted native journal",cut.Find(".source-panel").TextContent);cut.Find(".source-panel button").Click();Assert.Empty(cut.FindAll(".source-panel"));
    }
    [Fact]public void Oversized_query_keeps_scoped_filter_choices_for_recovery_without_old_rows()
    {
        var reject=false;using var c=Context(_=>reject?new(HttpStatusCode.BadRequest){Content=JsonContent.Create(new{detail="Budgets exceeds 2000 records. Narrow the account, dimension or period filters."})}:Json(Report()));
        var cut=c.RenderComponent<FinanceVariance>();cut.WaitForAssertion(()=>Assert.Equal(2,cut.FindAll("tbody tr").Count));reject=true;
        cut.FindAll("button").First(x=>x.TextContent=="Refresh").Click();cut.WaitForAssertion(()=>Assert.Contains("Narrow the account",cut.Find("[role='alert']").TextContent));
        Assert.Empty(cut.FindAll("table,.variance-summary,.source-panel"));Assert.Equal(2,cut.FindAll("select[aria-label='Account'] option").Count);Assert.Equal(2,cut.FindAll("#budget-version option").Count);Assert.DoesNotContain("Download CSV",cut.Markup);
        reject=false;cut.Find("[role='alert'] button").Click();cut.WaitForAssertion(()=>Assert.Equal(2,cut.FindAll("tbody tr").Count));
    }
    [Fact]public void First_load_failure_keeps_query_range_editable_and_company_change_clears_old_choices()
    {
        var reject=true;using var c=Context(_=>reject?new(HttpStatusCode.BadRequest):Json(Report()));var cut=c.RenderComponent<FinanceVariance>();cut.WaitForAssertion(()=>Assert.Contains("Finance comparison unavailable",cut.Markup));
        Assert.Equal("2026",cut.FindAll("input[type='number']")[0].GetAttribute("value"));Assert.Equal("4",cut.FindAll("input[type='number']")[1].GetAttribute("value"));
        reject=false;cut.Find("[role='alert'] button").Click();cut.WaitForAssertion(()=>Assert.Contains("Recorded revenue",cut.Markup));reject=true;
        c.Services.GetRequiredService<NavigationManager>().NavigateTo($"/finance/reports/variance?companyId={Guid.NewGuid()}&"+FinanceRollingPlanningApiClient.Parameters(Query));
        cut.WaitForAssertion(()=>Assert.Contains("Finance comparison unavailable",cut.Markup));Assert.DoesNotContain("Recorded revenue",cut.Markup);Assert.Single(cut.FindAll("select[aria-label='Account'] option"));
    }
    [Theory][InlineData(400)][InlineData(403)][InlineData(404)][InlineData(409)][InlineData(422)][InlineData(503)]public async Task Typed_client_fails_closed_on_status_errors(int code){using var c=Context(_=>new((HttpStatusCode)code));var api=c.Services.GetRequiredService<FinanceRollingPlanningApiClient>();if(code==403)await Assert.ThrowsAsync<TodayWorkspaceAccessException>(()=>api.Report(Company,Query,default));else await Assert.ThrowsAsync<InvalidOperationException>(()=>api.Report(Company,Query,default));}
    [Fact]public async Task Typed_client_validates_company_filters_and_offline_state(){using var c=Context(_=>Json(Report() with{Query=Query with{Currency="EUR"}}));var api=c.Services.GetRequiredService<FinanceRollingPlanningApiClient>();await Assert.ThrowsAsync<InvalidDataException>(()=>api.Report(Company,Query,default));await Assert.ThrowsAsync<InvalidOperationException>(()=>new FinanceRollingPlanningApiClient(new CompanyApiTransport(new HttpClient()),true).Report(Company,Query,default));}
}
