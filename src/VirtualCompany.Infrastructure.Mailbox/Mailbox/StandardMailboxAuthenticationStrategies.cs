using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;

namespace VirtualCompany.Infrastructure.Mailbox;

public sealed class ApplicationPasswordMailboxAuthenticationStrategy : IMailboxAuthenticationStrategy
{
    private readonly IFieldEncryptionService _fieldEncryption;

    public ApplicationPasswordMailboxAuthenticationStrategy(IFieldEncryptionService fieldEncryption)
    {
        _fieldEncryption = fieldEncryption;
    }

    public MailboxAuthenticationType AuthenticationType => MailboxAuthenticationType.ApplicationPassword;

    public Task<MailboxCredentialLease> ResolveAsync(
        MailboxAuthenticationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(context.EncryptedCredentialEnvelope))
        {
            throw new InvalidOperationException("The mailbox application password must be replaced before this connection can be used.");
        }

        var credential = _fieldEncryption.Decrypt(
            context.CompanyId,
            StandardMailboxCredentialPurposes.ApplicationPassword(context.ConnectionId),
            context.EncryptedCredentialEnvelope);
        return Task.FromResult(new MailboxCredentialLease(AuthenticationType, context.Username, credential));
    }
}

public sealed class OAuthMailboxAuthenticationStrategy : IMailboxAuthenticationStrategy
{
    private static readonly TimeSpan MinimumLease = TimeSpan.FromMinutes(5);
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IFieldEncryptionService _fieldEncryption;
    private readonly TimeProvider _timeProvider;

    public OAuthMailboxAuthenticationStrategy(
        VirtualCompanyDbContext dbContext,
        IFieldEncryptionService fieldEncryption,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _fieldEncryption = fieldEncryption;
        _timeProvider = timeProvider;
    }

    public MailboxAuthenticationType AuthenticationType => MailboxAuthenticationType.OAuth2;

    public async Task<MailboxCredentialLease> ResolveAsync(
        MailboxAuthenticationContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.EncryptedAccessToken))
        {
            throw new InvalidOperationException("Reconnect this mailbox to restore OAuth access.");
        }

        if (context.AccessTokenExpiresUtc is { } expiresUtc &&
            expiresUtc <= _timeProvider.GetUtcNow().UtcDateTime.Add(MinimumLease))
        {
            var connectionExists = await _dbContext.MailboxConnections
                .AsNoTracking()
                .AnyAsync(connection => connection.CompanyId == context.CompanyId && connection.Id == context.ConnectionId, cancellationToken);
            if (!connectionExists || string.IsNullOrWhiteSpace(context.EncryptedRefreshToken))
            {
                throw new InvalidOperationException("Reconnect this mailbox because its OAuth access has expired.");
            }

            throw new InvalidOperationException("The mailbox OAuth token must be refreshed before a transport session is created.");
        }

        var token = _fieldEncryption.Decrypt(
            context.CompanyId,
            StandardMailboxCredentialPurposes.AccessToken(context.ConnectionId),
            context.EncryptedAccessToken);
        return new MailboxCredentialLease(AuthenticationType, context.Username, token, context.AccessTokenExpiresUtc);
    }
}
