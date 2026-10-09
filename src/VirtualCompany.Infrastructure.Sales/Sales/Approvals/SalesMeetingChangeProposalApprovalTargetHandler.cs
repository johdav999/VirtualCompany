using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesMeetingChangeProposalApprovalTargetHandler : IApprovalTargetHandler
{
    private readonly VirtualCompanyDbContext _dbContext;
    public SalesMeetingChangeProposalApprovalTargetHandler(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> ExistsAsync(Guid companyId, Guid targetEntityId, CancellationToken cancellationToken) => await _dbContext.SalesMeetingChangeProposals.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == targetEntityId, cancellationToken);
    public async Task<object?> GetReviewMaterialAsync(ApprovalRequest approval, CancellationToken ct)
    {
        object? material = null;
        var proposal = await _dbContext.SalesMeetingChangeProposals.AsNoTracking().SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, ct);
        material = new
        {
            proposal.TargetType,
            proposal.TargetId,
            proposal.Action,
            proposal.Field,
            proposal.ProposedValueJson,
            proposal.BeforeValueJson,
            proposal.TargetVersion,
            proposal.EvidenceVersionHash,
            proposal.SourceIdsJson,
            proposal.PolicyVersion
        };
        return material;
    }

    public async Task<ApprovalTargetStateTransition?> ApplyDecisionAsync(ApprovalRequest approval, CancellationToken cancellationToken)
    {
        if (approval.Status == ApprovalRequestStatus.Pending)
            return null;
        var proposal = await _dbContext.SalesMeetingChangeProposals.SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, cancellationToken);
        var binding = approval.ThresholdContext.TryGetValue("bindingHash", out var node) ? node?.GetValue<string>() : null;
        if (proposal.ApprovalRequestId != approval.Id || string.IsNullOrWhiteSpace(binding) || !string.Equals(binding, proposal.ApprovalBindingHash, StringComparison.Ordinal))
            return null;
        if (approval.Status == ApprovalRequestStatus.Approved)
        {
            var approver = approval.Steps.FirstOrDefault(x => x.DecidedByUserId.HasValue)?.DecidedByUserId;
            if (approver.HasValue)
                proposal.Approve(proposal.ConcurrencyVersion, binding, approver.Value, DateTime.UtcNow);
        }
        else if (approval.Status is ApprovalRequestStatus.Rejected or ApprovalRequestStatus.Expired or ApprovalRequestStatus.Cancelled or ApprovalRequestStatus.ChangesRequested or ApprovalRequestStatus.Stale or ApprovalRequestStatus.Superseded or ApprovalRequestStatus.Revoked)
        {
            var reviewer = approval.Steps.FirstOrDefault(x => x.DecidedByUserId.HasValue)?.DecidedByUserId ?? approval.RequestedByActorId;
            proposal.Reject(proposal.ConcurrencyVersion, reviewer, approval.DecisionSummary, DateTime.UtcNow);
        }

        return null;
        return null;
    }
}
