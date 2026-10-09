using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;
public sealed class SalesManagementIntegrationTests
{
    public static HttpClient Client(TestWebApplicationFactory factory,Guid company,string subject="p19-owner"){var http=WeeklyWorkspaceFixture.Client(factory,subject);http.DefaultRequestHeaders.Add("X-Company-Id",company.ToString());return http;}
    public const string Root="/api/sales/management";
    public static SaveSalesCapacityProposal Command(Guid? previous=null,int? revision=null)=>new(Guid.NewGuid(),new(2026,9),new(160,8,[new("North",60),new("South",40)],"Recorded planning assumptions"),previous,revision);
    public static async Task<SalesCapacityProposal> Save(HttpClient http,SaveSalesCapacityProposal? command=null){var response=await http.PostAsJsonAsync(Root+"/proposals",command??Command());Assert.Equal(HttpStatusCode.OK,response.StatusCode);return (await response.Content.ReadFromJsonAsync<SalesCapacityProposal>())!;}
    [Fact]
    public async Task Cohort_boundaries_reopened_last_outcome_missing_reasons_stage_gaps_and_currency_are_explicit()
    {
        using var factory=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await WeeklyWorkspaceFixture.Seed(factory);using var http=Client(factory,s.Company);
        var won=Guid.NewGuid();var reopened=Guid.NewGuid();var lost=Guid.NewGuid();var prior=Guid.NewGuid();var foreign=Guid.NewGuid();
        var start=new DateTime(2026,8,31,22,0,0,DateTimeKind.Utc);
        await factory.SeedAsync(db=>{
            db.Deals.AddRange(new Deal(won,s.Company,"Boundary win",SalesPipelineStage.NewStageId,50,"EUR",expectedCloseUtc:WeeklyWorkspaceFixture.Now.AddDays(1),createdUtc:start),
                new Deal(reopened,s.Company,"Reopened",SalesPipelineStage.NewStageId,50,"EUR",createdUtc:start.AddDays(1)),
                new Deal(lost,s.Company,"Loss without reason",SalesPipelineStage.NewStageId,50,"SEK",createdUtc:start.AddDays(2)),
                new Deal(prior,s.Company,"Prior",SalesPipelineStage.NewStageId,50,"EUR",createdUtc:start.AddTicks(-1)),
                new Deal(foreign,s.Foreign,"Foreign secret",SalesPipelineStage.NewStageId,50,"EUR",expectedCloseUtc:WeeklyWorkspaceFixture.Now.AddDays(1),createdUtc:start));
            db.SalesActivities.AddRange(new SalesActivity(Guid.NewGuid(),s.Company,"won","Recorded win",start.AddDays(4),dealId:won).RecordTransition(s.Owner,SalesPipelineStage.NewStageId,SalesPipelineStage.WonStageId,"Product fit"),
                new SalesActivity(Guid.NewGuid(),s.Company,"won","Duplicate event counted once",start.AddDays(5),dealId:won),
                new SalesActivity(Guid.NewGuid(),s.Company,"won","Then reopened",start.AddDays(3),dealId:reopened),
                new SalesActivity(Guid.NewGuid(),s.Company,"reopened","Reopened record",start.AddDays(4),dealId:reopened),
                new SalesActivity(Guid.NewGuid(),s.Company,"lost","Do not infer this summary as reason",start.AddDays(6),dealId:lost),
                new SalesActivity(Guid.NewGuid(),s.Company,"lost","At excluded month end",start.AddMonths(1),dealId:reopened));
            return Task.CompletedTask;
        });
        var report=(await http.GetFromJsonAsync<SalesManagementReport>(Root+"?year=2026&month=9"))!;
        Assert.Equal(1,report.Selected.Won);Assert.Equal(1,report.Selected.Lost);Assert.Equal(1,report.Prior.Created);
        Assert.Equal("undecided",report.Opportunities.Single(x=>x.DealId==reopened).Outcome);
        Assert.Null(report.Opportunities.Single(x=>x.DealId==lost).RecordedReason);Assert.True(report.Selected.MissingReasons>=1);
        Assert.DoesNotContain(report.Opportunities,x=>x.DealId==foreign);Assert.Contains("not recorded",report.OwnershipCoverage);
        Assert.Equal(5,report.Opportunities.Single(x=>x.DealId==won).CycleDays);
        var eur=(await http.GetFromJsonAsync<SalesManagementReport>(Root+"?year=2026&month=9&currency=eur"))!;
        Assert.All(eur.Opportunities,x=>Assert.Equal("EUR",x.Currency));Assert.Equal(2,eur.Selected.Created);Assert.Equal(50,eur.Selected.WonCreatedPercent);
        Assert.Contains(eur.CurrentForecastWindows,x=>x.Currency=="EUR");
    }
    [Fact]
    public async Task Empty_cohort_zero_denominators_are_unavailable_not_invented()
    {
        using var factory=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await WeeklyWorkspaceFixture.Seed(factory);using var http=Client(factory,s.Company);
        var r=(await http.GetFromJsonAsync<SalesManagementReport>(Root+"?year=2025&month=1"))!;Assert.Equal(0,r.Selected.Created);Assert.Null(r.Selected.WonCreatedPercent);Assert.Null(r.Selected.WinDecidedPercent);Assert.Null(r.Selected.MedianCycleDays);Assert.Null(r.ConversionChangePoints);
    }
    [Fact]
    public async Task Native_stage_and_loss_commands_capture_typed_ids_actor_and_only_explicit_reason()
    {
        using var factory=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await WeeklyWorkspaceFixture.Seed(factory);using var http=Client(factory,s.Company);
        var move=await http.PostAsJsonAsync($"/api/sales/deals/{s.Deal}/stage",new ChangeDealStageRequest(SalesPipelineStage.QualifiedStageId,"Discovery completed"));Assert.Equal(HttpStatusCode.OK,move.StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await http.PostAsJsonAsync($"/api/sales/deals/{s.Deal}/lost",new SalesActionRequest("Budget withdrawn"))).StatusCode);
        await factory.SeedAsync(async db=>{var rows=await db.SalesActivities.IgnoreQueryFilters().Where(x=>x.DealId==s.Deal && x.NewStageId!=null).OrderBy(x=>x.OccurredUtc).ToListAsync();Assert.Equal(2,rows.Count);Assert.Equal(SalesPipelineStage.NewStageId,rows[0].PreviousStageId);Assert.Equal(SalesPipelineStage.LostStageId,rows[1].NewStageId);Assert.Equal("Budget withdrawn",rows[1].RecordedReason);Assert.Equal(s.Owner,rows[1].ActorUserId);});
    }
    [Fact]
    public async Task Forecast_capture_reconciles_original_inputs_after_native_amount_changes_and_discloses_legacy()
    {
        using var factory=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await WeeklyWorkspaceFixture.Seed(factory);using var http=Client(factory,s.Company);
        using(var scope=factory.Services.CreateScope())await scope.ServiceProvider.GetRequiredService<IRevenueForecastService>().CalculateAndPersistForecastAsync(s.Company,WeeklyWorkspaceFixture.Now.AddDays(-1),default);
        var first=(await http.GetFromJsonAsync<SalesManagementReport>(Root+"?year=2026&month=9"))!;
        var snapshot=Assert.Single(first.ForecastHistory);Assert.True(snapshot.InputsAvailable);Assert.Equal(snapshot.Expected30,snapshot.Capture!.Inputs.Sum(x=>x.ExpectedAmount));
        await factory.SeedAsync(async db=>{var deal=await db.Deals.IgnoreQueryFilters().SingleAsync(x=>x.Id==s.Deal);db.Entry(deal).Property(x=>x.Amount).CurrentValue=9000;});
        var second=(await http.GetFromJsonAsync<SalesManagementReport>(Root+"?year=2026&month=9"))!;Assert.Equal(snapshot.Expected30,second.ForecastHistory[0].Expected30);Assert.Equal(1000,second.ForecastHistory[0].Capture!.Inputs[0].Amount);
    }
    [Fact]
    public async Task Proposal_reopens_exact_assumptions_calculations_original_report_and_appends_audited_revision()
    {
        using var factory=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await WeeklyWorkspaceFixture.Seed(factory);using var http=Client(factory,s.Company);
        var command=Command();var saved=await Save(http,command);Assert.Equal(20,saved.Result.OpportunityCapacity);Assert.Equal(96,saved.Result.Allocations[0].SellingHours);Assert.Equal(s.Owner,saved.Summary.AccountableUserId);
        Assert.Equal(saved.Summary.Id,(await Save(http,command)).Summary.Id);
        await factory.SeedAsync(async db=>{var deal=await db.Deals.IgnoreQueryFilters().SingleAsync(x=>x.Id==s.Deal);deal.MarkLost();});
        var opened=(await http.GetFromJsonAsync<SalesCapacityProposal>(Root+$"/proposals/{saved.Summary.Id}"))!;Assert.Equal(JsonSerializer.Serialize(saved),JsonSerializer.Serialize(opened));
        var revised=await Save(http,Command(saved.Summary.Id,1));Assert.Equal(2,revised.Summary.Revision);Assert.Equal(saved.Summary.SeriesId,revised.Summary.SeriesId);Assert.Equal(0,revised.Result.OpenOpportunities);Assert.Equal(1,saved.Result.OpenOpportunities);
        Assert.Equal(HttpStatusCode.Conflict,(await http.PostAsJsonAsync(Root+"/proposals",Command(saved.Summary.Id,1))).StatusCode);
        await factory.SeedAsync(async db=>Assert.Equal(2,await db.AuditEvents.IgnoreQueryFilters().CountAsync(x=>x.CompanyId==s.Company && x.Action=="sales.capacity.proposal_saved")));
        var changed=command with{Assumptions=command.Assumptions with{AvailableSellingHours=40}};Assert.Equal(HttpStatusCode.Conflict,(await http.PostAsJsonAsync(Root+"/proposals",changed)).StatusCode);
    }
    [Fact]
    public async Task Company_user_and_current_sales_scope_control_report_history_open_and_save()
    {
        using var factory=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await WeeklyWorkspaceFixture.Seed(factory);using var owner=Client(factory,s.Company);using var manager=Client(factory,s.Company,s.ManagerSubject);
        var saved=await Save(owner);Assert.Equal(HttpStatusCode.NotFound,(await manager.GetAsync(Root+$"/proposals/{saved.Summary.Id}")).StatusCode);Assert.Empty((await manager.GetFromJsonAsync<SalesCapacityProposalSummary[]>(Root+"/proposals"))!);
        manager.DefaultRequestHeaders.Remove("X-Company-Id");manager.DefaultRequestHeaders.Add("X-Company-Id",s.Foreign.ToString());Assert.Equal(HttpStatusCode.Forbidden,(await manager.GetAsync(Root+"?year=2026&month=9")).StatusCode);
        await factory.SeedAsync(async db=>{var rows=await db.CompanyResponsibilityAssignments.IgnoreQueryFilters().Where(x=>x.CompanyId==s.Company && x.ResponsibilityArea==ResponsibilityArea.Sales).ToListAsync();db.CompanyResponsibilityAssignments.RemoveRange(rows);});
        Assert.Equal(HttpStatusCode.Forbidden,(await owner.GetAsync(Root+$"/proposals/{saved.Summary.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await owner.PostAsJsonAsync(Root+"/proposals",Command())).StatusCode);
    }
    [Theory][InlineData(-1,8,100)][InlineData(160,0,100)][InlineData(160,8,99)][InlineData(100001,8,100)]
    public async Task Bounded_invalid_assumptions_are_rejected(decimal hours,decimal per,decimal percent)
    {
        using var factory=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await WeeklyWorkspaceFixture.Seed(factory);using var http=Client(factory,s.Company);
        var command=Command() with{Assumptions=new(hours,per,[new("North",percent)],"")};Assert.Equal(HttpStatusCode.BadRequest,(await http.PostAsJsonAsync(Root+"/proposals",command)).StatusCode);
    }
    [Fact]
    public async Task Proposal_immutability_corruption_and_monthly_report_reproduction_hold()
    {
        using var factory=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await WeeklyWorkspaceFixture.Seed(factory);using var http=Client(factory,s.Company);
        var saved=await Save(http);var monthly=await MonthlyReviewSnapshotIntegrationTests.Save(http,s.Company);Assert.NotNull(monthly.Workspace.SalesManagement);
        await factory.SeedAsync(async db=>{var row=await db.SalesCapacityProposalRevisions.IgnoreQueryFilters().SingleAsync(x=>x.Id==saved.Summary.Id);db.Entry(row).Property(x=>x.Payload).CurrentValue="{}";await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE sales_capacity_proposal_revisions SET Payload='{{}}' WHERE Id={saved.Summary.Id}");});
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await http.GetAsync(Root+$"/proposals/{saved.Summary.Id}")).StatusCode);
        await factory.SeedAsync(async db=>{var rows=await db.SalesActivities.IgnoreQueryFilters().Where(x=>x.CompanyId==s.Company).ToListAsync();db.SalesActivities.RemoveRange(rows);});
        var reopened=(await (await http.PostAsync(MonthlyReviewSnapshotIntegrationTests.Root(s.Company)+$"/{monthly.Summary.Id}/open",null)).Content.ReadFromJsonAsync<VirtualCompany.Application.Cockpit.MonthlyReviewSnapshotDto>())!;
        Assert.Equal(JsonSerializer.Serialize(monthly.Workspace.SalesManagement),JsonSerializer.Serialize(reopened.Workspace.SalesManagement));
    }

    [Fact]
    public async Task Unrepresentable_capacity_is_validation_failure_without_a_saved_revision()
    {
        using var factory = new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());
        var s = await WeeklyWorkspaceFixture.Seed(factory);
        using var http = Client(factory, s.Company);
        var command = Command() with
        {
            Assumptions = new(160, 0.0000000000000000000000000001m, [new("North", 100)], "")
        };

        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync(Root + "/proposals", command)).StatusCode);
        Assert.Empty((await http.GetFromJsonAsync<SalesCapacityProposalSummary[]>(Root + "/proposals"))!);
        await factory.SeedAsync(async db => Assert.False(await db.AuditEvents.IgnoreQueryFilters()
            .AnyAsync(x => x.CompanyId == s.Company && x.Action == "sales.capacity.proposal_saved")));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Report\":null,\"Assumptions\":null,\"Result\":null}")]
    public async Task Structurally_invalid_retained_proposal_with_matching_checksum_is_withheld(string payload)
    {
        using var factory = new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());
        var s = await WeeklyWorkspaceFixture.Seed(factory);
        using var http = Client(factory, s.Company);
        var checksum = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload)));
        var row = new SalesCapacityProposalRevision(s.Company, s.Owner, Guid.NewGuid(), Guid.NewGuid(), 1, null,
            2026, 9, null, WeeklyWorkspaceFixture.Now, payload, checksum);
        await factory.SeedAsync(db => { db.SalesCapacityProposalRevisions.Add(row); return Task.CompletedTask; });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await http.GetAsync(Root + $"/proposals/{row.Id}")).StatusCode);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    public async Task Structurally_invalid_forecast_inputs_keep_aggregate_totals_and_explicit_coverage(string inputs)
    {
        using var factory = new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());
        var s = await WeeklyWorkspaceFixture.Seed(factory);
        using var http = Client(factory, s.Company);
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IRevenueForecastService>()
                .CalculateAndPersistForecastAsync(s.Company, WeeklyWorkspaceFixture.Now.AddDays(-1), default);
        await factory.SeedAsync(async db =>
        {
            var capture = await db.RevenueForecastSnapshots.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == s.Company);
            db.Entry(capture).Property(x => x.InputsJson).CurrentValue =
                $"{{\"CalculationAsOfUtc\":\"2026-09-30T12:00:00Z\",\"CalculationVersion\":\"{RevenueForecastCalculation.Version}\",\"Inputs\":{inputs}}}";
        });

        var report = (await http.GetFromJsonAsync<SalesManagementReport>(Root + "?year=2026&month=9"))!;
        var snapshot = Assert.Single(report.ForecastHistory);
        Assert.False(snapshot.InputsAvailable);
        Assert.Null(snapshot.Capture);
        Assert.Contains("aggregates only", snapshot.Coverage);
        Assert.True(snapshot.Expected30 > 0);
    }

    [Theory]
    [InlineData("Assumptions")]
    [InlineData("Result")]
    [InlineData("CurrentOpenOpportunityIds")]
    [InlineData("HoursPerOpportunity")]
    [InlineData("Currency")]
    public async Task Invalid_retained_assumptions_or_report_context_are_integrity_failures(string field)
    {
        using var factory = new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());
        var s = await WeeklyWorkspaceFixture.Seed(factory);
        using var http = Client(factory, s.Company);
        var saved = await Save(http);
        var contents = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(new
        {
            saved.Report, saved.Assumptions, saved.Result
        }))!;
        if (field == "CurrentOpenOpportunityIds") contents["Report"]![field] = null;
        else if (field == "HoursPerOpportunity") contents["Assumptions"]![field] = 0;
        else if (field == "Currency") contents["Report"]![field] = "EUR";
        else contents[field] = null;
        var payload = contents.ToJsonString();
        var checksum = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload)));
        var row = new SalesCapacityProposalRevision(s.Company, s.Owner, Guid.NewGuid(), Guid.NewGuid(), 1, null,
            2026, 9, null, WeeklyWorkspaceFixture.Now, payload, checksum);
        await factory.SeedAsync(db => { db.SalesCapacityProposalRevisions.Add(row); return Task.CompletedTask; });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await http.GetAsync(Root + $"/proposals/{row.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(Root + $"/proposals/{saved.Summary.Id}")).StatusCode);
    }

    [Fact]
    public async Task Representable_fractional_capacity_assumptions_remain_supported()
    {
        using var factory = new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());
        var s = await WeeklyWorkspaceFixture.Seed(factory);
        using var http = Client(factory, s.Company);
        var command = Command() with { Assumptions = new(160, 0.25m, [new("North", 100)], "") };

        var saved = await Save(http, command);
        Assert.Equal(640, saved.Result.OpportunityCapacity);
        Assert.Equal(0.25m, saved.Result.ExpectedWorkloadHours);
        var reopened = (await http.GetFromJsonAsync<SalesCapacityProposal>(Root + $"/proposals/{saved.Summary.Id}"))!;
        Assert.Equal(JsonSerializer.Serialize(saved), JsonSerializer.Serialize(reopened));
    }
}




