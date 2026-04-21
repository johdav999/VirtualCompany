using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceApprovalTaskBackfillJobRunnerTests
{
    [Fact]
    public async Task RunDueAsync_backfills_seeded_companies_without_creating_duplicates()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        SeedCompany(dbContext, companyId, 1500m, "Runner finance company");
        SeedCompany(dbContext, otherCompanyId, 10000m, "Runner non-qualifying company");
        await dbContext.SaveChangesAsync();

        var accessor = new TestCompanyContextAccessor();
        var service = new CompanyFinanceApprovalTaskService(dbContext, accessor, NullLogger<CompanyFinanceApprovalTaskService>.Instance);
        var runner = new FinanceApprovalTaskBackfillJobRunner(
            dbContext,
            service,
            new TestCompanyExecutionScopeFactory(accessor),
            Options.Create(new FinanceApprovalTaskBackfillWorkerOptions
            {
                BatchSize = 10,
                BackfillBatchSize = 50
            }),
            NullLogger<FinanceApprovalTaskBackfillJobRunner>.Instance);

        var first = await runner.RunDueAsync(CancellationToken.None);
        var second = await runner.RunDueAsync(CancellationToken.None);

        Assert.Equal(2, first);
        Assert.Equal(2, second);

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

        Assert.Equal(8, companyTasks.Count);
        Assert.Equal(2, companyTasks.Count(x => x.TargetType == ApprovalTargetType.Bill));
        Assert.Equal(6, companyTasks.Count(x => x.TargetType == ApprovalTargetType.Payment));
        Assert.Empty(otherCompanyTasks);
    }

    private static void SeedCompany(VirtualCompanyDbContext dbContext, Guid companyId, decimal billApprovalThreshold, string companyName)
    {
        var approverUser = new User(Guid.NewGuid(), $"{companyId:N}@example.com", companyName, "dev-header", $"{companyId:N}-runner");
        dbContext.Companies.Add(new Company(companyId, companyName));
        dbContext.Users.Add(approverUser);
        dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, approverUser.Id, CompanyMembershipRole.FinanceApprover, CompanyMembershipStatus.Active));
        FinanceSeedData.AddMockFinanceData(dbContext, companyId);

        var policy = dbContext.FinancePolicyConfigurations.Local.Single(x => x.CompanyId == companyId);
        policy.Update(
            policy.ApprovalCurrency,
            policy.InvoiceApprovalThreshold,
            billApprovalThreshold,
            policy.RequireCounterpartyForTransactions,
            policy.AnomalyDetectionLowerBound,
            policy.AnomalyDetectionUpperBound,
            policy.CashRunwayWarningThresholdDays,
            policy.CashRunwayCriticalThresholdDays);
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

    private sealed class TestCompanyExecutionScopeFactory : ICompanyExecutionScopeFactory
    {
        private readonly TestCompanyContextAccessor _accessor;

        public TestCompanyExecutionScopeFactory(TestCompanyContextAccessor accessor)
        {
            _accessor = accessor;
        }

        public IDisposable BeginScope(Guid companyId)
        {
            var previousCompanyId = _accessor.CompanyId;
            _accessor.SetCompanyId(companyId);
            return new ResetScope(_accessor, previousCompanyId);
        }

        private sealed class ResetScope : IDisposable
        {
            private readonly TestCompanyContextAccessor _accessor;
            private readonly Guid? _previousCompanyId;
            private bool _disposed;

            public ResetScope(TestCompanyContextAccessor accessor, Guid? previousCompanyId)
            {
                _accessor = accessor;
                _previousCompanyId = previousCompanyId;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _accessor.SetCompanyId(_previousCompanyId);
                _disposed = true;
            }
        }
    }
}
