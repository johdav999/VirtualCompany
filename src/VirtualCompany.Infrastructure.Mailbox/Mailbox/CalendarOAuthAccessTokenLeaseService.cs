using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Security;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Mailbox;

public sealed class CalendarOAuthAccessTokenLeaseService : ICalendarOAuthAccessTokenLeaseService
{
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(5);
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IMailboxProviderRegistry _providerRegistry;
    private readonly IFieldEncryptionService _encryption;

    public CalendarOAuthAccessTokenLeaseService(
        VirtualCompanyDbContext dbContext,
        IMailboxProviderRegistry providerRegistry,
        IFieldEncryptionService encryption)
    {
        _dbContext = dbContext;
        _providerRegistry = providerRegistry;
        _encryption = encryption;
    }

    public async Task<CalendarOAuthAccessTokenLease> AcquireAsync(
        Guid companyId, Guid calendarConnectionId,
        IReadOnlyCollection<string> requiredScopes,
        CancellationToken cancellationToken)
    {
        var calendar = await _dbContext.CalendarConnections
            .Include(x => x.ExternalAccountConnection)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == calendarConnectionId, cancellationToken)
            ?? throw new KeyNotFoundException("Calendar connection not found.");
        var external = calendar.ExternalAccountConnection;
        if (calendar.Status != ExternalConnectionStatus.Active || external.Status != ExternalConnectionStatus.Active)
            throw new InvalidOperationException("Reconnect this calendar before scheduling a meeting.");
        var missing = requiredScopes.Where(scope =>
            !external.GrantedScopes.Contains(scope, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException("Reconnect this calendar and grant the required calendar permissions.");
        if (string.IsNullOrWhiteSpace(external.EncryptedAccessToken) ||
            external.AccessTokenExpiresUtc is { } expiry && expiry <= DateTime.UtcNow.Add(RefreshWindow))
            await RefreshAsync(external, requiredScopes, cancellationToken);
        var accessToken = _encryption.Decrypt(
            companyId, external.CredentialPurpose("access_token"),
            external.EncryptedAccessToken ?? throw new InvalidOperationException("Reconnect this calendar before scheduling a meeting."));
        return new CalendarOAuthAccessTokenLease(
            calendar.Id, external.Id, companyId, calendar.Provider,
            calendar.AccountEmail, accessToken, external.AccessTokenExpiresUtc,
            external.GrantedScopes, calendar.CalendarId);
    }

    private async Task RefreshAsync(
        ExternalAccountConnection external,
        IReadOnlyCollection<string> requiredScopes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(external.EncryptedRefreshToken))
            throw new InvalidOperationException("Reconnect this calendar before scheduling a meeting.");
        var refresh = _encryption.Decrypt(
            external.CompanyId, external.CredentialPurpose("refresh_token"), external.EncryptedRefreshToken);
        var provider = external.Provider switch
        {
            ExternalAccountProvider.Google => MailboxProvider.Gmail,
            ExternalAccountProvider.Microsoft365 => MailboxProvider.Microsoft365,
            _ => throw new InvalidOperationException("This calendar provider is unavailable.")
        };
        var result = await _providerRegistry.Resolve(provider).RefreshTokenAsync(
            new MailboxRefreshTokenRequest(
                refresh, RequestedScopes: external.GrantedScopes), cancellationToken);
        external.StoreEncryptedCredentials(
            _encryption.Encrypt(external.CompanyId, external.CredentialPurpose("access_token"), result.AccessToken),
            string.IsNullOrWhiteSpace(result.RefreshToken)
                ? external.EncryptedRefreshToken
                : _encryption.Encrypt(external.CompanyId, external.CredentialPurpose("refresh_token"), result.RefreshToken),
            result.AccessTokenExpiresUtc,
            result.GrantedScopes.Count == 0 ? external.GrantedScopes : result.GrantedScopes);
        external.SetStatus(ExternalConnectionStatus.Active);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
