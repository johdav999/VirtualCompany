using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Infrastructure.Companies;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class ExecutiveCockpitDashboardCacheTests
{
    [Fact]
    public async Task Cache_returns_miss_then_hit_for_the_same_tenant()
    {
        var distributedCache = new RecordingDistributedCache();
        var cache = CreateCache(distributedCache);
        var companyId = Guid.NewGuid();
        var snapshot = CreateSnapshot(companyId, "Tenant A");

        Assert.Null(await cache.TryGetAsync(companyId, CancellationToken.None));

        await cache.SetAsync(snapshot, CancellationToken.None);
        var cached = await cache.TryGetAsync(companyId, CancellationToken.None);

        Assert.NotNull(cached);
        Assert.Equal(companyId, cached!.CompanyId);
        Assert.Equal("Tenant A", cached.Dashboard.CompanyName);
        Assert.Single(distributedCache.SetKeys);
    }

    [Fact]
    public async Task Cache_keys_and_payload_validation_prevent_cross_tenant_reuse()
    {
        var distributedCache = new RecordingDistributedCache();
        var cache = CreateCache(distributedCache);
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        await cache.SetAsync(CreateSnapshot(companyA, "Tenant A"), CancellationToken.None);

        Assert.Null(await cache.TryGetAsync(companyB, CancellationToken.None));
        Assert.Contains(companyA.ToString("N"), ExecutiveCockpitDashboardCache.BuildCacheKey(companyA));
        Assert.Contains(companyB.ToString("N"), ExecutiveCockpitDashboardCache.BuildCacheKey(companyB));
        Assert.NotEqual(
            ExecutiveCockpitDashboardCache.BuildCacheKey(companyA),
            ExecutiveCockpitDashboardCache.BuildCacheKey(companyB));
    }

    [Fact]
    public async Task Cache_read_failures_fail_open_as_misses()
    {
        var distributedCache = new RecordingDistributedCache
        {
            ThrowOnGet = true
        };
        var cache = CreateCache(distributedCache);

        var result = await cache.TryGetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Cache_write_and_invalidation_failures_do_not_throw()
    {
        var distributedCache = new RecordingDistributedCache
        {
            ThrowOnSet = true,
            ThrowOnRemove = true
        };
        var cache = CreateCache(distributedCache);
        var companyId = Guid.NewGuid();

        await cache.SetAsync(CreateSnapshot(companyId, "Tenant A"), CancellationToken.None);
        await cache.InvalidateAsync(companyId, CancellationToken.None);
    }

    [Fact]
    public async Task Cache_invalidation_removes_only_the_target_tenant()
    {
        var distributedCache = new RecordingDistributedCache();
        var cache = CreateCache(distributedCache);
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        await cache.SetAsync(CreateSnapshot(companyA, "Tenant A"), CancellationToken.None);
        await cache.SetAsync(CreateSnapshot(companyB, "Tenant B"), CancellationToken.None);

        await cache.InvalidateAsync(companyA, CancellationToken.None);

        Assert.Null(await cache.TryGetAsync(companyA, CancellationToken.None));
        Assert.NotNull(await cache.TryGetAsync(companyB, CancellationToken.None));
    }

    private static ExecutiveCockpitDashboardCache CreateCache(RecordingDistributedCache distributedCache) =>
        new(
            distributedCache,
            Options.Create(new ExecutiveCockpitDashboardCacheOptions
            {
                Enabled = true,
                KeyPrefix = "vc:executive-cockpit",
                KeyVersion = "tests-v1",
                TtlSeconds = 60
            }),
            NullLogger<ExecutiveCockpitDashboardCache>.Instance);

    private static CachedExecutiveCockpitDashboardDto CreateSnapshot(Guid companyId, string companyName)
    {
        var now = DateTime.UtcNow;
        var dashboard = new ExecutiveCockpitDashboardDto(
            companyId,
            companyName,
            now,
            null,
            [],
            null,
            new ExecutiveCockpitPendingApprovalsDto(0, [], $"/approvals?companyId={companyId}&status=pending"),
            [],
            [],
            [],
            new ExecutiveCockpitSetupStateDto(false, false, false, 0, 0, 0, true),
            new ExecutiveCockpitEmptyStateFlagsDto(true, true, true, true, true, true));

        return new CachedExecutiveCockpitDashboardDto(companyId, now, dashboard);
    }

    private sealed class RecordingDistributedCache : IDistributedCache
    {
        private readonly ConcurrentDictionary<string, byte[]> _entries = new(StringComparer.Ordinal);
        private readonly ConcurrentQueue<string> _setKeys = new();

        public bool ThrowOnGet { get; init; }
        public bool ThrowOnSet { get; init; }
        public bool ThrowOnRemove { get; init; }
        public IReadOnlyList<string> SetKeys => _setKeys.ToArray();

        public byte[]? Get(string key)
        {
            if (ThrowOnGet)
            {
                throw new InvalidOperationException("Configured cache get failure.");
            }

            return _entries.TryGetValue(key, out var value) ? value.ToArray() : null;
        }

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Get(key));
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            if (ThrowOnSet)
            {
                throw new InvalidOperationException("Configured cache set failure.");
            }

            _entries[key] = value.ToArray();
            _setKeys.Enqueue(key);
        }

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public void Remove(string key)
        {
            if (ThrowOnRemove)
            {
                throw new InvalidOperationException("Configured cache remove failure.");
            }

            _entries.TryRemove(key, out _);
        }

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            Remove(key);
            return Task.CompletedTask;
        }
    }
}