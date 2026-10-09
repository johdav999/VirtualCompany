using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Application.Sales;
namespace VirtualCompany.Api.Tests;
[Trait("Category","SqlServer")]
public sealed class SalesManagementSqlServerTests
{
    [ApiSqlServerFact]
    public async Task Upgrade_down_up_preserves_native_activity_forecast_and_monthly_history_without_backfill()
    {
        using var factory=TestWebApplicationFactory.CreateSqlServer(new WeeklyWorkspaceFixture.Clock());var s=await WeeklyWorkspaceFixture.Seed(factory);using var http=SalesManagementIntegrationTests.Client(factory,s.Company);
        var monthly=await MonthlyReviewSnapshotIntegrationTests.Save(http,s.Company);
        await factory.SeedAsync(async db=>{
            var before=await db.SalesActivities.IgnoreQueryFilters().CountAsync(x=>x.CompanyId==s.Company);var migrations=(await db.Database.GetAppliedMigrationsAsync()).ToArray();var i=Array.FindIndex(migrations,x=>x.EndsWith("AddSalesManagementHistoryAndCapacityProposals"));Assert.True(i>0);
            await db.GetService<IMigrator>().MigrateAsync(migrations[i-1]);await db.GetService<IMigrator>().MigrateAsync();db.ChangeTracker.Clear();
            Assert.Equal(before,await db.SalesActivities.IgnoreQueryFilters().CountAsync(x=>x.CompanyId==s.Company));Assert.Empty(await db.SalesCapacityProposalRevisions.IgnoreQueryFilters().ToListAsync());
            Assert.All(await db.SalesActivities.IgnoreQueryFilters().Where(x=>x.CompanyId==s.Company).ToArrayAsync(),x=>Assert.Null(x.RecordedReason));Assert.True(await db.MonthlyReviewSnapshots.IgnoreQueryFilters().AnyAsync(x=>x.Id==monthly.Summary.Id));
        });
        var proposal=await SalesManagementIntegrationTests.Save(http);Assert.Equal(20,proposal.Result.OpportunityCapacity);
    }
    [ApiSqlServerFact]
    public async Task Concurrent_successors_and_duplicate_requests_are_durable_and_reproduce()
    {
        using var factory=TestWebApplicationFactory.CreateSqlServer(new WeeklyWorkspaceFixture.Clock());var s=await WeeklyWorkspaceFixture.Seed(factory);using var a=SalesManagementIntegrationTests.Client(factory,s.Company);using var b=SalesManagementIntegrationTests.Client(factory,s.Company);
        var saved=await SalesManagementIntegrationTests.Save(a);var responses=await Task.WhenAll(a.PostAsJsonAsync(SalesManagementIntegrationTests.Root+"/proposals",SalesManagementIntegrationTests.Command(saved.Summary.Id,1)),b.PostAsJsonAsync(SalesManagementIntegrationTests.Root+"/proposals",SalesManagementIntegrationTests.Command(saved.Summary.Id,1)));
        Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.Conflict);
        var retry=SalesManagementIntegrationTests.Command();responses=await Task.WhenAll(a.PostAsJsonAsync(SalesManagementIntegrationTests.Root+"/proposals",retry),b.PostAsJsonAsync(SalesManagementIntegrationTests.Root+"/proposals",retry));
        Assert.All(responses,x=>Assert.Equal(HttpStatusCode.OK,x.StatusCode));var one=(await responses[0].Content.ReadFromJsonAsync<SalesCapacityProposal>())!;var two=(await responses[1].Content.ReadFromJsonAsync<SalesCapacityProposal>())!;Assert.Equal(one.Summary.Id,two.Summary.Id);
    }
}

