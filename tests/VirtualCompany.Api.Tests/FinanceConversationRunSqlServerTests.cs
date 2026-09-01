using Microsoft.Data.SqlClient;

namespace VirtualCompany.Api.Tests;

[Trait("Category", "SqlServer")]
public sealed class FinanceConversationRunSqlServerTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

    [ApiSqlServerFact]
    public async Task Sql_server_enforces_single_worker_concurrency_idempotency_and_transaction_rollback()
    {
        var baseConnection = Environment.GetEnvironmentVariable(ApiSqlServerFactAttribute.ConnectionVariable)!;
        var builder = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"virtualcompany_finance_runs_{Guid.NewGuid():N}",
            MultipleActiveResultSets = false
        };
        var connectionString = builder.ConnectionString;
        var companyId = Guid.NewGuid();
        var runId = Guid.NewGuid();

        await using (var setup = CreateContext(connectionString, null))
        {
            await setup.Database.MigrateAsync();
            setup.Companies.Add(new Company(companyId, "Durable Finance run SQL test"));
            setup.FinanceConversationRuns.Add(CreateRun(runId, companyId, "sql-idempotency"));
            await setup.SaveChangesAsync();
        }

        try
        {
            await using var first = CreateContext(connectionString, companyId);
            await using var duplicateWorker = CreateContext(connectionString, companyId);
            var firstClaim = await first.FinanceConversationRuns.SingleAsync(x => x.Id == runId);
            var staleClaim = await duplicateWorker.FinanceConversationRuns.SingleAsync(x => x.Id == runId);
            Assert.True(firstClaim.TryClaim("sql-worker-a", Now, TimeSpan.FromMinutes(1)));
            Assert.True(staleClaim.TryClaim("sql-worker-b", Now, TimeSpan.FromMinutes(1)));
            await first.SaveChangesAsync();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => duplicateWorker.SaveChangesAsync());

            await using (var duplicate = CreateContext(connectionString, companyId))
            {
                duplicate.FinanceConversationRuns.Add(CreateRun(Guid.NewGuid(), companyId, "sql-idempotency"));
                await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
            }

            await using (var rollback = CreateContext(connectionString, companyId))
            await using (var transaction = await rollback.Database.BeginTransactionAsync())
            {
                rollback.FinanceConversationRuns.Add(CreateRun(Guid.NewGuid(), companyId, "rolled-back"));
                await rollback.SaveChangesAsync();
                await transaction.RollbackAsync();
            }

            await using var verify = CreateContext(connectionString, companyId);
            Assert.Equal(1, await verify.FinanceConversationRuns.CountAsync());
            Assert.DoesNotContain(await verify.FinanceConversationRuns.ToArrayAsync(),
                run => run.IdempotencyKey == "rolled-back");
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString, null);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static VirtualCompanyDbContext CreateContext(string connectionString, Guid? companyId) => new(
        new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(
                typeof(VirtualCompany.Persistence.Migrations.Persistence.MigrationAssemblyMarker)
                    .Assembly.GetName().Name)).Options,
        new SqlCompanyContextAccessor(companyId));

    private static FinanceConversationRun CreateRun(Guid runId, Guid companyId, string idempotencyKey) => new(
        runId, companyId, Guid.NewGuid(), Guid.NewGuid(), idempotencyKey, new string('a', 64),
        $"sql-run-{runId:N}", "authority-v1", new string('b', 64), "planning-v1", new string('c', 64),
        Now, Now.AddDays(90));

    private sealed class SqlCompanyContextAccessor(Guid? companyId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId;
        public Guid? UserId => null;
        public bool IsResolved => CompanyId.HasValue;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? value) => CompanyId = value;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext) => CompanyId = companyContext?.CompanyId;
    }
}
