using System.Net;
using System.Net.Http.Json;
using System.Web;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Components.Work;
using VirtualCompany.Web.Pages;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class AgentWorkJourneyTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime Now = new(2026,10,2,12,0,0,DateTimeKind.Utc);
    private static AgentWorkItemDto Item(string state="blocked") => new("task",Id,"Review retained renewal terms","Confirm signed customer terms","finance",state,"blocked","Johan",
        [new(Guid.NewGuid(),"Laura","Finance reviewer")],"Review terms","Obtain the signed customer terms",Now.AddDays(-1),Now,null,
        $"/agents/work/task/{Id}?companyId={Company}",$"/work?companyId={Company}&tab=tasks&taskId={Id}",
        [new("Open in Work",$"/work?companyId={Company}&tab=tasks&taskId={Id}")],[],[new("Retained rationale",$"/work?companyId={Company}&taskId={Id}",Now)],[]);
    private static AgentWorkBoardDto Board() => new(Company,"North",Now,[Item()],AgentWorkStates.All.ToDictionary(x=>x,x=>x=="blocked"?1:0),
        [],["finance"],1,0,24,false,true,["Counts describe the bounded source window"]);

    [Fact]
    public async Task Typed_client_uses_company_transport_and_escapes_filters()
    {
        var handler=new Handler(_=>new(HttpStatusCode.OK){Content=JsonContent.Create(Board())});
        using var http=new HttpClient(handler){BaseAddress=new("http://localhost/")};
        var result=await new AgentWorkApiClient(new CompanyApiTransport(http)).ListAsync(new(Company,"finance",Id,"terms & review","blocked",24, PerState: true));
        Assert.Equal(Company,result!.CompanyId); Assert.Contains("objective=terms%20%26%20review",handler.Request!.RequestUri!.OriginalString);
        Assert.Contains("skip=24",handler.Request.RequestUri.OriginalString); Assert.Equal(Company.ToString("D"),handler.Request.Headers.GetValues("X-Company-Id").Single());
        Assert.True(handler.Request.Headers.Contains("X-Correlation-Id"));
        Assert.Contains("perState=true", handler.Request.RequestUri.OriginalString);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)] [InlineData(HttpStatusCode.NotFound)] [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Unavailable_or_revoked_access_drops_old_evidence(HttpStatusCode status)
    {
        var transport=new Transport(_=>Task.FromResult(new HttpResponseMessage(status)));
        Assert.Null(await new AgentWorkApiClient(transport).GetAsync(Company,"task",Id));
    }

    [Theory]
    [InlineData("malformed")] [InlineData("incomplete")] [InlineData("failure")] [InlineData("timeout")]
    public async Task Failed_refresh_is_retryable_and_never_returns_synthetic_work(string failure)
    {
        var transport=new Transport(_=> failure=="timeout" ? Task.FromException<HttpResponseMessage>(new TaskCanceledException()) :
            Task.FromResult(new HttpResponseMessage(failure=="failure"?HttpStatusCode.ServiceUnavailable:HttpStatusCode.OK){Content=new StringContent(failure=="incomplete"?"{}":"invalid JSON")}));
        await Assert.ThrowsAsync<OnboardingApiException>(()=>new AgentWorkApiClient(transport).ListAsync(new(Company)));
        await Assert.ThrowsAsync<OnboardingApiException>(()=>new AgentWorkApiClient(transport,true).ListAsync(new(Company)));
    }

    [Fact]
    public async Task Cancellation_is_preserved_for_a_superseded_read()
    {
        using var cts=new CancellationTokenSource(); cts.Cancel();
        var client=new AgentWorkApiClient(new Transport(token=>Task.FromCanceled<HttpResponseMessage>(token)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>client.GetAsync(Company,"task",Id,cts.Token));
    }

    [Fact]
    public void Board_detail_Work_round_trip_preserves_exact_filtered_board_and_Overview_with_bounded_parents()
    {
        var overview=$"/dashboard?companyId={Company}&lens=finance&view=monthly&year=2026&month=9";
        var board=DashboardRoutes.WithQuery("/agents/staff",("companyId",Company.ToString()),("responsibility","finance"),("agentId",Id.ToString()),("objective","signed terms"),("skip","24"),("returnUrl",overview));
        var detail=AgentWorkRoutes.Detail("task",Id,Company,"http://localhost"+board);
        var work=AgentWorkRoutes.Related($"/work?companyId={Company}&tab=tasks&taskId={Id}",Company,"http://localhost"+detail);
        var returned=AgentWorkRoutes.Detail("task",Id,Company,"http://localhost"+work);
        var query=HttpUtility.ParseQueryString(new Uri("http://localhost"+returned).Query);
        Assert.Equal(board,query["boardReturnUrl"]); Assert.Equal(overview,query["returnUrl"]);
        Assert.Equal(detail,AgentWorkRoutes.Back("http://localhost"+work,Company));
        var business=AgentWorkRoutes.Related($"/support/cases/{Id}?companyId={Company}",Company,"http://localhost"+detail);
        Assert.Equal(detail,AgentWorkRoutes.Back("http://localhost"+business,Company));
        Assert.Null(AgentWorkRoutes.Back($"http://localhost/work?companyId={Company}&agentWorkReturnUrl="+Uri.EscapeDataString($"/agents/work/task/{Id}?companyId={Guid.NewGuid()}"),Company));
        for(var i=0;i<12;i++) returned=AgentWorkRoutes.Detail("task",Id,Company,"http://localhost"+AgentWorkRoutes.Related($"/work?companyId={Company}&taskId={Id}",Company,"http://localhost"+returned));
        Assert.True(returned.Length<12000); Assert.Equal(board,HttpUtility.ParseQueryString(new Uri("http://localhost"+returned).Query)["boardReturnUrl"]);
        Assert.Null(AgentWorkRoutes.Local("https://unsafe.example/",Company)); Assert.Null(AgentWorkRoutes.Board($"/agents/staff?companyId={Guid.NewGuid()}",Company));
    }

    [Fact]
    public void Board_exposes_seven_states_shared_identity_partial_counts_and_actionable_dependency()
    {
        using var context=new TestContext().AddVirtualCompanyWebPresentationServices();
        var cut=context.RenderComponent<AgentWorkBoard>(p=>p.Add(x=>x.Data,Board()).Add(x=>x.Location,$"http://localhost/agents/staff?companyId={Company}&objective=terms"));
        Assert.Single(cut.FindAll("[data-testid='agent-work-item']")); Assert.Equal(7,cut.FindAll(".work-stage-counts strong").Count);
        Assert.Contains("Obtain the signed customer terms",cut.Markup); Assert.Contains("bounded source window",cut.Markup);
        Assert.Contains(Id.ToString(),cut.Find("article a").GetAttribute("href")); Assert.Contains("boardReturnUrl",cut.Find("article a").GetAttribute("href"));
    }

    [Fact]
    public void Kanban_groups_each_outcome_once_by_recorded_state_and_keeps_completed_work_in_its_own_lane()
    {
        using var context=new TestContext().AddVirtualCompanyWebPresentationServices();
        var items=AgentWorkStates.All.Select((state,i)=>Item(state) with {Id=Guid.NewGuid(),Title=$"Outcome {i}"}).ToArray();
        var board=Board() with {Items=items,StateCounts=AgentWorkStates.All.ToDictionary(x=>x,_=>1),Total=7};
        var cut=context.RenderComponent<AgentWorkBoard>(p=>p.Add(x=>x.Data,board).Add(x=>x.Location,$"http://localhost/agents/staff?companyId={Company}"));
        Assert.Equal(7,cut.FindAll(".work-lane").Count);Assert.Equal(7,cut.FindAll("[data-testid='agent-work-item']").Count);
        foreach(var item in items)
        {
            var lane=cut.Find($"[data-state='{item.State}']");
            Assert.Single(lane.QuerySelectorAll("[data-testid='agent-work-item']"));Assert.Contains(item.Title,lane.TextContent);
        }
        Assert.DoesNotContain("Outcome 3",cut.Find("[data-state='in_progress']").TextContent);
        Assert.Equal("0",cut.Find("[data-testid='agent-work-kanban']").GetAttribute("tabindex"));
    }

    [Fact]
    public void Kanban_distinguishes_page_slice_from_total_and_state_link_resets_page_preserving_filters_and_overview()
    {
        using var context=new TestContext().AddVirtualCompanyWebPresentationServices();
        var overview=DashboardRoutes.BuildTodayPath(Company,"company");
        var location=DashboardRoutes.WithQuery("/agents/staff",("companyId",Company.ToString()),("responsibility","finance"),("objective","signed terms"),("agentId",Id.ToString()),("skip","24"),("returnUrl",overview));
        var board=Board() with {StateCounts=AgentWorkStates.All.ToDictionary(x=>x,x=>x=="in_progress"?8:x=="blocked"?1:0)};
        var cut=context.RenderComponent<AgentWorkBoard>(p=>p.Add(x=>x.Data,board).Add(x=>x.Location,"http://localhost"+location));
        var lane=cut.Find("[data-state='in_progress']");
        Assert.Equal("8",lane.QuerySelector("strong")!.TextContent);Assert.Contains("0 on this page",lane.TextContent);Assert.Contains("another page",lane.TextContent);
        var query=HttpUtility.ParseQueryString(new Uri("http://localhost"+lane.QuerySelector("a")!.GetAttribute("href")).Query);
        Assert.Equal("in_progress",query["state"]);Assert.Equal("0",query["skip"]);Assert.Equal("finance",query["responsibility"]);Assert.Equal("signed terms",query["objective"]);Assert.Equal(Id.ToString(),query["agentId"]);Assert.Equal(overview,query["returnUrl"]);
        cut.SetParametersAndRender(p=>p.Add(x=>x.Data,board with {Items=[],Total=0,StateCounts=AgentWorkStates.All.ToDictionary(x=>x,_=>0)}));
        Assert.Contains("No work matches these filters",cut.Markup);Assert.Empty(cut.FindAll("[data-testid='agent-work-item']"));
        Assert.Equal(7,cut.FindAll(".work-lane__empty").Count);
    }

    [Fact]
    public void Detail_retry_refresh_and_company_change_clear_prior_evidence_and_preserve_safe_returns()
    {
        using var context=new TestContext().AddVirtualCompanyWebPresentationServices(); var reads=0;
        context.Services.AddSingleton(new AgentWorkApiClient(new Transport(_=>Task.FromResult(++reads==1 ?
            new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(Item())} : new HttpResponseMessage(HttpStatusCode.Forbidden)))));
        context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/agents/work/task/{Id}?companyId={Company}");
        var cut=context.RenderComponent<AgentWorkDetail>(p=>p.Add(x=>x.Kind,"task").Add(x=>x.Id,Id));
        cut.WaitForAssertion(()=>Assert.Contains("Review retained renewal terms",cut.Markup));
        Assert.Contains("Completion not recorded",cut.Markup); Assert.Contains("No retained output evidence",cut.Markup);
        cut.Find("button").Click(); cut.WaitForAssertion(()=>Assert.Contains("Work unavailable",cut.Markup));
        Assert.DoesNotContain("Review retained renewal terms",cut.Markup);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/agents/work/task/{Id}?companyId={Guid.NewGuid()}");
        cut.WaitForAssertion(()=>Assert.Contains("Work unavailable",cut.Markup)); Assert.DoesNotContain("Laura",cut.Markup);
    }

    [Fact]
    public void Per_state_lane_pages_keep_filters_and_do_not_label_an_exhausted_page_as_no_work()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var overview = DashboardRoutes.BuildTodayPath(Company, "company");
        var location = DashboardRoutes.WithQuery("/agents/staff", ("companyId", Company.ToString()), ("responsibility", "finance"),
            ("objective", "signed terms"), ("agentId", Id.ToString()), ("returnUrl", overview));
        var items = Enumerable.Range(1, 24).Select(i => Item("completed") with { Id = Guid.NewGuid() }).Append(Item("in_progress")).ToArray();
        var board = Board() with { Items = items, StateCounts = AgentWorkStates.All.ToDictionary(x => x, x => x == "completed" ? 55 : x == "in_progress" ? 1 : 0), Total = 56 };
        var cut = context.RenderComponent<AgentWorkBoard>(p => p.Add(x => x.Data, board).Add(x => x.Location, "http://localhost" + location).Add(x => x.PerStatePages, true));
        Assert.Contains("1–1 of 1", cut.Find("[data-state='in_progress']").TextContent);
        Assert.Single(cut.FindAll("[data-state='in_progress'] article"));
        var next = cut.Find("[data-state='completed'] .work-lane__pagination a");
        var query = HttpUtility.ParseQueryString(new Uri("http://localhost" + next.GetAttribute("href")).Query);
        Assert.Equal("completed", query["state"]); Assert.Equal("24", query["skip"]);
        Assert.Equal("finance", query["responsibility"]); Assert.Equal("signed terms", query["objective"]);
        Assert.Equal(Id.ToString(), query["agentId"]); Assert.Equal(overview, query["returnUrl"]);
        cut.SetParametersAndRender(p => p.Add(x => x.Data, board with { Items = [], Skip = 72 }));
        Assert.DoesNotContain("No work matches these filters", cut.Markup);
        Assert.Contains("No work on this page", cut.Markup);
        Assert.Equal("Previous page", cut.Find("[data-state='completed'] .work-lane__pagination a").TextContent);
    }

    [Fact]
    public void Board_apply_and_refresh_retain_visible_filters_and_exact_url()
    {
        using var context=new TestContext().AddVirtualCompanyWebPresentationServices();
        context.Services.AddSingleton(new OnboardingApiClient(new HttpClient {BaseAddress=new("http://localhost/")},true));
        var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = JsonContent.Create(Board()) });
        context.Services.AddSingleton(new AgentWorkApiClient(new CompanyApiTransport(new HttpClient(handler) { BaseAddress = new("http://localhost/") })));
        var navigation=context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/agents/staff?companyId={Company}&responsibility=finance&objective=terms");
        var cut=context.RenderComponent<AgentStaffOverview>();
        cut.WaitForAssertion(()=>Assert.Equal("finance",cut.FindAll("select")[0].GetAttribute("value")));
        Assert.Contains("perState=true", handler.Request!.RequestUri!.OriginalString);
        Assert.Empty(cut.FindAll("nav[aria-label='Work pages']"));
        cut.Find("input").Input("signed terms"); cut.Find("form").Submit();
        Assert.Equal("signed terms",HttpUtility.ParseQueryString(new Uri(navigation.Uri).Query)["objective"]);
        cut.Find("header button").Click();
        cut.WaitForAssertion(()=>Assert.Equal("finance",cut.FindAll("select")[0].GetAttribute("value")));
        Assert.Equal("signed terms",cut.Find("input").GetAttribute("value"));
    }

    private sealed class Transport(Func<CancellationToken,Task<HttpResponseMessage>> send):ICompanyApiTransport
    {
        public Uri? BaseAddress=>new("http://localhost/");
        public Task<HttpResponseMessage> SendAsync(Guid companyId,HttpMethod method,string uri,HttpContent? content,CancellationToken cancellationToken)=>send(cancellationToken);
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> send):HttpMessageHandler
    {
        public HttpRequestMessage? Request {get;private set;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Request=request;return Task.FromResult(send(request));}
    }
}
