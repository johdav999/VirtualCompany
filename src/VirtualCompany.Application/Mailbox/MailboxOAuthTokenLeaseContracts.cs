using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Mailbox;

public sealed record MailboxOAuthAccessTokenLease(
    Guid ConnectionId,
    Guid CompanyId,
    MailboxProvider Provider,
    string EmailAddress,
    string AccessToken,
    DateTime? ExpiresUtc,
    IReadOnlyCollection<string> GrantedScopes);

public interface IMailboxOAuthAccessTokenLeaseService
{
    Task<MailboxOAuthAccessTokenLease> AcquireAsync(
        Guid companyId,
        Guid connectionId,
        IReadOnlyCollection<string> requiredScopes,
        CancellationToken cancellationToken);
}
