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

public sealed class SupportMailboxIngestionService : ISupportMailboxIngestionService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ISupportCaseService _cases;
    private readonly ISupportTriageService _triage;

    public SupportMailboxIngestionService(VirtualCompanyDbContext dbContext, ISupportCaseService cases, ISupportTriageService triage)
    {
        _dbContext = dbContext;
        _cases = cases;
        _triage = triage;
    }

    public async Task<SupportMailboxIngestionResult> IngestMessageAsync(Guid companyId, SupportMailboxMessageInput input, CancellationToken cancellationToken)
    {
        var existingMessage = await _dbContext.SupportMessages.AsNoTracking().FirstOrDefaultAsync(x =>
            x.CompanyId == companyId &&
            input.ProviderMessageId != null &&
            x.ProviderMessageId == input.ProviderMessageId, cancellationToken);
        if (existingMessage is not null)
        {
            return new SupportMailboxIngestionResult(existingMessage.SupportCaseId, existingMessage.Id, false, true);
        }

        var supportCase = await FindMatchingCaseAsync(companyId, input, cancellationToken);
        var created = false;
        if (supportCase is null)
        {
            // Mailbox ingestion persists the provider-backed message below. Do
            // not ask the manual case-creation path to synthesize a second copy.
            var createdDetail = await _cases.CreateCaseAsync(
                companyId,
                Guid.Empty,
                new CreateSupportCaseRequest(input.Subject, input.Body, "Email"),
                cancellationToken);
            supportCase = await _dbContext.SupportCases.FirstAsync(x => x.Id == createdDetail.Id, cancellationToken);
            created = true;
        }

        var message = new SupportMessage(Guid.NewGuid(), companyId, supportCase.Id, SupportMessageDirections.Inbound, "email", input.SenderEmail, input.RecipientEmail, input.Body, input.OccurredUtc, input.EmailMessageSnapshotId, input.ProviderMessageId, input.ProviderThreadId);
        _dbContext.SupportMessages.Add(message);
        supportCase.MarkCustomerMessage(input.OccurredUtc);
        supportCase.LinkProviderMessage(input.ProviderThreadId, input.ProviderMessageId);
        _dbContext.SupportCaseEvents.Add(new SupportCaseEvent(Guid.NewGuid(), companyId, supportCase.Id, SupportCaseEventTypes.MessageReceived, "Customer message received.", AuditActorTypes.System, null, input.OccurredUtc));
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _triage.TriageAsync(companyId, Guid.Empty, supportCase.Id, cancellationToken);
        return new SupportMailboxIngestionResult(supportCase.Id, message.Id, created, false);
    }

    private async Task<SupportCase?> FindMatchingCaseAsync(Guid companyId, SupportMailboxMessageInput input, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(input.Subject))
        {
            var match = System.Text.RegularExpressions.Regex.Match(input.Subject, "SUP-[0-9]{8}-[0-9]{4}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var byNumber = await _dbContext.SupportCases.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.CaseNumber == match.Value, cancellationToken);
                if (byNumber is not null) return byNumber;
            }
        }

        if (!string.IsNullOrWhiteSpace(input.ProviderThreadId))
        {
            var byThread = await _dbContext.SupportCases.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.ProviderThreadId == input.ProviderThreadId && x.Status != SupportCaseStatuses.Closed, cancellationToken);
            if (byThread is not null) return byThread;
        }

        // Without an explicit case number or provider thread, this is a new
        // conversation. Sender identity and recency alone are not sufficient
        // evidence to merge unrelated customer requests into an existing case.
        return null;
    }
}
