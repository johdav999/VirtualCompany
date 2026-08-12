using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesMeetingChangeDeliveryDispatcher : ISalesMeetingChangeDeliveryDispatcher
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICalendarOAuthAccessTokenLeaseService _tokenLeaseService;
    private readonly ICalendarProviderRegistry _providerRegistry;

    public SalesMeetingChangeDeliveryDispatcher(
        VirtualCompanyDbContext dbContext,
        ICalendarOAuthAccessTokenLeaseService tokenLeaseService,
        ICalendarProviderRegistry providerRegistry)
    {
        _dbContext = dbContext;
        _tokenLeaseService = tokenLeaseService;
        _providerRegistry = providerRegistry;
    }

    public async Task DispatchAsync(
        SalesMeetingChangeDeliveryRequestedMessage message,
        CancellationToken cancellationToken)
    {
        var change = await _dbContext.SalesMeetingChangeRequests
            .Include(x => x.Invitation)
            .SingleOrDefaultAsync(x => x.CompanyId == message.CompanyId && x.Id == message.ChangeRequestId, cancellationToken)
            ?? throw new InvalidOperationException("Meeting change delivery target was not found.");
        if (!string.Equals(change.IdempotencyKey, message.IdempotencyKey, StringComparison.Ordinal))
            throw new InvalidOperationException("Meeting change idempotency key does not match.");
        if (change.Status == SalesMeetingChangeRequestStatus.Completed) return;
        if (!change.ApprovalRequestId.HasValue)
            throw new InvalidOperationException("Meeting change has no approval request.");

        var approved = await _dbContext.ApprovalRequests.AsNoTracking().AnyAsync(
            x => x.CompanyId == message.CompanyId && x.Id == change.ApprovalRequestId &&
                x.Status == ApprovalRequestStatus.Approved,
            cancellationToken);
        if (!approved) throw new InvalidOperationException("Meeting change is not approved.");

        var invitation = change.Invitation;
        if (invitation.Status != SalesMeetingInvitationStatus.Scheduled || string.IsNullOrWhiteSpace(invitation.ExternalEventId))
            throw new InvalidOperationException("The confirmed provider event is no longer available for this change.");

        var provider = _providerRegistry.Resolve(invitation.Provider);
        try
        {
            var lease = await _tokenLeaseService.AcquireAsync(
                invitation.CompanyId, invitation.CalendarConnectionId,
                provider.RequiredScopes, cancellationToken);
            change.BeginExecution();
            await _dbContext.SaveChangesAsync(cancellationToken);
            var context = new CalendarProviderContext(
                invitation.CompanyId, invitation.CalendarConnectionId,
                invitation.Provider, invitation.OrganizerEmail,
                lease.AccessToken, invitation.CalendarId);

            if (change.Operation == SalesMeetingChangeOperation.Reschedule)
            {
                var result = await provider.UpdateMeetingAsync(
                    context,
                    new CalendarMeetingUpdateRequest(
                        change.Id, change.IdempotencyKey, invitation.ExternalEventId,
                        change.Title!, change.Description!, change.StartsUtc!.Value,
                        change.EndsUtc!.Value, change.TimeZoneId!, change.Location,
                        invitation.AttendeeEmail, invitation.AttendeeName,
                        change.CreateOnlineMeeting ?? invitation.CreateOnlineMeeting),
                    cancellationToken);
                invitation.ApplyReschedule(
                    change.Title!, change.Description!, change.StartsUtc.Value,
                    change.EndsUtc.Value, change.TimeZoneId!, change.Location,
                    change.CreateOnlineMeeting ?? invitation.CreateOnlineMeeting,
                    result.ProviderWebUrl, result.OnlineMeetingUrl, DateTime.UtcNow);
            }
            else
            {
                await provider.CancelMeetingAsync(
                    context, invitation.ExternalEventId,
                    change.IdempotencyKey, cancellationToken);
                invitation.MarkCancelled(DateTime.UtcNow);
            }

            change.MarkCompleted(DateTime.UtcNow);
            _dbContext.SalesActivities.Add(new SalesActivity(
                Guid.NewGuid(), invitation.CompanyId, "meeting change",
                change.Operation == SalesMeetingChangeOperation.Reschedule
                    ? $"Meeting with {invitation.AttendeeEmail} rescheduled."
                    : $"Meeting with {invitation.AttendeeEmail} cancelled.",
                DateTime.UtcNow, invitation.LeadId, invitation.DealId, invitation.ContactId));
            AddAudit(change, invitation, message.CorrelationId,
                $"sales.meeting_invitation.{change.Operation.ToStorageValue()}",
                AuditEventOutcomes.Succeeded,
                "The approved calendar change was applied to the existing provider event.");
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (CalendarProviderException ex)
        {
            if (ex.Kind == CalendarProviderFailureKind.Ambiguous)
                change.MarkReconciliationRequired(ex.Code, ex.Message);
            else
                change.MarkFailed(ex.Code, ex.Message);
            AddAudit(change, invitation, message.CorrelationId,
                ex.Kind == CalendarProviderFailureKind.Ambiguous
                    ? "sales.meeting_change.reconciliation_required"
                    : "sales.meeting_change.delivery_failed",
                ex.Kind == CalendarProviderFailureKind.Ambiguous
                    ? AuditEventOutcomes.Blocked
                    : AuditEventOutcomes.Failed,
                ex.Message);
            await _dbContext.SaveChangesAsync(cancellationToken);
            if (ex.Kind == CalendarProviderFailureKind.Retryable) throw;
        }
        catch (InvalidOperationException ex)
        {
            change.MarkFailed("calendar_connection_unavailable", ex.Message);
            AddAudit(change, invitation, message.CorrelationId,
                "sales.meeting_change.delivery_failed",
                AuditEventOutcomes.Failed,
                ex.Message);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private void AddAudit(
        SalesMeetingChangeRequest change, SalesMeetingInvitation invitation,
        string? correlationId, string action, string outcome, string rationale)
    {
        _dbContext.AuditEvents.Add(new AuditEvent(
            Guid.NewGuid(), change.CompanyId, AuditActorTypes.System, actorId: null,
            action, "sales_meeting_change_request", change.Id.ToString("D"), outcome,
            rationale, ["calendar provider", "approval request"],
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["approvalRequestId"] = change.ApprovalRequestId?.ToString("D"),
                ["invitationId"] = invitation.Id.ToString("D"),
                ["externalEventId"] = invitation.ExternalEventId,
                ["provider"] = invitation.Provider.ToStorageValue(),
                ["operation"] = change.Operation.ToStorageValue(),
                ["errorCode"] = change.LastErrorCode
            },
            correlationId));
    }
}
