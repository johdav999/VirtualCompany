using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Companies;

public sealed partial class CompanyTaskService
{
    private async Task InvalidateTaskReviewsAsync(WorkTask changedTask, CancellationToken ct)
    {
        var membership = await RequireMembershipAsync(changedTask.CompanyId, ct);
        var affected = new List<Guid> { changedTask.Id };
        if (changedTask.ParentTaskId is Guid parent)
        {
            affected.Add(parent);
            var root = await _dbContext.WorkTasks.Where(x => x.CompanyId == changedTask.CompanyId && x.Id == parent)
                .Select(x => x.ParentTaskId).SingleOrDefaultAsync(ct);
            if (root is Guid rootId) affected.Add(rootId);
        }
        var approvals = await _dbContext.ApprovalRequests.Include(x => x.Steps).Where(x => x.CompanyId == changedTask.CompanyId &&
            x.TargetEntityType == "task" && affected.Contains(x.TargetEntityId) &&
            (x.Status == ApprovalRequestStatus.Pending || x.Status == ApprovalRequestStatus.Approved)).ToListAsync(ct);
        foreach (var approval in approvals)
        {
            approval.MarkStale("Proposal material or ownership changed. Create and review a new request.");
            var target = await _dbContext.WorkTasks.SingleAsync(x => x.CompanyId == changedTask.CompanyId && x.Id == approval.TargetEntityId, ct);
            target.UpdateStatus(WorkTaskStatus.Blocked, rationaleSummary: approval.DecisionSummary);
            await _auditEventWriter.WriteAsync(new AuditEventWriteRequest(changedTask.CompanyId, "user", membership.UserId,
                "approval.review.proposal_changed", AuditTargetTypes.ApprovalRequest, approval.Id.ToString("N"), "stale",
                RationaleSummary: approval.DecisionSummary, DataSources: ["tasks", "approvals"],
                Metadata: new Dictionary<string, string?> { ["approvalRequestId"] = approval.Id.ToString("N"),
                    ["targetEntityId"] = approval.TargetEntityId.ToString("N"), ["changedTaskId"] = changedTask.Id.ToString("N") }), ct);
        }
    }
}
