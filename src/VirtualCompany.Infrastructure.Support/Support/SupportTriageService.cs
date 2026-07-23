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

public sealed class SupportTriageService : ISupportTriageService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAuditEventWriter _audit;

    public SupportTriageService(VirtualCompanyDbContext dbContext, IAuditEventWriter audit)
    {
        _dbContext = dbContext;
        _audit = audit;
    }

    public async Task<SupportTriageResult?> TriageAsync(Guid companyId, Guid userId, Guid supportCaseId, CancellationToken cancellationToken)
    {
        var supportCase = await _dbContext.SupportCases.Include(x => x.Messages).Include(x => x.Events).FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == supportCaseId, cancellationToken);
        if (supportCase is null) return null;
        var text = $"{supportCase.Subject} {supportCase.Description} {string.Join(' ', supportCase.Messages.Select(x => x.Body))}".ToLowerInvariant();
        var category = text.Contains("refund") || text.Contains("credit") ? SupportCaseCategories.Refund :
            text.Contains("invoice") || text.Contains("payment") || text.Contains("billing") ? SupportCaseCategories.Billing :
            text.Contains("bug") || text.Contains("crash") || text.Contains("error") ? SupportCaseCategories.BugReport :
            text.Contains("cancel") || text.Contains("churn") ? SupportCaseCategories.ChurnRisk :
            text.Contains("angry") || text.Contains("complaint") || text.Contains("unacceptable") ? SupportCaseCategories.Complaint :
            text.Contains("login") || text.Contains("password") ? SupportCaseCategories.AccountAccess :
            SupportCaseCategories.GeneralQuestion;
        var urgent = text.Contains("urgent") || text.Contains("asap") || text.Contains("immediately");
        var negative = text.Contains("angry") || text.Contains("unhappy") || text.Contains("complaint") || text.Contains("cancel");
        var priority = urgent || category == SupportCaseCategories.ChurnRisk || category == SupportCaseCategories.Complaint ? SupportPriorities.High : SupportPriorities.Normal;
        var confidence = category == SupportCaseCategories.GeneralQuestion ? 0.55m : 0.82m;
        var suggested = category switch
        {
            SupportCaseCategories.Billing => "Review invoice and payment context, then send a source-backed explanation.",
            SupportCaseCategories.Refund => "Validate refund policy and prepare an approval request if money movement is needed.",
            SupportCaseCategories.BugReport => "Collect reproduction details and create an internal task.",
            SupportCaseCategories.ChurnRisk => "Escalate to customer success or sales with retention context.",
            _ => "Draft a helpful response from approved knowledge."
        };
        supportCase.SetTriage(category, priority, negative ? "Negative" : "Neutral", confidence, suggested, "Support case triaged from message content and linked context.", false, category == SupportCaseCategories.ChurnRisk, urgent);
        _dbContext.SupportCaseEvents.Add(new SupportCaseEvent(Guid.NewGuid(), companyId, supportCase.Id, SupportCaseEventTypes.Triaged, "Support case triaged.", userId == Guid.Empty ? AuditActorTypes.System : AuditActorTypes.Human, userId == Guid.Empty ? null : userId, DateTime.UtcNow));
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, userId == Guid.Empty ? AuditActorTypes.System : AuditActorTypes.Human, userId == Guid.Empty ? null : userId, "support.case.triaged", "support_case", supportCase.Id.ToString("D"), AuditEventOutcomes.Succeeded, supportCase.RationaleSummary, ["support"]), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new SupportTriageResult(supportCase.Id, category, priority, supportCase.Sentiment ?? "Neutral", confidence, suggested, supportCase.RationaleSummary ?? string.Empty, false, supportCase.IsChurnRisk, supportCase.IsSlaRisk);
    }
}
