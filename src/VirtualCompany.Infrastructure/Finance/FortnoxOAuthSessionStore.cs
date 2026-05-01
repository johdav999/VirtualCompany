using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Distributed;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class DistributedCacheFortnoxOAuthSessionStore : IFortnoxOAuthSessionStore
{
    private const int HandleBytes = 32;
    private readonly IDistributedCache _cache;
    private readonly IFortnoxOAuthStateProtector _stateProtector;

    public DistributedCacheFortnoxOAuthSessionStore(
        IDistributedCache cache,
        IFortnoxOAuthStateProtector stateProtector)
    {
        _cache = cache;
        _stateProtector = stateProtector;
    }

    public async Task<string> CreateAsync(FortnoxOAuthState state, TimeSpan ttl, CancellationToken cancellationToken)
    {
        if (state.CompanyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(state));
        }

        var handle = CreateHandle();
        await _cache.SetStringAsync(
            BuildKey(state.CompanyId, handle),
            _stateProtector.Protect(state),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl
            },
            cancellationToken);

        return handle;
    }

    public async Task<FortnoxOAuthState> ConsumeAsync(Guid companyId, string stateHandle, CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty || string.IsNullOrWhiteSpace(stateHandle))
        {
            throw new UnauthorizedAccessException("Fortnox OAuth state was invalid.");
        }

        var key = BuildKey(companyId, stateHandle);
        var protectedState = await _cache.GetStringAsync(key, cancellationToken);
        if (string.IsNullOrWhiteSpace(protectedState))
        {
            throw new UnauthorizedAccessException("Fortnox OAuth state was invalid or expired.");
        }

        await _cache.RemoveAsync(key, cancellationToken);
        var state = _stateProtector.Unprotect(protectedState);
        if (state.CompanyId != companyId)
        {
            throw new UnauthorizedAccessException("Fortnox OAuth state was invalid.");
        }

        return state;
    }

    private static string CreateHandle()
    {
        Span<byte> bytes = stackalloc byte[HandleBytes];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static string BuildKey(Guid companyId, string handle) =>
        $"fortnox-oauth:company:{companyId:N}:state:{handle.Trim()}";
}