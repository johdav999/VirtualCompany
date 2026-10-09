using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Support;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
namespace VirtualCompany.Api.Tests;

public sealed class SupportQualityIntegrationTests
{
    [Fact]public async Task Same_instant_committed_state_sequence_reproduces_reopening_and_empty_samples_remain_unavailable(){
        using var f=new TestWebApplicationFactory(new SupportQualityFixture.Clock());var s=await SupportQualityFixture.Seed(f);using var h=s.Client(f);var id=Guid.NewGuid();
        await f.SeedAsync(db=>{var c=new SupportCase(id,s.Company.Company,"P24-INSTANT","Sequenced service evidence",null,"manual",createdUtc:SupportQualityFixture.Now);c.SetCategory(SupportCaseCategories.Billing);db.Add(c);return Task.CompletedTask;});
        foreach(var state in new[]{SupportCaseStatuses.Resolved,SupportCaseStatuses.Reopened})await f.SeedAsync(async db=>(await db.SupportCases.IgnoreQueryFilters().SingleAsync(x=>x.Id==id)).SetStatus(state));
        var r=await Read<SupportQualityReport>(await h.GetAsync(s.Root+"?year=2026&month=10&category=billing"));Assert.Equal(1,r.Metrics.ResolvedCases);Assert.Equal(1,r.Metrics.ReopenedCases);Assert.Equal(100,r.Metrics.ReopenRate);Assert.Null(r.Metrics.MeanResponseMinutes);
        await f.SeedAsync(async db=>{var events=await db.SupportCaseEvents.IgnoreQueryFilters().Where(x=>x.SupportCaseId==id&&x.EventType==SupportCaseEventTypes.StateRecorded).OrderBy(x=>x.StateSequence).ToArrayAsync();Assert.Equal(new[]{1,2,3},events.Select(x=>x.StateSequence));});
    }
    [Fact]public async Task Source_bounds_withhold_complete_totals_export_and_capacity(){
        using var f=new TestWebApplicationFactory(new SupportQualityFixture.Clock());var s=await SupportQualityFixture.Seed(f);using var h=s.Client(f);
        await f.SeedAsync(db=>{for(var i=0;i<2001;i++)db.Add(new SupportCase(Guid.NewGuid(),s.Company.Company,"BOUND-"+i,"Bounded case",null,"manual",createdUtc:SupportQualityFixture.D(9,1)));return Task.CompletedTask;});
        var r=await Read<SupportQualityReport>(await h.GetAsync(s.Url));Assert.False(r.CompleteSourceCoverage);Assert.Contains("Source limit reached",r.Coverage);Assert.All(r.Backlog,b=>Assert.Null(b.Open));
        Assert.Equal(HttpStatusCode.Conflict,(await h.GetAsync(s.Root+"/export?year=2026&month=9&category=billing")).StatusCode);Assert.Equal(HttpStatusCode.BadRequest,(await h.PostAsJsonAsync(s.Root+"/preview",s.Input)).StatusCode);
    }
    internal static readonly JsonSerializerOptions Options=new(JsonSerializerDefaults.Web){Converters={new System.Text.Json.Serialization.JsonStringEnumConverter()}};
    internal static async Task<T> Read<T>(HttpResponseMessage response){Assert.Equal(HttpStatusCode.OK,response.StatusCode);return (await response.Content.ReadFromJsonAsync<T>(Options))!;}
    internal static async Task<SupportCapacityProposal> Save(HttpClient h,SupportQualityFixture s,PreviewSupportCapacity? input=null,Guid? previous=null){input??=s.Input;var p=await Read<SupportCapacityPreview>(await h.PostAsJsonAsync(s.Root+"/preview",input));return await Read<SupportCapacityProposal>(await h.PostAsJsonAsync(s.Root+"/proposals",new SaveSupportCapacityProposal(Guid.NewGuid(),"Support explicit proposal",input,p.Fingerprint,previous)));}
    [Fact]public async Task Cohorts_cross_period_reopen_waiting_merged_aliases_and_calendar_sources_reconcile(){
        using var f=new TestWebApplicationFactory(new SupportQualityFixture.Clock());var s=await SupportQualityFixture.Seed(f);using var h=s.Client(f);var r=await Read<SupportQualityReport>(await h.GetAsync(s.Url));
        Assert.Equal(3,r.Metrics.Arrivals);Assert.Equal(2,r.Metrics.ResolvedCases);Assert.Equal(1,r.Metrics.ReopenedCases);Assert.Equal(50,r.Metrics.ReopenRate);Assert.Equal(100,r.Previous.ReopenRate);Assert.Equal(2,r.Metrics.ResponseSample);Assert.Equal(1440,r.Metrics.MeanResponseMinutes);Assert.Equal(60,r.Metrics.MeanResponseBusinessMinutes);
        Assert.Equal(SupportQualityFixture.D(10,2,10),r.Cases.Single(x=>x.Id==s.First).ReopenedUtc);Assert.True(r.Cases.Single(x=>x.Id==s.Alias).Merged);Assert.False(r.Cases.Single(x=>x.Id==s.Alias).ResolutionCohort);
        var end=r.Backlog.Last();Assert.Null(end.Open);Assert.Equal(2,end.KnownOpen);Assert.Equal(1,end.UnknownCases);Assert.False(r.Cases.Single(x=>x.Id==s.Legacy).CompleteStateHistory);
        Assert.DoesNotContain(r.Cases,x=>x.Subject.Contains("Foreign"));Assert.Contains("do not pause",r.Coverage);var csv=await Read<SupportQualityExport>(await h.GetAsync(s.Root+"/export?year=2026&month=9&category=billing"));Assert.Equal(r.Fingerprint,csv.Fingerprint);Assert.Contains("\"50\"",csv.Content);Assert.Contains("P24-A",csv.Content);
    }
    [Fact]public async Task Reviewed_grouping_is_audited_idempotent_shared_and_does_not_change_native_case_category(){
        using var f=new TestWebApplicationFactory(new SupportQualityFixture.Clock());var s=await SupportQualityFixture.Seed(f);using var h=s.Client(f);var r=await Read<SupportQualityReport>(await h.GetAsync(s.Url));
        var c=new CorrectSupportIssueGrouping(Guid.NewGuid(),s.Query,s.First,"=Reviewed duplicate billing","Reviewed source evidence; this is a group, not a causal claim.",r.Fingerprint);
        var one=await Read<SupportIssueGroupingSummary>(await h.PostAsJsonAsync(s.Root+"/groupings",c));var two=await Read<SupportIssueGroupingSummary>(await h.PostAsJsonAsync(s.Root+"/groupings",c));Assert.Equal(one.Id,two.Id);
        Assert.Equal(HttpStatusCode.Conflict,(await h.PostAsJsonAsync(s.Root+"/groupings",c with{Reason="Changed request"})).StatusCode);
        var refreshed=await Read<SupportQualityReport>(await h.GetAsync(s.Url));Assert.True(refreshed.Cases.Single(x=>x.Id==s.First).ReviewedGroup);Assert.Equal(one.Id,refreshed.Cases.Single(x=>x.Id==s.First).GroupRevisionId);Assert.Equal(50,refreshed.Metrics.ReopenRate);
        var csv=await Read<SupportQualityExport>(await h.GetAsync(s.Root+"/export?year=2026&month=9&category=billing"));Assert.Contains("'=Reviewed",csv.Content);
        var successor=await Read<SupportIssueGroupingSummary>(await h.PostAsJsonAsync(s.Root+"/groupings",c with{RequestId=Guid.NewGuid(),Group="Second reviewed group",ExpectedFingerprint=refreshed.Fingerprint,PreviousId=one.Id}));
        var latest=await Read<SupportQualityReport>(await h.GetAsync(s.Url));Assert.Equal(successor.Id,latest.Cases.Single(x=>x.Id==s.First).GroupRevisionId);
        await f.SeedAsync(async db=>{Assert.Equal(SupportCaseCategories.Billing,(await db.SupportCases.IgnoreQueryFilters().SingleAsync(x=>x.Id==s.First)).Category);Assert.Equal(2,await db.AuditEvents.IgnoreQueryFilters().CountAsync(x=>x.CompanyId==s.Company.Company&&x.Action=="support.issue_grouping.corrected"));});
    }
    [Fact]public async Task Capacity_preview_save_reopen_successor_export_and_original_monthly_evidence_are_immutable(){
        using var f=new TestWebApplicationFactory(new SupportQualityFixture.Clock());var s=await SupportQualityFixture.Seed(f);using var h=s.Client(f);var saved=await Save(h,s);
        Assert.Equal(21,saved.Preview.Result.BusinessDays);Assert.Equal(12.5m,saved.Preview.Result.RequiredHours);Assert.Equal(67.2m,saved.Preview.Result.AvailableHours);Assert.Equal(134,saved.Preview.Result.CaseCapacity);
        var snapshot=await MonthlyReviewSnapshotIntegrationTests.Save(h,s.Company.Company,"customers");Assert.NotNull(snapshot.Workspace.SupportQuality);
        await f.SeedAsync(async db=>{var c=await db.SupportCases.IgnoreQueryFilters().SingleAsync(x=>x.Id==s.First);c.SetCategory(SupportCaseCategories.TechnicalIssue);});
        var original=await Read<SupportCapacityProposal>(await h.GetAsync(s.Root+"/proposals/"+saved.Summary.Id));Assert.Equal(saved.Checksum,original.Checksum);Assert.Equal(3,original.Preview.Result.ObservedArrivals);
        var changed=s.Input with{Assumptions=s.Input.Assumptions with{HandlingMinutes=60}};var later=await Save(h,s,changed,saved.Summary.Id);Assert.Equal(25,later.Preview.Result.RequiredHours);
        var export=await Read<SupportQualityExport>(await h.GetAsync(s.Root+"/proposals/"+saved.Summary.Id+"/export"));Assert.Equal(saved.Checksum,export.Fingerprint);Assert.Contains("\"12.5\"",export.Content);
        var reopened=await Read<VirtualCompany.Application.Cockpit.MonthlyReviewSnapshotDto>(await h.PostAsync(MonthlyReviewSnapshotIntegrationTests.Root(s.Company.Company)+$"/{snapshot.Summary.Id}/open",null));Assert.Equal(snapshot.Workspace.SupportQuality!.Fingerprint,reopened.Workspace.SupportQuality!.Fingerprint);
    }
    [Fact]public async Task Invalid_calculation_boundaries_stale_sources_and_changed_requests_fail_explicitly(){
        using var f=new TestWebApplicationFactory(new SupportQualityFixture.Clock());var s=await SupportQualityFixture.Seed(f);using var h=s.Client(f);
        foreach(var a in new[]{s.Input.Assumptions with{HandlingMinutes=0},s.Input.Assumptions with{AvailablePeople=-1},s.Input.Assumptions with{HoursPerBusinessDay=10},s.Input.Assumptions with{UtilizationPercent=100.01m},s.Input.Assumptions with{HandlingMinutes=30.001m},s.Input.Assumptions with{TargetMonth=9},s.Input.Assumptions with{ExpectedArrivals=100001},s.Input.Assumptions with{ResponseTargetMinutes=0}})Assert.Equal(HttpStatusCode.BadRequest,(await h.PostAsJsonAsync(s.Root+"/preview",s.Input with{Assumptions=a})).StatusCode);
        var p=await Read<SupportCapacityPreview>(await h.PostAsJsonAsync(s.Root+"/preview",s.Input));var c=new SaveSupportCapacityProposal(Guid.NewGuid(),"Repeatable proposal",s.Input,p.Fingerprint);
        var one=await Read<SupportCapacityProposal>(await h.PostAsJsonAsync(s.Root+"/proposals",c));var two=await Read<SupportCapacityProposal>(await h.PostAsJsonAsync(s.Root+"/proposals",c));Assert.Equal(one.Summary.Id,two.Summary.Id);
        Assert.Equal(HttpStatusCode.Conflict,(await h.PostAsJsonAsync(s.Root+"/proposals",c with{Name="Other name"})).StatusCode);
        await f.SeedAsync(async db=>{var native=await db.SupportCases.IgnoreQueryFilters().SingleAsync(x=>x.Id==s.Waiting);native.SetCategory(SupportCaseCategories.Refund);});
        Assert.Equal(HttpStatusCode.Conflict,(await h.PostAsJsonAsync(s.Root+"/proposals",c with{RequestId=Guid.NewGuid()})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await h.GetAsync(s.Root+"?year=2026&month=13")).StatusCode);
    }
    [Fact]public async Task Restricted_tenant_revoked_nested_snapshot_and_corrupt_retention_are_withheld(){
        using var f=new TestWebApplicationFactory(new SupportQualityFixture.Clock());var s=await SupportQualityFixture.Seed(f);using var h=s.Client(f);using var restricted=s.Client(f,true);var saved=await Save(h,s);var review=await MonthlyReviewSnapshotIntegrationTests.Save(h,s.Company.Company,"company");
        Assert.Equal(HttpStatusCode.Forbidden,(await restricted.GetAsync(s.Url)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await restricted.GetAsync(s.Root+"/proposals")).StatusCode);
        using var foreign=s.Client(f);foreign.DefaultRequestHeaders.Remove("X-Company-Id");foreign.DefaultRequestHeaders.Add("X-Company-Id",s.Company.Foreign.ToString());Assert.NotEqual(HttpStatusCode.OK,(await foreign.GetAsync(s.Root+"/proposals/"+saved.Summary.Id)).StatusCode);
        await f.SeedAsync(async db=>{var row=await db.SupportCapacityProposalRevisions.IgnoreQueryFilters().SingleAsync(x=>x.Id==saved.Summary.Id);db.Entry(row).Property(x=>x.Name).CurrentValue="tampered";await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE support_capacity_proposal_revisions SET Payload = '{{}}' WHERE Id = {saved.Summary.Id}");});
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await h.GetAsync(s.Root+"/proposals/"+saved.Summary.Id)).StatusCode);
        await f.SeedAsync(async db=>{db.CompanyResponsibilityAssignments.RemoveRange(await db.CompanyResponsibilityAssignments.IgnoreQueryFilters().Where(x=>x.CompanyId==s.Company.Company&&x.ResponsibilityArea==ResponsibilityArea.CustomerSupport).ToListAsync());});
        Assert.Equal(HttpStatusCode.Forbidden,(await h.GetAsync(s.Root+"/proposals/"+saved.Summary.Id+"/export")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await h.PostAsync(MonthlyReviewSnapshotIntegrationTests.Root(s.Company.Company)+$"/{review.Summary.Id}/open",null)).StatusCode);
    }
    [Fact]public async Task Native_status_mutations_capture_state_evidence_without_inventing_legacy_history(){
        using var f=new TestWebApplicationFactory(new SupportQualityFixture.Clock());var s=await SupportQualityFixture.Seed(f);using var h=s.Client(f);
        await f.SeedAsync(async db=>{var legacy=await db.SupportCases.IgnoreQueryFilters().SingleAsync(x=>x.Id==s.Legacy);legacy.SetStatus(SupportCaseStatuses.Escalated);});
        await f.SeedAsync(async db=>{var events=await db.SupportCaseEvents.IgnoreQueryFilters().Where(x=>x.SupportCaseId==s.Legacy&&x.EventType==SupportCaseEventTypes.StateRecorded).OrderBy(x=>x.OccurredUtc).ToListAsync();Assert.All(events,x=>Assert.Equal(SupportQualityFixture.Now,x.OccurredUtc));Assert.Contains(events,x=>x.FromStatus==SupportCaseStatuses.WaitingInternal&&x.ToStatus==SupportCaseStatuses.Escalated);});
        var r=await Read<SupportQualityReport>(await h.GetAsync(s.Url));Assert.Null(r.Backlog.Last().Open);Assert.Equal(1,r.Backlog.Last().UnknownCases);
    }
}
