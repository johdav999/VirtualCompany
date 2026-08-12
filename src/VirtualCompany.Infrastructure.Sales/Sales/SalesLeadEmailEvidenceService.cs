using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesLeadEmailEvidenceService(
    VirtualCompanyDbContext dbContext,
    IMailboxProviderRegistry providerRegistry,
    IMailboxOAuthAccessTokenLeaseService tokenLeaseService,
    ILogger<SalesLeadEmailEvidenceService> logger) : ISalesLeadEmailEvidenceService
{
    public async Task<IReadOnlyList<SalesLeadSourceEmailResponse>> ListAsync(Guid companyId, Guid leadId, CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty || leadId == Guid.Empty) return [];

        var leadExists = await dbContext.Leads.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId && x.Id == leadId && !x.IsDeleted, cancellationToken);
        if (!leadExists) return [];

        var links = await dbContext.SalesEmailLinks.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.LeadId == leadId && !x.IsDeleted && x.LinkKind == SalesEmailLinkKinds.Message)
            .OrderByDescending(x => x.CreatedUtc)
            .Take(20)
            .ToListAsync(cancellationToken);
        var connectionIds = links.Where(x => x.MailboxConnectionId.HasValue).Select(x => x.MailboxConnectionId!.Value).Distinct().ToArray();
        var connections = await dbContext.MailboxConnections.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && connectionIds.Contains(x.Id) && x.Purpose == MailboxPurpose.Sales)
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var result = new List<SalesLeadSourceEmailResponse>(links.Count);
        foreach (var link in links)
        {
            if (link.MailboxConnectionId is not Guid connectionId || !connections.TryGetValue(connectionId, out var connection))
            {
                result.Add(Fallback(link, "The linked sales mailbox is no longer available."));
                continue;
            }

            try
            {
                var provider = providerRegistry.Resolve(connection.Provider);
                var lease = await tokenLeaseService.AcquireAsync(
                    companyId, connection.Id, provider.ReadRequiredScopes, cancellationToken);
                var message = await provider.GetMessageAsync(
                    lease.AccessToken,
                    new MailboxMessageFetchRequest(link.ExternalMessageId),
                    cancellationToken);
                result.Add(new SalesLeadSourceEmailResponse(
                    link.Id,
                    link.ExternalMessageId,
                    link.InternetMessageId ?? message.InternetMessageId,
                    message.Subject,
                    message.Sender.DisplayName,
                    message.Sender.Email,
                    message.Recipients.Select(x => x.Email).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    message.ReceivedUtc,
                    FirstNonEmpty(message.PlainTextBody, StripHtml(message.HtmlBody)),
                    link.DetectedIntent,
                    link.ProductOrServiceInterest,
                    link.Confidence,
                    link.Rationale,
                    null));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not retrieve source email for sales lead. CompanyId: {CompanyId}. LeadId: {LeadId}. LinkId: {LinkId}.", companyId, leadId, link.Id);
                result.Add(Fallback(link, "The source email content is temporarily unavailable."));
            }
        }

        return result;
    }

    private static SalesLeadSourceEmailResponse Fallback(SalesEmailLink link, string message) =>
        new(link.Id, link.ExternalMessageId, link.InternetMessageId, null, null, null, [], null, null, link.DetectedIntent,
            link.ProductOrServiceInterest, link.Confidence, link.Rationale, message);

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
    private static string? StripHtml(string? value) => string.IsNullOrWhiteSpace(value) ? null : Regex.Replace(value, "<.*?>", " ");
}
