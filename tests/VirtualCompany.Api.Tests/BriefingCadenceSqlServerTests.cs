using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Api.Tests;
[Trait("Category", "SqlServer")]
public sealed class BriefingCadenceSqlServerTests
{
    [ApiSqlServerFact] public async Task Additive_migration_roundtrip_preserves_prior_preferences_and_native_work()
    {
        using var f = TestWebApplicationFactory.CreateSqlServer(new BriefingCadenceFixture.Clock()); var s = await BriefingCadenceFixture.Seed(f); using var h = s.Client(f); await s.Save(h);
        await f.SeedAsync(async db => { var history = (await db.Database.GetAppliedMigrationsAsync()).ToArray(); var index = Array.FindIndex(history, x => x.EndsWith("AddBriefingCadenceDelivery")); Assert.True(index > 0);
            var taskCount = await db.WorkTasks.IgnoreQueryFilters().CountAsync(); await db.GetService<IMigrator>().MigrateAsync(history[index - 1]); await db.GetService<IMigrator>().MigrateAsync(); db.ChangeTracker.Clear();
            Assert.Equal(taskCount, await db.WorkTasks.IgnoreQueryFilters().CountAsync()); Assert.Single(await db.CompanyBriefingDeliveryPreferences.IgnoreQueryFilters().ToArrayAsync()); });
        await s.Save(h); Assert.Equal(1, await s.Schedule(f, new(2026,10,6,7,0,0,DateTimeKind.Utc))); await s.RunJobs(f); await s.Dispatch(f);
        await f.SeedAsync(async db => Assert.Equal("sent", (await db.BriefingCadenceDeliveries.IgnoreQueryFilters().SingleAsync()).Status));
    }
    [ApiSqlServerFact] public async Task Concurrent_native_schedulers_and_workers_deliver_one_local_slot()
    {
        using var f = TestWebApplicationFactory.CreateSqlServer(new BriefingCadenceFixture.Clock { Now = new(2026,10,5,7,0,0,DateTimeKind.Utc) }); var s = await BriefingCadenceFixture.Seed(f); using var h = s.Client(f); await s.Save(h);
        await Task.WhenAll(s.Schedule(f, new(2026,10,5,7,0,0,DateTimeKind.Utc)), s.Schedule(f, new(2026,10,5,7,0,0,DateTimeKind.Utc)));
        await Task.WhenAll(s.RunJobs(f), s.RunJobs(f)); await Task.WhenAll(s.Dispatch(f), s.Dispatch(f));
        await f.SeedAsync(async db => { Assert.Single(await db.BriefingCadenceDeliveries.IgnoreQueryFilters().ToArrayAsync()); Assert.Single(await db.CompanyNotifications.IgnoreQueryFilters().Where(x => x.RelatedEntityType == "briefing_cadence_delivery").ToArrayAsync()); });
    }
}
