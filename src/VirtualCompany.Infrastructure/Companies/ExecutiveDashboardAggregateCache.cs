using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Briefings;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class ExecutiveDashboardAggregateCache : IExecutiveDashboardAggregateCache
{
    private const string CacheKeyVersion = "v1";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan TimeToLive = TimeSpan.FromHours(2);

    private readonly IDistributedCache _distributedCache;
    private readonly ILogger<ExecutiveDashboardAggregateCache> _logger;

    public ExecutiveDashboardAggregateCache(
        IDistributedCache distributedCache,
        ILogger<ExecutiveDashboardAggregateCache> logger)
    {
        _distributedCache = distributedCache;
        _logger = logger;
    }

    public async Task<CachedExecutiveDashboardAggregateDto?> TryGetAsync(
        Guid companyId,
        string briefingType,
        DateTime periodStartUtc,
        DateTime periodEndUtc,
        CancellationToken cancellationToken)
    {
        var cacheKey = BuildCacheKey(companyId, briefingType, periodStartUtc, periodEndUtc);
        try
        {
            var payload = await _distributedCache.GetAsync(cacheKey, cancellationToken);
            if (payload is null || payload.Length == 0)
            {
                return null;
            }

            var snapshot = JsonSerializer.Deserialize<CachedExecutiveDashboardAggregateDto>(payload, SerializerOptions);
            if (snapshot is null ||
                snapshot.CompanyId != companyId ||
                snapshot.Aggregate.CompanyId != companyId ||
                !string.Equals(snapshot.BriefingType, briefingType, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(snapshot.Aggregate.BriefingType, briefingType, StringComparison.OrdinalIgnoreCase) ||
                snapshot.PeriodStartUtc != periodStartUtc ||
                snapshot.PeriodEndUtc != periodEndUtc ||
                snapshot.Aggregate.PeriodStartUtc != periodStartUtc ||
                snapshot.Aggregate.PeriodEndUtc != periodEndUtc)
            {
                await _distributedCache.RemoveAsync(cacheKey, cancellationToken);
                return null;
            }

            return snapshot;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "Ignoring malformed dashboard aggregate cache entry for company {CompanyId}, briefing type {BriefingType}.",
                companyId,
                briefingType);

            try
            {
                await _distributedCache.RemoveAsync(cacheKey, cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
            }

            return null;
        }
    }

    public async Task SetAsync(CachedExecutiveDashboardAggregateDto snapshot, CancellationToken cancellationToken)
    {
        if (snapshot.CompanyId == Guid.Empty ||
            snapshot.Aggregate.CompanyId != snapshot.CompanyId ||
            string.IsNullOrWhiteSpace(snapshot.BriefingType) ||
            !string.Equals(snapshot.Aggregate.BriefingType, snapshot.BriefingType, StringComparison.OrdinalIgnoreCase) ||
            snapshot.Aggregate.PeriodStartUtc != snapshot.PeriodStartUtc ||
            snapshot.Aggregate.PeriodEndUtc != snapshot.PeriodEndUtc)
        {
            return;
        }

        try
        {
            await _distributedCache.SetAsync(
                BuildCacheKey(snapshot.CompanyId, snapshot.BriefingType, snapshot.PeriodStartUtc, snapshot.PeriodEndUtc),
                JsonSerializer.SerializeToUtf8Bytes(snapshot, SerializerOptions),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeToLive
                },
                cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Failed to cache dashboard aggregate for company {CompanyId}.", snapshot.CompanyId);
        }
    }

    private static string BuildCacheKey(Guid companyId, string briefingType, DateTime periodStartUtc, DateTime periodEndUtc) =>
        $"vc:dashboard:aggregate:{CacheKeyVersion}:{companyId:N}:{briefingType.Trim().ToLowerInvariant()}:{periodStartUtc.Ticks}:{periodEndUtc.Ticks}";
}
