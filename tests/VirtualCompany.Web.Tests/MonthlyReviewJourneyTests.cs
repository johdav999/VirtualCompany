using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Components.Dashboard;
using VirtualCompany.Web.Services;
namespace VirtualCompany.Web.Tests;
public sealed class MonthlyReviewJourneyTests
{
    public static readonly Guid Company=Guid.NewGuid();
    public static MonthlyWorkspaceViewModel Workspace(string lens="sales",decimal value=12)=>new(Company,new("Northstar","Monthly review","Recorded sources"),lens,
        [new(lens,lens,true,"Primary responsibility")],new(2026,9,"Europe/Stockholm",DateTime.UnixEpoch,DateTime.UnixEpoch.AddDays(30),DateTime.UnixEpoch.AddDays(-30),DateTime.UnixEpoch,"September 2026","August 2026"),
        new("Review next-period risks","Recorded results","1 of 1 sources current",true),
        [new(lens+".movement","Recorded movement",value,value.ToString(),9,"9","events","current",DateTime.UnixEpoch,"recorded_source","/app/sales/pipeline")],[],[],[],[],
        [new("native","Recorded source","current",DateTime.UnixEpoch,"Authorized captured coverage")],DateTime.UnixEpoch,null,false,[],
        Review:new("monthly-management.v1",[new(lens+".movement","Recorded movement",value,9,"events","Retained native event count","actual","recorded_source","/app/sales/pipeline",null,null,null,"No matching monthly target",value-9,null,"Review linked source for the variance")],"Coverage available","Close reviewed in Finance","Source links open current authorized records"));
    public static MonthlyReviewSnapshotViewModel Snapshot(MonthlyWorkspaceViewModel? w=null,Guid? id=null,int revision=1,Guid? previous=null)=>new(
        new(id??Guid.NewGuid(),Company,Guid.NewGuid(),revision,previous,(w??Workspace()).ActiveLens,2026,9,DateTime.UnixEpoch,DateTime.UnixEpoch,"1 of 1 sources current","monthly-management.v1"),w??Workspace(),"Reviewed risk",["Recorded movement changed"],new string('a',64),"Retained until company deletion");
    private static TestContext Context(FakeReviews fake)
    {
        var ctx=new TestContext().AddVirtualCompanyWebPresentationServices();ctx.Services.AddSingleton<IMonthlyReviewApiClient>(fake);
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo(DashboardRoutes.BuildMonthlyPath(Company,"sales",2026,9));return ctx;
    }
    [Theory][InlineData("company")][InlineData("sales")][InlineData("marketing")][InlineData("finance")][InlineData("customers")]
    public void All_role_reviews_show_meaning_target_availability_variance_and_sources(string lens)
    {
        using var ctx=Context(new());var cut=ctx.RenderComponent<MonthlyWorkspace>(p=>p.Add(x=>x.CompanyId,Company).Add(x=>x.Workspace,Workspace(lens)));
        Assert.Contains("No matching monthly target",cut.Markup);Assert.Contains("Results, targets and variances",cut.Markup);
        Assert.Contains("Retained native event count",cut.Markup);Assert.Contains("Close reviewed in Finance",cut.Markup);Assert.Single(cut.FindAll("[data-testid=monthly-review-history]"));
        var source=cut.Find(".monthly-review-table a").GetAttribute("href")!;Assert.Contains("companyId=",source);Assert.Contains("returnUrl=",source);
    }
    [Fact]
    public void Save_and_history_preserve_company_responsibility_period_and_reloadable_identity()
    {
        var fake=new FakeReviews();using var ctx=Context(fake);var cut=ctx.RenderComponent<MonthlyReviewHistory>(p=>p.Add(x=>x.Workspace,Workspace()));
        cut.Find("textarea").Change("Review decision retained");cut.FindAll("button")[0].Click();
        Assert.Equal("Review decision retained",fake.Notes);Assert.Equal(Company,fake.Company);
        Assert.Contains("snapshot="+fake.Saved.Summary.Id,ctx.Services.GetRequiredService<NavigationManager>().Uri);
        var fresh=ctx.RenderComponent<MonthlyReviewHistory>(p=>p.Add(x=>x.Workspace,Workspace()));
        fresh.FindAll("button").Single(x=>x.TextContent=="Saved reviews").Click();
        Assert.Contains("Revision 1",fresh.Markup);Assert.Contains("snapshot="+fake.Saved.Summary.Id,fresh.Find(".review-history-row a").GetAttribute("href"));
    }
    [Fact]
    public void Opened_snapshot_replaces_current_results_and_refresh_creates_new_route()
    {
        var fake=new FakeReviews{Saved=Snapshot(Workspace(value:7))};using var ctx=Context(fake);
        var cut=ctx.RenderComponent<MonthlyWorkspace>(p=>p.Add(x=>x.CompanyId,Company).Add(x=>x.Workspace,Workspace(value:99)).Add(x=>x.SnapshotId,fake.Saved.Summary.Id));
        cut.WaitForAssertion(()=>Assert.Contains("Saved review · Revision 1",cut.Markup));
        Assert.Equal("7",cut.Find(".monthly-result strong").TextContent);Assert.DoesNotContain(">99<",cut.Markup);
        var origin=cut.Find(".monthly-review-table a").GetAttribute("href")!;Assert.Contains("snapshot%3d",origin,StringComparison.OrdinalIgnoreCase);
        cut.FindAll("button").Single(x=>x.TextContent=="Refresh as new revision").Click();
        Assert.Equal(1,fake.ExpectedRevision);Assert.Contains("snapshot="+fake.Refreshed.Summary.Id,ctx.Services.GetRequiredService<NavigationManager>().Uri);
    }
    [Fact]
    public void Saved_company_overview_uses_retained_business_figures_before_current_notes()
    {
        MonthlyWorkspaceViewModel CompanyReview(string cash) => Workspace("company") with {
            AvailableLenses=[new("company","Company",true,"Owner"),new("finance","Finance",false,"Oversight")],
            Sections=[new("finance","Finance","Recorded source","current",DateTime.UnixEpoch,[new("Cash",cash)],[],"/finance","Saved source")]
        };
        var fake=new FakeReviews{Saved=Snapshot(CompanyReview("7 SEK"))};using var ctx=Context(fake);
        var cut=ctx.RenderComponent<MonthlyWorkspace>(p=>p.Add(x=>x.CompanyId,Company).Add(x=>x.Workspace,CompanyReview("99 SEK")).Add(x=>x.SnapshotId,fake.Saved.Summary.Id));
        cut.WaitForAssertion(()=>Assert.Contains("Retained review values",cut.Find("[data-testid=company-period-overview]").TextContent));
        var summary=cut.Find("[data-testid=company-period-overview]");Assert.Contains("7 SEK",summary.TextContent);Assert.DoesNotContain("99 SEK",summary.TextContent);
        Assert.True(cut.Markup.IndexOf("company-period-overview",StringComparison.Ordinal)<cut.Markup.IndexOf("monthly-review-history",StringComparison.Ordinal));
        Assert.Contains("snapshot%3d",summary.QuerySelector("footer a")!.GetAttribute("href"),StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task Late_open_after_company_switch_cannot_restore_old_results()
    {
        var fake=new FakeReviews{PendingOpen=new(TaskCreationOptions.RunContinuationsAsynchronously)};using var ctx=Context(fake);
        MonthlyReviewSnapshotViewModel? selected=null;
        var cut=ctx.RenderComponent<MonthlyReviewHistory>(p=>p.Add(x=>x.Workspace,Workspace()).Add(x=>x.SnapshotId,fake.Saved.Summary.Id)
            .Add(x=>x.Opened,EventCallback.Factory.Create<MonthlyReviewSnapshotViewModel?>(this,x=>selected=x)));
        var foreign=Workspace() with {CompanyId=Guid.NewGuid()};cut.SetParametersAndRender(p=>p.Add(x=>x.Workspace,foreign).Add(x=>x.SnapshotId,null));
        fake.PendingOpen.SetResult(fake.Saved);await cut.InvokeAsync(()=>Task.CompletedTask);
        cut.WaitForAssertion(()=>Assert.Null(selected));Assert.DoesNotContain("Saved review · Revision",cut.Markup);
    }
    [Fact]
    public async Task Late_save_after_departure_cannot_navigate_back_to_overview()
    {
        var fake=new FakeReviews{PendingSave=new(TaskCreationOptions.RunContinuationsAsynchronously)};using var ctx=Context(fake);
        var cut=ctx.RenderComponent<MonthlyReviewHistory>(p=>p.Add(x=>x.Workspace,Workspace()));var pending=cut.FindAll("button")[0].ClickAsync(new MouseEventArgs());
        var navigation=ctx.Services.GetRequiredService<NavigationManager>();navigation.NavigateTo("/work?companyId="+Company);
        fake.PendingSave.SetResult(fake.Saved);await pending;Assert.Contains("/work?",navigation.Uri);
    }
    [Fact]
    public void Conflict_retry_retains_notes_and_request_identity()
    {
        var fake=new FakeReviews{FailSave=true};using var ctx=Context(fake);var cut=ctx.RenderComponent<MonthlyReviewHistory>(p=>p.Add(x=>x.Workspace,Workspace()));
        cut.Find("textarea").Change("Preserve this decision");cut.FindAll("button")[0].Click();Assert.Contains("review has changed",cut.Find("[role=alert]").TextContent);
        var request=fake.Request;fake.FailSave=false;cut.FindAll("button").Single(x=>x.TextContent=="Retry").Click();
        Assert.Equal(request,fake.Request);Assert.Equal("Preserve this decision",fake.Notes);
    }
    [Fact]
    public void Export_reauthorizes_before_blob_handoff_and_access_loss_withholds_saved_view()
    {
        var fake=new FakeReviews();using var ctx=Context(fake);var module=ctx.JSInterop.SetupModule("./js/reportDownload.js");module.SetupVoid("downloadReport",_=>true).SetVoidResult();
        MonthlyReviewSnapshotViewModel? selected=null;
        var cut=ctx.RenderComponent<MonthlyReviewHistory>(p=>p.Add(x=>x.Workspace,Workspace()).Add(x=>x.SnapshotId,fake.Saved.Summary.Id)
            .Add(x=>x.Opened,EventCallback.Factory.Create<MonthlyReviewSnapshotViewModel?>(this,x=>selected=x)));
        cut.FindAll("button").Single(x=>x.TextContent=="Export saved results").Click();Assert.Equal(1,fake.ExportCalls);Assert.Single(module.Invocations);
        fake.DenyExport=true;cut.FindAll("button").Single(x=>x.TextContent=="Export saved results").Click();Assert.Equal(2,fake.ExportCalls);Assert.Single(module.Invocations);
        Assert.Null(selected);Assert.Contains("Access changed",cut.Markup);Assert.DoesNotContain("saved-review-provenance",cut.Markup);
    }
    [Fact]
    public void History_empty_and_paging_have_explicit_access_context()
    {
        var fake=new FakeReviews{EmptyHistory=true};using var ctx=Context(fake);var cut=ctx.RenderComponent<MonthlyReviewHistory>(p=>p.Add(x=>x.Workspace,Workspace()));
        cut.FindAll("button").Single(x=>x.TextContent=="Saved reviews").Click();Assert.Contains("No accessible saved reviews",cut.Markup);
        Assert.True(cut.FindAll("button").Single(x=>x.TextContent=="Previous page").HasAttribute("disabled"));
    }
    [Theory][InlineData(403)][InlineData(404)][InlineData(409)][InlineData(422)][InlineData(503)]
    public async Task Typed_client_maps_safe_recovery_and_never_returns_failed_payload(int status)
    {
        var client=new MonthlyReviewApiClient(new CompanyApiTransport(new HttpClient(new Handler(_=>new((HttpStatusCode)status))) {BaseAddress=new("http://localhost/")}),false);
        if(status==403) await Assert.ThrowsAsync<TodayWorkspaceAccessException>(()=>client.OpenAsync(Company,Guid.NewGuid(),default));
        else if(status==503) await Assert.ThrowsAsync<HttpRequestException>(()=>client.OpenAsync(Company,Guid.NewGuid(),default));
        else await Assert.ThrowsAsync<InvalidOperationException>(()=>client.OpenAsync(Company,Guid.NewGuid(),default));
    }
    [Theory][InlineData("company")][InlineData("id")][InlineData("period")]
    public async Task Typed_client_rejects_mismatched_snapshot_context(string mismatch)
    {
        var saved=Snapshot();var value=mismatch switch {"company"=>saved with {Summary=saved.Summary with {CompanyId=Guid.NewGuid()}},"period"=>saved with {Summary=saved.Summary with {Month=8}},_=>saved};
        var client=new MonthlyReviewApiClient(new CompanyApiTransport(new HttpClient(new Handler(_=>new(HttpStatusCode.OK){Content=JsonContent.Create(value)})) {BaseAddress=new("http://localhost/")}),false);
        await Assert.ThrowsAsync<InvalidDataException>(()=>client.OpenAsync(Company,mismatch=="id"?Guid.NewGuid():saved.Summary.Id,default));
    }
    [Fact]
    public async Task Typed_client_preserves_company_headers_command_body_and_empty_company_guard()
    {
        HttpRequestMessage? sent=null;string? body=null;var saved=Snapshot();
        var handler=new Handler(r=>{sent=r;body=r.Content!.ReadAsStringAsync().GetAwaiter().GetResult();return new(HttpStatusCode.OK){Content=JsonContent.Create(saved)};});
        var client=new MonthlyReviewApiClient(new CompanyApiTransport(new HttpClient(handler){BaseAddress=new("http://localhost/")}),false);var request=Guid.NewGuid();
        await client.SaveAsync(Company,"sales",2026,9,request,"Recorded note",default);
        Assert.Equal(Company.ToString(),sent!.Headers.GetValues("X-Company-Id").Single());Assert.Contains(request.ToString(),body);Assert.Contains("Recorded note",body);
        Assert.Contains("/workspace/monthly/reviews",sent.RequestUri!.AbsolutePath);
        await Assert.ThrowsAsync<ArgumentException>(()=>client.OpenAsync(Guid.Empty,Guid.NewGuid(),default));
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){ct.ThrowIfCancellationRequested();return Task.FromResult(response(request));}
    }
    public sealed class FakeReviews : IMonthlyReviewApiClient
    {
        public MonthlyReviewSnapshotViewModel Saved=Snapshot();public MonthlyReviewSnapshotViewModel Refreshed=Snapshot(revision:2);
        public TaskCompletionSource<MonthlyReviewSnapshotViewModel>? PendingOpen,PendingSave;
        public bool FailSave,DenyExport,EmptyHistory;public Guid Company,Request;public string? Notes;public int ExpectedRevision,ExportCalls;
        public Task<MonthlyReviewHistoryViewModel> ListAsync(Guid company,string lens,int year,int month,int skip,CancellationToken ct)=>Task.FromResult(new MonthlyReviewHistoryViewModel(EmptyHistory?[]:[Saved.Summary],skip,10,false));
        public Task<MonthlyReviewSnapshotViewModel> SaveAsync(Guid company,string lens,int year,int month,Guid request,string notes,CancellationToken ct)
        {Company=company;Request=request;Notes=notes;if(FailSave)throw new InvalidOperationException("This review has changed. Open latest and retry.");return PendingSave?.Task??Task.FromResult(Saved);}
        public Task<MonthlyReviewSnapshotViewModel> OpenAsync(Guid company,Guid id,CancellationToken ct)=>PendingOpen?.Task??Task.FromResult(Saved);
        public Task<MonthlyReviewSnapshotViewModel> RefreshAsync(Guid company,Guid id,int revision,Guid request,string notes,CancellationToken ct)
        {ExpectedRevision=revision;return Task.FromResult(Refreshed with {Summary=Refreshed.Summary with {PreviousId=id}});}
        public Task<MonthlyReviewExportViewModel> ExportAsync(Guid company,Guid id,CancellationToken ct)
        {ExportCalls++;if(DenyExport)throw new TodayWorkspaceAccessException(HttpStatusCode.Forbidden);return Task.FromResult(new MonthlyReviewExportViewModel("review.csv","retained,values"));}
    }
}

