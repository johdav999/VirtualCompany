using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceInsightsServiceTests
{
    [Fact]
    public async Task Insights_handle_sparse_data_without_throwing_and_emit_normalized_payload()
    {
        var companyId = Guid.NewGuid();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options);
        await dbContext.Database.EnsureCreatedAsync();

        var company = new Company(companyId, "Sparse Finance Company");
        company.SetFinanceSeedStatus(FinanceSeedingState.Seeded, DateTime.UtcNow, DateTime.UtcNow);
        dbContext.Companies.Add(company);

        var accountId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        dbContext.FinanceAccounts.Add(new FinanceAccount(
            accountId,
            companyId,
            "1000",
            "Operating Cash",
            "asset",
            "USD",
            5000m,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        dbContext.FinanceCounterparties.Add(new FinanceCounterparty(
            supplierId,
            companyId,
            "Sparse Supplier",
            "supplier",
            "supplier@example.com"));
        dbContext.FinanceTransactions.Add(new FinanceTransaction(
            Guid.NewGuid(),
            companyId,
            accountId,
            supplierId,
            null,
            null,
            new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            "software",
            -250m,
            "USD",
            "Software renewal",
            "SPARSE-001"));
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceReadService(dbContext);
        var result = await service.GetInsightsAsync(
            new GetFinanceInsightsQuery(companyId, new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc)),
            CancellationToken.None);

        Assert.Equal(companyId, result.CompanyId);
        Assert.True(result.GeneratedAt > DateTime.MinValue);
        Assert.NotNull(result.Items);
        Assert.All(result.Items, item => Assert.InRange(item.Confidence, 0m, 1m));
        Assert.All(result.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.CheckName)));
        Assert.All(result.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.EntityType)));
        Assert.All(result.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.EntityId)));
        Assert.All(result.Items, item => Assert.True(item.ObservedAt > DateTime.MinValue));
        Assert.All(result.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.ConditionKey)));
        Assert.Contains(result.Items, item => item.CheckCode == FinancialCheckDefinitions.SparseDataCoverage.Code);
        Assert.Contains(result.Items, item => item.CheckCode == FinancialCheckDefinitions.ForecastGap.Code);
        Assert.Contains(result.Items, item => item.CheckCode == FinancialCheckDefinitions.BudgetGap.Code);
    }

    [Fact]
    public async Task Insights_remain_company_scoped_when_multiple_tenants_exist()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options);
        await dbContext.Database.EnsureCreatedAsync();

        var companyAEntity = new Company(companyA, "Tenant A");
        companyAEntity.SetFinanceSeedStatus(FinanceSeedingState.Seeded, DateTime.UtcNow, DateTime.UtcNow);
        var companyBEntity = new Company(companyB, "Tenant B");
        companyBEntity.SetFinanceSeedStatus(FinanceSeedingState.Seeded, DateTime.UtcNow, DateTime.UtcNow);
        dbContext.Companies.AddRange(companyAEntity, companyBEntity);
        FinanceSeedData.AddMockFinanceData(dbContext, companyA);
        FinanceSeedData.AddMockFinanceData(dbContext, companyB);
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceReadService(dbContext);
        var result = await service.GetInsightsAsync(new GetFinanceInsightsQuery(companyA), CancellationToken.None);
        Assert.Equal(companyA, result.CompanyId);
    }

    [Fact]
    public async Task Insights_can_be_filtered_to_a_single_entity_without_changing_response_shape()
    {
        var companyId = Guid.NewGuid();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options);
        await dbContext.Database.EnsureCreatedAsync();

        var company = new Company(companyId, "Entity Filter Company");
        company.SetFinanceSeedStatus(FinanceSeedingState.Seeded, DateTime.UtcNow, DateTime.UtcNow);
        dbContext.Companies.Add(company);
        FinanceSeedData.AddMockFinanceData(dbContext, companyId);
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceReadService(dbContext);
        var fullResult = await service.GetInsightsAsync(new GetFinanceInsightsQuery(companyId), CancellationToken.None);
        var sourceInsight = Assert.Single(fullResult.Items.Where(x => x.PrimaryEntity is not null));
        var entityType = sourceInsight.PrimaryEntity!.EntityType;
        var entityId = sourceInsight.PrimaryEntity.EntityId;

        var filtered = await service.GetInsightsAsync(
            new GetFinanceInsightsQuery(companyId, EntityType: entityType, EntityId: entityId, IncludeResolved: false, PreferSnapshot: false),
            CancellationToken.None);

        Assert.NotEmpty(filtered.Items);
        Assert.All(filtered.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.ConditionKey)));
        Assert.All(filtered.Items, item =>
            Assert.Contains(item.AffectedEntities, entity => entity.EntityType == entityType && entity.EntityId == entityId));
    }

    [Fact]
    public async Task Refresh_snapshot_is_idempotent_per_company_and_served_from_cache_until_retention_expires()
    {
        var companyId = Guid.NewGuid();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options);
        await dbContext.Database.EnsureCreatedAsync();

        var company = new Company(companyId, "Cached Insight Company");
        company.SetFinanceSeedStatus(FinanceSeedingState.Seeded, DateTime.UtcNow, DateTime.UtcNow);
        dbContext.Companies.Add(company);
        FinanceSeedData.AddMockFinanceData(dbContext, companyId);
        await dbContext.SaveChangesAsync();

        var cache = CreateDistributedCache();
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 20, 8, 0, 0, TimeSpan.Zero));
        var accessor = new TestCompanyContextAccessor(companyId);
        var service = new CompanyFinanceReadService(dbContext, accessor, null, null, null, cache, timeProvider);

        var refreshed = await service.RefreshInsightsSnapshotAsync(
            new RefreshFinanceInsightsSnapshotCommand(companyId, Retention: TimeSpan.FromMinutes(30)),
            CancellationToken.None);

        Assert.True(refreshed.Refreshed);
        Assert.NotNull(refreshed.Insights);
        Assert.NotNull(refreshed.Insights!.Items);

        var cached = await service.GetInsightsAsync(new GetFinanceInsightsQuery(companyId), CancellationToken.None);
        Assert.True(cached.FromSnapshot);
        Assert.NotNull(cached.SnapshotExpiresAtUtc);

        timeProvider.SetUtcNow(new DateTimeOffset(2026, 4, 20, 9, 0, 0, TimeSpan.Zero));

        var staleRead = await service.GetInsightsAsync(new GetFinanceInsightsQuery(companyId), CancellationToken.None);
        Assert.False(staleRead.FromSnapshot);
        Assert.Null(staleRead.SnapshotExpiresAtUtc);
    }

    [Fact]
    public async Task Queue_snapshot_refresh_prevents_duplicate_background_executions_for_same_scope()
    {
        var companyId = Guid.NewGuid();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options);
        await dbContext.Database.EnsureCreatedAsync();

        var company = new Company(companyId, "Queued Insight Company");
        company.SetFinanceSeedStatus(FinanceSeedingState.Seeded, DateTime.UtcNow, DateTime.UtcNow);
        dbContext.Companies.Add(company);
        FinanceSeedData.AddMockFinanceData(dbContext, companyId);
        await dbContext.SaveChangesAsync();

        var accessor = new TestCompanyContextAccessor(companyId);
        var service = new CompanyFinanceReadService(dbContext, accessor, null, null, null, CreateDistributedCache(), TimeProvider.System);

        await service.QueueInsightsSnapshotRefreshAsync(
            new QueueFinanceInsightsSnapshotRefreshCommand(companyId, CorrelationId: "insights-queue-001"),
            CancellationToken.None);
        await service.QueueInsightsSnapshotRefreshAsync(
            new QueueFinanceInsightsSnapshotRefreshCommand(companyId, CorrelationId: "insights-queue-001"),
            CancellationToken.None);

        var executionCount = await dbContext.BackgroundExecutions
            .IgnoreQueryFilters()
            .CountAsync(x => x.CompanyId == companyId && x.ExecutionType == BackgroundExecutionType.FinanceInsightRefresh);
        Assert.Equal(1, executionCount);
    }

    private static IDistributedCache CreateDistributedCache() =>
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

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

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public MutableTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void SetUtcNow(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }
    }
}
