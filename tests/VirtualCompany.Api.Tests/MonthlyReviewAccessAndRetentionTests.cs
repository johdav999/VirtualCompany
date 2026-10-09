using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
namespace VirtualCompany.Api.Tests;
public sealed class MonthlyReviewAccessAndRetentionTests
{
    [Fact]
    public async Task Retained_count_inputs_recheck_task_scope_without_a_responsibility_revision_change()
    {
        using var factory=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await WeeklyWorkspaceFixture.Seed(factory);
        await factory.SeedAsync(async db=>{
            var member=await db.CompanyMemberships.IgnoreQueryFilters().SingleAsync(x=>x.CompanyId==s.Company && x.UserId==s.Manager);
            db.Add(new CompanyResponsibilityAssignment(Guid.NewGuid(),s.Company,ResponsibilityArea.CompanyPerformance,ResponsibilityAssignmentKind.Primary,member.Id,null,AgentAutonomyLevel.Level1,null,null));
            var task=await db.WorkTasks.IgnoreQueryFilters().SingleAsync(x=>x.Id==s.TaskId);db.Entry(task).Property(x=>x.CompletedUtc).CurrentValue=WeeklyWorkspaceFixture.Start.AddHours(2);
        });
        using var http=WeeklyWorkspaceFixture.Client(factory,s.ManagerSubject);var saved=await MonthlyReviewSnapshotIntegrationTests.Save(http,s.Company,"company");
        Assert.Contains(s.TaskId,saved.Workspace.Review!.WorkSourceIds!);
        await factory.SeedAsync(async db=>{var task=await db.WorkTasks.IgnoreQueryFilters().SingleAsync(x=>x.Id==s.TaskId);db.Entry(task).Property(x=>x.Type).CurrentValue="finance_private_review";});
        var root=MonthlyReviewSnapshotIntegrationTests.Root(s.Company);
        Assert.Equal(HttpStatusCode.Forbidden,(await http.PostAsync(root+$"/{saved.Summary.Id}/open",null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await http.PostAsync(root+$"/{saved.Summary.Id}/export",null)).StatusCode);
        Assert.Empty((await http.GetFromJsonAsync<MonthlyReviewHistoryDto>(root+"?lens=company&year=2026&month=9"))!.Items);
        var live=await http.GetFromJsonAsync<MonthlyWorkspaceDto>($"/api/companies/{s.Company}/workspace/monthly?lens=company&year=2026&month=9");
        Assert.DoesNotContain(s.TaskId,live!.Review!.WorkSourceIds!);
    }
    [Fact]
    public async Task Deleted_Work_input_reopens_retained_results_but_company_deletion_ends_retention()
    {
        using var factory=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await WeeklyWorkspaceFixture.Seed(factory);
        await factory.SeedAsync(async db=>{var task=await db.WorkTasks.IgnoreQueryFilters().SingleAsync(x=>x.Id==s.TaskId);db.Entry(task).Property(x=>x.CompletedUtc).CurrentValue=WeeklyWorkspaceFixture.Start.AddHours(2);});
        using var http=WeeklyWorkspaceFixture.Client(factory);var saved=await MonthlyReviewSnapshotIntegrationTests.Save(http,s.Company,"company");
        await factory.SeedAsync(db=>db.WorkTasks.IgnoreQueryFilters().Where(x=>x.Id==s.TaskId).ExecuteDeleteAsync());
        var opened=await (await http.PostAsync(MonthlyReviewSnapshotIntegrationTests.Root(s.Company)+$"/{saved.Summary.Id}/open",null)).Content.ReadFromJsonAsync<MonthlyReviewSnapshotDto>();
        Assert.Equal(saved.Checksum,opened!.Checksum);Assert.Contains(s.TaskId,opened.Workspace.Review!.WorkSourceIds!);
        // A minimal independent company verifies the retention FK without unrelated native deletion restrictions.
        var company=Guid.NewGuid();var rowId=Guid.NewGuid();
        await factory.SeedAsync(db=>{
            db.Add(new Company(company,"Retention boundary"));db.Add(new MonthlyReviewSnapshot(rowId,company,s.Owner,Guid.NewGuid(),1,null,Guid.NewGuid(),"sales",2026,9,new string('a',64),"{}",new string('b',64),WeeklyWorkspaceFixture.Now,WeeklyWorkspaceFixture.Now,"v1"));return Task.CompletedTask;
        });
        await factory.SeedAsync(async db=>{
            var companyRow=await db.Companies.IgnoreQueryFilters().SingleAsync(x=>x.Id==company);
            _=await db.MonthlyReviewSnapshots.IgnoreQueryFilters().SingleAsync(x=>x.Id==rowId);db.Remove(companyRow);await db.SaveChangesAsync();
            Assert.False(await db.MonthlyReviewSnapshots.IgnoreQueryFilters().AnyAsync(x=>x.Id==rowId));
        });
    }
    [Fact]
    public async Task History_pagination_is_bounded_and_preserves_immutable_records()
    {
        using var factory=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await WeeklyWorkspaceFixture.Seed(factory);using var http=WeeklyWorkspaceFixture.Client(factory);
        for(var i=0;i<3;i++)await MonthlyReviewSnapshotIntegrationTests.Save(http,s.Company,notes:$"Review {i}");
        var root=MonthlyReviewSnapshotIntegrationTests.Root(s.Company)+"?lens=sales&year=2026&month=9&take=2";
        var first=await http.GetFromJsonAsync<MonthlyReviewHistoryDto>(root);var second=await http.GetFromJsonAsync<MonthlyReviewHistoryDto>(root+"&skip=2");
        Assert.Equal(2,first!.Items.Count);Assert.True(first.HasMore);Assert.Single(second!.Items);Assert.False(second.HasMore);
        Assert.Empty(first.Items.Select(x=>x.Id).Intersect(second.Items.Select(x=>x.Id)));
    }
}
