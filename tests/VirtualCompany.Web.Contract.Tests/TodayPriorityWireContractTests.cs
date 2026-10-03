using System.Net.Http.Json;
using VirtualCompany.Api.Tests;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Contract.Tests;

public sealed class TodayPriorityWireContractTests
{
    [Fact]
    public async Task Finance_operational_client_and_daily_contract_read_real_scoped_obligations()
    {
        using var factory = new TestWebApplicationFactory(); var company = Guid.NewGuid(); var user = Guid.NewGuid(); var customer = Guid.NewGuid();
        await factory.SeedAsync(db => { db.Users.Add(new User(user, "p06-wire@example.com", "Finance wire", "dev-header", "p06-wire"));
            db.Companies.Add(new Company(company, "Finance wire")); db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), company, user, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            db.FinanceCounterparties.Add(new FinanceCounterparty(customer, company, "Wire customer", "customer"));
            db.FinanceInvoices.Add(new FinanceInvoice(Guid.NewGuid(), company, customer, "Wire obligation", DateTime.UtcNow.AddDays(-40), DateTime.UtcNow.AddDays(-31), 100, "SEK", "open", paidAmount: 20)); return Task.CompletedTask; });
        using var http = factory.CreateClient(); http.DefaultRequestHeaders.Add("X-Dev-Auth-Subject", "p06-wire"); http.DefaultRequestHeaders.Add("X-Dev-Auth-Email", "p06-wire@example.com");
        var transport = new CompanyApiTransport(http); var result = await new FinanceApiClient(transport).GetOperationalReportAsync(company, DateOnly.FromDateTime(DateTime.UtcNow), 14, "SEK", "overdue");
        Assert.Equal(80, result!.Receivables.Single().RemainingAmount); Assert.Equal(80, result.Totals.Single().ExpectedInflows);
        var today = (await new TodayWorkspaceApiClient(transport, false).RefreshAsync(company, "finance"))!;
        Assert.Equal(1, today.Finance!.OverdueReceivables); Assert.Contains(today.Finance.Items, x => x.Title.Contains("Wire obligation"));
        Assert.NotNull(today.Finance.CoverageGaps);
    }
    [Fact]
    public async Task Marketing_operational_and_daily_wire_clients_read_the_same_authenticated_source()
    {
        using var factory = new TestWebApplicationFactory(); var company = Guid.NewGuid(); var user = Guid.NewGuid(); var brief = Guid.NewGuid();
        await factory.SeedAsync(db => { db.Users.Add(new User(user, "p05-wire@example.com", "P05 wire", "dev-header", "p05-wire")); db.Companies.Add(new Company(company, "P05 wire"));
            db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), company, user, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            db.MarketingContentBriefs.Add(new MarketingContentBrief(brief, company, "Due message", "Test brief", "Customers", "email", "English", "Clear", "Read", null, null, DateTime.UtcNow, user, null)); return Task.CompletedTask; });
        using var http = factory.CreateClient(); http.DefaultRequestHeaders.Add("X-Dev-Auth-Subject", "p05-wire"); http.DefaultRequestHeaders.Add("X-Dev-Auth-Email", "p05-wire@example.com");
        var transport = new CompanyApiTransport(http); var reports = new MarketingOperationalApiClient(transport, false);
        var result = await reports.ReviewAsync(company, null, brief); Assert.Equal(brief, result.Content.Single().Id);
        var report = await reports.ReportAsync(company, new(DateTime.UtcNow.AddDays(-30), DateTime.UtcNow.AddDays(1))); Assert.Empty(report.Campaigns);
        var today = (await new TodayWorkspaceApiClient(transport, false).RefreshAsync(company, "marketing"))!;
        Assert.Equal(1, today.Marketing!.DueContentItems); Assert.Contains(today.Priorities, x => x.EvidenceSourceId == brief.ToString("D") && x.DeepLink.Contains("briefId="));
    }
    [Fact]
    public async Task Sales_report_and_commitment_typed_clients_reconcile_real_API_and_today_agenda()
    {
        using var factory = new TestWebApplicationFactory(); var company = Guid.NewGuid(); var user = Guid.NewGuid(); var deal = Guid.NewGuid();
        await factory.SeedAsync(db => {
            db.Users.Add(new User(user, "p04-wire@example.com", "P04 wire owner", "dev-header", "p04-wire"));
            db.Companies.Add(new Company(company, "P04 wire"));
            db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), company, user, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            db.Deals.Add(new Deal(deal, company, "Renewal", SalesPipelineStage.QualifiedStageId, 12000, "SEK", expectedCloseUtc: DateTime.UtcNow.AddDays(1)));
            return Task.CompletedTask;
        });
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, "p04-wire");
        http.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, "p04-wire@example.com");
        var transport = new CompanyApiTransport(http); var sales = new SalesOperationalApiClient(transport, false);
        var report = await sales.OpportunitiesAsync(company, true, 30, "SEK", null);
        Assert.Equal(4050m, report.Totals.Single().ExpectedAmount); Assert.Equal(deal, report.Rows.Single().DealId);
        var commitment = await sales.RecordAsync(company, deal, Guid.NewGuid(), "Review terms", DateTime.UtcNow.AddMinutes(-1));
        var today = (await new TodayWorkspaceApiClient(transport, false).RefreshAsync(company, "sales"))!;
        Assert.Contains(today.Sales!.Agenda!, x => x.Title == "Review terms");
        Assert.Contains((await sales.ActivitiesAsync(company, "overdue")).Commitments, x => x.Id == commitment.Id);
        await sales.ReviewAsync(company, commitment);
        Assert.Empty((await sales.ActivitiesAsync(company, "overdue")).Commitments);
        Assert.Single((await sales.ActivitiesAsync(company, "completed")).Commitments);
    }

    [Fact]
    public void Finance_plan_contract_preserves_period_version_account_currency_and_recorded_comparison()
    {
        var id=Guid.NewGuid();var now=new DateTime(2026,10,1,0,0,0,DateTimeKind.Utc);
        var variance=new VirtualCompany.Application.Finance.FinanceVarianceResultDto(id,"actual_vs_budget",now,now,"v2",false,
            [new(now,Guid.NewGuid(),"3000","Revenue","income","Income",null,null,null,80,100,-20,-20,"SEK")]);
        var source=new VirtualCompany.Application.Cockpit.TodayWorkspaceFinanceSectionDto(true,"Recorded",now,12000,"SEK",30,"attention",1,[],"/finance",
            new("recorded_unapproved",now,now.AddMonths(1).AddTicks(-1),now,["v2"],variance));
        var options=new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var view=System.Text.Json.JsonSerializer.Deserialize<TodayWorkspaceFinanceSectionViewModel>(System.Text.Json.JsonSerializer.Serialize(source,options),options)!;
        Assert.Equal(source.CashBalance,view.CashBalance);Assert.Equal("recorded_unapproved",view.PlanComparison!.State);Assert.Equal("v2",view.PlanComparison.RecordedComparison!.Version);
        Assert.Equal(id,view.PlanComparison.RecordedComparison.CompanyId);Assert.Equal(source.PlanComparison!.PeriodEndUtc,view.PlanComparison.PeriodEndUtc);
        var row=Assert.Single(view.PlanComparison.RecordedComparison.Rows);Assert.Equal(-20m,row.VarianceAmount);Assert.Equal("SEK",row.Currency);Assert.Equal("3000",row.AccountCode);
    }

    [Fact]
    public async Task Company_health_wire_and_typed_follow_up_use_the_real_authorized_API()
    {
        using var factory = new TestWebApplicationFactory(); var company=Guid.NewGuid();var user=Guid.NewGuid();var deal=Guid.NewGuid();
        await factory.SeedAsync(db=> {
            db.Users.Add(new User(user,"health-wire@example.com","Health owner","dev-header","health-wire"));
            db.Companies.Add(new Company(company,"Health wire"));db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(),company,user,CompanyMembershipRole.Owner,CompanyMembershipStatus.Active));
            db.Deals.Add(new Deal(deal,company,"Renewal risk",SalesPipelineStage.QualifiedStageId,12000,"SEK",expectedCloseUtc:DateTime.UtcNow.AddHours(-1)));
            db.SalesAgentRecommendations.Add(new SalesAgentRecommendation(Guid.NewGuid(),company,"Review renewal","Material revenue exposure",null,deal));return Task.CompletedTask;
        });
        using var http=factory.CreateClient();http.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader,"health-wire");http.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader,"health-wire@example.com");http.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader,"Health owner");
        var workspace=(await new TodayWorkspaceApiClient(new CompanyApiTransport(http),false).RefreshAsync(company,"company"))!;
        var risk=Assert.Single(workspace.CompanyRisks!.Where(x=>x.Lens=="sales"));Assert.NotNull(workspace.Departments);Assert.NotNull(workspace.Sales);
        var client=new TaskApiClient(http);var result=await client.CreateRiskFollowUpAsync(company,risk);var task=await client.GetAsync(company,result.Id);
        Assert.Equal(risk.Key,task.InputPayload["priorityEvidenceKey"]!.ToString());Assert.Equal("user",task.CreatedByActorType);Assert.Null(task.AssignedAgentId);
        var open=(await new TodayWorkspaceApiClient(new CompanyApiTransport(http),false).RefreshAsync(company,"company"))!;Assert.Contains(open.RiskFollowUps!,x=>x.TaskId==result.Id && x.EvidenceKey==risk.Key);
        Assert.Single(open.Priorities.Where(x=>x.Key==risk.Key));
        Assert.DoesNotContain(open.Priorities,x=>x.RelatedTaskId==result.Id);
        await client.CompleteFollowUpAsync(company,result.Id);Assert.Equal("completed",(await client.GetAsync(company,result.Id)).Status);
        var fresh=(await new TodayWorkspaceApiClient(new CompanyApiTransport(http),false).RefreshAsync(company,"company"))!;
        Assert.Contains(fresh.CompanyRisks!,x=>x.Key==risk.Key);
    }

    [Fact]
    public async Task Typed_Web_client_reads_real_API_priority_evidence_and_refreshes_after_task_completion()
    {
        using var factory = new TestWebApplicationFactory();
        var company = Guid.NewGuid(); var user = Guid.NewGuid();
        await factory.SeedAsync(db => {
            db.Users.Add(new User(user, "p02-contract@example.com", "Contract owner", "dev-header", "p02-contract"));
            db.Companies.Add(new Company(company, "Contract company"));
            db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), company, user, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            return Task.CompletedTask;
        });
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, "p02-contract");
        http.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, "p02-contract@example.com");
        http.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, "Contract owner");
        var createdResponse = await http.PostAsJsonAsync($"/api/companies/{company:D}/tasks",
            new CreateTaskCommand("follow_up", "Check renewal", "Confirm source terms", "high", DateTime.UtcNow.AddHours(-1), null, null));
        createdResponse.EnsureSuccessStatusCode();
        var task = (await createdResponse.Content.ReadFromJsonAsync<TaskCommandResultDto>())!;
        var client = new TodayWorkspaceApiClient(new CompanyApiTransport(http), false);
        var before = (await client.RefreshAsync(company, "company"))!;
        var item = Assert.Single(before.Priorities, x => x.RelatedTaskId == task.Id);
        Assert.Equal(task.UpdatedAt, item.ObservedAtUtc);
        Assert.Equal("new", item.SourceState);
        Assert.Contains("Overdue", item.RankingReason);
        await new TaskApiClient(http).CompleteFollowUpAsync(company, task.Id);
        var after = (await client.RefreshAsync(company, "company"))!;
        Assert.DoesNotContain(after.Priorities, x => x.RelatedTaskId == task.Id);
    }
}
