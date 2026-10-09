using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Api.Tests;

[Trait("Category", "SqlServer")]
public sealed class CollaborationEvidenceSqlServerTests
{
    [ApiSqlServerFact]
    public async Task Migration_upgrade_versions_uniqueness_and_downgrade_preserve_existing_work()
    {
        using var factory = TestWebApplicationFactory.CreateSqlServer(TimeProvider.System);
        var company = Guid.NewGuid(); var owner = Guid.NewGuid();
        await CollaborationEvidenceIntegrationTests.SeedOwner(factory,company,owner);
        var fixture = await CollaborationEvidenceFixture.SeedAsync(factory,company,owner);
        await factory.SeedAsync(async db =>
        {
            var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
            var contributionMigration = Array.FindIndex(migrations, x => x.EndsWith("AddCollaborationContributionEvidence", StringComparison.Ordinal));
            Assert.True(contributionMigration > 0);
            Assert.Equal(6,await db.CollaborationContributions.IgnoreQueryFilters().CountAsync(x => x.CompanyId == company));
            var original = await db.CollaborationContributions.IgnoreQueryFilters().FirstAsync(x => x.CompanyId == company);
            var duplicate = new CollaborationContribution(original.CompanyId,original.ParentTaskId,original.SourceTaskId,original.PlanId,
                original.AgentId,original.Sequence,original.Version,original.Role,original.Pattern,original.Objective,original.Status,original.Output,original.Rationale);
            db.Add(duplicate); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.Entry(duplicate).State = EntityState.Detached;
            await db.GetService<IMigrator>().MigrateAsync(migrations[contributionMigration - 1]);
            Assert.True(await db.WorkTasks.IgnoreQueryFilters().AnyAsync(x => x.Id == fixture.RootId));
            var legacyParentId=Guid.NewGuid();var legacyWorkerId=Guid.NewGuid();
            var legacyInput=new Dictionary<string,System.Text.Json.Nodes.JsonNode?> {
                ["planId"]=System.Text.Json.Nodes.JsonValue.Create(original.PlanId.ToString("N")),
                ["sourceTaskId"]=System.Text.Json.Nodes.JsonValue.Create(fixture.RootId.ToString("D")) };
            var legacyOutput=new Dictionary<string,System.Text.Json.Nodes.JsonNode?> {
                ["contributions"]=System.Text.Json.JsonSerializer.SerializeToNode(new[] {new {AgentId=original.AgentId,SubtaskId=legacyWorkerId,
                    Sequence=1,Status="completed",Output="Legacy retained output",RationaleSummary="Retained business conclusion"}}) };
            // Insert the legacy wire shape against the downgraded schema, which intentionally lacks later phase columns.
            var inputJson = System.Text.Json.JsonSerializer.Serialize(legacyInput);
            var outputJson = System.Text.Json.JsonSerializer.Serialize(legacyOutput);
            var now = DateTime.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO tasks (id,company_id,type,title,priority,status,created_by_actor_type,created_by_actor_id,assigned_agent_id,input_payload,output_payload,created_at,updated_at,source_type) VALUES ({legacyParentId},{company},{"manager_worker_collaboration"},{"Legacy collaboration"},{"normal"},{"planned"},{"user"},{owner},{original.AgentId},{inputJson},{outputJson},{now},{now},{"user"})");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO tasks (id,company_id,type,title,priority,status,created_by_actor_type,created_by_actor_id,assigned_agent_id,parent_task_id,input_payload,output_payload,created_at,updated_at,source_type) VALUES ({legacyWorkerId},{company},{"manager_worker_subtask"},{"Legacy worker"},{"normal"},{"planned"},{"user"},{owner},{original.AgentId},{legacyParentId},{"{}"},{"{}"},{now},{now},{"user"})");
            await db.GetService<IMigrator>().MigrateAsync();
            Assert.True(await db.WorkTasks.IgnoreQueryFilters().AnyAsync(x => x.Id == fixture.RootId));
            var imported=Assert.Single(await db.CollaborationContributions.IgnoreQueryFilters().AsNoTracking().ToListAsync());
            Assert.Equal("Legacy retained output",imported.Output);Assert.Equal(legacyWorkerId,imported.SourceTaskId);Assert.Equal(1,imported.Version);
            Assert.Contains("earlier version history",imported.Rationale);
            db.ChangeTracker.Clear();
            Assert.Equal(fixture.RootId,await db.WorkTasks.IgnoreQueryFilters().Where(x=>x.Id==legacyParentId).Select(x=>x.ParentTaskId).SingleAsync());
        });
    }
}
