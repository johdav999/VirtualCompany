using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class OperatingPlanApprovalTargetHandler : IApprovalTargetHandler
{
    private readonly VirtualCompanyDbContext _dbContext;
    public OperatingPlanApprovalTargetHandler(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> ExistsAsync(Guid companyId, Guid targetEntityId, CancellationToken cancellationToken) => await _dbContext.OperatingPlans.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == targetEntityId, cancellationToken);
    public async Task<ApprovalTargetStateTransition?> ApplyDecisionAsync(ApprovalRequest approval, CancellationToken cancellationToken)
    {
        if (approval.Status == ApprovalRequestStatus.Pending)
            return null;
        var plan = await _dbContext.OperatingPlans.SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, cancellationToken);
        var previousStatus = plan.Status.ToStorageValue();
        if (approval.Status == ApprovalRequestStatus.Approved)
        {
            if (plan.Status == OperatingPlanStatus.AwaitingReview)
                plan.Approve();
            return ApprovalTargetStateTransition.ForOperatingPlan(plan.Id, previousStatus, plan.Status.ToStorageValue());
        }

        if (approval.Status is ApprovalRequestStatus.Rejected or ApprovalRequestStatus.Expired or ApprovalRequestStatus.Cancelled or ApprovalRequestStatus.ChangesRequested or ApprovalRequestStatus.Stale or ApprovalRequestStatus.Superseded or ApprovalRequestStatus.Revoked)
        {
            if (plan.Status == OperatingPlanStatus.AwaitingReview)
                plan.Reject();
            return ApprovalTargetStateTransition.ForOperatingPlan(plan.Id, previousStatus, plan.Status.ToStorageValue());
        }

        return null;
    }
}
