using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Components.Dashboard;
using VirtualCompany.Web.Pages;
using VirtualCompany.Web.Services;
namespace VirtualCompany.Web.Tests;
public sealed class CompanyHealthTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static TodayWorkspaceViewModel Workspace() => TodayPriorityChangesTests.Workspace() with {
        ActiveLens="company", AvailableLenses=[new("company","Company",true,"Oversight"),new("sales","Sales",false,"Oversight")],
        CompanyRisks=TodayPriorityChangesTests.Workspace().Priorities,
        Departments=[new("sales","Sales owner",null,true)],
        Sales=new(true,"Recorded",DateTime.UtcNow,12000,"SEK",0,0,1,3000,[],"/app/sales") };

    private static TodayWorkspaceViewModel AllDepartments()
    {
        var w = Workspace(); var now = w.GeneratedAtUtc;
        return w with {
            AvailableLenses=[..w.AvailableLenses,new("finance","Finance",false,"Assigned"),new("marketing","Marketing",false,"Assigned"),new("customers","Support",false,"Assigned")],
            Finance=new(true,"Partial",now,12000,"SEK",null,"attention",0,[],"/finance",OverdueReceivables:3,DuePayables:2,ReconciliationExceptions:1,CoverageGaps:["Missing balance snapshot"]),
            Marketing=new(true,"Recorded",now,0,0,2,0,[],"/marketing",DueLaunches:1,SpendExceptions:3,AttributionGaps:4),
            Support=new(true,"Recorded",now,8,1,0,2,1,[],"/support",WaitingCases:3)
        };
    }

    [Fact] public void Department_cards_show_source_indicators_and_separate_pipeline_from_weighted_forecast()
    {
        using var ctx = new TestContext().AddVirtualCompanyWebPresentationServices();
        var cut = ctx.RenderComponent<CompanyHealthSummary>(p=>p.Add(x=>x.Workspace,AllDepartments()));
        var finance=cut.Find("[data-department='finance']");
        Assert.Equal(new[]{"3","2","1"},finance.QuerySelectorAll("dd").Select(x=>x.TextContent));
        Assert.Contains("Some Finance source evidence is missing",finance.TextContent);
        var sales=cut.Find("[data-department='sales']");
        Assert.Equal("SEK 12,000.00",sales.QuerySelector("dd")!.TextContent);
        Assert.Equal("SEK 3,000.00",cut.Find("[data-testid='health-forecast']").TextContent);
        Assert.Equal(new[]{"1","2","3","4"},cut.Find("[data-department='marketing']").QuerySelectorAll("dd").Select(x=>x.TextContent));
        Assert.Equal(new[]{"8","1","2","3","1"},cut.Find("[data-department='customers']").QuerySelectorAll("dd").Select(x=>x.TextContent));
        Assert.DoesNotContain("Observed</span>",cut.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Finance_indicator_links_open_owning_screens_with_company_and_exact_overview_return_even_for_zero_counts(bool zeroCounts)
    {
        using var ctx = new TestContext().AddVirtualCompanyWebPresentationServices();
        var workspace = AllDepartments();
        if (zeroCounts)
            workspace = workspace with { Finance = workspace.Finance! with { OverdueReceivables = 0, DuePayables = 0, ReconciliationExceptions = 0 } };
        var origin = DashboardRoutes.BuildTodayPath(Company, "company") + "&filter=owned";
        var cut = ctx.RenderComponent<CompanyHealthSummary>(p => p.Add(x => x.Workspace, workspace).Add(x => x.ReturnUrl, origin));
        var links = cut.FindAll("[data-department='finance'] .company-summary__indicators a");
        Assert.Equal(new[] { "Overdue invoices", "Supplier bills due", "Reconciliation exceptions" }, links.Select(x => x.TextContent));
        var paths = new[] { "/finance/invoices", "/finance/supplier-bills", "/finance/accounting/reconciliation" };
        for (var i = 0; i < paths.Length; i++)
        {
            var destination = new Uri("http://localhost" + links[i].GetAttribute("href"));
            var query = System.Web.HttpUtility.ParseQueryString(destination.Query);
            Assert.Equal(paths[i], destination.AbsolutePath);
            Assert.Equal(Company.ToString("D"), query["companyId"]);
            Assert.Equal(origin, query["returnUrl"]);
        }
        if (zeroCounts)
            Assert.All(cut.FindAll("[data-department='finance'] dd"), x => Assert.Equal("0", x.TextContent));
    }

    [Theory]
    [InlineData("sales", false)]
    [InlineData("marketing", false)]
    [InlineData("customers", false)]
    [InlineData("sales", true)]
    [InlineData("marketing", true)]
    [InlineData("customers", true)]
    public void Other_department_indicators_link_to_relevant_screens_and_keep_filters_and_overview_return(string lens, bool zeroCounts)
    {
        using var ctx = new TestContext().AddVirtualCompanyWebPresentationServices();
        var workspace = AllDepartments();
        if (zeroCounts)
            workspace = workspace with {
                Sales = workspace.Sales! with { PipelineValue = 0, DealsNeedingAttention = 0, HotLeads = 0 },
                Marketing = workspace.Marketing! with { DueLaunches = 0, DueContentItems = 0, SpendExceptions = 0, AttributionGaps = 0 },
                Support = workspace.Support! with { OpenCases = 0, SlaBreached = 0, SlaAtRisk = 0, WaitingCases = 0, AwaitingApproval = 0 }
            };
        var expected = lens switch {
            "sales" => new[] { "/app/sales/pipeline", "/app/sales#sales-deals-attention", "/app/sales/prospects?view=leads" },
            "marketing" => new[] { "/marketing?section=Calendar", "/marketing?section=Content", "/marketing/reports/spend", "/marketing?section=Performance" },
            _ => new[] { "/support?view=open", "/support?view=breached", "/support?view=sla-risk", "/support/reports?view=backlog", "/support?view=approvals" }
        };
        var origin = DashboardRoutes.BuildTodayPath(Company, "company") + "&filter=owned";
        var cut = ctx.RenderComponent<CompanyHealthSummary>(p => p.Add(x => x.Workspace, workspace).Add(x => x.ReturnUrl, origin));
        var links = cut.FindAll($"[data-department='{lens}'] .company-summary__indicators a");
        Assert.Equal(expected.Length, links.Count);
        for (var i = 0; i < expected.Length; i++)
        {
            var target = new Uri("http://localhost" + links[i].GetAttribute("href"));
            var wanted = new Uri("http://localhost" + expected[i]);
            Assert.Equal(wanted.AbsolutePath, target.AbsolutePath);
            Assert.Equal(wanted.Fragment, target.Fragment);
            var query = System.Web.HttpUtility.ParseQueryString(target.Query);
            Assert.Equal(Company.ToString("D"), query["companyId"]);
            Assert.Equal(origin, query["returnUrl"]);
            var filters = System.Web.HttpUtility.ParseQueryString(wanted.Query);
            foreach (var key in filters.AllKeys) Assert.Equal(filters[key], query[key]);
        }
    }

    [Fact] public void Department_review_uses_existing_ranked_evidence_and_preserves_company_overview_return()
    {
        using var ctx = new TestContext().AddVirtualCompanyWebPresentationServices(); var w=AllDepartments();
        var priority=w.Priorities[0];
        w=w with {Priorities=[priority with {Rank=9,Key="later",WhatHappened="Later Sales action"}],CompanyRisks=[priority,priority with {Lens="finance",Key="invoice:1",WhatHappened="Overdue invoice",DeepLink="/finance/invoices/22222222-2222-2222-2222-222222222222?financeSource=operational"}]};
        var origin=DashboardRoutes.BuildTodayPath(Company,"company")+"&filter=owned";
        var cut=ctx.RenderComponent<CompanyHealthSummary>(p=>p.Add(x=>x.Workspace,w).Add(x=>x.ReturnUrl,origin));
        var sales=cut.Find("[data-department='sales']");
        Assert.Contains("Renewal needs attention",sales.TextContent);Assert.DoesNotContain("Later Sales action",sales.TextContent);
        var query=System.Web.HttpUtility.ParseQueryString(new Uri("http://localhost"+sales.QuerySelector("[data-testid='health-department-review']")!.GetAttribute("href")).Query);
        Assert.Equal(Company.ToString("D"),query["companyId"]);Assert.Equal(origin,query["returnUrl"]);
        Assert.Equal(DashboardRoutes.EnsureWorkspaceContext(priority.DeepLink,Company,origin),sales.QuerySelector("[data-testid='health-department-review']")!.GetAttribute("href"));
        Assert.Null(sales.QuerySelector("[data-testid='health-department-evidence']"));
        var financeReview=cut.Find("[data-department='finance'] [data-testid='health-department-review']").GetAttribute("href")!;
        Assert.StartsWith("/finance/invoices/",financeReview,StringComparison.Ordinal);
        Assert.Equal("operational",System.Web.HttpUtility.ParseQueryString(new Uri("http://localhost"+financeReview).Query)["financeSource"]);
        Assert.Contains("Overdue invoice",cut.Find("[data-department='finance']").TextContent);
        Assert.Contains("No priority recorded in this summary",cut.Find("[data-department='customers']").TextContent);
    }

    [Theory]
    [InlineData("finance", "/finance/invoices/22222222-2222-2222-2222-222222222222?financeSource=operational")]
    [InlineData("marketing", "/marketing/review?campaignId=22222222-2222-2222-2222-222222222222")]
    [InlineData("customers", "/support/cases/22222222-2222-2222-2222-222222222222")]
    public void Company_department_review_opens_action_record_without_priority_detail_link(string lens, string source)
    {
        using var ctx = new TestContext().AddVirtualCompanyWebPresentationServices();
        var workspace=AllDepartments();
        var priority=workspace.Priorities[0] with {Lens=lens,DeepLink=source};
        workspace=workspace with {CompanyRisks=[priority]};
        var origin=DashboardRoutes.BuildTodayPath(Company,"company")+"&filter=owned";
        var cut=ctx.RenderComponent<CompanyHealthSummary>(p=>p.Add(x=>x.Workspace,workspace).Add(x=>x.ReturnUrl,origin));
        var department=cut.Find($"[data-department='{lens}']");
        Assert.Equal(DashboardRoutes.EnsureWorkspaceContext(source,Company,origin),department.QuerySelector("[data-testid='health-department-review']")!.GetAttribute("href"));
        Assert.Null(department.QuerySelector("[data-testid='health-department-evidence']"));
    }

    [Fact] public void Missing_counts_are_unavailable_and_revoked_or_unavailable_departments_hide_previous_indicators_and_actions()
    {
        using var ctx = new TestContext().AddVirtualCompanyWebPresentationServices(); var w=AllDepartments();
        w=w with {Finance=w.Finance! with {OverdueReceivables=null,DuePayables=null,ReconciliationExceptions=null},CompanyRisks=[w.Priorities[0] with {Lens="finance",Key="invoice:secret",WhatHappened="Private invoice"}]};
        var cut=ctx.RenderComponent<CompanyHealthSummary>(p=>p.Add(x=>x.Workspace,w));
        Assert.All(cut.Find("[data-department='finance']").QuerySelectorAll("dd"),x=>Assert.Equal("Unavailable",x.TextContent));
        cut.SetParametersAndRender(p=>p.Add(x=>x.Workspace,w with {AvailableLenses=w.AvailableLenses.Where(x=>x.Value!="finance").ToArray()}));
        var finance=cut.Find("[data-department='finance']");
        Assert.Empty(finance.QuerySelectorAll("dd"));Assert.DoesNotContain("Private invoice",finance.TextContent);Assert.Empty(finance.QuerySelectorAll("a"));
        Assert.Contains("Outside your responsibilities",finance.TextContent);
        cut.SetParametersAndRender(p=>p.Add(x=>x.Workspace,w with {Finance=w.Finance! with {IsAvailable=false,OverdueReceivables=99}}));
        finance=cut.Find("[data-department='finance']");Assert.Empty(finance.QuerySelectorAll("dd"));Assert.DoesNotContain("Private invoice",finance.TextContent);
    }

    [Fact] public void Zero_counts_do_not_claim_good_health_and_report_keeps_its_existing_coverage_table()
    {
        using var ctx = new TestContext().AddVirtualCompanyWebPresentationServices();var w=AllDepartments();
        w=w with {Support=w.Support! with {OpenCases=0,AwaitingApproval=0,SlaAtRisk=0,SlaBreached=0,WaitingCases=0,ObservedAtUtc=w.GeneratedAtUtc.AddHours(-1)}};
        var cut=ctx.RenderComponent<CompanyHealthSummary>(p=>p.Add(x=>x.Workspace,w));
        var support=cut.Find("[data-department='customers']");Assert.All(support.QuerySelectorAll("dd"),x=>Assert.Equal("0",x.TextContent));
        Assert.Contains("Stale",support.TextContent);Assert.DoesNotContain("Healthy",support.TextContent);Assert.DoesNotContain("On track",support.TextContent);
        cut.SetParametersAndRender(p=>p.Add(x=>x.InReport,true));Assert.Empty(cut.FindAll("[data-testid='health-department']"));
    }
    [Fact] public void Company_summary_and_report_reconcile_forecast_cash_gaps_and_dated_coverage()
    {
        using var ctx = new TestContext().AddVirtualCompanyWebPresentationServices(); var w=Workspace();
        var summary=ctx.RenderComponent<CompanyHealthSummary>(p=>p.Add(x=>x.Workspace,w));
        var report=ctx.RenderComponent<CompanyHealthReport>(p=>p.Add(x=>x.CompanyId,Company).Add(x=>x.Workspace,w));
        Assert.Equal(summary.Find("[data-testid='health-forecast']").TextContent,report.Find("[data-testid='health-forecast']").TextContent);
        Assert.Equal("Unavailable",report.Find("[data-testid='health-cash']").TextContent);
        Assert.Contains("No approved baseline",report.Markup); Assert.Contains("Outside your responsibilities",report.Markup);
        Assert.Empty(report.FindAll("[data-testid='health-variance']"));
        var risk=report.Find("[data-testid='health-risk'] a").GetAttribute("href")!;
        var query=System.Web.HttpUtility.ParseQueryString(new Uri("http://localhost"+risk).Query);
        Assert.Equal(new Uri("http://localhost"+w.CompanyRisks![0].DeepLink).AbsolutePath,new Uri("http://localhost"+risk).AbsolutePath); Assert.NotNull(DashboardRoutes.NormalizeHealthPath(query["healthReturnUrl"],Company));
    }

    [Theory] [InlineData("no_baseline")] [InlineData("selection_required")] [InlineData("unavailable")]
    public void Missing_or_ambiguous_plan_never_renders_favorable_variance(string state)
    {
        using var ctx=new TestContext().AddVirtualCompanyWebPresentationServices();var w=Workspace();
        w=w with { AvailableLenses=[..w.AvailableLenses,new("finance","Finance",false,"Assigned")], Finance=new(true,"",DateTime.UtcNow,12000,"SEK",30,"attention",0,[],"/finance",new(state,DateTime.UtcNow.Date,DateTime.UtcNow.Date,null,["v1","v2"],null)) };
        var cut=ctx.RenderComponent<CompanyHealthReport>(p=>p.Add(x=>x.CompanyId,Company).Add(x=>x.Workspace,w));
        Assert.Contains("No approved baseline",cut.Markup);Assert.Empty(cut.FindAll("[data-testid='health-variance']"));
    }
    [Fact] public void Recorded_plan_rows_preserve_account_currency_and_owning_variance_without_approval_claim()
    {
        using var ctx=new TestContext().AddVirtualCompanyWebPresentationServices();var w=Workspace(); var now=w.GeneratedAtUtc;
        var comparison=new TodayRecordedComparisonViewModel(Company,"actual_vs_budget",now,now,"v1",false,[new(now,Guid.NewGuid(),"3000","Revenue","income","Income",null,null,null,80,100,-20,-20,"SEK")]);
        w=w with { Finance=new(true,"",now,12000,"SEK",30,"attention",0,[],"/finance",new("recorded_unapproved",now,now,now,["v1"],comparison)) };
        var cut=ctx.RenderComponent<CompanyHealthReport>(p=>p.Add(x=>x.CompanyId,Company).Add(x=>x.Workspace,w));
        var table=cut.Find("[data-testid='health-variance']").TextContent;
        Assert.Contains("3000 Revenue",table);Assert.Contains("SEK 80.00",table);Assert.Contains("SEK -20.00",table);Assert.Contains("No approved baseline",cut.Markup);
        cut.SetParametersAndRender(p=>p.Add(x=>x.Workspace,w with {CompanyId=Guid.NewGuid()}));Assert.Empty(cut.FindAll("[data-testid='health-variance']"));
    }
    [Fact] public void Department_filter_keeps_company_summary_and_invalid_filter_exposes_no_risk()
    {
        using var ctx=new TestContext().AddVirtualCompanyWebPresentationServices();var w=Workspace();
        var cut=ctx.RenderComponent<CompanyHealthReport>(p=>p.Add(x=>x.CompanyId,Company).Add(x=>x.Workspace,w).Add(x=>x.Department,"sales"));
        Assert.Single(cut.FindAll("[data-testid='health-coverage-row']"));Assert.Single(cut.FindAll("[data-testid='health-risk']"));
        cut.SetParametersAndRender(p=>p.Add(x=>x.Department,"finance"));Assert.Empty(cut.FindAll("[data-testid='health-risk']"));Assert.Contains("department filter is unavailable",cut.Markup);
    }
    [Fact] public void Restricted_report_never_shows_the_previous_source_summary()
    {
        using var ctx=new TestContext().AddVirtualCompanyWebPresentationServices();
        var cut=ctx.RenderComponent<CompanyHealthReport>(p=>p.Add(x=>x.CompanyId,Company).Add(x=>x.Workspace,Workspace()).Add(x=>x.IsRestricted,true));
        Assert.DoesNotContain("Renewal needs attention",cut.Markup);Assert.Empty(cut.FindAll("[data-testid='company-health-summary']"));
    }
    [Fact] public async Task Company_switch_ignores_a_late_health_response()
    {
        using var ctx=new TestContext().AddVirtualCompanyWebPresentationServices();var old=new TaskCompletionSource<TodayWorkspaceViewModel?>();var second=Guid.NewGuid();
        ctx.Services.AddSingleton<ITodayWorkspaceApiClient>(new ReadClient(id=>id==Company?old.Task:Task.FromResult<TodayWorkspaceViewModel?>(Workspace() with {CompanyId=second,CompanyRisks=[]})));
        ctx.Services.AddSingleton(new OnboardingApiClient(new HttpClient {BaseAddress=new("http://localhost/")},useOfflineMode:true));
        var nav=ctx.Services.GetRequiredService<NavigationManager>();nav.NavigateTo(DashboardRoutes.BuildHealthPath(Company));
        var cut=ctx.RenderComponent<CompanyHealth>();nav.NavigateTo(DashboardRoutes.BuildHealthPath(second));
        cut.WaitForAssertion(()=>cut.Find("[data-testid='company-health-summary']"));old.SetResult(Workspace());await cut.InvokeAsync(()=>Task.CompletedTask);
        cut.WaitForAssertion(()=>Assert.DoesNotContain("Renewal needs attention",cut.Markup));
    }
    [Fact] public async Task Typed_follow_up_command_preserves_evidence_company_scope_and_never_assigns_an_agent()
    {
        var handler=new CommandHandler();var client=new TaskApiClient(new HttpClient(handler){BaseAddress=new("http://localhost/")});
        var observed = new DateTime(2026,9,23,16,49,0,DateTimeKind.Unspecified);
        await client.CreateRiskFollowUpAsync(Company,Workspace().CompanyRisks![0] with { ObservedAtUtc=observed });
        Assert.Equal(Company.ToString(),handler.Header);using var json=JsonDocument.Parse(handler.Body!);var root=json.RootElement;
        Assert.Equal("follow_up",root.GetProperty("type").GetString());Assert.Equal(JsonValueKind.Null,root.GetProperty("assignedAgentId").ValueKind);
        Assert.Equal("deal:1",root.GetProperty("inputPayload").GetProperty("priorityEvidenceKey").GetString());Assert.Equal(Company,root.GetProperty("inputPayload").GetProperty("sourceCompanyId").GetGuid());
        Assert.Equal("2026-09-23T16:49:00Z",root.GetProperty("inputPayload").GetProperty("sourceObservedUtc").GetString());
    }

    private sealed class CommandHandler:HttpMessageHandler
    {
        public string? Body,Header; public int Writes;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        { Writes++; Body=await request.Content!.ReadAsStringAsync(ct);Header=request.Headers.GetValues("X-Company-Id").Single();return new(HttpStatusCode.OK){Content=JsonContent.Create(new RiskFollowUpResult(Guid.NewGuid(),Company,"new",DateTime.UtcNow))}; }
    }
    private sealed class ReadClient(Func<Guid,Task<TodayWorkspaceViewModel?>> read):ITodayWorkspaceApiClient
    {
        public Task<TodayWorkspaceViewModel?> GetAsync(Guid companyId,string? lens=null,CancellationToken cancellationToken=default)=>throw new InvalidOperationException();
        public Task<TodayWorkspaceViewModel?> RefreshAsync(Guid companyId,string? lens=null,CancellationToken cancellationToken=default)=>read(companyId);
        public Task<TodayWorkspaceManualReviewViewModel> RequestReviewAsync(Guid companyId,CancellationToken cancellationToken=default)=>throw new InvalidOperationException();
    }
}
