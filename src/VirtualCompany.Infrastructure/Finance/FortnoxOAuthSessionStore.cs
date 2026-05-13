using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Persistence;
using DomainFortnoxOAuthState = VirtualCompany.Domain.Entities.FortnoxOAuthState;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class EfFortnoxOAuthSessionStore : IFortnoxOAuthSessionStore
{
    private const int HandleBytes = 32;
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IFortnoxOAuthStateProtector _stateProtector;

    public EfFortnoxOAuthSessionStore(
        VirtualCompanyDbContext dbContext,
        IFortnoxOAuthStateProtector stateProtector)
    {
        _dbContext = dbContext;
        _stateProtector = stateProtector;
    }

    public async Task<string> CreateAsync(FortnoxOAuthState state, TimeSpan ttl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        var handle = CreateHandle();
        var expiresUtc = state.IssuedUtc.Add(ttl);
        var record = new DomainFortnoxOAuthState(
            Guid.NewGuid(),
            state.CompanyId,
            state.UserId,
            HashState(handle),
            state.IssuedUtc,
            expiresUtc,
            state.ReturnUri?.ToString(),
            _stateProtector.Protect(state with { ExpiresUtc = expiresUtc }));

        _dbContext.FortnoxOAuthStates.Add(record);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return handle;
    }

    public async Task<FortnoxOAuthState> GetAsync(Guid companyId, string stateHandle, CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty || string.IsNullOrWhiteSpace(stateHandle))
        {
            throw new UnauthorizedAccessException("Fortnox OAuth state was invalid.");
        }

        var stateHash = HashState(stateHandle);
        var record = await _dbContext.FortnoxOAuthStates
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.StateHash == stateHash, cancellationToken);

        if (record is null || record.ExpiresUtc <= DateTime.UtcNow)
        {
            throw new UnauthorizedAccessException("Fortnox OAuth state was invalid or expired.");
        }

        if (record.ConsumedUtc.HasValue)
        {
            throw new UnauthorizedAccessException("Fortnox OAuth state was already used.");
        }

        if (string.IsNullOrWhiteSpace(record.CodeVerifierCiphertext))
        {
            throw new UnauthorizedAccessException("Fortnox OAuth state was invalid or expired.");
        }

        var state = _stateProtector.Unprotect(record.CodeVerifierCiphertext);
        if (state.CompanyId != companyId)
        {
            throw new UnauthorizedAccessException("Fortnox OAuth state was invalid.");
        }

        return state;
    }

    public async Task<FortnoxOAuthState> GetAsync(string stateHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stateHandle))
        {
            throw new UnauthorizedAccessException("Fortnox OAuth state was invalid.");
        }

        var stateHash = HashState(stateHandle);
        var record = await _dbContext.FortnoxOAuthStates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.StateHash == stateHash, cancellationToken);

        if (record is null || record.ExpiresUtc <= DateTime.UtcNow)
        {
            throw new UnauthorizedAccessException("Fortnox OAuth state was invalid or expired.");
        }

        if (record.ConsumedUtc.HasValue)
        {
            throw new UnauthorizedAccessException("Fortnox OAuth state was already used.");
        }

        if (string.IsNullOrWhiteSpace(record.CodeVerifierCiphertext))
        {
            throw new UnauthorizedAccessException("Fortnox OAuth state was invalid or expired.");
        }

        var state = _stateProtector.Unprotect(record.CodeVerifierCiphertext);
        if (state.CompanyId != record.CompanyId)
        {
            throw new UnauthorizedAccessException("Fortnox OAuth state was invalid.");
        }

        return state;
    }

    public async Task<FortnoxOAuthRedirectState?> GetRedirectStateAsync(string stateHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stateHandle))
        {
            return null;
        }

        var stateHash = HashState(stateHandle);
        var record = await _dbContext.FortnoxOAuthStates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.StateHash == stateHash)
            .Select(x => new { x.CompanyId, x.RedirectUri })
            .SingleOrDefaultAsync(cancellationToken);

        if (record is null)
        {
            return null;
        }

        var returnUri = Uri.TryCreate(record.RedirectUri, UriKind.Absolute, out var parsedReturnUri)
            ? parsedReturnUri
            : null;

        return new FortnoxOAuthRedirectState(record.CompanyId, returnUri);
    }

    public async Task MarkConsumedAsync(
        Guid companyId,
        string stateHandle,
        Guid? connectionId,
        DateTime consumedUtc,
        CancellationToken cancellationToken)
    {
        var stateHash = HashState(stateHandle);
        var affected = await _dbContext.FortnoxOAuthStates
            .Where(x => x.CompanyId == companyId && x.StateHash == stateHash && x.ConsumedUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.ConnectionId, connectionId)
                    .SetProperty(x => x.ConsumedUtc, consumedUtc)
                    .SetProperty(x => x.CallbackReceivedUtc, consumedUtc)
                    .SetProperty(x => x.FailureReason, (string?)null),
                cancellationToken);

        if (affected != 1)
        {
            throw new UnauthorizedAccessException("Fortnox OAuth state was already used.");
        }
    }

    public async Task AttachConnectionAsync(
        Guid companyId,
        string stateHandle,
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        var stateHash = HashState(stateHandle);
        var affected = await _dbContext.FortnoxOAuthStates
            .Where(x => x.CompanyId == companyId && x.StateHash == stateHash && x.ConsumedUtc != null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.ConnectionId, connectionId),
                cancellationToken);

        if (affected != 1)
        {
            throw new UnauthorizedAccessException("Fortnox OAuth state was invalid.");
        }
    }

    public async Task MarkFailedAsync(
        Guid companyId,
        string stateHandle,
        string safeReason,
        DateTime receivedUtc,
        CancellationToken cancellationToken)
    {
        var stateHash = HashState(stateHandle);
        await _dbContext.FortnoxOAuthStates
            .Where(x => x.CompanyId == companyId && x.StateHash == stateHash && x.ConsumedUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.CallbackReceivedUtc, receivedUtc)
                    .SetProperty(x => x.FailureReason, safeReason),
                cancellationToken);
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

    private static string HashState(string handle)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(handle.Trim()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
