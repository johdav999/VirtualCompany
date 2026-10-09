using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Api.Tests;
[Trait("Category", "SqlServer")]
public sealed class QuarterlyPlanningSqlServerTests
{
    [ApiSqlServerFact] public async Task Migration_roundtrip_preserves_native_goals_and_private_monthly_payloads()
    {
        using var f = TestWebApplicationFactory.CreateSqlServer(new SupportQualityFixture.Clock()); var s = await QuarterlyPlanningFixture.Seed(f);
        await f.SeedAsync(async db => { var before = await db.MonthlyReviewSnapshots.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.PayloadJson, x.Checksum }).ToListAsync();
            var goal = await db.CompanyGoals.IgnoreQueryFilters().Where(x => x.Id == s.Goal).Select(x => new { x.Name, x.Version, x.OwnerUserId }).SingleAsync();
            var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray(); var i = Array.FindIndex(migrations, x => x.EndsWith("AddQuarterlyPlanning")); Assert.True(i > 0);
            await db.GetService<IMigrator>().MigrateAsync(migrations[i - 1]); await db.GetService<IMigrator>().MigrateAsync(); db.ChangeTracker.Clear();
            Assert.Equal(before, await db.MonthlyReviewSnapshots.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.PayloadJson, x.Checksum }).ToListAsync()); Assert.Equal(goal, await db.CompanyGoals.IgnoreQueryFilters().Where(x => x.Id == s.Goal).Select(x => new { x.Name, x.Version, x.OwnerUserId }).SingleAsync()); });
        using var h = s.Client(f); Assert.Equal(s.Actual, (await QuarterlyPlanningIntegrationTests.Save(h, s)).Review.Objectives.Single().Actual);
    }
    [ApiSqlServerFact] public async Task Concurrent_successors_have_one_winner_and_company_bound_source_foreign_key_is_enforced()
    {
        using var f = TestWebApplicationFactory.CreateSqlServer(new SupportQualityFixture.Clock()); var s = await QuarterlyPlanningFixture.Seed(f); using var a = s.Client(f); using var b = s.Client(f); var first = await QuarterlyPlanningIntegrationTests.Save(a, s);
        var p = await QuarterlyPlanningIntegrationTests.Read<QuarterReviewPreview>(await a.PostAsJsonAsync(s.Root + "/preview", s.Proposal)); var cmd = new SaveQuarterReview(s.Proposal, first.Summary.Id, 1, Guid.NewGuid(), p.Fingerprint);
        var results = await Task.WhenAll(a.PostAsJsonAsync(s.Root + "/reviews", cmd), b.PostAsJsonAsync(s.Root + "/reviews", cmd with { RequestId = Guid.NewGuid() })); Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        await f.SeedAsync(async db => { var objective = await db.Set<QuarterlyObjective>().IgnoreQueryFilters().FirstAsync(x => x.ReviewId == first.Summary.Id);
            db.Add(new QuarterlyMeasureLink { Id = Guid.NewGuid(), CompanyId = s.Company.Foreign, ObjectiveId = objective.Id, SnapshotId = s.Snapshots[0], MeasureKey = "support.volume", SnapshotChecksum = new string('0', 64) });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear(); });
    }
}
