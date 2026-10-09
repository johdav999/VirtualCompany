using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Api.Tests;
[Trait("Category", "SqlServer")]
public sealed class DecisionWorkSqlServerTests
{
    [ApiSqlServerFact] public async Task Migration_roundtrip_preserves_native_review_and_plan_versions()
    {
        using var f = TestWebApplicationFactory.CreateSqlServer(new SupportQualityFixture.Clock()); var s = await DecisionWorkFixture.Seed(f);
        await f.SeedAsync(async db => { var source = await db.Set<StrategicScenarioVersion>().IgnoreQueryFilters().AsNoTracking().Where(x => x.Id == s.ScenarioId).Select(x => new { x.Id, x.Checksum, x.SourceJson }).SingleAsync();
            var history = (await db.Database.GetAppliedMigrationsAsync()).ToArray(); var index = Array.FindIndex(history, x => x.EndsWith("AddDecisionWorkOrigins")); Assert.True(index > 0);
            await db.GetService<IMigrator>().MigrateAsync(history[index - 1]); await db.GetService<IMigrator>().MigrateAsync(); db.ChangeTracker.Clear();
            Assert.Equal(source, await db.Set<StrategicScenarioVersion>().IgnoreQueryFilters().AsNoTracking().Where(x => x.Id == s.ScenarioId).Select(x => new { x.Id, x.Checksum, x.SourceJson }).SingleAsync()); });
        using var h = s.Planning.Annual.Client(f); Assert.Equal(s.ScenarioId, (await DecisionWorkFixture.Create(h, s)).Created.Source.Reference.VersionId);
    }
    [ApiSqlServerFact] public async Task Concurrent_origin_confirmation_has_one_task_and_rejects_cross_company_source_foreign_key()
    {
        using var f = TestWebApplicationFactory.CreateSqlServer(new SupportQualityFixture.Clock()); var s = await DecisionWorkFixture.Seed(f); using var a = s.Planning.Annual.Client(f); using var b = s.Planning.Annual.Client(f);
        var input = s.Input(); var p = await AnnualPlanningFixture.Read<DecisionWorkPreview>(await a.PostAsync(s.Root + "/preview", JsonContent.Create(input))); var cmd = new ConfirmDecisionWork(Guid.NewGuid(), input, p.Fingerprint);
        var results = await Task.WhenAll(a.PostAsync(s.Root, JsonContent.Create(cmd)), b.PostAsync(s.Root, JsonContent.Create(cmd with { RequestId = Guid.NewGuid() })));
        Assert.All(results, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode));
        var ids = new List<Guid>(); foreach (var response in results) ids.Add((await AnnualPlanningFixture.Read<DecisionWorkDocument>(response)).TaskId); Assert.Single(ids.Distinct());
        var reviews = await Task.WhenAll(a.PostAsync(s.Root + $"/tasks/{ids[0]}/review", null), b.PostAsync(s.Root + $"/tasks/{ids[0]}/review", null));
        var approvals = new List<Guid?>(); foreach (var response in reviews) approvals.Add((await AnnualPlanningFixture.Read<DecisionWorkDocument>(response)).ApprovalId); Assert.Single(approvals.Distinct()); Assert.NotNull(approvals[0]);
        await f.SeedAsync(async db => { Assert.Equal(1, await db.Set<DecisionWorkOrigin>().IgnoreQueryFilters().CountAsync(x => x.CompanyId == s.Company));
            var task = await db.WorkTasks.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == ids[0]); Assert.Equal("Review service capacity", task.Title);
            await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE decision_work_origins SET ScenarioId = {Guid.NewGuid()}, SourceVersionId = {Guid.NewGuid()} WHERE TaskId = {ids[0]}"));
            // The source exists, but the owning company differs. Retain a valid typed-source shape.
            var foreignCompany = s.Planning.Annual.Quarter.Company.Foreign;
            var foreignTaskId = Guid.NewGuid(); db.WorkTasks.Add(new(foreignTaskId, foreignCompany, "follow_up", "Foreign task", null,
                VirtualCompany.Domain.Enums.WorkTaskPriority.Normal, null, null, "user", input.OwnerUserId)); await db.SaveChangesAsync();
            var origin = await db.Set<DecisionWorkOrigin>().IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.TaskId == ids[0]);
            db.Add(new DecisionWorkOrigin { Id = Guid.NewGuid(), CompanyId = foreignCompany, TaskId = foreignTaskId, RequestId = Guid.NewGuid(),
                CreatedByUserId = origin.CreatedByUserId, OwnerUserId = origin.OwnerUserId, SourceKind = origin.SourceKind, SourceVersionId = origin.SourceVersionId,
                ItemKey = origin.ItemKey, SourceVersion = origin.SourceVersion, SourceFingerprint = origin.SourceFingerprint, ScenarioId = origin.ScenarioId,
                Objective = origin.Objective, AcceptanceOutcome = origin.AcceptanceOutcome, ProposedConstraints = origin.ProposedConstraints,
                DueUtc = origin.DueUtc, CreatedUtc = origin.CreatedUtc, CommandHash = origin.CommandHash, PreviewJson = origin.PreviewJson, PreviewChecksum = origin.PreviewChecksum });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Contains("FK_decision_work_origins_strategic_scenario_versions", error.InnerException!.Message);
            db.ChangeTracker.Clear(); Assert.Equal(1, await db.ApprovalRequests.IgnoreQueryFilters().CountAsync(x => x.CompanyId == s.Company && x.TargetEntityId == ids[0])); });
    }
}
