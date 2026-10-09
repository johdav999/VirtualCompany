using VirtualCompany.Application.Sales;
namespace VirtualCompany.Shared.Contracts.SalesEmail;
public sealed record ProcessSalesEmailMessageRequest(
        Guid MailboxConnectionId,
        string ProviderMessageId);
public sealed record ProcessSalesEmailThreadRequest(
        Guid MailboxConnectionId,
        string ProviderThreadId);
