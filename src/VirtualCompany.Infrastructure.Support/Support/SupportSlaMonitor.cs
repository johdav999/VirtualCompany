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

public sealed class SupportSlaMonitor : ISupportSlaMonitor
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ILogger<SupportSlaMonitor> _logger;
    private readonly ISupportSlaPolicyService? _slaPolicies;
    private readonly ICompanyOutboxEnqueuer? _outbox;

    public SupportSlaMonitor(VirtualCompanyDbContext dbContext, ILogger<SupportSlaMonitor> logger, ISupportSlaPolicyService? slaPolicies = null, ICompanyOutboxEnqueuer? outbox = null)
    {
        _dbContext = dbContext;
        _logger = logger;
        _slaPolicies = slaPolicies;
        _outbox = outbox;
    }

    public async Task<SupportSlaMonitorResult> RunAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var cases = await _dbContext.SupportCases.IgnoreQueryFilters().Include(x => x.Events)
            .Where(x => x.Status != SupportCaseStatuses.Closed && x.Status != SupportCaseStatuses.Resolved)
            .ToListAsync(cancellationToken);
        var risks = 0;
        var breaches = 0;
        var notifications = 0;
        foreach (var supportCase in cases)
        {
            var riskThresholdMinutes = 240;
            var recipientRole = "support_supervisor";
            SupportSlaResolutionDto? appliedPolicy = null;
            if (_slaPolicies is not null)
            {
                appliedPolicy = await _slaPolicies.ResolveAsync(supportCase.CompanyId, supportCase.Category, supportCase.Priority, null, supportCase.CreatedUtc, cancellationToken);
                riskThresholdMinutes = appliedPolicy.RiskThresholdMinutes;
                recipientRole = appliedPolicy.EscalationRecipientRole;
            }
            if (supportCase.FirstResponseDueUtc is null || supportCase.ResolutionDueUtc is null)
            {
                if (appliedPolicy is not null)
                {
                    supportCase.SetSla(appliedPolicy.FirstResponseDueUtc, appliedPolicy.ResolutionDueUtc);
                }
                else
                {
                    var first = supportCase.CreatedUtc.AddHours(supportCase.Priority == SupportPriorities.High ? 2 : 8);
                    var resolution = supportCase.CreatedUtc.AddHours(supportCase.Priority == SupportPriorities.High ? 24 : 72);
                    supportCase.SetSla(first, resolution);
                }
            }

            var previousRisk = supportCase.IsSlaRisk;
            var previousBreach = supportCase.IsSlaBreached;
            var breached = (supportCase.FirstResponseSentUtc is null && supportCase.FirstResponseDueUtc < nowUtc) || supportCase.ResolutionDueUtc < nowUtc;
            var risk = !breached && supportCase.ResolutionDueUtc <= nowUtc.AddMinutes(riskThresholdMinutes);
            if (breached && !supportCase.IsSlaBreached)
            {
                breaches++;
                _dbContext.SupportCaseEvents.Add(new SupportCaseEvent(Guid.NewGuid(), supportCase.CompanyId, supportCase.Id, SupportCaseEventTypes.SlaBreached, "Support SLA breached.", AuditActorTypes.System, null, nowUtc));
            }
            else if (risk && !supportCase.IsSlaRisk)
            {
                risks++;
                _dbContext.SupportCaseEvents.Add(new SupportCaseEvent(Guid.NewGuid(), supportCase.CompanyId, supportCase.Id, SupportCaseEventTypes.SlaRisk, "Support SLA is at risk.", AuditActorTypes.System, null, nowUtc));
            }
            if (_outbox is not null && (breached != previousBreach || risk != previousRisk))
            {
                var transition = breached ? "breached" : risk ? "risk" : "recovered";
                var priority = breached ? CompanyNotificationPriority.Critical : CompanyNotificationPriority.High;
                var title = breached ? $"Support case {supportCase.CaseNumber} breached its target" : risk ? $"Support case {supportCase.CaseNumber} is at risk" : $"Support case {supportCase.CaseNumber} SLA recovered";
                var dueVersion = supportCase.ResolutionDueUtc?.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none";
                var dedupe = $"support-sla:{supportCase.Id:N}:{transition}:{dueVersion}";
                _outbox.Enqueue(supportCase.CompanyId, CompanyOutboxTopics.NotificationDeliveryRequested,
                    new NotificationDeliveryRequestedMessage(supportCase.CompanyId, CompanyNotificationType.Escalation.ToStorageValue(), priority.ToStorageValue(), title, $"Open {supportCase.CaseNumber} to review its response and resolution target.", "support_case", supportCase.Id, $"/support/cases/{supportCase.Id:D}", supportCase.AssignedUserId, supportCase.AssignedUserId.HasValue ? null : recipientRole, null, null, dedupe, null),
                    idempotencyKey: $"notification:{dedupe}", causationId: supportCase.Id.ToString("N"));
                notifications++;
            }
            supportCase.MarkSlaState(risk, breached);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Support SLA monitor scanned {Count} cases, created {Risks} risks and {Breaches} breaches.", cases.Count, risks, breaches);
        return new SupportSlaMonitorResult(cases.Count, risks, breaches, notifications);
    }
}
