using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Finance.Tests;

[Trait("Category", "SqlServer")]
public sealed class FinanceInsightMigrationCompatibilityTests
{
    private static readonly DateTime LegacySeedAnchorUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const string MockFinanceSchemaMigration = "20260422090000_AddApprovalTaskInboxIndexes";
    private const string FinanceInsightSchemaMigration = "20260422110000_AddFinanceAgentInsights";
    private const string PartiallySeededCompanyMigration = "20260422120000_AddBudgetAndForecastPlanning";

    [SqlServerFact]
    public async Task Clean_database_migrates_to_latest_schema_without_pending_migrations()
    {
        await using var database = CreateDatabase();
        var connection = database.Connection;
        await MigrateAsync(connection);

        await using var dbContext = CreateContext(connection);
        var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();

        Assert.Empty(pendingMigrations);
        Assert.True(await TableExistsAsync(connection, "finance_accounts"));
        Assert.True(await TableExistsAsync(connection, "budgets"));
        Assert.True(await TableExistsAsync(connection, "forecasts"));
        Assert.True(await TableExistsAsync(connection, "financial_statement_snapshots"));
        Assert.True(await TableExistsAsync(connection, "financial_statement_snapshot_lines"));
        Assert.True(await IndexExistsAsync(connection, "IX_budgets_company_id_period_start_at_finance_account_id_version_null_cost_center"));
        Assert.True(await IndexExistsAsync(connection, "IX_forecasts_company_id_period_start_at_finance_account_id_version_null_cost_center"));
    }

    [SqlServerFact]
    public async Task Clean_database_migration_supports_finance_insight_aggregation()
    {
        var companyId = Guid.NewGuid();
        await using var database = CreateDatabase();
        var connection = database.Connection;
        await MigrateAsync(connection);
        await SeedMockFinanceCompanyAsync(connection, companyId, "Insight Company");

        await using var dbContext = CreateContext(connection);
        var service = new CompanyFinanceReadService(dbContext, new TestCompanyContextAccessor(companyId));
        var result = await service.GetInsightsAsync(new GetFinanceInsightsQuery(companyId), CancellationToken.None);

        Assert.Equal(companyId, result.CompanyId);
        Assert.True(result.GeneratedAt > DateTime.MinValue);
        Assert.False(result.FromSnapshot);
        Assert.NotNull(result.Items);
        Assert.All(result.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.CheckCode)));
        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
    }

    [SqlServerFact]
    public async Task Mock_finance_schema_migrates_to_latest_and_bootstrap_rerun_stays_idempotent()
    {
        var companyId = Guid.NewGuid();
        await using var database = CreateDatabase();
        var connection = database.Connection;
        await MigrateAsync(connection, MockFinanceSchemaMigration);
        await SeedMockFinanceCompanyAsync(connection, companyId, "Existing Mock Finance Company");

        Assert.False(await TableExistsAsync(connection, "budgets"));
        Assert.False(await TableExistsAsync(connection, "forecasts"));

        await MigrateAsync(connection);

        await using var dbContext = CreateContext(connection);
        var service = CreateBootstrapRerunService(dbContext, companyId);
        var first = await service.RerunAsync(new RerunFinanceBootstrapCommand(companyId, BatchSize: 250, CorrelationId: "finance-bootstrap-rerun-001"), CancellationToken.None);
        var second = await service.RerunAsync(new RerunFinanceBootstrapCommand(companyId, BatchSize: 250, CorrelationId: "finance-bootstrap-rerun-001"), CancellationToken.None);

        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
        Assert.True(first.PlanningRowsInserted > 0);
        Assert.Equal(0, second.PlanningRowsInserted);
        Assert.Equal(0, second.ApprovalBackfill.CreatedCount);
        Assert.True(await dbContext.Budgets.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId));
        Assert.True(await dbContext.Forecasts.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId));

        var duplicateApprovalTargets = await dbContext.ApprovalTasks
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .GroupBy(x => new { x.TargetType, x.TargetId })
            .CountAsync(x => x.Count() > 1);
        Assert.Equal(0, duplicateApprovalTargets);
    }

    [SqlServerFact]
    public async Task Financial_reset_removes_finance_approval_inbox_requests()
    {
        var companyId = Guid.NewGuid();
        await using var database = CreateDatabase();
        var connection = database.Connection;
        await MigrateAsync(connection);
        await SeedMockFinanceCompanyAsync(connection, companyId, "Approval Reset Company");

        var actorId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();

        await using (var seedContext = CreateContext(connection))
        {
            seedContext.WorkTasks.Add(
                new WorkTask(
                    taskId,
                    companyId,
                    "approval_review",
                    "Bill requires approval",
                    "Review generated bill SIM-BILL-20440323-HUMAN.",
                    WorkTaskPriority.High,
                    null,
                    null,
                    "system",
                    actorId,
                    inputPayload: new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["billNumber"] = JsonValue.Create("SIM-BILL-20440323-HUMAN"),
                        ["amount"] = JsonValue.Create(6900m),
                        ["currency"] = JsonValue.Create("USD")
                    },
                    correlationId: "finance-sim:bill:SIM-BILL-20440323-HUMAN",
                    sourceType: "system",
                    triggerEventId: "finance.bill.approval",
                    status: WorkTaskStatus.AwaitingApproval));

            seedContext.ApprovalRequests.Add(
                ApprovalRequest.CreateForTarget(
                    approvalId,
                    companyId,
                    ApprovalTargetEntityType.Task,
                    taskId,
                    "system",
                    actorId,
                    "threshold",
                    new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["billNumber"] = JsonValue.Create("SIM-BILL-20440323-HUMAN"),
                        ["amount"] = JsonValue.Create(6900m),
                        ["currency"] = JsonValue.Create("USD")
                    },
                    "owner",
                    null,
                    []));

            await seedContext.SaveChangesAsync();
        }

        await using var dbContext = CreateContext(connection);
        var service = new CompanyFinanceMaintenanceService(
            dbContext,
            null,
            NullLogger<CompanyFinanceMaintenanceService>.Instance);

        var result = await service.ResetFinancialDataAsync(companyId, CancellationToken.None);

        Assert.True(result.DeletedCounts.GetValueOrDefault("approval_requests") > 0);
        Assert.False(await dbContext.ApprovalRequests.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Id == approvalId));
        Assert.False(await dbContext.WorkTasks.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Id == taskId));
    }

    [SqlServerFact]
    public async Task Finance_insights_tolerate_missing_planning_tables_on_pre_planning_schema()
    {
        var companyId = Guid.NewGuid();
        await using var database = CreateDatabase();
        var connection = database.Connection;
        await MigrateAsync(connection, FinanceInsightSchemaMigration);
        await SeedMockFinanceCompanyAsync(connection, companyId, "Insight Compatibility Company");

        Assert.False(await TableExistsAsync(connection, "budgets"));
        Assert.False(await TableExistsAsync(connection, "forecasts"));

        await using var dbContext = CreateContext(connection);
        var service = new CompanyFinanceReadService(dbContext, new TestCompanyContextAccessor(companyId));

        var result = await service.GetInsightsAsync(new GetFinanceInsightsQuery(companyId), CancellationToken.None);

        Assert.Equal(companyId, result.CompanyId);
        Assert.NotEmpty(result.Items);
        Assert.Contains(result.Items, item => item.CheckCode == FinancialCheckDefinitions.BudgetGap.Code);
        Assert.Contains(result.Items, item => item.CheckCode == FinancialCheckDefinitions.ForecastGap.Code);
    }

    [SqlServerFact]
    public async Task Partially_seeded_company_migrates_to_latest_and_rerun_repairs_missing_planning_without_duplicate_tasks()
    {
        var companyId = Guid.NewGuid();
        await using var database = CreateDatabase();
        var connection = database.Connection;
        await MigrateAsync(connection, PartiallySeededCompanyMigration);
        await SeedMockFinanceCompanyAsync(connection, companyId, "Partial Finance Company", FinanceSeedingState.Seeding);
        await SeedPartialPlanningBaselineAsync(connection, companyId);

        await MigrateAsync(connection);

        await using var dbContext = CreateContext(connection);
        var initialBudgetCount = await dbContext.Budgets.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId);
        var initialForecastCount = await dbContext.Forecasts.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId);

        var service = CreateBootstrapRerunService(dbContext, companyId);
        var first = await service.RerunAsync(new RerunFinanceBootstrapCommand(companyId, BatchSize: 250, CorrelationId: "finance-bootstrap-rerun-partial"), CancellationToken.None);
        var second = await service.RerunAsync(new RerunFinanceBootstrapCommand(companyId, BatchSize: 250, CorrelationId: "finance-bootstrap-rerun-partial"), CancellationToken.None);

        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
        Assert.True(first.PlanningRowsInserted > 0);
        Assert.Equal(0, second.PlanningRowsInserted);
        Assert.Equal(0, second.ApprovalBackfill.CreatedCount);
        Assert.True(await dbContext.Budgets.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId) > initialBudgetCount);
        Assert.True(await dbContext.Forecasts.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId) > initialForecastCount);

        var duplicateBudgetRows = await dbContext.Budgets
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .GroupBy(x => new { x.FinanceAccountId, x.PeriodStartUtc, x.Version, x.CostCenterId })
            .CountAsync(x => x.Count() > 1);
        Assert.Equal(0, duplicateBudgetRows);

        var duplicateForecastRows = await dbContext.Forecasts
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .GroupBy(x => new { x.FinanceAccountId, x.PeriodStartUtc, x.Version, x.CostCenterId })
            .CountAsync(x => x.Count() > 1);
        Assert.Equal(0, duplicateForecastRows);

        var duplicateTargets = await dbContext.ApprovalTasks
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .GroupBy(x => new { x.TargetType, x.TargetId })
            .CountAsync(x => x.Count() > 1);

        Assert.Equal(0, duplicateTargets);
    }

    private static async Task SeedMockFinanceCompanyAsync(
        SqlConnection connection,
        Guid companyId,
        string companyName,
        FinanceSeedingState seedState = FinanceSeedingState.Seeded)
    {
        await using var dbContext = CreateContext(connection);

        var company = new Company(companyId, companyName);
        company.SetFinanceSeedStatus(
            seedState,
            LegacySeedAnchorUtc,
            seedState == FinanceSeedingState.Seeded ? LegacySeedAnchorUtc : null);

        var owner = new User(
            Guid.NewGuid(),
            $"{companyId:N}@example.com",
            "Owner",
            "dev-header",
            $"{companyId:N}-owner");

        dbContext.Companies.Add(company);
        dbContext.Users.Add(owner);
        dbContext.CompanyMemberships.Add(
            new CompanyMembership(
                Guid.NewGuid(),
                companyId,
                owner.Id,
                CompanyMembershipRole.Owner,
                CompanyMembershipStatus.Active));

        await dbContext.SaveChangesAsync();
        await SeedLegacyFinanceAccountsAsync(connection, companyId);
    }

    private static async Task SeedLegacyFinanceAccountsAsync(SqlConnection connection, Guid companyId)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var accounts = new[]
        {
            (Guid.NewGuid(), "1000", "Operating Cash", "asset", 25_000m),
            (Guid.NewGuid(), "1100", "Receivables", "asset", 12_000m),
            (Guid.NewGuid(), "2000", "Payables", "liability", -8_000m)
        };
        foreach (var account in accounts)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO finance_accounts
                    (id, company_id, code, name, account_type, currency, opening_balance, opened_at, created_at, updated_at)
                VALUES
                    (@id, @company_id, @code, @name, @account_type, 'USD', @opening_balance, @at, @at, @at)
                """;
            command.Parameters.AddWithValue("@id", account.Item1);
            command.Parameters.AddWithValue("@company_id", companyId);
            command.Parameters.AddWithValue("@code", account.Item2);
            command.Parameters.AddWithValue("@name", account.Item3);
            command.Parameters.AddWithValue("@account_type", account.Item4);
            command.Parameters.AddWithValue("@opening_balance", account.Item5);
            command.Parameters.AddWithValue("@at", LegacySeedAnchorUtc);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task SeedPartialPlanningBaselineAsync(SqlConnection connection, Guid companyId)
    {
        await using var dbContext = CreateContext(connection);

        var company = await dbContext.Companies.IgnoreQueryFilters().SingleAsync(x => x.Id == companyId);
        var account = await dbContext.FinanceAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Code)
            .Select(x => new { x.Id, x.Currency })
            .FirstAsync();

        var periodStartUtc = new DateTime(company.CreatedUtc.Year, company.CreatedUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        dbContext.Budgets.Add(new Budget(
            Guid.NewGuid(),
            companyId,
            account.Id,
            periodStartUtc,
            FinancePlanningVersions.Baseline,
            0m,
            account.Currency));

        dbContext.Forecasts.Add(new Forecast(
            Guid.NewGuid(),
            companyId,
            account.Id,
            periodStartUtc,
            FinancePlanningVersions.Baseline,
            0m,
            account.Currency));

        await dbContext.SaveChangesAsync();
    }

    private static SqlServerTestDatabase CreateDatabase() =>
        new(Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!);

    private static async Task MigrateAsync(SqlConnection connection, string? targetMigration = null)
    {
        await using var dbContext = CreateContext(connection);

        if (string.IsNullOrWhiteSpace(targetMigration))
        {
            await dbContext.Database.MigrateAsync();
            return;
        }

        var migrator = dbContext.GetService<IMigrator>();
        await migrator.MigrateAsync(targetMigration);
    }

    private static async Task<bool> TableExistsAsync(SqlConnection connection, string tableName)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE name = @tableName;";
        command.Parameters.AddWithValue("@tableName", tableName);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result) > 0;
    }

    private static async Task<bool> IndexExistsAsync(SqlConnection connection, string indexName)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.indexes WHERE name = @indexName;";
        command.Parameters.AddWithValue("@indexName", indexName);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result) > 0;
    }

    private static VirtualCompanyDbContext CreateContext(SqlConnection connection) =>
        new(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlServer(
                connection,
                sqlServer => sqlServer.MigrationsAssembly(typeof(VirtualCompany.Persistence.Migrations.Persistence.Migrations.PersistPreferredCompanySelection).Assembly.GetName().Name))
            .Options);

    private sealed class SqlServerTestDatabase : IAsyncDisposable
    {
        public SqlServerTestDatabase(string baseConnectionString)
        {
            var builder = new SqlConnectionStringBuilder(baseConnectionString)
            {
                InitialCatalog = $"virtualcompany_finance_migration_{Guid.NewGuid():N}",
                MultipleActiveResultSets = false
            };
            Connection = new SqlConnection(builder.ConnectionString);
        }

        public SqlConnection Connection { get; }

        public async ValueTask DisposeAsync()
        {
            await Connection.CloseAsync();
            await using var cleanup = CreateContext(Connection);
            await cleanup.Database.EnsureDeletedAsync();
            await Connection.DisposeAsync();
        }
    }

    private static CompanyFinanceBootstrapRerunService CreateBootstrapRerunService(VirtualCompanyDbContext dbContext, Guid companyId)
    {
        var accessor = new TestCompanyContextAccessor(companyId);
        var approvalTaskService = new CompanyFinanceApprovalTaskService(dbContext, accessor, NullLogger<CompanyFinanceApprovalTaskService>.Instance);
        return new CompanyFinanceBootstrapRerunService(
            dbContext,
            new PlanningBaselineService(dbContext),
            approvalTaskService,
            accessor,
            NullLogger<CompanyFinanceBootstrapRerunService>.Instance);
    }

    private sealed class TestCompanyContextAccessor : ICompanyContextAccessor
    {
        public TestCompanyContextAccessor(Guid companyId)
        {
            CompanyId = companyId;
        }

        public Guid? CompanyId { get; private set; }
        public Guid? UserId => null;
        public bool IsResolved => CompanyId.HasValue;
        public ResolvedCompanyMembershipContext? Membership => null;

        public void SetCompanyId(Guid? companyId) => CompanyId = companyId;

        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext) => CompanyId = companyContext?.CompanyId;
    }
}
