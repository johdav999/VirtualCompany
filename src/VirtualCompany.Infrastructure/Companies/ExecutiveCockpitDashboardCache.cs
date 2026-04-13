using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Cockpit;
using Microsoft.Extensions.Options;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class ExecutiveCockpitDashboardCache : IExecutiveCockpitDashboardCache
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IDistributedCache _distributedCache;
    private readonly ExecutiveCockpitDashboardCacheOptions _options;
    private readonly ILogger<ExecutiveCockpitDashboardCache> _logger;

    public ExecutiveCockpitDashboardCache(
        IDistributedCache distributedCache,
        IOptions<ExecutiveCockpitDashboardCacheOptions> options,
        ILogger<ExecutiveCockpitDashboardCache> logger)
    {
        _distributedCache = distributedCache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CachedExecutiveCockpitDashboardDto?> TryGetAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return null;
        }

        var cacheKey = BuildCacheKey(companyId, _options);
        byte[]? payload;
        try
        {
            payload = await _distributedCache.GetAsync(cacheKey, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Executive cockpit dashboard cache read failed for company {CompanyId}; falling back to database.", companyId);
            return null;
        }

            if (payload is null || payload.Length == 0)
            {
            _logger.LogDebug("Executive cockpit dashboard cache miss for company {CompanyId}.", companyId);
                return null;
            }

        try
        {
            var snapshot = JsonSerializer.Deserialize<CachedExecutiveCockpitDashboardDto>(payload, SerializerOptions);
            if (snapshot is null ||
                snapshot.CompanyId != companyId ||
                snapshot.Dashboard.CompanyId != companyId)
            {
                await SafeRemoveAsync(cacheKey, companyId, cancellationToken);
                return null;
            }

            _logger.LogDebug("Executive cockpit dashboard cache hit for company {CompanyId}.", companyId);
            return snapshot;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Ignoring malformed executive cockpit dashboard cache entry for company {CompanyId}.", companyId);
            await SafeRemoveAsync(cacheKey, companyId, cancellationToken);
            return null;
        }
    }

    public async Task SetAsync(CachedExecutiveCockpitDashboardDto snapshot, CancellationToken cancellationToken)
    {
        if (!_options.Enabled ||
            snapshot.CompanyId == Guid.Empty ||
            snapshot.Dashboard.CompanyId != snapshot.CompanyId)
        {
            return;
        }

        try
        {
            await _distributedCache.SetAsync(
                BuildCacheKey(snapshot.CompanyId, _options),
                JsonSerializer.SerializeToUtf8Bytes(snapshot, SerializerOptions),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = _options.GetTtl()
                },
                cancellationToken);
            _logger.LogDebug("Cached executive cockpit dashboard for company {CompanyId}.", snapshot.CompanyId);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Failed to cache executive cockpit dashboard for company {CompanyId}.", snapshot.CompanyId);
        }
    }

    public async Task InvalidateAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (!_options.Enabled || companyId == Guid.Empty)
        {
            return;
        }

        await SafeRemoveAsync(BuildCacheKey(companyId, _options), companyId, cancellationToken);
    }

    private async Task SafeRemoveAsync(string cacheKey, Guid companyId, CancellationToken cancellationToken)
    {
        try
        {
            await _distributedCache.RemoveAsync(cacheKey, cancellationToken);
            _logger.LogDebug("Invalidated executive cockpit dashboard cache for company {CompanyId}.", companyId);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Failed to invalidate executive cockpit dashboard cache for company {CompanyId}.", companyId);
        }
    }

    public static string BuildCacheKey(Guid companyId) =>
        BuildCacheKey(companyId, new ExecutiveCockpitDashboardCacheOptions());

    private static string BuildCacheKey(Guid companyId, ExecutiveCockpitDashboardCacheOptions options) =>
        $"{options.KeyPrefix.Trim().TrimEnd(':')}:{options.KeyVersion.Trim().ToLowerInvariant()}:company:{companyId:N}";
}