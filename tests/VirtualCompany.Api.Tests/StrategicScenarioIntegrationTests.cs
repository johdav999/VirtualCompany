using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using F=VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Api.Tests;
public sealed class StrategicScenarioIntegrationTests
{
    [Fact]public async Task Native_sources_duplicate_edit_compare_and_reopen_remain_stable_when_live_actuals_change()
    {
        using var f=new TestWebApplicationFactory(new SupportQualityFixture.Clock());var s=await StrategicScenarioFixture.Seed(f);using var h=s.Annual.Client(f);
        var baseline=await StrategicScenarioFixture.Save(h,s);Assert.Equal(3550.85m,baseline.Scenario.Source.Revenue);Assert.Equal(1700,baseline.Scenario.Source.Expense);Assert.Contains(baseline.Scenario.Warnings,x=>x.Contains("explicit annual multiplier 3"));
        var duplicate=await AnnualPlanningFixture.Read<F.StrategicScenarioDocument>(await h.PostAsJsonAsync(s.Root+$"/versions/{baseline.Summary.Id}/duplicate",new F.DuplicateStrategicScenario(Guid.NewGuid(),"Higher capacity")));
        Assert.Equal(baseline.Scenario.Years,duplicate.Scenario.Years);Assert.NotEqual(baseline.Summary.SeriesId,duplicate.Summary.SeriesId);
        var changed=duplicate.Scenario.Input with{Drivers=duplicate.Scenario.Input.Drivers with{CapacityUnits=100}};
        var next=await StrategicScenarioFixture.Save(h,s,changed,duplicate);Assert.Equal(2,next.Summary.Revision);
        var comparison=await AnnualPlanningFixture.Read<F.StrategicScenarioComparison>(await h.PostAsJsonAsync(s.Root+"/compare",new{Baseline=baseline.Summary.Id,Alternative=next.Summary.Id}));Assert.True(comparison.Deltas[0].Revenue>0);Assert.Contains(comparison.AssumptionChanges,x=>x.Contains("Base capacity (orders)"));
        await f.SeedAsync(db=>{var id=Guid.NewGuid();var cash=db.FinanceAccounts.IgnoreQueryFilters().Single(x=>x.CompanyId==s.Annual.Quarter.Company.Company&&x.Code=="P23-1000");db.Add(new LedgerEntry(id,s.Annual.Quarter.Company.Company,s.Finance.Period,"SCENARIO-LIVE",FinanceRollingPlanningFixture.Date(2026,9).AddDays(20),LedgerEntryStatuses.Posted,"Live actual changed",postedAtUtc:FinanceRollingPlanningFixture.Date(2026,9).AddDays(20),postingDate:new DateOnly(2026,9,21),baseCurrency:"SEK"));db.AddRange(new LedgerEntryLine(Guid.NewGuid(),s.Annual.Quarter.Company.Company,id,s.Finance.Account,0,500,"SEK"),new LedgerEntryLine(Guid.NewGuid(),s.Annual.Quarter.Company.Company,id,cash.Id,500,0,"SEK"));return Task.CompletedTask;});
        var old=await AnnualPlanningFixture.Read<F.StrategicScenarioDocument>(await h.PostAsync(s.Root+$"/versions/{baseline.Summary.Id}/open",null));Assert.Equal(baseline.Scenario.Fingerprint,old.Scenario.Fingerprint);Assert.Equal(baseline.Scenario.Years,old.Scenario.Years);
        await f.SeedAsync(async db=>{Assert.Equal("approved",(await db.Set<AnnualPlanVersion>().IgnoreQueryFilters().SingleAsync(x=>x.Id==s.PlanId)).Status);Assert.Equal(4,await db.Forecasts.IgnoreQueryFilters().CountAsync(x=>x.RevisionId==s.ForecastId));Assert.Empty(await db.PaymentInstructions.IgnoreQueryFilters().Where(x=>x.CompanyId==s.Annual.Quarter.Company.Company).ToListAsync());});
    }
    [Fact]public async Task Idempotent_commands_stale_parents_scope_and_currency_boundaries_are_enforced()
    {
        using var f=new TestWebApplicationFactory(new SupportQualityFixture.Clock());var s=await StrategicScenarioFixture.Seed(f);using var h=s.Annual.Client(f);
        var p=await AnnualPlanningFixture.Read<F.StrategicScenarioPreview>(await h.PostAsJsonAsync(s.Root+"/preview",s.Input));var cmd=new F.SaveStrategicScenario(Guid.NewGuid(),s.Input,p.Fingerprint);
        var a=await AnnualPlanningFixture.Read<F.StrategicScenarioDocument>(await h.PostAsJsonAsync(s.Root+"/versions",cmd));var retry=await AnnualPlanningFixture.Read<F.StrategicScenarioDocument>(await h.PostAsJsonAsync(s.Root+"/versions",cmd));Assert.Equal(a.Summary.Id,retry.Summary.Id);
        Assert.Equal(HttpStatusCode.Conflict,(await h.PostAsJsonAsync(s.Root+"/versions",cmd with{Input=s.Input with{Name="Request altered"}})).StatusCode);
        await StrategicScenarioFixture.Save(h,s,s.Input,a);Assert.Equal(HttpStatusCode.Conflict,(await h.PostAsJsonAsync(s.Root+"/versions",cmd with{RequestId=Guid.NewGuid(),PreviousId=a.Summary.Id,ExpectedRevision=1})).StatusCode);
        foreach(var bad in new[]{s.Input with{Currency="EUR"},s.Input with{OwnerId=Guid.NewGuid()},s.Input with{Cash=s.Input.Cash.Take(2).ToArray()},s.Input with{Checkpoints=[new(1,"Foreign dependency",s.Input.OwnerId,Guid.NewGuid())]}})
            Assert.Equal(HttpStatusCode.BadRequest,(await h.PostAsJsonAsync(s.Root+"/preview",bad)).StatusCode);
        using var manager=s.Annual.Client(f,"p19-manager");using var foreign=s.Annual.Client(f,"p19-foreign");foreach(var denied in new[]{manager,foreign}){Assert.Equal(HttpStatusCode.Forbidden,(await denied.PostAsync(s.Root+$"/versions/{a.Summary.Id}/open",null)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await denied.PostAsJsonAsync(s.Root+"/preview",s.Input)).StatusCode);}
        var duplicateCommand=new F.DuplicateStrategicScenario(Guid.NewGuid(),"Stable alternative");var copy=await AnnualPlanningFixture.Read<F.StrategicScenarioDocument>(await h.PostAsJsonAsync(s.Root+$"/versions/{a.Summary.Id}/duplicate",duplicateCommand));var copyRetry=await AnnualPlanningFixture.Read<F.StrategicScenarioDocument>(await h.PostAsJsonAsync(s.Root+$"/versions/{a.Summary.Id}/duplicate",duplicateCommand));Assert.Equal(copy.Summary.Id,copyRetry.Summary.Id);
    }
    [Fact]public async Task Missing_numeric_inputs_and_corrupted_saved_outputs_fail_explicitly()
    {
        using var f=new TestWebApplicationFactory(new SupportQualityFixture.Clock());var s=await StrategicScenarioFixture.Seed(f);using var h=s.Annual.Client(f);
        var node=System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(s.Input))!;node["Drivers"]!.AsObject().Remove("OpeningCash");Assert.Equal(HttpStatusCode.BadRequest,(await h.PostAsync(s.Root+"/preview",JsonContent.Create(node))).StatusCode);
        var d=await StrategicScenarioFixture.Save(h,s);await f.SeedAsync(async db=>{await db.Set<StrategicScenarioOutput>().IgnoreQueryFilters().Where(x=>x.ScenarioId==d.Summary.Id).ExecuteDeleteAsync();});
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await h.PostAsync(s.Root+$"/versions/{d.Summary.Id}/open",null)).StatusCode);
    }
    [Fact]public async Task Draft_baselines_unavailable_sources_duplicate_checkpoints_and_incompatible_comparisons_block()
    {
        using var f=new TestWebApplicationFactory(new SupportQualityFixture.Clock());var s=await StrategicScenarioFixture.Seed(f);using var h=s.Annual.Client(f);
        var original=await StrategicScenarioFixture.Save(h,s);Assert.Equal(original.Scenario.Owner,original.Scenario.Checkpoints.Single().Owner);
        h.DefaultRequestHeaders.Remove("X-Dev-Auth-DisplayName");h.DefaultRequestHeaders.Add("X-Dev-Auth-DisplayName","Renamed scenario owner");
        var retained=await AnnualPlanningFixture.Read<F.StrategicScenarioDocument>(await h.PostAsync(s.Root+$"/versions/{original.Summary.Id}/open",null));Assert.Equal(original.Scenario.Checkpoints.Single().Owner,retained.Scenario.Checkpoints.Single().Owner);
        var approved=await AnnualPlanningFixture.Read<VirtualCompany.Application.Orchestration.AnnualPlanDocument>(await h.PostAsync(s.Annual.Root+$"/versions/{s.PlanId}/open",null));
        var draft=await AnnualPlanningFixture.Save(h,s.Annual,s.Annual.Input,approved);
        Assert.Equal(HttpStatusCode.BadRequest,(await h.PostAsJsonAsync(s.Root+"/preview",s.Input with{AnnualPlanId=draft.Summary.Id})).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await h.PostAsJsonAsync(s.Root+"/preview",s.Input with{ForecastRevisionId=Guid.NewGuid()})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await h.PostAsJsonAsync(s.Root+"/preview",s.Input with{Checkpoints=[s.Input.Checkpoints[0],s.Input.Checkpoints[0]]})).StatusCode);
        var longer=await StrategicScenarioFixture.Save(h,s,s.Input with{Drivers=s.Input.Drivers with{Years=4},Cash=s.Input.Cash.Append(new(4,0,0,"Fourth-year explicit zero amounts")).ToArray()});
        Assert.Equal(HttpStatusCode.BadRequest,(await h.PostAsJsonAsync(s.Root+"/compare",new{Baseline=original.Summary.Id,Alternative=longer.Summary.Id})).StatusCode);
    }
    [Fact]public async Task Bounded_history_keeps_an_explicit_cursor_and_reopens_older_source_versions()
    {
        using var f=new TestWebApplicationFactory(new SupportQualityFixture.Clock());var s=await StrategicScenarioFixture.Seed(f);using var h=s.Annual.Client(f);var d=await StrategicScenarioFixture.Save(h,s);
        for(var n=0;n<20;n++)await AnnualPlanningFixture.Read<F.StrategicScenarioDocument>(await h.PostAsJsonAsync(s.Root+$"/versions/{d.Summary.Id}/duplicate",new F.DuplicateStrategicScenario(Guid.NewGuid(),$"Alternative {n}")));
        var first=await AnnualPlanningFixture.Read<F.StrategicScenarioHistoryPage>(await h.GetAsync(s.Root+"/versions?skip=0"));Assert.True(first.HasMore);Assert.Equal(20,first.Items.Count);
        var next=await AnnualPlanningFixture.Read<F.StrategicScenarioHistoryPage>(await h.GetAsync(s.Root+"/versions?skip=20"));Assert.False(next.HasMore);Assert.Equal(20,next.Skip);Assert.Single(next.Items);
        var ids=first.Items.Concat(next.Items).Select(x=>x.Id).ToArray();Assert.Equal(21,ids.Distinct().Count());Assert.Contains(d.Summary.Id,ids);
        var reopened=await AnnualPlanningFixture.Read<F.StrategicScenarioDocument>(await h.PostAsync(s.Root+$"/versions/{d.Summary.Id}/open",null));Assert.Equal(d.Scenario.Fingerprint,reopened.Scenario.Fingerprint);
    }
}
