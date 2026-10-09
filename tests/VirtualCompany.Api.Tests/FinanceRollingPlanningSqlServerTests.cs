using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Application.Finance;
using Revision=VirtualCompany.Application.Finance.FinanceForecastRevision;
namespace VirtualCompany.Api.Tests;

[Trait("Category","SqlServer")]
public sealed class FinanceRollingPlanningSqlServerTests
{
    [ApiSqlServerFact]public async Task Upgrade_down_up_preserves_native_plans_closed_actuals_and_review_snapshots(){
        using var f=TestWebApplicationFactory.CreateSqlServer(new WeeklyWorkspaceFixture.Clock());var s=await FinanceRollingPlanningFixture.Seed(f);using var h=s.Client(f);
        var monthly=await MonthlyReviewSnapshotIntegrationTests.Save(h,s.Company.Company,"finance");
        await f.SeedAsync(async db=>{var plans=await db.Budgets.IgnoreQueryFilters().CountAsync();var actuals=await db.LedgerEntryLines.IgnoreQueryFilters().CountAsync();
            var migrations=(await db.Database.GetAppliedMigrationsAsync()).ToArray();var index=Array.FindIndex(migrations,x=>x.EndsWith("AddFinanceRollingPlanningRevisions"));Assert.True(index>0);
            await db.GetService<IMigrator>().MigrateAsync(migrations[index-1]);await db.GetService<IMigrator>().MigrateAsync();db.ChangeTracker.Clear();
            Assert.Equal(plans,await db.Budgets.IgnoreQueryFilters().CountAsync());Assert.Equal(actuals,await db.LedgerEntryLines.IgnoreQueryFilters().CountAsync());Assert.Empty(await db.FinanceForecastRevisions.IgnoreQueryFilters().ToArrayAsync());
            Assert.True((await db.FiscalPeriods.IgnoreQueryFilters().SingleAsync(x=>x.Id==s.Period)).IsClosed);Assert.True(await db.MonthlyReviewSnapshots.IgnoreQueryFilters().AnyAsync(x=>x.Id==monthly.Summary.Id));});
        var saved=await FinanceRollingPlanningIntegrationTests.Save(h,s);Assert.Equal(-1250.25m,saved.Preview.Values.Single(x=>x.MonthUtc.Month==11).Forecast);
    }
    [ApiSqlServerFact]public async Task Duplicate_requests_and_concurrent_successors_have_one_native_write_and_audit_per_version(){
        using var f=TestWebApplicationFactory.CreateSqlServer(new WeeklyWorkspaceFixture.Clock());var s=await FinanceRollingPlanningFixture.Seed(f);using var a=s.Client(f);using var b=s.Client(f);
        var parent=await FinanceRollingPlanningIntegrationTests.Save(a,s);var input=s.Input();var p=await FinanceRollingPlanningIntegrationTests.Read<FinanceForecastPreview>(await a.PostAsJsonAsync(s.Root+"/preview",input));
        var c=new SaveFinanceForecast(Guid.NewGuid(),"Concurrent successor",input,p.Fingerprint,parent.Summary.Id);
        var responses=await Task.WhenAll(a.PostAsJsonAsync(s.Root+"/versions",c),b.PostAsJsonAsync(s.Root+"/versions",c with{RequestId=Guid.NewGuid()}));Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.Conflict);
        c=c with{RequestId=Guid.NewGuid(),PreviousId=null};responses=await Task.WhenAll(a.PostAsJsonAsync(s.Root+"/versions",c),b.PostAsJsonAsync(s.Root+"/versions",c));
        var one=await FinanceRollingPlanningIntegrationTests.Read<Revision>(responses[0]);var two=await FinanceRollingPlanningIntegrationTests.Read<Revision>(responses[1]);Assert.Equal(one.Summary.Id,two.Summary.Id);
        await f.SeedAsync(async db=>{Assert.Equal(3,await db.FinanceForecastRevisions.IgnoreQueryFilters().CountAsync());Assert.Equal(6,await db.Forecasts.IgnoreQueryFilters().CountAsync());Assert.Equal(3,await db.AuditEvents.IgnoreQueryFilters().CountAsync(x=>x.CompanyId==s.Company.Company&&x.Action=="finance.forecast.version_saved"));});
    }
    [ApiSqlServerFact]public async Task Immutable_links_decimal_values_composite_tenant_fk_and_corruption_are_enforced(){
        using var f=TestWebApplicationFactory.CreateSqlServer(new WeeklyWorkspaceFixture.Clock());var s=await FinanceRollingPlanningFixture.Seed(f);using var h=s.Client(f);var saved=await FinanceRollingPlanningIntegrationTests.Save(h,s);
        await f.SeedAsync(async db=>{var native=await db.Forecasts.IgnoreQueryFilters().FirstAsync(x=>x.RevisionId==saved.Summary.Id);Assert.Equal(saved.Summary.NativeVersion,native.Version);
            db.Entry(native).Property(x=>x.RevisionId).CurrentValue=null;await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
            var foreignAccount=await db.FinanceAccounts.IgnoreQueryFilters().FirstAsync(x=>x.CompanyId==s.Company.Foreign);var invalid=new VirtualCompany.Domain.Entities.Forecast(Guid.NewGuid(),s.Company.Foreign,foreignAccount.Id,FinanceRollingPlanningFixture.Date(2026,11),"cross-tenant",1,"SEK");invalid.LinkRevision(saved.Summary.Id);db.Forecasts.Add(invalid);await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE finance_forecast_revisions SET Payload = N'{{}}' WHERE Id = {saved.Summary.Id}");});
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await h.GetAsync(s.Root+"/versions/"+saved.Summary.Id)).StatusCode);
    }
}
