using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

[Trait("Category", "SqlServer")]
public sealed class AdvancedReconciliationSqlServerIntegrationTests
{
    [SqlServerFact]
    public async Task Sql_server_rejects_two_decisions_from_the_same_group_version()
    {
        var builder = new SqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!)
        {
            InitialCatalog = $"virtualcompany_advanced_reconciliation_{Guid.NewGuid():N}",
            MultipleActiveResultSets = false
        };
        var connectionString = builder.ConnectionString;
        await using (var setup = CreateContext(connectionString)) await setup.Database.MigrateAsync();

        try
        {
            var groupId = await SeedGroupAsync(connectionString);
            var now = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
            await using var acceptContext = CreateContext(connectionString);
            await using var rejectContext = CreateContext(connectionString);
            var accepted = await acceptContext.AdvancedReconciliationGroups.IgnoreQueryFilters().SingleAsync(x => x.Id == groupId);
            var rejected = await rejectContext.AdvancedReconciliationGroups.IgnoreQueryFilters().SingleAsync(x => x.Id == groupId);
            accepted.Accept(accepted.Version, Guid.NewGuid(), "Approved", now);
            rejected.Reject(rejected.Version, Guid.NewGuid(), "Rejected", now);

            await acceptContext.SaveChangesAsync();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => rejectContext.SaveChangesAsync());

            await using var verification = CreateContext(connectionString);
            var persisted = await verification.AdvancedReconciliationGroups.IgnoreQueryFilters().SingleAsync(x => x.Id == groupId);
            Assert.Equal("accepted", persisted.Status);
            Assert.Equal(2, persisted.Version);
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<Guid> SeedGroupAsync(string connectionString)
    {
        var companyId = Guid.NewGuid(); var userId = Guid.NewGuid(); var ruleId = Guid.NewGuid(); var groupId = Guid.NewGuid();
        var now = new DateTime(2026, 8, 28, 11, 0, 0, DateTimeKind.Utc);
        await using var db = CreateContext(connectionString);
        db.AddRange(
            new Company(companyId, "Advanced reconciliation SQL company"),
            new AdvancedReconciliationRule(ruleId, companyId, 1, "Rule v1", @"[\s\-_/]+", @"[\s\-_/.,]+", ".*",
                .01m, 10, .30m, .80m, 5000m, userId, now),
            new AdvancedReconciliationGroup(groupId, companyId, ruleId, 1, null, "SQL-CONCURRENCY", "SQL counterparty",
                "SEK", 100m, .90m, true, userId, now));
        await db.SaveChangesAsync();
        return groupId;
    }

    private static VirtualCompanyDbContext CreateContext(string connectionString) => new(
        new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(
                typeof(VirtualCompany.Persistence.Migrations.Persistence.MigrationAssemblyMarker)
                    .Assembly.GetName().Name))
            .Options);
}
