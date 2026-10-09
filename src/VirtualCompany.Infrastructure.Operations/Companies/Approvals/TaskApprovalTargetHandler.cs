using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Support;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class TaskApprovalTargetHandler : IApprovalTargetHandler
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IServiceProvider _serviceProvider;
    public TaskApprovalTargetHandler(VirtualCompanyDbContext dbContext, IServiceProvider serviceProvider)
    {
        _dbContext = dbContext;
        _serviceProvider = serviceProvider;
    }

    public async Task<bool> ExistsAsync(Guid companyId, Guid targetEntityId, CancellationToken cancellationToken) => await _dbContext.WorkTasks.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == targetEntityId, cancellationToken);
    public async Task ValidateCreationAsync(Guid companyId, CreateApprovalRequestCommand command, Guid actorUserId, CancellationToken cancellationToken)
    {
        var planningOrigin = await _dbContext.Set<DecisionWorkOrigin>().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.TaskId == command.TargetEntityId, cancellationToken);
        if (planningOrigin != null && (planningOrigin.ApprovalId.HasValue || command.ApprovalType != "planning_work_review" || command.RequestedByActorType != "user" || command.RequestedByActorId != actorUserId || command.RequiredRole != "owner" || command.RequiredUserId.HasValue || command.Steps?.Count > 0 || command.ThresholdContext?.GetValueOrDefault("sourceFingerprint")?.ToString() != planningOrigin.SourceFingerprint))
            throw new UnauthorizedAccessException("Owned planning work requires its canonical owner review with the retained source binding.");
    }

    public async Task BindCreatedAsync(ApprovalRequest approval, CancellationToken cancellationToken)
    {
        var planningOrigin = await _dbContext.Set<DecisionWorkOrigin>().SingleOrDefaultAsync(x => x.CompanyId == approval.CompanyId && x.TaskId == approval.TargetEntityId, cancellationToken);
        if (planningOrigin != null)
            planningOrigin.ApprovalId = approval.Id;
        var task = await _dbContext.WorkTasks.SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, cancellationToken);
        task.UpdateStatus(WorkTaskStatus.AwaitingApproval);
    }

    public async Task<object?> GetReviewMaterialAsync(ApprovalRequest approval, CancellationToken ct)
    {
        object? material = null;
        var task = await _dbContext.WorkTasks.AsNoTracking().SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, ct);
        var artifacts = await _dbContext.CollaborationContributions.AsNoTracking().Where(x => x.CompanyId == approval.CompanyId && (x.SourceTaskId == task.Id || _dbContext.WorkTasks.Any(t => t.CompanyId == approval.CompanyId && t.Id == x.ParentTaskId && t.ParentTaskId == task.Id))).OrderBy(x => x.Id).Select(x => new { x.Id, x.Version }).ToListAsync(ct);
        var artifactIds = artifacts.Select(x => x.Id).ToArray();
        var handoffs = await _dbContext.CollaborationArtifactHandoffs.AsNoTracking().Where(x => x.CompanyId == approval.CompanyId && artifactIds.Contains(x.ReceivingContributionId)).OrderBy(x => x.Id).Select(x => new { x.Id, x.InputContributionId, x.ReceivingContributionId, x.Passed, x.Reason }).ToListAsync(ct);
        var planningOrigin = await _dbContext.Set<DecisionWorkOrigin>().AsNoTracking().Where(x => x.CompanyId == approval.CompanyId && x.TaskId == task.Id).Select(x => new { x.OwnerUserId, x.DueUtc, x.Objective, x.AcceptanceOutcome, x.ProposedConstraints, x.SourceKind, x.SourceVersionId, x.SourceVersion, x.SourceFingerprint, x.PreviewChecksum }).SingleOrDefaultAsync(ct);
        material = new
        {
            task.Title,
            task.Description,
            task.Type,
            task.AssignedAgentId,
            task.InputPayload,
            task.OutputPayload,
            artifacts,
            handoffs
        };
        if (planningOrigin != null)
            material = new
            {
                task.Title,
                task.Description,
                task.Type,
                task.AssignedAgentId,
                task.InputPayload,
                task.OutputPayload,
                artifacts,
                handoffs,
                planningOrigin
            };
        return material;
    }

    public async Task<ApprovalTargetReviewDetails> GetReviewDetailsAsync(ApprovalRequest approval, CancellationToken ct)
    {
        var comparisons = new List<ApprovalComparisonDto>();
        var evidence = new List<ApprovalEvidenceDto>();
        string? executionStatus = null;
        executionStatus = (await _dbContext.WorkTasks.Where(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId).Select(x => x.Status).SingleAsync(ct)).ToStorageValue().Replace('_', ' ');
        evidence.Add(new("Open proposed work and source evidence", $"/work?companyId={approval.CompanyId}&tab=tasks&taskId={approval.TargetEntityId}"));
        if (approval.ApprovalType == "planning_work_review")
        {
            var origin = await _dbContext.Set<DecisionWorkOrigin>().AsNoTracking().SingleAsync(x => x.CompanyId == approval.CompanyId && x.TaskId == approval.TargetEntityId, ct);
            comparisons.Add(new("Objective", null, origin.Objective));
            comparisons.Add(new("Accountable owner", null, await _dbContext.Users.Where(x => x.Id == origin.OwnerUserId).Select(x => x.DisplayName).SingleAsync(ct)));
            comparisons.Add(new("Due date (UTC)", null, origin.DueUtc.ToString("yyyy-MM-dd HH:mm")));
            comparisons.Add(new("Acceptance outcome", null, origin.AcceptanceOutcome));
            comparisons.Add(new("Proposed constraints", null, origin.ProposedConstraints));
            var people = await _dbContext.Set<DecisionWorkCollaborator>().Where(x => x.CompanyId == approval.CompanyId && x.OriginId == origin.Id).OrderBy(x => x.Name).Select(x => x.Name).ToListAsync(ct);
            comparisons.Add(new("Proposed collaborators", null, people.Count == 0 ? "None proposed" : string.Join(", ", people)));
            comparisons.Add(new("Retained source version", null, $"Version {origin.SourceVersion}"));
            evidence.Add(new("Open retained source decision and work snapshot", $"/work/source?companyId={approval.CompanyId}&taskId={approval.TargetEntityId}"));
        }

        var artifacts = await _dbContext.CollaborationContributions.AsNoTracking().Where(x => x.CompanyId == approval.CompanyId && (x.SourceTaskId == approval.TargetEntityId || _dbContext.WorkTasks.Any(t => t.CompanyId == approval.CompanyId && t.Id == x.ParentTaskId && t.ParentTaskId == approval.TargetEntityId))).OrderBy(x => x.Sequence).ThenBy(x => x.Version).Take(50).ToListAsync(ct);
        evidence.AddRange(artifacts.Select(x => new ApprovalEvidenceDto($"{x.Objective}, version {x.Version}", $"/agents/work/task/{approval.TargetEntityId}/collaboration?companyId={approval.CompanyId}&artifactId={x.Id}&view=list")));
        return new(comparisons, evidence, executionStatus);
    }

    public async Task<ApprovalTargetStateTransition?> ApplyDecisionAsync(ApprovalRequest approval, CancellationToken cancellationToken)
    {
        if (approval.Status == ApprovalRequestStatus.Pending)
            return null;
        var task = await _dbContext.WorkTasks.SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, cancellationToken);
        var previousStatus = task.Status.ToStorageValue();
        if (approval.Status == ApprovalRequestStatus.Approved)
        {
            var approvalCompletesTask = string.Equals(approval.ApprovalType, SupplierPaymentProposalApprovalType, StringComparison.OrdinalIgnoreCase) && string.Equals(task.Type, SupplierPaymentProposalTaskType, StringComparison.OrdinalIgnoreCase);
            task.UpdateStatus(approvalCompletesTask ? WorkTaskStatus.Completed : WorkTaskStatus.InProgress);
            await UpdateSupplierPaymentProposalAfterTaskApprovalAsync(approval, task, approved: true, cancellationToken);
            await UpdateSupportRefundAfterTaskApprovalAsync(approval, task, cancellationToken);
            return ApprovalTargetStateTransition.ForTask(task.Id, previousStatus, task.Status.ToStorageValue());
        }

        if (approval.Status is ApprovalRequestStatus.Rejected or ApprovalRequestStatus.Expired or ApprovalRequestStatus.ChangesRequested or ApprovalRequestStatus.Stale or ApprovalRequestStatus.Superseded or ApprovalRequestStatus.Revoked)
        {
            task.UpdateStatus(WorkTaskStatus.Blocked, rationaleSummary: approval.DecisionSummary);
            await UpdateSupplierPaymentProposalAfterTaskApprovalAsync(approval, task, approved: false, cancellationToken);
            await UpdateSupportRefundAfterTaskApprovalAsync(approval, task, cancellationToken);
            return ApprovalTargetStateTransition.ForTask(task.Id, previousStatus, task.Status.ToStorageValue());
        }

        if (approval.Status == ApprovalRequestStatus.Cancelled)
        {
            task.UpdateStatus(WorkTaskStatus.Blocked, rationaleSummary: approval.DecisionSummary);
            await UpdateSupplierPaymentProposalAfterTaskApprovalAsync(approval, task, approved: false, cancellationToken);
            await UpdateSupportRefundAfterTaskApprovalAsync(approval, task, cancellationToken);
            return ApprovalTargetStateTransition.ForTask(task.Id, previousStatus, task.Status.ToStorageValue());
        }

        return null;
    }

    private async Task UpdateSupplierPaymentProposalAfterTaskApprovalAsync(ApprovalRequest approval, WorkTask task, bool approved, CancellationToken cancellationToken)
    {
        if (!task.InputPayload.TryGetValue("paymentProposalId", out var value) || value is null || !Guid.TryParse(value.GetValue<string>(), out var proposalId))
        {
            return;
        }

        var proposal = await _dbContext.SupplierInvoicePaymentProposals.SingleOrDefaultAsync(x => x.CompanyId == approval.CompanyId && x.Id == proposalId, cancellationToken);
        if (proposal is null)
        {
            return;
        }

        var decidedBy = approval.Steps.FirstOrDefault(step => step.DecidedByUserId.HasValue)?.DecidedByUserId;
        var decidedUtc = approval.DecidedUtc ?? DateTime.UtcNow;
        if (approved)
        {
            proposal.MarkReadyForPayment(decidedBy, decidedUtc, approval.DecisionSummary);
        }
        else
        {
            proposal.MarkRejected(decidedBy, decidedUtc, approval.DecisionSummary);
        }
    }

    private async Task UpdateSupportRefundAfterTaskApprovalAsync(ApprovalRequest approval, WorkTask task, CancellationToken cancellationToken)
    {
        if (!string.Equals(approval.ApprovalType, "support_refund_credit", StringComparison.OrdinalIgnoreCase) || !task.InputPayload.ContainsKey("refundRequestId"))
        {
            return;
        }

        var handler = _serviceProvider.GetRequiredService<ISupportRefundApprovalOutcomeHandler>();
        var decidedBy = approval.Steps.FirstOrDefault(step => step.DecidedByUserId.HasValue)?.DecidedByUserId;
        await handler.ProcessAsync(approval.CompanyId, approval.Id, approval.Status.ToStorageValue(), decidedBy, approval.DecisionSummary, cancellationToken);
    }

    private const string SupplierPaymentProposalApprovalType = "supplier_invoice_payment_proposal";
    private const string SupplierPaymentProposalTaskType = "finance.supplier_invoice_payment_proposal";
}
