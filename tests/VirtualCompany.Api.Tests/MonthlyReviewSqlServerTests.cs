using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Application.Cockpit;
namespace VirtualCompany.Api.Tests;
[Trait("Category","SqlServer")]
public sealed class MonthlyReviewSqlServerTests
{
    [ApiSqlServerFact]
    public async Task Migration_upgrade_down_up_preserves_native_prior_work_and_unique_revision_indexes()
    {
        using var factory=TestWebApplicationFactory.CreateSqlServer(new WeeklyWorkspaceFixture.Clock());var s=await WeeklyWorkspaceFixture.Seed(factory);
        await factory.SeedAsync(async db=>{
            var migrations=(await db.Database.GetAppliedMigrationsAsync()).ToArray();var index=Array.FindIndex(migrations,x=>x.EndsWith("AddMonthlyManagementReviewSnapshots"));Assert.True(index>0);
            var taskCount=await db.WorkTasks.IgnoreQueryFilters().CountAsync(x=>x.CompanyId==s.Company);
            var sourceCount=await db.SalesActivities.IgnoreQueryFilters().CountAsync(x=>x.CompanyId==s.Company);
            await db.GetService<IMigrator>().MigrateAsync(migrations[index-1]);
            await db.GetService<IMigrator>().MigrateAsync();db.ChangeTracker.Clear();
            Assert.Equal(taskCount,await db.WorkTasks.IgnoreQueryFilters().CountAsync(x=>x.CompanyId==s.Company));
            Assert.Equal(sourceCount,await db.SalesActivities.IgnoreQueryFilters().CountAsync(x=>x.CompanyId==s.Company));
            Assert.Empty(await db.MonthlyReviewSnapshots.IgnoreQueryFilters().ToListAsync());
        });
        using var http=WeeklyWorkspaceFixture.Client(factory);
        var saved=await MonthlyReviewSnapshotIntegrationTests.Save(http,s.Company);
        Assert.Equal(1,saved.Summary.Revision);
    }
    [ApiSqlServerFact]
    public async Task Simultaneous_SQL_refreshes_admit_one_revision_and_repeated_request_returns_winner()
    {
        using var factory=TestWebApplicationFactory.CreateSqlServer(new WeeklyWorkspaceFixture.Clock());var s=await WeeklyWorkspaceFixture.Seed(factory);
        using var first=WeeklyWorkspaceFixture.Client(factory);using var second=WeeklyWorkspaceFixture.Client(factory);
        var saved=await MonthlyReviewSnapshotIntegrationTests.Save(first,s.Company);var uri=MonthlyReviewSnapshotIntegrationTests.Root(s.Company)+$"/{saved.Summary.Id}/refresh";
        var responses=await Task.WhenAll(first.PostAsJsonAsync(uri,new RefreshMonthlyReviewCommand(1,Guid.NewGuid())),second.PostAsJsonAsync(uri,new RefreshMonthlyReviewCommand(1,Guid.NewGuid())));
        Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.Conflict);
        await factory.SeedAsync(async db=>Assert.Equal(2,await db.MonthlyReviewSnapshots.IgnoreQueryFilters().CountAsync(x=>x.CompanyId==s.Company)));
        var request=Guid.NewGuid();var command=new SaveMonthlyReviewCommand("sales",2026,9,request);
        responses=await Task.WhenAll(first.PostAsJsonAsync(MonthlyReviewSnapshotIntegrationTests.Root(s.Company),command),second.PostAsJsonAsync(MonthlyReviewSnapshotIntegrationTests.Root(s.Company),command));
        Assert.All(responses,x=>Assert.Equal(HttpStatusCode.OK,x.StatusCode));
        var results=await Task.WhenAll(responses.Select(x=>x.Content.ReadFromJsonAsync<MonthlyReviewSnapshotDto>()));Assert.Equal(results[0]!.Summary.Id,results[1]!.Summary.Id);
    }
    [ApiSqlServerFact]
    public async Task SQL_native_all_role_snapshots_survive_source_deletion_and_export_retained_inputs()
    {
        using var factory=TestWebApplicationFactory.CreateSqlServer(new WeeklyWorkspaceFixture.Clock());var s=await WeeklyWorkspaceFixture.Seed(factory);using var http=WeeklyWorkspaceFixture.Client(factory);
        var saved=new List<MonthlyReviewSnapshotDto>();
        foreach(var lens in new[]{"company","sales","marketing","finance","customers"}) saved.Add(await MonthlyReviewSnapshotIntegrationTests.Save(http,s.Company,lens));
        await factory.SeedAsync(db=>db.SalesActivities.IgnoreQueryFilters().Where(x=>x.CompanyId==s.Company).ExecuteDeleteAsync());
        foreach(var old in saved)
        {
            var response=await http.PostAsync(MonthlyReviewSnapshotIntegrationTests.Root(s.Company)+$"/{old.Summary.Id}/open",null);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            var opened=await response.Content.ReadFromJsonAsync<MonthlyReviewSnapshotDto>();Assert.Equal(old.Checksum,opened!.Checksum);
            var export=await (await http.PostAsync(MonthlyReviewSnapshotIntegrationTests.Root(s.Company)+$"/{old.Summary.Id}/export",null)).Content.ReadFromJsonAsync<MonthlyReviewExportDto>();
            Assert.Contains(old.Checksum,export!.Csv);
        }
        var sales=saved.Single(x=>x.Summary.Lens=="sales");
        var revised=await (await http.PostAsJsonAsync(MonthlyReviewSnapshotIntegrationTests.Root(s.Company)+$"/{sales.Summary.Id}/refresh",new RefreshMonthlyReviewCommand(1,Guid.NewGuid()))).Content.ReadFromJsonAsync<MonthlyReviewSnapshotDto>();
        Assert.Equal(0,revised!.Workspace.Results.Single(x=>x.Key=="sales.stage_movement").Value);
    }
}
