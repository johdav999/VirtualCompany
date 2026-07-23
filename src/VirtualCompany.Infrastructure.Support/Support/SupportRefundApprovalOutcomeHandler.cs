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

public sealed class SupportRefundApprovalOutcomeHandler : ISupportRefundApprovalOutcomeHandler
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAuditEventWriter _audit;
    private readonly ISupportRefundFinanceService _finance;

    public SupportRefundApprovalOutcomeHandler(VirtualCompanyDbContext dbContext, IAuditEventWriter audit, ISupportRefundFinanceService finance)
    {
        _dbContext = dbContext;
        _audit = audit;
        _finance = finance;
    }

    public async Task<bool> ProcessAsync(
        Guid companyId,
        Guid approvalRequestId,
        string approvalStatus,
        Guid? decidedByUserId,
        string? decisionSummary,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty || approvalRequestId == Guid.Empty)
        {
            throw new ArgumentException("Company and approval identifiers are required.");
        }

        var refund = await _dbContext.SupportRefundRequests
            .Include(x => x.SupportCase)
            .ThenInclude(x => x.Events)
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId && x.ApprovalRequestId == approvalRequestId,
                cancellationToken);
        if (refund is null)
        {
            return false;
        }

        if (!refund.ApplyApprovalOutcome(approvalStatus))
        {
            return false;
        }

        var isApproved = string.Equals(refund.Status, SupportRefundRequestStatuses.Approved, StringComparison.OrdinalIgnoreCase);
        var summary = string.IsNullOrWhiteSpace(decisionSummary)
            ? isApproved
                ? "Refund or credit approved and ready for finance validation."
                : $"Refund or credit approval ended as {SupportLabels.Status(refund.Status).ToLowerInvariant()}."
            : decisionSummary.Trim();
        _dbContext.SupportCaseEvents.Add(new SupportCaseEvent(
            Guid.NewGuid(),
            companyId,
            refund.SupportCaseId,
            SupportCaseEventTypes.ApprovalResolved,
            summary,
            decidedByUserId.HasValue ? AuditActorTypes.Human : AuditActorTypes.System,
            decidedByUserId,
            DateTime.UtcNow));

        if (!isApproved && string.Equals(refund.SupportCase.Status, SupportCaseStatuses.AwaitingApproval, StringComparison.OrdinalIgnoreCase))
        {
            refund.SupportCase.SetStatus(SupportCaseStatuses.WaitingInternal);
        }

        await _audit.WriteAsync(new AuditEventWriteRequest(
            companyId,
            decidedByUserId.HasValue ? AuditActorTypes.Human : AuditActorTypes.System,
            decidedByUserId,
            "support.refund.approval_resolved",
            "support_refund_request",
            refund.Id.ToString("D"),
            isApproved ? AuditEventOutcomes.Approved : AuditEventOutcomes.Rejected,
            summary,
            ["support", "finance", "approvals"],
            Metadata: new Dictionary<string, string?>
            {
                ["approvalRequestId"] = approvalRequestId.ToString("D"),
                ["refundRequestId"] = refund.Id.ToString("D"),
                ["status"] = refund.Status
            }), cancellationToken);

        if (isApproved)
        {
            await _finance.CreateApprovedActionAsync(companyId, refund.Id, cancellationToken);
        }

        return true;
    }
}
