using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Application.Support;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;
namespace VirtualCompany.Api.Tests;

[Trait("Category","SqlServer")]
public sealed class SupportQualitySqlServerTests
{
    [ApiSqlServerFact]public async Task Migration_preserves_native_events_cases_close_flags_and_prior_review_payload(){
        using var f=TestWebApplicationFactory.CreateSqlServer(new SupportQualityFixture.Clock());var s=await SupportQualityFixture.Seed(f);using var h=s.Client(f);var review=await MonthlyReviewSnapshotIntegrationTests.Save(h,s.Company.Company,"customers");
        await f.SeedAsync(async db=>{var events=await db.SupportCaseEvents.IgnoreQueryFilters().AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.EventType,x.Summary,x.OccurredUtc}).ToListAsync();var closed=await db.SupportCases.IgnoreQueryFilters().Where(x=>x.Id==s.Alias).Select(x=>new{x.Status,x.ClosedUtc}).SingleAsync();
            var payload=await db.MonthlyReviewSnapshots.IgnoreQueryFilters().Where(x=>x.Id==review.Summary.Id).Select(x=>x.PayloadJson).SingleAsync();var migrations=(await db.Database.GetAppliedMigrationsAsync()).ToArray();var index=Array.FindIndex(migrations,x=>x.EndsWith("AddSupportQualityPlanning"));Assert.True(index>0);
            await db.GetService<IMigrator>().MigrateAsync(migrations[index-1]);await db.GetService<IMigrator>().MigrateAsync();db.ChangeTracker.Clear();
            Assert.Equal(events,await db.SupportCaseEvents.IgnoreQueryFilters().AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.EventType,x.Summary,x.OccurredUtc}).ToListAsync());Assert.Equal(closed,await db.SupportCases.IgnoreQueryFilters().Where(x=>x.Id==s.Alias).Select(x=>new{x.Status,x.ClosedUtc}).SingleAsync());Assert.Equal(payload,await db.MonthlyReviewSnapshots.IgnoreQueryFilters().Where(x=>x.Id==review.Summary.Id).Select(x=>x.PayloadJson).SingleAsync());Assert.All(await db.SupportCaseEvents.IgnoreQueryFilters().ToListAsync(),x=>Assert.Null(x.ToStatus));});
        var saved=await SupportQualityIntegrationTests.Save(h,s);Assert.Equal(12.5m,saved.Preview.Result.RequiredHours);
    }
    [ApiSqlServerFact]public async Task Concurrent_duplicate_and_successor_requests_have_one_proposal_audit_and_native_state_is_unchanged(){
        using var f=TestWebApplicationFactory.CreateSqlServer(new SupportQualityFixture.Clock());var s=await SupportQualityFixture.Seed(f);using var a=s.Client(f);using var b=s.Client(f);var parent=await SupportQualityIntegrationTests.Save(a,s);
        var p=await SupportQualityIntegrationTests.Read<SupportCapacityPreview>(await a.PostAsJsonAsync(s.Root+"/preview",s.Input));var c=new SaveSupportCapacityProposal(Guid.NewGuid(),"SQL successor",s.Input,p.Fingerprint,parent.Summary.Id);
        var responses=await Task.WhenAll(a.PostAsJsonAsync(s.Root+"/proposals",c),b.PostAsJsonAsync(s.Root+"/proposals",c with{RequestId=Guid.NewGuid()}));Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.Conflict);
        c=c with{RequestId=Guid.NewGuid(),PreviousId=null};responses=await Task.WhenAll(a.PostAsJsonAsync(s.Root+"/proposals",c),b.PostAsJsonAsync(s.Root+"/proposals",c));var one=await SupportQualityIntegrationTests.Read<SupportCapacityProposal>(responses[0]);var two=await SupportQualityIntegrationTests.Read<SupportCapacityProposal>(responses[1]);Assert.Equal(one.Summary.Id,two.Summary.Id);
        await f.SeedAsync(async db=>{Assert.Equal(3,await db.SupportCapacityProposalRevisions.IgnoreQueryFilters().CountAsync());Assert.Equal(3,await db.AuditEvents.IgnoreQueryFilters().CountAsync(x=>x.CompanyId==s.Company.Company&&x.Action=="support.capacity_proposal.saved"));Assert.Equal(SupportCaseStatuses.WaitingForCustomer,(await db.SupportCases.IgnoreQueryFilters().SingleAsync(x=>x.Id==s.Waiting)).Status);});
    }
    [ApiSqlServerFact]public async Task Merge_destination_tenancy_and_state_concurrency_are_enforced(){
        using var f=TestWebApplicationFactory.CreateSqlServer(new SupportQualityFixture.Clock());var s=await SupportQualityFixture.Seed(f);
        await f.SeedAsync(async db=>{var foreign=await db.SupportCases.IgnoreQueryFilters().FirstAsync(x=>x.CompanyId==s.Company.Foreign);db.SupportCaseEvents.Add(new SupportCaseEvent(Guid.NewGuid(),s.Company.Company,s.First,SupportCaseEventTypes.Merged,"Invalid cross-company merge.","human",s.Company.Owner,SupportQualityFixture.Now,mergeTargetCaseId:foreign.Id));await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();});
        string? connection=null;await f.SeedAsync(db=>{connection=db.Database.GetConnectionString();return Task.CompletedTask;});
        var options=new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlServer(connection!).Options;
        await using var first=new VirtualCompanyDbContext(options,clock:new SupportQualityFixture.Clock());await using var second=new VirtualCompanyDbContext(options,clock:new SupportQualityFixture.Clock());
        var a=await first.SupportCases.IgnoreQueryFilters().SingleAsync(x=>x.Id==s.Waiting);var b=await second.SupportCases.IgnoreQueryFilters().SingleAsync(x=>x.Id==s.Waiting);a.SetStatus(SupportCaseStatuses.Escalated);b.SetStatus(SupportCaseStatuses.WaitingInternal);await first.SaveChangesAsync();await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>second.SaveChangesAsync());
    }
}
