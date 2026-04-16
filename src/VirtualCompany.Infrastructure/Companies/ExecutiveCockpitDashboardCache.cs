using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Cockpit;
using Microsoft.Extensions.Options;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class ExecutiveCockpitDashboardCache : IExecutiveCockpitDashboardCache, IExecutiveCockpitDashboardCacheInvalidator
{
    private const string DefaultVersionToken = "0";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly ActivitySource ActivitySource = new("VirtualCompany.ExecutiveCockpit.Cache");
    private static readonly Meter Meter = new("VirtualCompany.ExecutiveCockpit");
    private static readonly Counter<long> CacheHits = Meter.CreateCounter<long>("executive_cockpit_cache_hits");
    private static readonly Counter<long> CacheMisses = Meter.CreateCounter<long>("executive_cockpit_cache_misses");
    private static readonly Counter<long> CacheSets = Meter.CreateCounter<long>("executive_cockpit_cache_sets");
    private static readonly Counter<long> CacheErrors = Meter.CreateCounter<long>("executive_cockpit_cache_errors");
    private static readonly Counter<long> CacheInvalidations = Meter.CreateCounter<long>("executive_cockpit_cache_invalidations");
    private static readonly Histogram<double> CacheLookupDuration = Meter.CreateHistogram<double>("executive_cockpit_cache_lookup_duration_ms", "ms");
    private static readonly Histogram<double> CacheInvalidationLag = Meter.CreateHistogram<double>("executive_cockpit_cache_invalidation_lag_ms", "ms");

    private readonly IDistributedCache _distributedCache;
    private readonly ExecutiveCockpitCacheKeyBuilder _keyBuilder;
    private readonly ExecutiveCockpitDashboardCacheOptions _options;
    private readonly ILogger<ExecutiveCockpitDashboardCache> _logger;

    public ExecutiveCockpitDashboardCache(
        IDistributedCache distributedCache,
        ExecutiveCockpitCacheKeyBuilder keyBuilder,
        IOptions<ExecutiveCockpitDashboardCacheOptions> options,
        ILogger<ExecutiveCockpitDashboardCache> logger)
    {
        _distributedCache = distributedCache;
        _keyBuilder = keyBuilder;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CachedExecutiveCockpitDashboardDto?> TryGetAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        await TryGetDashboardAsync(ExecutiveCockpitCacheKeyBuilder.DashboardScope(companyId, "member"), cancellationToken);

    public async Task<CachedExecutiveCockpitDashboardDto?> TryGetDashboardAsync(
        ExecutiveCockpitCacheScope scope,
        CancellationToken cancellationToken)
    {
        return await TryGetCoreAsync<CachedExecutiveCockpitDashboardDto>(scope, "dashboard", ValidateDashboard, cancellationToken);
    }

    public async Task SetAsync(CachedExecutiveCockpitDashboardDto snapshot, CancellationToken cancellationToken) =>
        await SetDashboardAsync(ExecutiveCockpitCacheKeyBuilder.DashboardScope(snapshot.CompanyId, "member"), snapshot, cancellationToken);

    public async Task SetDashboardAsync(
        ExecutiveCockpitCacheScope scope,
        CachedExecutiveCockpitDashboardDto snapshot,
        CancellationToken cancellationToken)
    {
        if (snapshot.CompanyId == Guid.Empty ||
            snapshot.Dashboard.CompanyId != snapshot.CompanyId ||
            snapshot.CompanyId != scope.CompanyId)
        {
            return;
        }

        await SetCoreAsync(scope, snapshot, "dashboard", _options.GetTtl(), cancellationToken);
    }

    public async Task<CachedExecutiveCockpitKpiDashboardDto?> TryGetKpiDashboardAsync(
        ExecutiveCockpitCacheScope scope,
        CancellationToken cancellationToken)
    {
        return await TryGetCoreAsync<CachedExecutiveCockpitKpiDashboardDto>(scope, "kpis", ValidateKpis, cancellationToken);
    }

    public async Task SetKpiDashboardAsync(
        ExecutiveCockpitCacheScope scope,
        CachedExecutiveCockpitKpiDashboardDto snapshot,
        CancellationToken cancellationToken)
    {
        if (snapshot.CompanyId == Guid.Empty ||
            snapshot.Dashboard.CompanyId != snapshot.CompanyId ||
            snapshot.CompanyId != scope.CompanyId)
        {
            return;
        }

        await SetCoreAsync(scope, snapshot, "kpis", _options.GetTtl(), cancellationToken);
    }

    public async Task<CachedExecutiveCockpitWidgetDto<TPayload>?> TryGetWidgetAsync<TPayload>(
        ExecutiveCockpitCacheScope scope,
        CancellationToken cancellationToken)
    {
        return await TryGetCoreAsync<CachedExecutiveCockpitWidgetDto<TPayload>>(scope, "widget", snapshot =>
            snapshot is not null &&
            snapshot.CompanyId == scope.CompanyId &&
            scope.Identity.EndsWith(snapshot.WidgetKey, StringComparison.OrdinalIgnoreCase), cancellationToken);
    }

    public async Task SetWidgetAsync<TPayload>(
        ExecutiveCockpitCacheScope scope,
        CachedExecutiveCockpitWidgetDto<TPayload> snapshot,
        CancellationToken cancellationToken)
    {
        if (snapshot.CompanyId == Guid.Empty ||
            snapshot.CompanyId != scope.CompanyId ||
            string.IsNullOrWhiteSpace(snapshot.WidgetKey))
        {
            return;
        }

        await SetCoreAsync(scope, snapshot, "widget", _options.GetWidgetTtl(), cancellationToken);
    }

    public Task InvalidateAsync(Guid companyId, CancellationToken cancellationToken) =>
        InvalidateAsync(
            new ExecutiveCockpitCacheInvalidationEvent(
                companyId,
                "direct",
                null,
                null,
                DateTime.UtcNow),
            cancellationToken);

    public async Task InvalidateAsync(
        ExecutiveCockpitCacheInvalidationEvent invalidationEvent,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled || invalidationEvent.CompanyId == Guid.Empty)
        {
            return;
        }

        using var activity = ActivitySource.StartActivity("executive_cockpit.cache.invalidate");
        activity?.SetTag("vc.cockpit.cache.trigger", NormalizeTag(invalidationEvent.TriggerType));
        activity?.SetTag("vc.cockpit.cache.entity_type", NormalizeTag(invalidationEvent.EntityType));

        try
        {
            var version = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var completedUtc = DateTime.UtcNow;
            var lag = completedUtc - NormalizeUtc(invalidationEvent.OccurredAtUtc);

            await _distributedCache.SetStringAsync(
                _keyBuilder.BuildVersionKey(invalidationEvent.CompanyId),
                version,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = _options.GetVersionTokenTtl()
                },
                cancellationToken);
            CacheInvalidations.Add(
                1,
                new KeyValuePair<string, object?>("trigger", NormalizeTag(invalidationEvent.TriggerType)),
                new KeyValuePair<string, object?>("entity_type", NormalizeTag(invalidationEvent.EntityType)));
            CacheInvalidationLag.Record(
                Math.Max(0, lag.TotalMilliseconds),
                new KeyValuePair<string, object?>("trigger", NormalizeTag(invalidationEvent.TriggerType)),
                new KeyValuePair<string, object?>("entity_type", NormalizeTag(invalidationEvent.EntityType)));
            _logger.LogInformation(
                "Invalidated executive cockpit cache namespace for company {CompanyId} from trigger {TriggerType}; lag {InvalidationLagMilliseconds} ms.",
                invalidationEvent.CompanyId,
                invalidationEvent.TriggerType,
                Math.Max(0, lag.TotalMilliseconds));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            CacheErrors.Add(
                1,
                new KeyValuePair<string, object?>("operation", "invalidate"),
                new KeyValuePair<string, object?>("trigger", NormalizeTag(invalidationEvent.TriggerType)));
            _logger.LogWarning(
                ex,
                "Failed to invalidate executive cockpit cache namespace for company {CompanyId} from trigger {TriggerType}.",
                invalidationEvent.CompanyId,
                invalidationEvent.TriggerType);
        }
    }

    private async Task<TSnapshot?> TryGetCoreAsync<TSnapshot>(
        ExecutiveCockpitCacheScope scope,
        string cacheArea,
        Func<TSnapshot?, bool> validate,
        CancellationToken cancellationToken)
        where TSnapshot : class
    {
        if (!_options.Enabled)
        {
            return null;
        }

        using var activity = ActivitySource.StartActivity("executive_cockpit.cache.lookup");
        activity?.SetTag("vc.cockpit.cache.area", cacheArea);
        activity?.SetTag("vc.cockpit.cache.identity", NormalizeTag(scope.Identity));

        var cacheKey = _keyBuilder.BuildDataKey(scope, await GetVersionTokenAsync(scope.CompanyId, cancellationToken));
        byte[]? payload;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            payload = await _distributedCache.GetAsync(cacheKey, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            CacheLookupDuration.Record(stopwatch.Elapsed.TotalMilliseconds, CacheTags(cacheArea, "error"));
            CacheErrors.Add(1, new KeyValuePair<string, object?>("area", cacheArea), new KeyValuePair<string, object?>("operation", "read"));
            activity?.SetTag("vc.cockpit.cache.outcome", "error");
            _logger.LogWarning(ex, "Executive cockpit cache read failed for company {CompanyId}, area {CacheArea}.", scope.CompanyId, cacheArea);
            return null;
        }

        if (payload is null || payload.Length == 0)
        {
            CacheLookupDuration.Record(stopwatch.Elapsed.TotalMilliseconds, CacheTags(cacheArea, "miss"));
            CacheMisses.Add(1, CacheTags(cacheArea, "miss"));
            activity?.SetTag("vc.cockpit.cache.outcome", "miss");
            return null;
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize<TSnapshot>(payload, SerializerOptions);
            if (!validate(snapshot))
            {
                await SafeRemoveAsync(cacheKey, scope.CompanyId, cancellationToken);
                CacheLookupDuration.Record(stopwatch.Elapsed.TotalMilliseconds, CacheTags(cacheArea, "invalid"));
                CacheMisses.Add(1, CacheTags(cacheArea, "invalid"));
                activity?.SetTag("vc.cockpit.cache.outcome", "invalid");
                return null;
            }

            CacheLookupDuration.Record(stopwatch.Elapsed.TotalMilliseconds, CacheTags(cacheArea, "hit"));
            CacheHits.Add(1, CacheTags(cacheArea, "hit"));
            activity?.SetTag("vc.cockpit.cache.outcome", "hit");
            _logger.LogDebug("Executive cockpit cache hit for company {CompanyId}, area {CacheArea}.", scope.CompanyId, cacheArea);
            return snapshot;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            CacheLookupDuration.Record(stopwatch.Elapsed.TotalMilliseconds, CacheTags(cacheArea, "deserialize_error"));
            CacheErrors.Add(1, new KeyValuePair<string, object?>("area", cacheArea), new KeyValuePair<string, object?>("operation", "deserialize"));
            activity?.SetTag("vc.cockpit.cache.outcome", "deserialize_error");
            _logger.LogWarning(ex, "Ignoring malformed executive cockpit cache entry for company {CompanyId}, area {CacheArea}.", scope.CompanyId, cacheArea);
            await SafeRemoveAsync(cacheKey, scope.CompanyId, cancellationToken);
            return null;
        }
    }

    private async Task SetCoreAsync<TSnapshot>(
        ExecutiveCockpitCacheScope scope,
        TSnapshot snapshot,
        string cacheArea,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        using var activity = ActivitySource.StartActivity("executive_cockpit.cache.set");
        activity?.SetTag("vc.cockpit.cache.area", cacheArea);
        activity?.SetTag("vc.cockpit.cache.identity", NormalizeTag(scope.Identity));

        try
        {
            await _distributedCache.SetAsync(
                _keyBuilder.BuildDataKey(scope, await GetVersionTokenAsync(scope.CompanyId, cancellationToken)),
                JsonSerializer.SerializeToUtf8Bytes(snapshot, SerializerOptions),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ttl
                },
                cancellationToken);
            CacheSets.Add(1, new KeyValuePair<string, object?>("area", cacheArea));
            activity?.SetTag("vc.cockpit.cache.outcome", "set");
            _logger.LogDebug("Cached executive cockpit payload for company {CompanyId}, area {CacheArea}.", scope.CompanyId, cacheArea);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            CacheErrors.Add(1, new KeyValuePair<string, object?>("area", cacheArea), new KeyValuePair<string, object?>("operation", "write"));
            activity?.SetTag("vc.cockpit.cache.outcome", "write_error");
            _logger.LogWarning(ex, "Failed to cache executive cockpit payload for company {CompanyId}, area {CacheArea}.", scope.CompanyId, cacheArea);
        }
    }

    private async Task<string> GetVersionTokenAsync(Guid companyId, CancellationToken cancellationToken)
    {
        try
        {
            return await _distributedCache.GetStringAsync(_keyBuilder.BuildVersionKey(companyId), cancellationToken)
                ?? DefaultVersionToken;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            CacheErrors.Add(1, new KeyValuePair<string, object?>("operation", "version-read"));
            _logger.LogWarning(ex, "Failed to read executive cockpit cache version for company {CompanyId}; using default namespace.", companyId);
            return DefaultVersionToken;
        }
    }

    private async Task SafeRemoveAsync(string cacheKey, Guid companyId, CancellationToken cancellationToken)
    {
        try
        {
            await _distributedCache.RemoveAsync(cacheKey, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            CacheErrors.Add(1, new KeyValuePair<string, object?>("operation", "remove"));
            _logger.LogWarning(ex, "Failed to remove malformed executive cockpit cache entry for company {CompanyId}.", companyId);
        }
    }

    public static string BuildCacheKey(Guid companyId) =>
        new ExecutiveCockpitCacheKeyBuilder(Options.Create(new ExecutiveCockpitDashboardCacheOptions()))
            .BuildLegacyCompanyKey(companyId);

    public static string BuildCacheKey(ExecutiveCockpitCacheScope scope, string versionToken = "0") =>
        new ExecutiveCockpitCacheKeyBuilder(Options.Create(new ExecutiveCockpitDashboardCacheOptions()))
            .BuildDataKey(scope, versionToken);

    private static bool ValidateDashboard(CachedExecutiveCockpitDashboardDto? snapshot) =>
        snapshot is not null &&
        snapshot.CompanyId != Guid.Empty &&
        snapshot.Dashboard.CompanyId == snapshot.CompanyId;

    private static bool ValidateKpis(CachedExecutiveCockpitKpiDashboardDto? snapshot) =>
        snapshot is not null &&
        snapshot.CompanyId != Guid.Empty &&
        snapshot.Dashboard.CompanyId == snapshot.CompanyId;

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private static string NormalizeTag(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "none"
            : value.Trim().ToLowerInvariant().Replace(" ", "-").Replace("_", "-");

    private static KeyValuePair<string, object?>[] CacheTags(string cacheArea, string outcome) =>
    [
        new("area", cacheArea),
        new("outcome", outcome)
    ];
}