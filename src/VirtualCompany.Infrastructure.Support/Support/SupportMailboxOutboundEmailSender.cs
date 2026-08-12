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
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Support;

public sealed class SupportMailboxOutboundEmailSender : ISupportOutboundEmailSender
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IMailboxProviderRegistry _providerRegistry;
    private readonly IMailboxOAuthAccessTokenLeaseService _tokenLeaseService;

    public SupportMailboxOutboundEmailSender(
        VirtualCompanyDbContext dbContext,
        IMailboxProviderRegistry providerRegistry,
        IMailboxOAuthAccessTokenLeaseService tokenLeaseService)
    {
        _dbContext = dbContext;
        _providerRegistry = providerRegistry;
        _tokenLeaseService = tokenLeaseService;
    }

    public async Task<SupportOutboundEmailSendResult> SendReplyAsync(SupportOutboundEmailSendRequest request, CancellationToken cancellationToken)
    {
        var connectionQuery = _dbContext.MailboxConnections.IgnoreQueryFilters()
            .Where(x => x.CompanyId == request.CompanyId &&
                x.Purpose == MailboxPurpose.Support &&
                x.Status == MailboxConnectionStatus.Active &&
                x.CapabilityFlags.HasFlag(MailboxCapability.SendMessages));
        if (request.MailboxConnectionId is Guid mailboxConnectionId)
        {
            connectionQuery = connectionQuery.Where(x => x.Id == mailboxConnectionId);
        }

        var connection = await connectionQuery.OrderByDescending(x => x.UpdatedUtc).FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("A connected support mailbox is required before support replies can be sent.");
        var provider = _providerRegistry.Resolve(connection.Provider);
        var accessToken = (await _tokenLeaseService.AcquireAsync(
            request.CompanyId, connection.Id, provider.ReplyRequiredScopes, cancellationToken)).AccessToken;
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

}
