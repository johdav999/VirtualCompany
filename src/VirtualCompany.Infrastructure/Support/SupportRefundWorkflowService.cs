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

public sealed class SupportRefundWorkflowService : ISupportRefundWorkflowService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAuditEventWriter _audit;

    public SupportRefundWorkflowService(VirtualCompanyDbContext dbContext, IAuditEventWriter audit)
    {
        _dbContext = dbContext;
        _audit = audit;
    }

    public async Task<SupportRefundRequestDto?> RequestRefundAsync(Guid companyId, Guid userId, Guid supportCaseId, CreateSupportRefundRequest request, CancellationToken cancellationToken)
    {
        var supportCase = await _dbContext.SupportCases.Include(x => x.Events).FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == supportCaseId, cancellationToken);
        if (supportCase is null) return null;

        var actorType = userId == Guid.Empty ? AuditActorTypes.Agent : AuditActorTypes.Human;
        var actorId = userId == Guid.Empty ? supportCase.Id : userId;
        var refund = new SupportRefundRequest(Guid.NewGuid(), companyId, supportCase.Id, request.Amount, request.Currency, request.ReasonCode, request.Explanation, request.InvoiceId ?? supportCase.RelatedInvoiceId, request.PaymentId ?? supportCase.RelatedPaymentId, null, userId == Guid.Empty ? null : userId);
        var approvalTask = new WorkTask(
            Guid.NewGuid(),
            companyId,
            "support_refund_approval",
            $"Approve support refund or credit for {supportCase.CaseNumber}",
            request.Explanation,
            request.Amount >= 5000m ? WorkTaskPriority.High : WorkTaskPriority.Normal,
            null,
            null,
            actorType,
            actorId,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["supportCaseId"] = supportCase.Id.ToString("D"),
                ["refundRequestId"] = refund.Id.ToString("D"),
                ["amount"] = request.Amount,
                ["currency"] = request.Currency,
                ["reasonCode"] = request.ReasonCode,
                ["invoiceId"] = refund.InvoiceId?.ToString("D"),
                ["paymentId"] = refund.PaymentId?.ToString("D")
            },
            sourceType: WorkTaskSourceTypes.Agent,
            triggerSource: "support_refund_request",
            creationReason: "Support refund or credit requires approval before finance action.");
        approvalTask.UpdateStatus(WorkTaskStatus.AwaitingApproval, rationaleSummary: "Waiting for refund or credit approval.", confidenceScore: 0.9m);
        _dbContext.SupportRefundRequests.Add(refund);
        _dbContext.WorkTasks.Add(approvalTask);

        var approval = ApprovalRequest.CreateForTarget(
            Guid.NewGuid(),
            companyId,
            ApprovalTargetEntityType.Task,
            approvalTask.Id,
            actorType,
            actorId,
            "support_refund_credit",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["supportCaseId"] = supportCase.Id.ToString("D"),
                ["supportCaseNumber"] = supportCase.CaseNumber,
                ["refundRequestId"] = refund.Id.ToString("D"),
                ["amount"] = request.Amount,
                ["currency"] = request.Currency,
                ["reasonCode"] = request.ReasonCode,
                ["explanation"] = request.Explanation
            },
            null,
            null,
            [new ApprovalStepDefinition(1, ApprovalStepApproverType.Role, "owner")]);
        _dbContext.ApprovalRequests.Add(approval);
        refund.LinkApproval(approval.Id);
        supportCase.SetStatus(SupportCaseStatuses.AwaitingApproval);
        _dbContext.SupportCaseEvents.Add(new SupportCaseEvent(Guid.NewGuid(), companyId, supportCase.Id, SupportCaseEventTypes.ApprovalRequested, "Refund or credit approval requested.", actorType, actorId, DateTime.UtcNow));
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, actorType, actorId, "support.refund.requested", "support_case", supportCase.Id.ToString("D"), AuditEventOutcomes.Pending, request.Explanation, ["support", "finance", "approvals"], Metadata: new Dictionary<string, string?> { ["approvalRequestId"] = approval.Id.ToString("D"), ["approvalTaskId"] = approvalTask.Id.ToString("D"), ["refundRequestId"] = refund.Id.ToString("D") }), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return SupportCaseService.MapRefund(refund);
    }
}
