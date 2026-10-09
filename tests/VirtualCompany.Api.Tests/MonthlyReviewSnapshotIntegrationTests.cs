using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed class MonthlyReviewSnapshotIntegrationTests
{
    public static string Root(Guid company)=>$"/api/companies/{company}/workspace/monthly/reviews";
    public static async Task<MonthlyReviewSnapshotDto> Save(HttpClient http,Guid company,string lens="sales",Guid? request=null,string? notes="Reviewed next-period risks")
    {
        var response=await http.PostAsJsonAsync(Root(company),new SaveMonthlyReviewCommand(lens,2026,9,request??Guid.NewGuid(),notes));
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MonthlyReviewSnapshotDto>())!;
    }
    [Theory][InlineData("company")][InlineData("sales")][InlineData("marketing")][InlineData("finance")][InlineData("customers")]
    public async Task All_roles_save_open_list_and_export_the_same_authorized_values(string lens)
    {
        using var factory=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());
        var seed=await WeeklyWorkspaceFixture.Seed(factory); using var http=WeeklyWorkspaceFixture.Client(factory);
        var saved=await Save(http,seed.Company,lens);
        Assert.Equal(lens,saved.Workspace.ActiveLens); Assert.NotNull(saved.Workspace.Review);
        Assert.All(saved.Workspace.Results,m=>Assert.Contains(saved.Workspace.Review!.Measures,x=>x.Key==m.Key));
        var opened=await (await http.PostAsync(Root(seed.Company)+$"/{saved.Summary.Id}/open",null)).Content.ReadFromJsonAsync<MonthlyReviewSnapshotDto>();
        Assert.Equal(JsonSerializer.Serialize(saved.Workspace),JsonSerializer.Serialize(opened!.Workspace));
        var history=await http.GetFromJsonAsync<MonthlyReviewHistoryDto>(Root(seed.Company)+$"?lens={lens}&year=2026&month=9");
        Assert.Equal(saved.Summary.Id,Assert.Single(history!.Items).Id); Assert.Equal(saved.Summary.Coverage,history.Items[0].Coverage);
        var export=await (await http.PostAsync(Root(seed.Company)+$"/{saved.Summary.Id}/export",null)).Content.ReadFromJsonAsync<MonthlyReviewExportDto>();
        Assert.Contains(saved.Checksum,export!.Csv); Assert.Contains(saved.Summary.Id.ToString(),export.Csv);
        Assert.Equal(saved.Workspace.Review!.Measures.Count+1,export.Csv.Split('\n',StringSplitOptions.RemoveEmptyEntries).Length);
        if(lens=="marketing") Assert.Contains(saved.Workspace.Review.Measures,m=>m.Actual==null && m.Explanation.Contains("No authoritative"));
    }
    [Fact]
    public async Task Corrected_and_deleted_sources_leave_old_inputs_unchanged_and_refresh_appends_new_revision()
    {
        using var factory=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());
        var seed=await WeeklyWorkspaceFixture.Seed(factory);using var http=WeeklyWorkspaceFixture.Client(factory);
        var saved=await Save(http,seed.Company);
        var original=saved.Workspace.Results.Single(x=>x.Key=="sales.stage_movement").Value;
        await factory.SeedAsync(async db=>{
            var sources=await db.SalesActivities.IgnoreQueryFilters().Where(x=>x.CompanyId==seed.Company).ToListAsync();
            db.Entry(sources[0]).Property(x=>x.ActivityType).CurrentValue="note";
            db.SalesActivities.RemoveRange(sources.Skip(1));
        });
        var old=await (await http.PostAsync(Root(seed.Company)+$"/{saved.Summary.Id}/open",null)).Content.ReadFromJsonAsync<MonthlyReviewSnapshotDto>();
        Assert.Equal(original,old!.Workspace.Results.Single(x=>x.Key=="sales.stage_movement").Value); Assert.Equal(saved.Checksum,old.Checksum);
        var response=await http.PostAsJsonAsync(Root(seed.Company)+$"/{saved.Summary.Id}/refresh",new RefreshMonthlyReviewCommand(1,Guid.NewGuid(),"Corrected source review"));
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var revision=(await response.Content.ReadFromJsonAsync<MonthlyReviewSnapshotDto>())!;
        Assert.Equal(2,revision.Summary.Revision);Assert.Equal(saved.Summary.Id,revision.Summary.PreviousId);Assert.Equal(saved.Summary.SeriesId,revision.Summary.SeriesId);
        Assert.Equal(0,revision.Workspace.Results.Single(x=>x.Key=="sales.stage_movement").Value);Assert.NotEqual(saved.Checksum,revision.Checksum);
        Assert.Contains(revision.Changes,x=>x.Contains("Pipeline movement"));Assert.Equal("Corrected source review",revision.ReviewNotes);
        await factory.SeedAsync(async db=>{
            Assert.Equal(2,await db.MonthlyReviewSnapshots.IgnoreQueryFilters().CountAsync(x=>x.CompanyId==seed.Company));
            Assert.Contains(await db.AuditEvents.IgnoreQueryFilters().Where(x=>x.CompanyId==seed.Company).Select(x=>x.Action).ToListAsync(),x=>x=="monthly.review.refreshed");
        });
    }
    [Fact]
    public async Task Request_retries_are_idempotent_and_old_revision_cannot_fork()
    {
        using var factory=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await WeeklyWorkspaceFixture.Seed(factory);using var http=WeeklyWorkspaceFixture.Client(factory);
        var request=Guid.NewGuid();var saved=await Save(http,s.Company,request:request);
        Assert.Equal(saved.Summary.Id,(await Save(http,s.Company,request:request)).Summary.Id);
        Assert.Equal(HttpStatusCode.Conflict,(await http.PostAsJsonAsync(Root(s.Company),new SaveMonthlyReviewCommand("sales",2026,9,request,"Changed notes"))).StatusCode);
        var command=new RefreshMonthlyReviewCommand(1,Guid.NewGuid());var uri=Root(s.Company)+$"/{saved.Summary.Id}/refresh";
        var first=await (await http.PostAsJsonAsync(uri,command)).Content.ReadFromJsonAsync<MonthlyReviewSnapshotDto>();
        var retry=await (await http.PostAsJsonAsync(uri,command)).Content.ReadFromJsonAsync<MonthlyReviewSnapshotDto>();
        Assert.Equal(first!.Summary.Id,retry!.Summary.Id);
        Assert.Equal(HttpStatusCode.Conflict,(await http.PostAsJsonAsync(uri,new RefreshMonthlyReviewCommand(1,Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await http.PostAsJsonAsync(uri,new RefreshMonthlyReviewCommand(2,Guid.NewGuid()))).StatusCode);
    }
    [Fact]
    public async Task Company_user_and_current_responsibility_access_apply_to_save_open_history_refresh_and_export()
    {
        using var factory=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await WeeklyWorkspaceFixture.Seed(factory);
        using var owner=WeeklyWorkspaceFixture.Client(factory);using var manager=WeeklyWorkspaceFixture.Client(factory,s.ManagerSubject);
        var saved=await Save(owner,s.Company);
        foreach(var verb in new[]{"open","export"}) Assert.Equal(HttpStatusCode.NotFound,(await manager.PostAsync(Root(s.Company)+$"/{saved.Summary.Id}/{verb}",null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await manager.PostAsJsonAsync(Root(s.Company)+$"/{saved.Summary.Id}/refresh",new RefreshMonthlyReviewCommand(1,Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await manager.PostAsJsonAsync(Root(s.Foreign),new SaveMonthlyReviewCommand("sales",2026,9,Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await manager.PostAsJsonAsync(Root(s.Company),new SaveMonthlyReviewCommand("finance",2026,9,Guid.NewGuid()))).StatusCode);
        Assert.Empty((await manager.GetFromJsonAsync<MonthlyReviewHistoryDto>(Root(s.Company)+"?lens=sales&year=2026&month=9"))!.Items);
        await factory.SeedAsync(async db=>{
            var assignment=await db.CompanyResponsibilityAssignments.IgnoreQueryFilters().SingleAsync(x=>x.CompanyId==s.Company && x.ResponsibilityArea==ResponsibilityArea.Sales && x.AssignmentKind==ResponsibilityAssignmentKind.ExecutiveOversight);
            db.CompanyResponsibilityAssignments.Remove(assignment);
        });
        Assert.Equal(HttpStatusCode.Forbidden,(await owner.PostAsync(Root(s.Company)+$"/{saved.Summary.Id}/open",null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await owner.PostAsync(Root(s.Company)+$"/{saved.Summary.Id}/export",null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await owner.PostAsJsonAsync(Root(s.Company)+$"/{saved.Summary.Id}/refresh",new RefreshMonthlyReviewCommand(1,Guid.NewGuid()))).StatusCode);
    }
    [Fact]
    public async Task Integrity_failure_is_audited_and_never_returns_retained_results()
    {
        using var factory=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await WeeklyWorkspaceFixture.Seed(factory);using var http=WeeklyWorkspaceFixture.Client(factory);
        var saved=await Save(http,s.Company);
        await factory.SeedAsync(db=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE monthly_review_snapshots SET PayloadJson='{{}}' WHERE Id={saved.Summary.Id}"));
        var response=await http.PostAsync(Root(s.Company)+$"/{saved.Summary.Id}/open",null);
        Assert.Equal((HttpStatusCode)422,response.StatusCode);Assert.DoesNotContain("Weekly proposal",await response.Content.ReadAsStringAsync());
        await factory.SeedAsync(async db=>Assert.Equal(1,await db.AuditEvents.IgnoreQueryFilters().CountAsync(x=>x.CompanyId==s.Company && x.Action=="monthly.review.reproduction_failed")));
    }
    [Fact]
    public async Task Immutability_and_retention_do_not_allow_individual_update_or_delete()
    {
        using var factory=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await WeeklyWorkspaceFixture.Seed(factory);using var http=WeeklyWorkspaceFixture.Client(factory);var saved=await Save(http,s.Company);
        await factory.SeedAsync(async db=>{
            var row=await db.MonthlyReviewSnapshots.IgnoreQueryFilters().SingleAsync(x=>x.Id==saved.Summary.Id);
            db.Entry(row).Property(x=>x.PayloadJson).CurrentValue="changed";
            await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
            row=await db.MonthlyReviewSnapshots.IgnoreQueryFilters().SingleAsync(x=>x.Id==saved.Summary.Id);db.Remove(row);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
        });
        Assert.Equal(HttpStatusCode.MethodNotAllowed,(await http.DeleteAsync(Root(s.Company)+$"/{saved.Summary.Id}/open")).StatusCode);
        Assert.Contains("until company deletion",saved.Retention);
    }
    [Theory][InlineData(1999,9,0)][InlineData(2026,13,0)][InlineData(2026,9,-1)][InlineData(2026,9,10001)]
    public async Task History_validation_rejects_invalid_scope_or_unbounded_pages(int year,int month,int skip)
    {
        using var factory=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await WeeklyWorkspaceFixture.Seed(factory);using var http=WeeklyWorkspaceFixture.Client(factory);
        Assert.Equal(HttpStatusCode.BadRequest,(await http.GetAsync(Root(s.Company)+$"?lens=sales&year={year}&month={month}&skip={skip}")).StatusCode);
    }
    [Fact]
    public async Task Notes_and_payload_size_limits_are_enforced_including_UTF8_bytes()
    {
        using var factory=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await WeeklyWorkspaceFixture.Seed(factory);using var http=WeeklyWorkspaceFixture.Client(factory);
        Assert.Equal(HttpStatusCode.BadRequest,(await http.PostAsJsonAsync(Root(s.Company),new SaveMonthlyReviewCommand("sales",2026,9,Guid.NewGuid(),new string('x',2001)))).StatusCode);
        Assert.Throws<ArgumentException>(()=>new MonthlyReviewSnapshot(Guid.NewGuid(),s.Company,s.Owner,Guid.NewGuid(),1,null,Guid.NewGuid(),"sales",2026,9,new string('a',64),new string('å',MonthlyReviewSnapshot.MaximumPayloadBytes/2+1),new string('b',64),WeeklyWorkspaceFixture.Now,WeeklyWorkspaceFixture.Now,"v1"));
    }
    [Fact]
    public async Task Exact_monthly_targets_are_retained_ambiguous_annual_and_currency_mismatches_are_not_inferred()
    {
        using var factory=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await WeeklyWorkspaceFixture.Seed(factory);using var http=WeeklyWorkspaceFixture.Client(factory);
        var start=new DateTime(2026,8,31,22,0,0,DateTimeKind.Utc);var end=new DateTime(2026,9,30,22,0,0,DateTimeKind.Utc);var id=Guid.NewGuid();
        await factory.SeedAsync(db=>{
            var target=new CompanyGoal(id,s.Company,"Monthly movement","Retained monthly target",CompanyGoalPriority.Normal,start,end,"sales.stage_movement","stage changes",0,10,s.Owner,null);target.Activate();db.Add(target);
            var annual=new CompanyGoal(Guid.NewGuid(),s.Company,"Annual movement","Do not infer",CompanyGoalPriority.Normal,start.AddMonths(-8),end.AddMonths(3),"sales.stage_movement","stage changes",0,100,s.Owner,null);annual.Activate();db.Add(annual);
            return Task.CompletedTask;
        });
        var saved=await Save(http,s.Company);var m=saved.Workspace.Review!.Measures.Single(x=>x.Key=="sales.stage_movement");Assert.Equal(10,m.Target);Assert.Equal(id,m.TargetId);Assert.Equal(m.Actual-10,m.TargetVariance);
        await factory.SeedAsync(async db=>{
            var target=await db.CompanyGoals.IgnoreQueryFilters().SingleAsync(x=>x.Id==id);target.Update(target.Name,target.Outcome,target.Priority,start,end,target.MetricKey,"EUR",0,99,s.Owner,null,null);
        });
        var revised=await (await http.PostAsJsonAsync(Root(s.Company)+$"/{saved.Summary.Id}/refresh",new RefreshMonthlyReviewCommand(1,Guid.NewGuid()))).Content.ReadFromJsonAsync<MonthlyReviewSnapshotDto>();
        Assert.Null(revised!.Workspace.Review!.Measures.Single(x=>x.Key==m.Key).Target);
        Assert.Equal(10,(await (await http.PostAsync(Root(s.Company)+$"/{saved.Summary.Id}/open",null)).Content.ReadFromJsonAsync<MonthlyReviewSnapshotDto>())!.Workspace.Review!.Measures.Single(x=>x.Key==m.Key).Target);
    }
    [Fact]
    public async Task Monthly_SLA_includes_due_unanswered_cases_and_excludes_future_unanswered_targets()
    {
        using var factory=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await WeeklyWorkspaceFixture.Seed(factory);using var http=WeeklyWorkspaceFixture.Client(factory);
        await factory.SeedAsync(async db=>{
            var source=await db.SupportCases.IgnoreQueryFilters().SingleAsync(x=>x.Id==s.Case);source.SetSla(WeeklyWorkspaceFixture.Start.AddDays(1),WeeklyWorkspaceFixture.Start.AddDays(4));
            db.Entry(source).Property(x=>x.FirstResponseSentUtc).CurrentValue=WeeklyWorkspaceFixture.Start.AddHours(2);
            var unanswered=new SupportCase(Guid.NewGuid(),s.Company,"P20-DUE","Due unanswered",null,"manual",createdUtc:WeeklyWorkspaceFixture.Start);
            unanswered.SetSla(WeeklyWorkspaceFixture.Start.AddDays(1),WeeklyWorkspaceFixture.Start.AddDays(4));db.Add(unanswered);
            var future=new SupportCase(Guid.NewGuid(),s.Company,"P20-FUTURE","Future deadline",null,"manual",createdUtc:WeeklyWorkspaceFixture.Start);
            future.SetSla(WeeklyWorkspaceFixture.Now.AddDays(5),WeeklyWorkspaceFixture.Now.AddDays(6));db.Add(future);
        });
        var saved=await Save(http,s.Company,"customers");Assert.Equal(50,saved.Workspace.Results.Single(x=>x.Key=="support.sla").Value);
        Assert.Contains(saved.Workspace.Review!.Measures,x=>x.Key=="support.sla" && x.Definition.Contains("Overdue unanswered"));
    }
}
