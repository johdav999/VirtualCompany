using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Application.Marketing;

namespace VirtualCompany.Api.Tests;

[Trait("Category","SqlServer")]
public sealed class MarketingManagementSqlServerTests
{
    [ApiSqlServerFact]
    public async Task Upgrade_down_up_preserves_native_measurement_portfolio_and_monthly_history_without_backfill()
    {
        using var f=TestWebApplicationFactory.CreateSqlServer(new WeeklyWorkspaceFixture.Clock());var s=await MarketingManagementFixture.Seed(f);using var http=MarketingManagementFixture.Client(f,s.Company.Company);var monthly=await MonthlyReviewSnapshotIntegrationTests.Save(http,s.Company.Company,"marketing");
        await f.SeedAsync(async db=>{
            var count=await db.MarketingAttributionTouches.IgnoreQueryFilters().CountAsync();var migrations=(await db.Database.GetAppliedMigrationsAsync()).ToArray();var index=Array.FindIndex(migrations,x=>x.EndsWith("AddMarketingManagementBudgetProposals"));Assert.True(index>0);
            await db.GetService<IMigrator>().MigrateAsync(migrations[index-1]);await db.GetService<IMigrator>().MigrateAsync();db.ChangeTracker.Clear();
            Assert.Equal(count,await db.MarketingAttributionTouches.IgnoreQueryFilters().CountAsync());Assert.Empty(await db.MarketingBudgetProposalRevisions.IgnoreQueryFilters().ToArrayAsync());Assert.True(await db.MonthlyReviewSnapshots.IgnoreQueryFilters().AnyAsync(x=>x.Id==monthly.Summary.Id));Assert.Equal(600,(await db.MarketingPlans.IgnoreQueryFilters().SingleAsync(x=>x.CompanyId==s.Company.Company)).PlannedBudget);
        });var saved=await MarketingManagementIntegrationTests.Save(http,s.Command());Assert.Equal(20,Assert.Single(saved.Report.Channels.Single(x=>x.Channel=="email").Economics).CostPerAttributedUnit);
    }
    [ApiSqlServerFact]
    public async Task Concurrent_successors_and_duplicate_requests_are_durable_with_one_audit_per_revision()
    {
        using var f=TestWebApplicationFactory.CreateSqlServer(new WeeklyWorkspaceFixture.Clock());var s=await MarketingManagementFixture.Seed(f);using var a=MarketingManagementFixture.Client(f,s.Company.Company);using var b=MarketingManagementFixture.Client(f,s.Company.Company);var saved=await MarketingManagementIntegrationTests.Save(a,s.Command());
        var responses=await Task.WhenAll(a.PostAsJsonAsync(MarketingManagementIntegrationTests.Root+"/proposals",s.Command(saved.Summary.Id,1)),b.PostAsJsonAsync(MarketingManagementIntegrationTests.Root+"/proposals",s.Command(saved.Summary.Id,1)));
        Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.Conflict);
        var retry=s.Command();responses=await Task.WhenAll(a.PostAsJsonAsync(MarketingManagementIntegrationTests.Root+"/proposals",retry),b.PostAsJsonAsync(MarketingManagementIntegrationTests.Root+"/proposals",retry));Assert.All(responses,x=>Assert.Equal(HttpStatusCode.OK,x.StatusCode));
        var one=(await responses[0].Content.ReadFromJsonAsync<MarketingBudgetProposal>())!;var two=(await responses[1].Content.ReadFromJsonAsync<MarketingBudgetProposal>())!;Assert.Equal(one.Summary.Id,two.Summary.Id);
        await f.SeedAsync(async db=>Assert.Equal(3,await db.AuditEvents.IgnoreQueryFilters().CountAsync(x=>x.CompanyId==s.Company.Company&&x.Action=="marketing.budget.proposal_saved")));
    }
    [ApiSqlServerFact]
    public async Task Revision_immutability_and_corruption_are_enforced_on_the_real_provider()
    {
        using var f=TestWebApplicationFactory.CreateSqlServer(new WeeklyWorkspaceFixture.Clock());var s=await MarketingManagementFixture.Seed(f);using var http=MarketingManagementFixture.Client(f,s.Company.Company);var saved=await MarketingManagementIntegrationTests.Save(http,s.Command());
        await f.SeedAsync(async db=>{
            var row=await db.MarketingBudgetProposalRevisions.IgnoreQueryFilters().SingleAsync(x=>x.Id==saved.Summary.Id);db.Entry(row).Property(x=>x.Currency).CurrentValue="EUR";await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE marketing_budget_proposal_revisions SET Payload = N'{{}}' WHERE Id = {saved.Summary.Id}");
        });Assert.Equal(HttpStatusCode.UnprocessableEntity,(await http.GetAsync(MarketingManagementIntegrationTests.Root+"/proposals/"+saved.Summary.Id)).StatusCode);
    }
}
