using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class WorkflowApprovalTargetHandler : IApprovalTargetHandler
{
    private readonly VirtualCompanyDbContext _dbContext;
    public WorkflowApprovalTargetHandler(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> ExistsAsync(Guid companyId, Guid targetEntityId, CancellationToken cancellationToken) => await _dbContext.WorkflowInstances.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == targetEntityId, cancellationToken);
    public async Task BindCreatedAsync(ApprovalRequest approval, CancellationToken cancellationToken)
    {
        var workflow = await _dbContext.WorkflowInstances.SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, cancellationToken);
        workflow.UpdateState(WorkflowInstanceStatus.Blocked, workflow.CurrentStep);
    }

    public async Task<object?> GetReviewMaterialAsync(ApprovalRequest approval, CancellationToken ct)
    {
        object? material = null;
        var workflow = await _dbContext.WorkflowInstances.AsNoTracking().SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, ct);
        material = new
        {
            workflow.DefinitionId,
            workflow.InputPayload,
            workflow.ContextJson
        };
        return material;
    }

    public async Task<ApprovalTargetStateTransition?> ApplyDecisionAsync(ApprovalRequest approval, CancellationToken cancellationToken)
    {
        if (approval.Status == ApprovalRequestStatus.Pending)
            return null;
        var workflow = await _dbContext.WorkflowInstances.SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, cancellationToken);
        var previousStatus = workflow.State.ToStorageValue();
        if (approval.Status == ApprovalRequestStatus.Approved)
        {
            workflow.UpdateState(WorkflowInstanceStatus.Running, workflow.CurrentStep);
            return ApprovalTargetStateTransition.ForWorkflow(workflow.Id, previousStatus, workflow.State.ToStorageValue());
        }

        if (approval.Status is ApprovalRequestStatus.Rejected or ApprovalRequestStatus.Expired or ApprovalRequestStatus.ChangesRequested or ApprovalRequestStatus.Stale or ApprovalRequestStatus.Superseded or ApprovalRequestStatus.Revoked)
        {
            workflow.UpdateState(WorkflowInstanceStatus.Failed, workflow.CurrentStep);
            return ApprovalTargetStateTransition.ForWorkflow(workflow.Id, previousStatus, workflow.State.ToStorageValue());
        }

        if (approval.Status == ApprovalRequestStatus.Cancelled)
        {
            workflow.UpdateState(WorkflowInstanceStatus.Cancelled, workflow.CurrentStep);
            return ApprovalTargetStateTransition.ForWorkflow(workflow.Id, previousStatus, workflow.State.ToStorageValue());
        }

        return null;
    }
}
