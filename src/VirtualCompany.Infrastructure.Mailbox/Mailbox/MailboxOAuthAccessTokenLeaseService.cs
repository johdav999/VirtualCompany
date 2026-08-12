using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Security;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Mailbox;

public sealed class MailboxOAuthAccessTokenLeaseService : IMailboxOAuthAccessTokenLeaseService
{
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(5);
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IMailboxProviderRegistry _providerRegistry;
    private readonly IFieldEncryptionService _fieldEncryption;

    public MailboxOAuthAccessTokenLeaseService(
        VirtualCompanyDbContext dbContext,
        IMailboxProviderRegistry providerRegistry,
        IFieldEncryptionService fieldEncryption)
    {
        _dbContext = dbContext;
        _providerRegistry = providerRegistry;
        _fieldEncryption = fieldEncryption;
    }

    public async Task<MailboxOAuthAccessTokenLease> AcquireAsync(
        Guid companyId,
        Guid connectionId,
        IReadOnlyCollection<string> requiredScopes,
        CancellationToken cancellationToken)
    {
        var connection = await _dbContext.MailboxConnections
            .Include(x => x.ExternalAccountConnection)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == connectionId, cancellationToken)
            ?? throw new KeyNotFoundException("Mailbox connection not found.");

        if (connection.Status != MailboxConnectionStatus.Active)
            throw new InvalidOperationException("Reconnect this mailbox before sending a reply.");

        if (connection.Provider == MailboxProvider.StandardEmail)
        {
            if (!connection.CapabilityFlags.HasFlag(MailboxCapability.SendMessages))
                throw new InvalidOperationException("This mailbox connection is not configured to send messages.");

            return new MailboxOAuthAccessTokenLease(
                connection.Id, connection.CompanyId, connection.Provider,
                connection.EmailAddress,
                StandardMailboxSessionCodec.Create(connection, _fieldEncryption),
                ExpiresUtc: null,
                connection.GrantedScopes);
        }

        var external = connection.ExternalAccountConnection
            ?? throw new InvalidOperationException("Reconnect this mailbox to restore its OAuth account connection.");
        if (external.Status != ExternalConnectionStatus.Active)
            throw new InvalidOperationException("Reconnect this mailbox before sending a reply.");

        var expectedProvider = connection.Provider == MailboxProvider.Gmail
            ? ExternalAccountProvider.Google
            : ExternalAccountProvider.Microsoft365;
        if (external.Provider != expectedProvider)
            throw new InvalidOperationException("The mailbox is linked to an incompatible external account.");

        var missingScopes = requiredScopes
            .Where(required => !external.GrantedScopes.Contains(required, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (missingScopes.Length > 0)
            throw new InvalidOperationException("Reconnect this mailbox and grant permission to read and send replies.");

        if (string.IsNullOrWhiteSpace(external.EncryptedAccessToken) ||
            external.AccessTokenExpiresUtc is { } expiresUtc && expiresUtc <= DateTime.UtcNow.Add(RefreshWindow))
        {
            await RefreshAsync(connection, external, cancellationToken);
        }

        var accessToken = _fieldEncryption.Decrypt(
            external.CompanyId,
            external.CredentialPurpose("access_token"),
            external.EncryptedAccessToken ?? throw new InvalidOperationException("Reconnect this mailbox before sending a reply."));

        return new MailboxOAuthAccessTokenLease(
            connection.Id, connection.CompanyId, connection.Provider,
            connection.EmailAddress, accessToken, external.AccessTokenExpiresUtc,
            external.GrantedScopes);
    }

    private async Task RefreshAsync(
        MailboxConnection connection,
        ExternalAccountConnection external,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(external.EncryptedRefreshToken))
            throw new InvalidOperationException("Reconnect this mailbox before sending a reply.");

        var refreshToken = _fieldEncryption.Decrypt(
            external.CompanyId,
            external.CredentialPurpose("refresh_token"),
            external.EncryptedRefreshToken);
        var result = await _providerRegistry.Resolve(connection.Provider).RefreshTokenAsync(
            new MailboxRefreshTokenRequest(
                refreshToken,
                connection.ProfileKey,
                external.GrantedScopes), cancellationToken);

        external.StoreEncryptedCredentials(
            _fieldEncryption.Encrypt(external.CompanyId, external.CredentialPurpose("access_token"), result.AccessToken),
            string.IsNullOrWhiteSpace(result.RefreshToken)
                ? external.EncryptedRefreshToken
                : _fieldEncryption.Encrypt(external.CompanyId, external.CredentialPurpose("refresh_token"), result.RefreshToken),
            result.AccessTokenExpiresUtc,
            result.GrantedScopes.Count == 0 ? external.GrantedScopes : result.GrantedScopes);
        external.SetStatus(ExternalConnectionStatus.Active);
        connection.SetStatus(MailboxConnectionStatus.Active);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
