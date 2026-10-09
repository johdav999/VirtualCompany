using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using Revision=VirtualCompany.Application.Finance.FinanceForecastRevision;
namespace VirtualCompany.Api.Tests;

public sealed class FinanceRollingPlanningIntegrationTests
{
    public static string Parameters(FinancePlanningQuery q)=>$"year={q.Year}&month={q.Month}&months={q.Months}&budgetVersion={Uri.EscapeDataString(q.BudgetVersion??"")}&forecastVersion={Uri.EscapeDataString(q.ForecastVersion??"")}&currency={q.Currency}&financeAccountId={q.FinanceAccountId}&costCenterId={q.CostCenterId}&fiscalPeriodId={q.FiscalPeriodId}";
    public static async Task<T> Read<T>(HttpResponseMessage response){Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());return (await response.Content.ReadFromJsonAsync<T>())!;}
    public static async Task<Revision> Save(HttpClient http,FinanceRollingPlanningFixture s,decimal amount=-1250.25m,Guid? previous=null){var input=s.Input(amount);var p=await Read<FinanceForecastPreview>(await http.PostAsJsonAsync(s.Root+"/preview",input));return await Read<Revision>(await http.PostAsJsonAsync(s.Root+"/versions",new SaveFinanceForecast(Guid.NewGuid(),"Recorded outlook",input,p.Fingerprint,previous)));}
    [Fact]public async Task Variance_sources_negative_decimals_versions_history_gaps_and_export_reconcile_without_seeding(){
        using var f=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await FinanceRollingPlanningFixture.Seed(f);using var h=s.Client(f);
        var r=await Read<FinancePlanningReport>(await h.GetAsync(s.Root+"/analysis?"+Parameters(s.Query)));var row=Assert.Single(r.Rows.Where(x=>x.MonthUtc==FinanceRollingPlanningFixture.Date(2026,9)));
        Assert.Equal(-1000.10m,row.Actual);Assert.Equal(-1100,row.Budget);Assert.Equal(99.90m,row.Variance);Assert.Equal(-9.08m,row.VariancePercent);Assert.Equal(-900.05m,row.PriorYear);
        Assert.Null(r.Rows.Single(x=>x.MonthUtc.Month==11).Actual);Assert.DoesNotContain("Foreign secret",JsonSerializer.Serialize(r));Assert.DoesNotContain(r.Sources,x=>x.Amount==-777);
        var export=await Read<FinancePlanningExport>(await h.GetAsync(s.Root+"/export?"+Parameters(s.Query)));Assert.Equal(r.Fingerprint,export.Fingerprint);Assert.Contains("\"99.90\"",export.Content);
        var unselected=await Read<FinancePlanningReport>(await h.GetAsync(s.Root+"/analysis?"+Parameters(s.Query with{BudgetVersion=null})));Assert.All(unselected.Rows,x=>Assert.Null(x.Budget));
        await f.SeedAsync(async db=>{Assert.Equal(4,await db.Budgets.IgnoreQueryFilters().CountAsync());Assert.Empty(await db.Forecasts.IgnoreQueryFilters().ToArrayAsync());});
    }
    [Fact]public async Task Named_versions_native_rows_reopen_identically_notes_and_monthly_snapshots_retain_original_sources(){
        using var f=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await FinanceRollingPlanningFixture.Seed(f);using var h=s.Client(f);var report=await Read<FinancePlanningReport>(await h.GetAsync(s.Root+"/analysis?"+Parameters(s.Query)));
        var note=new ExplainFinanceVariance(Guid.NewGuid(),s.Query,FinanceRollingPlanningFixture.Date(2026,9),s.Account,null,"SEK",report.Fingerprint,"Revenue posted later than budget.");
        var explained=await Read<FinanceVarianceExplanationDto>(await h.PostAsJsonAsync(s.Root+"/explanations",note));Assert.Equal(explained.Id,(await Read<FinanceVarianceExplanationDto>(await h.PostAsJsonAsync(s.Root+"/explanations",note))).Id);
        var a=await Save(h,s);var b=await Save(h,s,-1400.25m,a.Summary.Id);var opened=await Read<Revision>(await h.GetAsync(s.Root+"/versions/"+a.Summary.Id));Assert.Equal(JsonSerializer.Serialize(a),JsonSerializer.Serialize(opened));
        var comparison=await Read<FinanceForecastComparison>(await h.GetAsync(s.Root+$"/compare?earlier={a.Summary.Id}&later={b.Summary.Id}&currency=SEK"));Assert.Equal(-150,comparison.Rows.Single(x=>x.MonthUtc.Month==11).Change);
        await f.SeedAsync(async db=>{Assert.Equal(4,await db.Forecasts.IgnoreQueryFilters().CountAsync(x=>x.CompanyId==s.Company.Company));Assert.True((await db.FiscalPeriods.IgnoreQueryFilters().SingleAsync(x=>x.Id==s.Period)).IsClosed);
            Assert.Equal(-1000.10m,a.Preview.Values.Single(x=>x.MonthUtc.Month==9).Actual);var budget=await db.Budgets.IgnoreQueryFilters().SingleAsync(x=>x.CompanyId==s.Company.Company&&x.FinanceAccountId==s.Account&&x.Version=="approved"&&x.PeriodStartUtc.Month==9);budget.Update(s.Account,budget.PeriodStartUtc,"approved",-5000,"SEK");});
        opened=await Read<Revision>(await h.GetAsync(s.Root+"/versions/"+a.Summary.Id));Assert.Equal(-1100,opened.Preview.Values.Single(x=>x.MonthUtc.Month==9).Budget);
        var monthly=await MonthlyReviewSnapshotIntegrationTests.Save(h,s.Company.Company,"finance");Assert.NotNull(monthly.Workspace.FinancePlanning);
        var retained=await Read<VirtualCompany.Application.Cockpit.MonthlyReviewSnapshotDto>(await h.PostAsync($"/api/companies/{s.Company.Company}/workspace/monthly/reviews/{monthly.Summary.Id}/open",null));Assert.Equal(monthly.Workspace.FinancePlanning!.Fingerprint,retained.Workspace.FinancePlanning!.Fingerprint);
    }
    [Fact]public async Task Preview_rejects_actual_closed_currency_dimension_precision_duplicate_and_future_cutoff_errors(){
        using var f=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await FinanceRollingPlanningFixture.Seed(f);using var h=s.Client(f);var i=s.Input();
        var cases=new[]{i with{Assumptions=[i.Assumptions[0] with{MonthUtc=FinanceRollingPlanningFixture.Date(2026,9)}]},i with{Assumptions=[i.Assumptions[0] with{Currency="EUR"}]},
            i with{Assumptions=[i.Assumptions[0] with{CostCenterId=s.ForeignDimension}]},s.Input(1.001m),i with{Assumptions=[i.Assumptions[0],i.Assumptions[0]]},i with{ActualThroughUtc=FinanceRollingPlanningFixture.Date(2026,11)}};
        foreach(var c in cases)Assert.Equal(HttpStatusCode.BadRequest,(await h.PostAsJsonAsync(s.Root+"/preview",c)).StatusCode);
        await f.SeedAsync(db=>{db.FiscalPeriods.Add(new VirtualCompany.Domain.Entities.FiscalPeriod(Guid.NewGuid(),s.Company.Company,"Future closed",FinanceRollingPlanningFixture.Date(2026,11),FinanceRollingPlanningFixture.Date(2026,12),true));return Task.CompletedTask;});
        Assert.Equal(HttpStatusCode.BadRequest,(await h.PostAsJsonAsync(s.Root+"/preview",i)).StatusCode);
    }
    [Fact]public async Task Source_changes_stale_preview_conflicts_retry_identity_immutability_and_company_scope_are_enforced(){
        using var f=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await FinanceRollingPlanningFixture.Seed(f);using var h=s.Client(f);var input=s.Input();var p=await Read<FinanceForecastPreview>(await h.PostAsJsonAsync(s.Root+"/preview",input));
        var command=new SaveFinanceForecast(Guid.NewGuid(),"Immutable outlook",input,p.Fingerprint);var saved=await Read<Revision>(await h.PostAsJsonAsync(s.Root+"/versions",command));Assert.Equal(saved.Summary.Id,(await Read<Revision>(await h.PostAsJsonAsync(s.Root+"/versions",command))).Summary.Id);
        Assert.Equal(HttpStatusCode.Conflict,(await h.PostAsJsonAsync(s.Root+"/versions",command with{Name="Different"})).StatusCode);
        await f.SeedAsync(async db=>{var row=await db.Forecasts.IgnoreQueryFilters().FirstAsync(x=>x.RevisionId==saved.Summary.Id);db.Entry(row).Property(x=>x.Amount).CurrentValue=99;await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
            var budget=await db.Budgets.IgnoreQueryFilters().FirstAsync(x=>x.CompanyId==s.Company.Company&&x.Version=="approved");budget.Update(budget.FinanceAccountId,budget.PeriodStartUtc,budget.Version,777,budget.Currency);});
        Assert.Equal(HttpStatusCode.Conflict,(await h.PostAsJsonAsync(s.Root+"/versions",command with{RequestId=Guid.NewGuid()})).StatusCode);
        using var manager=s.Client(f,"p19-manager");Assert.Equal(HttpStatusCode.Forbidden,(await manager.GetAsync(s.Root+"/versions/"+saved.Summary.Id)).StatusCode);
        using var foreign=WeeklyWorkspaceFixture.Client(f);Assert.Equal(HttpStatusCode.Forbidden,(await foreign.GetAsync(s.Root.Replace(s.Company.Company.ToString(),s.Company.Foreign.ToString())+"/versions/"+saved.Summary.Id)).StatusCode);
    }
    [Fact]public async Task Exact_fiscal_period_and_effective_dimension_policy_are_validated(){
        using var f=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await FinanceRollingPlanningFixture.Seed(f);using var h=s.Client(f);
        Assert.Equal(HttpStatusCode.OK,(await h.GetAsync(s.Root+"/analysis?"+Parameters(s.Query with{Months=1,FiscalPeriodId=s.Period}))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await h.GetAsync(s.Root+"/analysis?"+Parameters(s.Query with{FiscalPeriodId=s.Period}))).StatusCode);
        var i=s.Input() with{Assumptions=[s.Input().Assumptions[0] with{CostCenterId=s.Dimension}]};Assert.Equal(HttpStatusCode.OK,(await h.PostAsJsonAsync(s.Root+"/preview",i)).StatusCode);
        await f.SeedAsync(async db=>{var type=await db.AccountingDimensionTypes.IgnoreQueryFilters().SingleAsync(x=>x.CompanyId==s.Company.Company);db.AccountingDimensionAccountPolicies.Add(new VirtualCompany.Domain.Entities.AccountingDimensionAccountPolicy(Guid.NewGuid(),s.Company.Company,s.Account,type.Id,"required",new DateOnly(2026,1,1),null,s.Company.Owner,WeeklyWorkspaceFixture.Now));});
        Assert.Equal(HttpStatusCode.BadRequest,(await h.PostAsJsonAsync(s.Root+"/preview",s.Input())).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await h.PostAsJsonAsync(s.Root+"/preview",i)).StatusCode);
    }
    [Fact]public async Task Revoked_finance_scope_withholds_company_snapshot_nested_planning_and_all_current_exports(){
        using var f=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await FinanceRollingPlanningFixture.Seed(f);using var h=s.Client(f);
        var saved=await MonthlyReviewSnapshotIntegrationTests.Save(h,s.Company.Company,"company");Assert.NotNull(saved.Workspace.FinancePlanning);
        await f.SeedAsync(async db=>{db.CompanyResponsibilityAssignments.RemoveRange(await db.CompanyResponsibilityAssignments.IgnoreQueryFilters().Where(x=>x.CompanyId==s.Company.Company&&x.ResponsibilityArea==VirtualCompany.Domain.Enums.ResponsibilityArea.CashAndAccounting).ToListAsync());});
        Assert.Equal(HttpStatusCode.Forbidden,(await h.PostAsync($"/api/companies/{s.Company.Company}/workspace/monthly/reviews/{saved.Summary.Id}/open",null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await h.GetAsync(s.Root+"/export?"+Parameters(s.Query))).StatusCode);
    }
}

