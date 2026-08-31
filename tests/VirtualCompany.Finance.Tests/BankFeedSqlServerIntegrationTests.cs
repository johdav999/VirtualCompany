using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Finance.Tests;

[Trait("Category", "SqlServer")]
public sealed class BankFeedSqlServerIntegrationTests
{
    [SqlServerFact]
    public async Task Sql_server_enforces_checkpoint_concurrency_and_expired_lease_takeover()
    {
        var builder = new SqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!)
        {
            InitialCatalog = $"virtualcompany_bank_feed_{Guid.NewGuid():N}",
            MultipleActiveResultSets = false
        };
        var connectionString = builder.ConnectionString;
        await using (var setup = CreateContext(connectionString)) await setup.Database.MigrateAsync();

        try
        {
            var checkpointId = await SeedCheckpointAsync(connectionString);
            var now = new DateTime(2026, 8, 28, 10, 0, 0, DateTimeKind.Utc);
            await using var left = CreateContext(connectionString);
            await using var right = CreateContext(connectionString);
            var leftRow = await left.BankFeedCheckpoints.IgnoreQueryFilters().SingleAsync(x => x.Id == checkpointId);
            var rightRow = await right.BankFeedCheckpoints.IgnoreQueryFilters().SingleAsync(x => x.Id == checkpointId);
            leftRow.Queue(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 28), null, null, "left", now);
            rightRow.Queue(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 28), null, null, "right", now);
            await left.SaveChangesAsync();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => right.SaveChangesAsync());

            await using (var firstClaim = CreateContext(connectionString))
            {
                var row = await firstClaim.BankFeedCheckpoints.IgnoreQueryFilters().SingleAsync(x => x.Id == checkpointId);
                Assert.True(row.TryClaim("worker-1", now, TimeSpan.FromSeconds(30)));
                await firstClaim.SaveChangesAsync();
            }
            await using (var competingClaim = CreateContext(connectionString))
            {
                var row = await competingClaim.BankFeedCheckpoints.IgnoreQueryFilters().SingleAsync(x => x.Id == checkpointId);
                Assert.False(row.TryClaim("worker-2", now.AddSeconds(29), TimeSpan.FromSeconds(30)));
            }
            await using (var recoveredClaim = CreateContext(connectionString))
            {
                var row = await recoveredClaim.BankFeedCheckpoints.IgnoreQueryFilters().SingleAsync(x => x.Id == checkpointId);
                Assert.True(row.TryClaim("worker-2", now.AddSeconds(31), TimeSpan.FromSeconds(30)));
                await recoveredClaim.SaveChangesAsync();
            }
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<Guid> SeedCheckpointAsync(string connectionString)
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var financeAccountId = Guid.NewGuid();
        var bankAccountId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var discoveredId = Guid.NewGuid();
        var mappingId = Guid.NewGuid();
        var checkpointId = Guid.NewGuid();
        var now = new DateTime(2026, 8, 28, 9, 0, 0, DateTimeKind.Utc);
        await using var db = CreateContext(connectionString);
        var connection = new BankConnection(connectionId, companyId, "test-feed", "SE|Test Bank",
            "Test Bank", userId, now);
        connection.Activate(now.AddDays(90), BankConnectionHealthStatuses.Healthy, now);
        db.AddRange(
            new Company(companyId, "SQL feed company"),
            new User(userId, "sql-feed@example.test", "SQL Feed Operator", "test", $"sql-{userId:N}"),
            new FinanceAccount(financeAccountId, companyId, "1930", "Operating bank", "asset", "SEK", 0, now),
            new CompanyBankAccount(bankAccountId, companyId, financeAccountId, "Operating", "Test Bank", "•••• 1111", "SEK"),
            connection,
            new BankDiscoveredAccount(discoveredId, companyId, connectionId, "stable-account", "Operating",
                "•••• 1111", "SEK", BankAccountOwnershipStatuses.Verified, "Verified", now, "access-account"),
            new BankAccountMapping(mappingId, companyId, discoveredId, bankAccountId, 1, userId, "Explicit mapping", now),
            new BankFeedCheckpoint(checkpointId, companyId, connectionId, discoveredId, mappingId, 1,
                bankAccountId, "test-feed", "stable-account", "access-account", now));
        await db.SaveChangesAsync();
        return checkpointId;
    }

    private static VirtualCompanyDbContext CreateContext(string connectionString) => new(
        new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(
                typeof(VirtualCompany.Persistence.Migrations.Persistence.MigrationAssemblyMarker)
                    .Assembly.GetName().Name))
            .Options);
}
