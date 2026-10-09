using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesMeetingChangeRequestApprovalTargetHandler : IApprovalTargetHandler
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyOutboxEnqueuer _outboxEnqueuer;
    public SalesMeetingChangeRequestApprovalTargetHandler(VirtualCompanyDbContext dbContext, ICompanyOutboxEnqueuer outboxEnqueuer)
    {
        _dbContext = dbContext;
        _outboxEnqueuer = outboxEnqueuer;
    }

    public async Task<bool> ExistsAsync(Guid companyId, Guid targetEntityId, CancellationToken cancellationToken) => await _dbContext.SalesMeetingChangeRequests.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == targetEntityId, cancellationToken);
    public async Task<ApprovalTargetStateTransition?> ApplyDecisionAsync(ApprovalRequest approval, CancellationToken cancellationToken)
    {
        if (approval.Status == ApprovalRequestStatus.Pending)
            return null;
        var change = await _dbContext.SalesMeetingChangeRequests.SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, cancellationToken);
        var previousStatus = change.Status.ToStorageValue();
        if (approval.Status == ApprovalRequestStatus.Approved)
        {
            var approver = approval.Steps.FirstOrDefault(x => x.DecidedByUserId.HasValue)?.DecidedByUserId;
            change.MarkApproved(approver, DateTime.UtcNow);
            _outboxEnqueuer.Enqueue(approval.CompanyId, CompanyOutboxTopics.SalesMeetingChangeDeliveryRequested, new SalesMeetingChangeDeliveryRequestedMessage(approval.CompanyId, change.Id, change.IdempotencyKey, approval.Id.ToString("N")), correlationId: approval.Id.ToString("N"), idempotencyKey: $"sales-meeting-change-delivery:{approval.CompanyId:N}:{change.Id:N}:v1", causationId: approval.Id.ToString("N"));
            return ApprovalTargetStateTransition.ForSalesMeetingChangeRequest(change.Id, previousStatus, change.Status.ToStorageValue());
        }

        if (approval.Status is ApprovalRequestStatus.Rejected or ApprovalRequestStatus.Expired or ApprovalRequestStatus.Cancelled or ApprovalRequestStatus.ChangesRequested or ApprovalRequestStatus.Stale or ApprovalRequestStatus.Superseded or ApprovalRequestStatus.Revoked)
        {
            change.MarkRejected(DateTime.UtcNow);
            return ApprovalTargetStateTransition.ForSalesMeetingChangeRequest(change.Id, previousStatus, change.Status.ToStorageValue());
        }

        return null;
    }
}
