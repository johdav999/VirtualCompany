using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesMeetingInvitationApprovalTargetHandler : IApprovalTargetHandler
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyOutboxEnqueuer _outboxEnqueuer;
    public SalesMeetingInvitationApprovalTargetHandler(VirtualCompanyDbContext dbContext, ICompanyOutboxEnqueuer outboxEnqueuer)
    {
        _dbContext = dbContext;
        _outboxEnqueuer = outboxEnqueuer;
    }

    public async Task<bool> ExistsAsync(Guid companyId, Guid targetEntityId, CancellationToken cancellationToken) => await _dbContext.SalesMeetingInvitations.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == targetEntityId, cancellationToken);
    public async Task<object?> GetReviewMaterialAsync(ApprovalRequest approval, CancellationToken ct)
    {
        object? material = null;
        var invitation = await _dbContext.SalesMeetingInvitations.AsNoTracking().SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, ct);
        material = new
        {
            invitation.Title,
            invitation.Description,
            invitation.AttendeeEmail,
            invitation.OrganizerEmail,
            invitation.StartsUtc,
            invitation.EndsUtc,
            invitation.TimeZoneId,
            invitation.Location,
            invitation.CalendarConnectionId,
            invitation.Conferencing
        };
        return material;
    }

    public async Task<ApprovalTargetReviewDetails> GetReviewDetailsAsync(ApprovalRequest approval, CancellationToken ct)
    {
        var comparisons = new List<ApprovalComparisonDto>();
        var evidence = new List<ApprovalEvidenceDto>();
        string? executionStatus = null;
        var invitation = await _dbContext.SalesMeetingInvitations.AsNoTracking().SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, ct);
        comparisons.Add(new("Recipient", null, invitation.AttendeeEmail));
        comparisons.Add(new("Subject", null, invitation.Title));
        executionStatus = invitation.Status.ToStorageValue().Replace('_', ' ');
        evidence.Add(new("Open Sales meeting record", $"/app/sales/leads/{invitation.LeadId}?companyId={approval.CompanyId}"));
        return new(comparisons, evidence, executionStatus);
    }

    public async Task<ApprovalTargetStateTransition?> ApplyDecisionAsync(ApprovalRequest approval, CancellationToken cancellationToken)
    {
        if (approval.Status == ApprovalRequestStatus.Pending)
            return null;
        var invitation = await _dbContext.SalesMeetingInvitations.SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, cancellationToken);
        var previousStatus = invitation.Status.ToStorageValue();
        if (approval.Status == ApprovalRequestStatus.Approved)
        {
            var approver = approval.Steps.FirstOrDefault(x => x.DecidedByUserId.HasValue)?.DecidedByUserId;
            invitation.MarkApproved(approver, DateTime.UtcNow);
            _outboxEnqueuer.Enqueue(approval.CompanyId, CompanyOutboxTopics.SalesMeetingInvitationDeliveryRequested, new SalesMeetingInvitationDeliveryRequestedMessage(approval.CompanyId, invitation.Id, invitation.IdempotencyKey, approval.Id.ToString("N")), correlationId: approval.Id.ToString("N"), idempotencyKey: $"sales-meeting-delivery:{approval.CompanyId:N}:{invitation.Id:N}:v1", causationId: approval.Id.ToString("N"));
            return ApprovalTargetStateTransition.ForSalesMeetingInvitation(invitation.Id, previousStatus, invitation.Status.ToStorageValue());
        }

        if (approval.Status is ApprovalRequestStatus.Rejected or ApprovalRequestStatus.Expired or ApprovalRequestStatus.Cancelled or ApprovalRequestStatus.ChangesRequested or ApprovalRequestStatus.Stale or ApprovalRequestStatus.Superseded or ApprovalRequestStatus.Revoked)
        {
            invitation.MarkRejected(DateTime.UtcNow);
            return ApprovalTargetStateTransition.ForSalesMeetingInvitation(invitation.Id, previousStatus, invitation.Status.ToStorageValue());
        }

        return null;
    }
}
