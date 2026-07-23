using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Context;

namespace VirtualCompany.Infrastructure.Context;

public interface IGroundedContextRetrievalSectionCache
{
    Task<RetrievalSectionDto?> TryGetAsync(string cacheKey, CancellationToken cancellationToken);
    Task TrySetAsync(string cacheKey, RetrievalSectionDto section, TimeSpan timeToLive, CancellationToken cancellationToken);
}

public sealed class GroundedContextRetrievalSectionCache : IGroundedContextRetrievalSectionCache
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IDistributedCache _distributedCache;
    private readonly GroundedContextRetrievalCacheOptions _options;

    public GroundedContextRetrievalSectionCache(
        IDistributedCache distributedCache,
        IOptions<GroundedContextRetrievalCacheOptions> options)
    {
        _distributedCache = distributedCache;
        _options = options.Value;
    }

    public async Task<RetrievalSectionDto?> TryGetAsync(string cacheKey, CancellationToken cancellationToken)
    {
        if (!_options.IsPayloadCachingAllowed)
        {
            return null;
        }

        try
        {
            var payload = await _distributedCache.GetAsync(cacheKey, cancellationToken);
            if (payload is null || payload.Length == 0)
            {
                return null;
            }

            return JsonSerializer.Deserialize<RetrievalSectionDto>(payload, SerializerOptions);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
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

    public async Task TrySetAsync(string cacheKey, RetrievalSectionDto section, TimeSpan timeToLive, CancellationToken cancellationToken)
    {
        if (!_options.IsPayloadCachingAllowed || timeToLive <= TimeSpan.Zero)
        {
            return;
        }

        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(section, SerializerOptions);
            if (payload.Length > _options.MaxPayloadBytes)
            {
                return;
            }

            await _distributedCache.SetAsync(
                cacheKey,
                payload,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = timeToLive
                },
                cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
        }
    }
}
