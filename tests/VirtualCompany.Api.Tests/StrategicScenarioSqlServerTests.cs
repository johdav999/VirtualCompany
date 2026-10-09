using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using F=VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using FinanceForecastRevision=VirtualCompany.Domain.Entities.FinanceForecastRevision;
namespace VirtualCompany.Api.Tests;
[Trait("Category","SqlServer")]
public sealed class StrategicScenarioSqlServerTests
{
    [ApiSqlServerFact]public async Task Migration_roundtrip_preserves_approved_annual_and_native_finance_sources()
    {
        using var f=TestWebApplicationFactory.CreateSqlServer(new SupportQualityFixture.Clock());var s=await StrategicScenarioFixture.Seed(f);
        await f.SeedAsync(async db=>{var plan=await db.Set<AnnualPlanVersion>().IgnoreQueryFilters().SingleAsync(x=>x.Id==s.PlanId);var checksum=await db.FinanceForecastRevisions.IgnoreQueryFilters().Where(x=>x.Id==s.ForecastId).Select(x=>x.Checksum).SingleAsync();
            var migrations=(await db.Database.GetAppliedMigrationsAsync()).ToArray();var i=Array.FindIndex(migrations,x=>x.EndsWith("AddStrategicScenarios"));Assert.True(i>0);
            await db.GetService<IMigrator>().MigrateAsync(migrations[i-1]);await db.GetService<IMigrator>().MigrateAsync();db.ChangeTracker.Clear();
            Assert.Equal("approved",(await db.Set<AnnualPlanVersion>().IgnoreQueryFilters().SingleAsync(x=>x.Id==plan.Id)).Status);Assert.Equal(checksum,await db.FinanceForecastRevisions.IgnoreQueryFilters().Where(x=>x.Id==s.ForecastId).Select(x=>x.Checksum).SingleAsync());});
        using var h=s.Annual.Client(f);var d=await StrategicScenarioFixture.Save(h,s);Assert.Equal(3,d.Scenario.Years.Count);
    }
    [ApiSqlServerFact]public async Task Concurrent_successors_have_one_winner_and_cross_company_forecast_and_children_fk_reject()
    {
        using var f=TestWebApplicationFactory.CreateSqlServer(new SupportQualityFixture.Clock());var s=await StrategicScenarioFixture.Seed(f);using var h=s.Annual.Client(f);using var other=s.Annual.Client(f);
        var d=await StrategicScenarioFixture.Save(h,s);var p=await AnnualPlanningFixture.Read<F.StrategicScenarioPreview>(await h.PostAsJsonAsync(s.Root+"/preview",s.Input));var cmd=new F.SaveStrategicScenario(Guid.NewGuid(),s.Input,p.Fingerprint,d.Summary.Id,1);
        var replies=await Task.WhenAll(h.PostAsJsonAsync(s.Root+"/versions",cmd),other.PostAsJsonAsync(s.Root+"/versions",cmd with{RequestId=Guid.NewGuid()}));Assert.Single(replies,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(replies,x=>x.StatusCode==HttpStatusCode.Conflict);
        await f.SeedAsync(async db=>{db.Add(new StrategicScenarioCash{Id=Guid.NewGuid(),CompanyId=s.Annual.Quarter.Company.Foreign,ScenarioId=d.Summary.Id,Year=9,Rationale="Wrong company"});await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();});
        await f.SeedAsync(async db=>{var foreign=Guid.NewGuid();db.Add(new FinanceForecastRevision(foreign,s.Annual.Quarter.Company.Foreign,s.Annual.Quarter.Company.Owner,Guid.NewGuid(),"Foreign forecast","foreign-native",null,DateTime.UtcNow,DateTime.UtcNow,"{}",new string('A',64),new string('B',64)));await db.SaveChangesAsync();
            var ex=await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(()=>db.Set<StrategicScenarioVersion>().IgnoreQueryFilters().Where(x=>x.Id==d.Summary.Id).ExecuteUpdateAsync(u=>u.SetProperty(x=>x.ForecastRevisionId,foreign)));Assert.Equal(547,ex.Number);db.ChangeTracker.Clear();});
    }
}
