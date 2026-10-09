using A=VirtualCompany.Application.Orchestration;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Api.Tests;
[Trait("Category","SqlServer")]
public sealed class AnnualPlanningSqlServerTests
{
 [ApiSqlServerFact]public async Task Annual_migration_roundtrip_preserves_native_quarter_sources_and_finance_budget(){using var f=TestWebApplicationFactory.CreateSqlServer(new SupportQualityFixture.Clock());var s=await AnnualPlanningFixture.Seed(f);await f.SeedAsync(async db=>{var q=await db.Set<QuarterlyReview>().IgnoreQueryFilters().Where(x=>x.Id==s.ReviewId).Select(x=>x.Fingerprint).SingleAsync();var before=await db.Budgets.IgnoreQueryFilters().Where(x=>x.Id==s.BudgetId).Select(x=>new{x.Amount,x.Version,x.Currency}).SingleAsync();var all=(await db.Database.GetAppliedMigrationsAsync()).ToArray();var i=Array.FindIndex(all,x=>x.EndsWith("AddAnnualPlanning"));Assert.True(i>0);await db.GetService<IMigrator>().MigrateAsync(all[i-1]);await db.GetService<IMigrator>().MigrateAsync();db.ChangeTracker.Clear();Assert.Equal(q,await db.Set<QuarterlyReview>().IgnoreQueryFilters().Where(x=>x.Id==s.ReviewId).Select(x=>x.Fingerprint).SingleAsync());Assert.Equal(before,await db.Budgets.IgnoreQueryFilters().Where(x=>x.Id==s.BudgetId).Select(x=>new{x.Amount,x.Version,x.Currency}).SingleAsync());});using var h=s.Client(f);Assert.True((await AnnualPlanningFixture.Save(h,s)).CanReview);}
 [ApiSqlServerFact]public async Task Annual_concurrent_revisions_have_one_winner_and_cross_company_budget_fk_rejects(){using var f=TestWebApplicationFactory.CreateSqlServer(new SupportQualityFixture.Clock());var s=await AnnualPlanningFixture.Seed(f);using var h=s.Client(f);using var other=s.Client(f);var p=await AnnualPlanningFixture.Read<A.AnnualPlanPreview>(await h.PostAsJsonAsync(s.Root+"/preview",s.Input));var cmd=new A.SaveAnnualPlan(s.Input,null,0,Guid.NewGuid(),p.Fingerprint);var r=await Task.WhenAll(h.PostAsJsonAsync(s.Root+"/versions",cmd),other.PostAsJsonAsync(s.Root+"/versions",cmd with{RequestId=Guid.NewGuid()}));Assert.Single(r,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(r,x=>x.StatusCode==HttpStatusCode.Conflict);var plan=await AnnualPlanningFixture.Read<A.AnnualPlanDocument>(r.Single(x=>x.IsSuccessStatusCode));await f.SeedAsync(async db=>{db.Add(new AnnualBudgetBinding{Id=Guid.NewGuid(),CompanyId=s.Quarter.Company.Foreign,PlanId=plan.Summary.Id,BudgetId=s.BudgetId,AccountId=s.AccountId,Account="Wrong scope",Currency="SEK",NativeVersion="test",SourceFingerprint=new string('A',64)});await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();});}
}
