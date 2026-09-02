using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Tests;

[Trait("Category", "SqlServer")]
public sealed class FinanceAutonomyRunSqlServerTests
{
    [ApiSqlServerFact]
    public async Task Rowversion_allows_one_worker_claim_and_transaction_rollback_leaves_no_partial_run()
    {
        var baseConnection = Environment.GetEnvironmentVariable(ApiSqlServerFactAttribute.ConnectionVariable)!;
        var connection = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"virtualcompany_finance_autonomy_run_{Guid.NewGuid():N}",
            MultipleActiveResultSets = false
        };
        var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlServer(connection.ConnectionString, sql => sql.MigrationsAssembly(
                typeof(VirtualCompany.Persistence.Migrations.Persistence.MigrationAssemblyMarker).Assembly.GetName().Name)).Options;
        var companyId = Guid.NewGuid(); var agentId = Guid.NewGuid(); var grantId = Guid.NewGuid();
        var grantVersionId = Guid.NewGuid(); var runId = Guid.NewGuid(); var stepId = Guid.NewGuid();
        var triggerCursorId = Guid.NewGuid(); var budgetPolicyId = Guid.NewGuid(); var budgetWindowId = Guid.NewGuid();
        var now = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc); var hash = new string('a', 64);
        try
        {
            await using (var setup = new VirtualCompanyDbContext(options, new UnscopedCompanyContextAccessor()))
            {
                await setup.Database.MigrateAsync();
                var company = new Company(companyId, "Finance autonomy SQL test");
                var agent = new Agent(agentId, companyId, "laura-finance", "Laura", "Finance Manager", "Finance",
                    null, AgentSeniority.Senior, AgentStatus.Active, AgentAutonomyLevel.Guided);
                var grant = new FinanceAutonomyGrant(grantId, companyId, agentId, "daily_cash", now);
                var versionNumber = grant.ReserveNextVersion(now);
                var version = new FinanceAutonomyGrantVersion(grantVersionId, companyId, grantId, versionNumber,
                    FinanceAutonomyLevel.ReadMonitor, ["manual_review"], ["read"], ["get_cash_balance"],
                    10, null, 2, null, "UTC", "00:00", "23:59", 60, "no_confirmation", "owner",
                    now.AddMinutes(-1), now.AddDays(1), "catalogue-v1", hash, "authority-v1", hash,
                    Guid.NewGuid(), now, false);
                version.Activate(Guid.NewGuid(), "SQL test", now); grant.Activate(version.Id, grant.Version, now);
                var run = BuildRun(runId, companyId, agentId, grantId, grantVersionId, hash, now, "logical-1");
                var step = BuildStep(stepId, companyId, runId, hash, now);
                var triggerCursor = new FinanceAutonomyTriggerCursor(triggerCursorId, companyId, grantId,
                    grantVersionId, agentId, "daily_cash", "schedule", "schedule", now);
                var budgetPolicy = new FinanceAutonomyBudgetPolicy(budgetPolicyId, companyId, null, null,
                    "UTC", 1440, new(null, null, null, 100m, null, null, 10, 10, 100m, 10, 3600),
                    new(null, null, null, 100m, null, null, 10, 10, 100m, 10, 3600),
                    3, 3, 2, 5, 3, 2, 60, 60, now);
                var budgetWindow = new FinanceAutonomyBudgetWindow(budgetWindowId, companyId, budgetPolicyId,
                    now.Date, now.Date.AddDays(1), now);
                setup.AddRange(company, agent, grant, version, run, step, triggerCursor, budgetPolicy, budgetWindow);
                await setup.SaveChangesAsync();
            }

            await using var workerA = new VirtualCompanyDbContext(options, new UnscopedCompanyContextAccessor());
            await using var workerB = new VirtualCompanyDbContext(options, new UnscopedCompanyContextAccessor());
            var claimedA = await workerA.FinanceAutonomyRunSteps.IgnoreQueryFilters().SingleAsync(x => x.Id == stepId);
            var claimedB = await workerB.FinanceAutonomyRunSteps.IgnoreQueryFilters().SingleAsync(x => x.Id == stepId);
            Assert.True(claimedA.TryClaim("worker-a", "lease-a", now, TimeSpan.FromMinutes(1)));
            Assert.True(claimedB.TryClaim("worker-b", "lease-b", now, TimeSpan.FromMinutes(1)));
            await workerA.SaveChangesAsync();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => workerB.SaveChangesAsync());

            await using var triggerHostA = new VirtualCompanyDbContext(options, new UnscopedCompanyContextAccessor());
            await using var triggerHostB = new VirtualCompanyDbContext(options, new UnscopedCompanyContextAccessor());
            var cursorA = await triggerHostA.FinanceAutonomyTriggerCursors.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == triggerCursorId);
            var cursorB = await triggerHostB.FinanceAutonomyTriggerCursors.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == triggerCursorId);
            Assert.True(cursorA.TryClaim("trigger-host-a", "trigger-lease-a", now, TimeSpan.FromMinutes(2)));
            Assert.True(cursorB.TryClaim("trigger-host-b", "trigger-lease-b", now, TimeSpan.FromMinutes(2)));
            await triggerHostA.SaveChangesAsync();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => triggerHostB.SaveChangesAsync());

            await using var budgetWorkerA = new VirtualCompanyDbContext(options, new UnscopedCompanyContextAccessor());
            await using var budgetWorkerB = new VirtualCompanyDbContext(options, new UnscopedCompanyContextAccessor());
            var budgetA = await budgetWorkerA.FinanceAutonomyBudgetWindows.IgnoreQueryFilters().SingleAsync(x => x.Id == budgetWindowId);
            var budgetB = await budgetWorkerB.FinanceAutonomyBudgetWindows.IgnoreQueryFilters().SingleAsync(x => x.Id == budgetWindowId);
            var usage = new FinanceAutonomyUsageValues(0, 0, 0, 60m, 0, 0, 0, 1, 0, 0, 30);
            budgetA.Reserve(usage, now);
            budgetB.Reserve(usage, now);
            await budgetWorkerA.SaveChangesAsync();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => budgetWorkerB.SaveChangesAsync());
            await using (var budgetVerify = new VirtualCompanyDbContext(options, new UnscopedCompanyContextAccessor()))
                Assert.Equal(60m, (await budgetVerify.FinanceAutonomyBudgetWindows.IgnoreQueryFilters()
                    .SingleAsync(x => x.Id == budgetWindowId)).Reserved.AmountExposure);

            var rollbackRunId = Guid.NewGuid();
            await using (var rollback = new VirtualCompanyDbContext(options, new UnscopedCompanyContextAccessor()))
            await using (var transaction = await rollback.Database.BeginTransactionAsync())
            {
                rollback.FinanceAutonomyRuns.Add(BuildRun(rollbackRunId, companyId, agentId, grantId,
                    grantVersionId, hash, now, "logical-rollback"));
                await rollback.SaveChangesAsync();
                await transaction.RollbackAsync();
            }
            await using var verify = new VirtualCompanyDbContext(options, new UnscopedCompanyContextAccessor());
            Assert.False(await verify.FinanceAutonomyRuns.IgnoreQueryFilters().AnyAsync(x => x.Id == rollbackRunId));
        }
        finally
        {
            await using var cleanup = new VirtualCompanyDbContext(options, new UnscopedCompanyContextAccessor());
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static FinanceAutonomyRun BuildRun(Guid id, Guid companyId, Guid agentId, Guid grantId,
        Guid versionId, string hash, DateTime now, string logicalSeed) =>
        new(id, companyId, agentId, "daily_cash", grantId, versionId, 1, "manual_review", logicalSeed,
            now, now.AddHours(1), null, null, Hash(logicalSeed), logicalSeed, logicalSeed,
            "{}", hash, now, "{}", hash, "plan-v1", "{}", hash, "finance-autonomy-policy-v1",
            "catalogue-v1", "authority-v1", hash, null, null, null, null, null, null, now);

    private static FinanceAutonomyRunStep BuildStep(Guid id, Guid companyId, Guid runId, string hash, DateTime now)
    {
        var step = new FinanceAutonomyRunStep(id, companyId, runId, 1, "inspect", "read",
            "get_cash_balance", [], 3, "finance-autonomy-policy-v1", "authority-v1", hash,
            hash, hash, "Inspect", true, null, now);
        step.Queue(now);
        return step;
    }

    private static string Hash(string value) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class UnscopedCompanyContextAccessor : ICompanyContextAccessor
    {
        public Guid? CompanyId => null;
        public Guid? UserId => null;
        public bool IsResolved => false;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? companyId) { }
        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext) { }
    }
}
