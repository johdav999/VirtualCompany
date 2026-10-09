using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Finance.Tests;

public sealed class FinanceInsightReadBoundaryTests
{
    [Fact]
    public async Task Empty_projection_does_not_compute_seed_or_enqueue_on_read()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.ForbidWrites();
        var result = await fixture.Reader.GetInsightsAsync(new(fixture.Company), default);
        Assert.Empty(result.Items);
        Assert.Equal(0, fixture.Check.Executions);
        Assert.Empty(await fixture.Db.BackgroundExecutions.IgnoreQueryFilters().ToListAsync());
        Assert.False(fixture.Db.ChangeTracker.HasChanges());
    }

    [Theory]
    [InlineData("cached")]
    [InlineData("expired")]
    [InlineData("uncached")]
    [InlineData("filtered")]
    [InlineData("analytics")]
    public async Task Reads_never_reconcile_or_mutate_cache_even_when_checks_have_changed(string mode)
    {
        await using var fixture = await Fixture.CreateAsync();
        var refresh = await fixture.Commands.RefreshInsightsSnapshotAsync(new(fixture.Company), default);
        var before = await fixture.Db.FinanceAgentInsights.IgnoreQueryFilters().AsNoTracking()
            .Select(x => new { x.Id, x.Status, x.UpdatedUtc, x.ResolvedUtc }).ToListAsync();
        var executions = fixture.Check.Executions;
        fixture.Check.Active = false;
        if (mode == "expired") fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddHours(12);
        fixture.Db.ChangeTracker.Clear();
        fixture.ForbidWrites();

        var result = mode == "analytics"
            ? (await fixture.Reader.GetAnalyticsAsync(new(fixture.Company, RefreshInsightsSnapshot: true), default)).OperationalInsights
            : await fixture.Reader.GetInsightsAsync(new(fixture.Company,
                PreferSnapshot: mode != "uncached",
                EntityType: mode == "filtered" ? "counterparty" : null,
                EntityId: mode == "filtered" ? "test-customer" : null), default);

        Assert.Contains(result.Items, x => x.ConditionKey == "boundary-test" && x.Status == "active");
        Assert.Equal(mode == "cached", result.FromSnapshot);
        Assert.Equal(executions, fixture.Check.Executions);
        var after = await fixture.Db.FinanceAgentInsights.IgnoreQueryFilters().AsNoTracking()
            .Select(x => new { x.Id, x.Status, x.UpdatedUtc, x.ResolvedUtc }).ToListAsync();
        Assert.Equal(before.OrderBy(x => x.Id), after.OrderBy(x => x.Id));
        Assert.False(fixture.Db.ChangeTracker.HasChanges());
        Assert.NotNull(refresh.Insights);
    }

    [Fact]
    public async Task Explicit_refresh_updates_and_resolves_existing_identity_and_republishes_cache()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Commands.RefreshInsightsSnapshotAsync(new(fixture.Company), default);
        var original = Assert.Single(first.Insights!.Items, x => x.ConditionKey == "boundary-test");
        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddMinutes(10);
        fixture.Check.Active = false;
        var second = await fixture.Commands.RefreshInsightsSnapshotAsync(new(fixture.Company), default);
        var resolved = Assert.Single(second.Insights!.Items, x => x.ConditionKey == "boundary-test");
        Assert.Equal(original.Id, resolved.Id);
        Assert.Equal("resolved", resolved.Status);
        Assert.Equal(fixture.Clock.UtcNow.UtcDateTime, resolved.ResolvedAt);
        fixture.ForbidWrites();
        var read = await fixture.Reader.GetInsightsAsync(new(fixture.Company), default);
        Assert.True(read.FromSnapshot);
        Assert.Contains(read.Items, x => x.Id == original.Id && x.Status == "resolved");
        var active = await fixture.Reader.GetInsightsAsync(new(fixture.Company, IncludeResolved: false), default);
        Assert.DoesNotContain(active.Items, x => x.Id == original.Id);
    }

    [Fact]
    public async Task Reads_and_both_commands_reject_a_different_company_before_any_write()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.ForbidWrites();
        var other = Guid.NewGuid();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Reader.GetInsightsAsync(new(other), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Commands.RefreshInsightsSnapshotAsync(new(other), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Commands.QueueInsightsSnapshotRefreshAsync(new(other), default));
        Assert.Equal(0, fixture.Check.Executions);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private readonly WriteGuard writes = new();
        public Guid Company { get; } = Guid.NewGuid();
        public VirtualCompanyDbContext Db { get; private set; } = null!;
        public CompanyFinanceReadService Reader { get; private set; } = null!;
        public FinanceInsightRefreshService Commands { get; private set; } = null!;
        public ToggleCheck Check { get; } = new();
        public Clock Clock { get; } = new();
        public Cache Cache { get; } = new();

        public static async Task<Fixture> CreateAsync()
        {
            var f = new Fixture();
            await f.connection.OpenAsync();
            f.Db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseSqlite(f.connection).AddInterceptors(f.writes).Options);
            await f.Db.Database.EnsureCreatedAsync();
            f.Db.Companies.Add(new Company(f.Company, "Insight boundary test"));
            await f.Db.SaveChangesAsync();
            var persistence = new FinanceInsightPersistenceService(new FinanceAgentInsightRepository(f.Db), f.Clock);
            f.Reader = new CompanyFinanceReadService(f.Db, new CompanyContext(f.Company), null, null,
                financialChecks: [f.Check], financeInsightPersistenceService: persistence,
                insightSnapshotCache: f.Cache, timeProvider: f.Clock);
            f.Commands = new FinanceInsightRefreshService(f.Db, f.Reader, persistence, f.Cache, f.Clock);
            return f;
        }

        public void ForbidWrites() { writes.Forbidden = true; Cache.Forbidden = true; }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }

    private sealed class WriteGuard : SaveChangesInterceptor
    {
        public bool Forbidden { get; set; }
        public override InterceptionResult<int> SavingChanges(DbContextEventData data, InterceptionResult<int> result)
        { Assert.False(Forbidden, "A read attempted SaveChanges."); return result; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Assert.False(Forbidden, "A read attempted SaveChangesAsync."); return ValueTask.FromResult(result); }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 9, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class ToggleCheck : IFinancialCheck
    {
        public bool Active { get; set; } = true;
        public int Executions { get; private set; }
        public FinancialCheckDefinition Definition => FinancialCheckDefinitions.OverdueReceivables;
        public string CheckCode => Definition.Code;
        public Task<IReadOnlyList<FinancialCheckResult>> ExecuteAsync(FinancialCheckContext context, CancellationToken ct)
        {
            Executions++;
            var entity = new FinanceInsightEntityReferenceDto("counterparty", "test-customer", "Test customer", true);
            return Task.FromResult<IReadOnlyList<FinancialCheckResult>>(Active
                ? [new(Definition, "boundary-test", entity.EntityType, entity.EntityId, FinancialCheckSeverity.High,
                    "Overdue invoice", "Review invoice", 1m, entity, [entity])]
                : []);
        }
    }

    private sealed class CompanyContext(Guid company) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = company;
        public Guid? UserId => null;
        public bool IsResolved => CompanyId.HasValue;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? id) => CompanyId = id;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? context) => CompanyId = context?.CompanyId;
    }

    // Retain expired bytes deliberately so the reader, rather than this cache, handles expiration.
    private sealed class Cache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> values = new();
        public bool Forbidden { get; set; }
        public byte[]? Get(string key) => values.GetValueOrDefault(key);
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        { Assert.False(Forbidden, "A read attempted cache publication."); values[key] = value; }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        { Set(key, value, options); return Task.CompletedTask; }
        public void Remove(string key) { Assert.False(Forbidden, "A read attempted cache removal."); values.Remove(key); }
        public Task RemoveAsync(string key, CancellationToken token = default) { Remove(key); return Task.CompletedTask; }
        public void Refresh(string key) => Assert.False(Forbidden, "A read attempted cache refresh.");
        public Task RefreshAsync(string key, CancellationToken token = default) { Refresh(key); return Task.CompletedTask; }
    }
}
