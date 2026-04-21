using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceApprovalTaskBackfillServiceTests
{
    [Fact]
    public async Task Backfill_creates_tasks_for_matching_mock_bills_and_is_idempotent()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Companies.Add(new Company(companyId, "Approval backfill company"));
        dbContext.Companies.Add(new Company(otherCompanyId, "Other approval backfill company"));
        var approverUser = new User(Guid.NewGuid(), "approver@example.com", "Finance Approver", "dev-header", "approval-backfill");
        dbContext.Users.Add(approverUser);
        dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, approverUser.Id, CompanyMembershipRole.FinanceApprover, CompanyMembershipStatus.Active));
        dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), otherCompanyId, approverUser.Id, CompanyMembershipRole.FinanceApprover, CompanyMembershipStatus.Active));
        FinanceSeedData.AddMockFinanceData(dbContext, companyId);
        FinanceSeedData.AddMockFinanceData(dbContext, otherCompanyId);
        var policy = dbContext.FinancePolicyConfigurations.Local.Single(x => x.CompanyId == companyId);
        policy.Update(
            policy.ApprovalCurrency,
            policy.InvoiceApprovalThreshold,
            1000m,
            policy.RequireCounterpartyForTransactions,
            policy.AnomalyDetectionLowerBound,
            policy.AnomalyDetectionUpperBound,
            policy.CashRunwayWarningThresholdDays,
            policy.CashRunwayCriticalThresholdDays);
        await dbContext.SaveChangesAsync();

        var accessor = new TestCompanyContextAccessor(companyId);
        var logger = new ScopeCapturingLogger<CompanyFinanceApprovalTaskService>();
        var service = new CompanyFinanceApprovalTaskService(dbContext, accessor, logger);

        var first = await service.BackfillApprovalTasksAsync(
            new BackfillFinanceApprovalTasksCommand(companyId, 50, "approval-backfill-001"),
            CancellationToken.None);
        var second = await service.BackfillApprovalTasksAsync(
            new BackfillFinanceApprovalTasksCommand(companyId, 50, "approval-backfill-001"),
            CancellationToken.None);

        Assert.Equal(15, first.ScannedCount);
        Assert.Equal(10, first.MatchedCount);
        Assert.Equal(10, first.CreatedCount);
        Assert.Equal(0, first.SkippedExistingCount);
        Assert.Equal(5, first.BillScannedCount);
        Assert.Equal(10, first.PaymentScannedCount);
        Assert.Equal(3, first.BillCreatedCount);
        Assert.Equal(7, first.PaymentCreatedCount);
        Assert.Equal(0, second.CreatedCount);
        Assert.Equal(10, second.SkippedExistingCount);

        var companyTasks = await dbContext.ApprovalTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .ToListAsync();
        var otherCompanyTasks = await dbContext.ApprovalTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == otherCompanyId)
            .ToListAsync();
        Assert.Equal(10, companyTasks.Count);
        Assert.Equal(3, companyTasks.Count(x => x.TargetType == ApprovalTargetType.Bill));
        Assert.Equal(7, companyTasks.Count(x => x.TargetType == ApprovalTargetType.Payment));
        Assert.All(companyTasks, task => Assert.Equal(ApprovalTaskStatus.Pending, task.Status));
        Assert.Empty(otherCompanyTasks);

        var logEntry = Assert.Single(logger.Entries.Where(x => x.Message.Contains("Completed finance approval task backfill", StringComparison.Ordinal)));
        Assert.Equal("approval-backfill-001", AssertScopeValue(logEntry.Scope, "CorrelationId"));
        Assert.Equal(companyId, AssertScopeValue(logEntry.Scope, "CompanyId"));
    }

    [Fact]
    public async Task Backfill_skips_non_qualifying_records_and_existing_bills_remain_readable_without_tasks()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Companies.Add(new Company(companyId, "Approval compatibility company"));
        var approverUser = new User(Guid.NewGuid(), "compat@example.com", "Compatibility Approver", "dev-header", "approval-compatibility");
        dbContext.Users.Add(approverUser);
        dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, approverUser.Id, CompanyMembershipRole.FinanceApprover, CompanyMembershipStatus.Active));
        FinanceSeedData.AddMockFinanceData(dbContext, companyId);
        var policy = dbContext.FinancePolicyConfigurations.Local.Single(x => x.CompanyId == companyId);
        policy.Update(
            policy.ApprovalCurrency,
            policy.InvoiceApprovalThreshold,
            10000m,
            policy.RequireCounterpartyForTransactions,
            policy.AnomalyDetectionLowerBound,
            policy.AnomalyDetectionUpperBound,
            policy.CashRunwayWarningThresholdDays,
            policy.CashRunwayCriticalThresholdDays);
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceApprovalTaskService(dbContext, new TestCompanyContextAccessor(companyId), new ScopeCapturingLogger<CompanyFinanceApprovalTaskService>());
        var result = await service.BackfillApprovalTasksAsync(
            new BackfillFinanceApprovalTasksCommand(companyId, 50, "approval-backfill-compat"),
            CancellationToken.None);

        Assert.Equal(15, result.ScannedCount);
        Assert.Equal(0, result.MatchedCount);
        Assert.Equal(0, result.CreatedCount);
        Assert.Empty(await dbContext.ApprovalTasks.IgnoreQueryFilters().AsNoTracking().ToListAsync());

        var bills = await dbContext.FinanceBills
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.BillNumber)
            .ToListAsync();
        Assert.Equal(5, bills.Count);
        Assert.Contains(bills, bill => string.Equals(bill.Status, "open", StringComparison.OrdinalIgnoreCase));
    }

    private static object? AssertScopeValue(IReadOnlyDictionary<string, object?> scope, string key)
    {
        Assert.True(scope.TryGetValue(key, out var value));
        return value;
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static VirtualCompanyDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite(connection)
            .Options);

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

        public void SetCompanyId(Guid? companyId)
        {
            CompanyId = companyId;
        }

        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext)
        {
            CompanyId = companyContext?.CompanyId;
        }
    }
}
