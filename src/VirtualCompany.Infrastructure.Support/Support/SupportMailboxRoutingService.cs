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
using VirtualCompany.Infrastructure.Security;

namespace VirtualCompany.Infrastructure.Support;

public sealed class SupportMailboxRoutingService : ISupportMailboxRoutingService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ISupportMailboxIngestionService _ingestion;
    private readonly ISupportAgentOrchestrationService? _agentOrchestration;
    private readonly ILogger<SupportMailboxRoutingService>? _logger;

    public SupportMailboxRoutingService(VirtualCompanyDbContext dbContext, ISupportMailboxIngestionService ingestion)
    {
        _dbContext = dbContext;
        _ingestion = ingestion;
    }

    public SupportMailboxRoutingService(
        VirtualCompanyDbContext dbContext,
        ISupportMailboxIngestionService ingestion,
        ISupportAgentOrchestrationService agentOrchestration,
        ILogger<SupportMailboxRoutingService> logger)
    {
        _dbContext = dbContext;
        _ingestion = ingestion;
        _agentOrchestration = agentOrchestration;
        _logger = logger;
    }

    public async Task<SupportMailboxRoutingResult> RouteUnlinkedInboundMessagesAsync(DateTime sinceUtc, int batchSize, CancellationToken cancellationToken)
    {
        var supportConnectionIds = _dbContext.MailboxConnections.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Purpose == MailboxPurpose.Support)
            .Select(x => x.Id);
        var snapshots = await _dbContext.EmailMessageSnapshots.IgnoreQueryFilters().AsNoTracking()
            .Where(x => supportConnectionIds.Contains(x.MailboxConnectionId) &&
                x.FromAddress != null &&
                (x.ReceivedUtc ?? x.CreatedUtc) >= sinceUtc)
            .OrderBy(x => x.ReceivedUtc ?? x.CreatedUtc)
            .Take(Math.Clamp(batchSize, 1, 200))
            .ToListAsync(cancellationToken);
        var routed = 0;
        var created = 0;
        var duplicates = 0;
        foreach (var snapshot in snapshots)
        {
            var existingSupportMessage = await _dbContext.SupportMessages.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(x => x.CompanyId == snapshot.CompanyId && x.EmailMessageSnapshotId == snapshot.Id, cancellationToken);
            if (existingSupportMessage is not null)
            {
                duplicates++;
                await TryRunAgentAsync(snapshot.CompanyId, existingSupportMessage.SupportCaseId, existingSupportMessage.Id, cancellationToken);
                continue;
            }

            var result = await _ingestion.IngestMessageAsync(snapshot.CompanyId, new SupportMailboxMessageInput(
                snapshot.MailboxConnectionId,
                snapshot.Id,
                snapshot.FromAddress!,
                snapshot.FromDisplayName,
                null,
                snapshot.Subject ?? "Support request",
                snapshot.UntrustedBodyText ?? snapshot.Subject ?? "Support request",
                snapshot.ExternalMessageId,
                null,
                snapshot.ReceivedUtc ?? snapshot.CreatedUtc), cancellationToken);
            routed++;
            if (result.CreatedCase)
            {
                created++;
            }

            await TryRunAgentAsync(snapshot.CompanyId, result.SupportCaseId, result.SupportMessageId, cancellationToken);
        }

        return new SupportMailboxRoutingResult(snapshots.Count, routed, created, duplicates);
    }

    private async Task TryRunAgentAsync(Guid companyId, Guid supportCaseId, Guid supportMessageId, CancellationToken cancellationToken)
    {
        if (_agentOrchestration is null) return;
        try
        {
            await _agentOrchestration.RunAsync(
                companyId,
                Guid.Empty,
                supportCaseId,
                new RunSupportAgentRequest($"support-inbound:{supportMessageId:N}"),
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Message ingestion remains durable; the next polling pass retries the same idempotent execution.
            _logger?.LogError(
                ex,
                "Support agent drafting failed after mailbox routing. CompanyId: {CompanyId}, SupportCaseId: {SupportCaseId}, SupportMessageId: {SupportMessageId}.",
                companyId,
                supportCaseId,
                supportMessageId);
        }
    }
}
