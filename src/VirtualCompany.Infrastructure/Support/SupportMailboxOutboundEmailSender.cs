using System.Text.Json.Nodes;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Approvals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Support;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Mailbox;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;

namespace VirtualCompany.Infrastructure.Support;

public sealed class SupportMailboxOutboundEmailSender : ISupportOutboundEmailSender
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IMailboxProviderRegistry _providerRegistry;
    private readonly IFieldEncryptionService _fieldEncryption;
    private readonly TimeProvider _timeProvider;

    public SupportMailboxOutboundEmailSender(
        VirtualCompanyDbContext dbContext,
        IMailboxProviderRegistry providerRegistry,
        IFieldEncryptionService fieldEncryption,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _providerRegistry = providerRegistry;
        _fieldEncryption = fieldEncryption;
        _timeProvider = timeProvider;
    }

    public async Task<SupportOutboundEmailSendResult> SendReplyAsync(SupportOutboundEmailSendRequest request, CancellationToken cancellationToken)
    {
        var connectionQuery = _dbContext.MailboxConnections.IgnoreQueryFilters()
            .Where(x => x.CompanyId == request.CompanyId &&
                x.Purpose == MailboxPurpose.Support &&
                x.Status == MailboxConnectionStatus.Active &&
                (x.EncryptedAccessToken != null || x.EncryptedCredentialEnvelope != null));
        if (request.MailboxConnectionId is Guid mailboxConnectionId)
        {
            connectionQuery = connectionQuery.Where(x => x.Id == mailboxConnectionId);
        }

        var connection = await connectionQuery.OrderByDescending(x => x.UpdatedUtc).FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("A connected support mailbox is required before support replies can be sent.");
        var provider = _providerRegistry.Resolve(connection.Provider);
        var accessToken = await GetMailboxAccessTokenAsync(provider, connection, cancellationToken);
        var result = await provider.SendReplyAsync(accessToken, new MailboxReplyExecutionRequest(
            request.CompanyId,
            connection.Id,
            connection.Provider.ToStorageValue(),
            request.OriginalMessageId,
            request.ProviderThreadId,
            request.InternetMessageId,
            request.ToEmail,
            request.ToDisplayName,
            request.Subject,
            request.BodyText,
            request.IdempotencyKey), cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return new SupportOutboundEmailSendResult(connection.Provider.ToStorageValue(), connection.Id, result.ProviderMessageId, result.ProviderThreadId, result.Status);
    }

    private async Task<string> GetMailboxAccessTokenAsync(IMailboxProviderClient provider, MailboxConnection connection, CancellationToken cancellationToken)
    {
        if (connection.Provider == MailboxProvider.StandardEmail)
        {
            return StandardMailboxSessionCodec.Create(connection, _fieldEncryption);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (!string.IsNullOrWhiteSpace(connection.EncryptedAccessToken) &&
            (!connection.AccessTokenExpiresUtc.HasValue || connection.AccessTokenExpiresUtc.Value > now.AddMinutes(5)))
        {
            return _fieldEncryption.Decrypt(
                connection.CompanyId,
                CompanyMailboxConnectionService.BuildTokenPurpose(connection.Provider, "access_token"),
                connection.EncryptedAccessToken);
        }

        if (string.IsNullOrWhiteSpace(connection.EncryptedRefreshToken))
        {
            if (!string.IsNullOrWhiteSpace(connection.EncryptedAccessToken))
            {
                return _fieldEncryption.Decrypt(
                    connection.CompanyId,
                    CompanyMailboxConnectionService.BuildTokenPurpose(connection.Provider, "access_token"),
                    connection.EncryptedAccessToken);
            }

            throw new InvalidOperationException("Mailbox access token is missing.");
        }

        var refreshToken = _fieldEncryption.Decrypt(
            connection.CompanyId,
            CompanyMailboxConnectionService.BuildTokenPurpose(connection.Provider, "refresh_token"),
            connection.EncryptedRefreshToken);
        var tokenResult = await provider.RefreshTokenAsync(new MailboxRefreshTokenRequest(refreshToken), cancellationToken);
        connection.StoreEncryptedCredentials(
            _fieldEncryption.Encrypt(connection.CompanyId, CompanyMailboxConnectionService.BuildTokenPurpose(connection.Provider, "access_token"), tokenResult.AccessToken),
            string.IsNullOrWhiteSpace(tokenResult.RefreshToken)
                ? connection.EncryptedRefreshToken
                : _fieldEncryption.Encrypt(connection.CompanyId, CompanyMailboxConnectionService.BuildTokenPurpose(connection.Provider, "refresh_token"), tokenResult.RefreshToken),
            tokenResult.AccessTokenExpiresUtc,
            tokenResult.GrantedScopes.Count > 0 ? tokenResult.GrantedScopes : connection.GrantedScopes);
        connection.SetStatus(MailboxConnectionStatus.Active);
        return tokenResult.AccessToken;
    }
}
