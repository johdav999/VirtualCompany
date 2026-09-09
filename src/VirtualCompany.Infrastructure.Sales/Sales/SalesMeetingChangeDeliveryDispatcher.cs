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
    private readonly ISalesBrowserMeetingScheduling? _browserMeetings;

    public SalesMeetingChangeDeliveryDispatcher(
        VirtualCompanyDbContext dbContext,
        ICalendarOAuthAccessTokenLeaseService tokenLeaseService,
        ICalendarProviderRegistry providerRegistry, ISalesBrowserMeetingScheduling? browserMeetings = null)
    {
        _dbContext = dbContext;
        _tokenLeaseService = tokenLeaseService;
        _providerRegistry = providerRegistry;
        _browserMeetings = browserMeetings;
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

        if(!message.ReconcileOnly&&invitation.Conferencing==SalesMeetingConferencing.Browser&&change.Status is SalesMeetingChangeRequestStatus.Executing or SalesMeetingChangeRequestStatus.ReconciliationRequired)
        {change.MarkReconciliationRequired("calendar_change_outcome_unknown","Inspect the calendar before retrying this change.");await _dbContext.SaveChangesAsync(cancellationToken);return;}
        var provider = _providerRegistry.Resolve(invitation.Provider);
        try
        {
            var lease = await _tokenLeaseService.AcquireAsync(
                invitation.CompanyId, invitation.CalendarConnectionId,
                provider.RequiredScopes, cancellationToken);
            if(!message.ReconcileOnly)change.BeginExecution();
            await _dbContext.SaveChangesAsync(cancellationToken);
            var context = new CalendarProviderContext(
                invitation.CompanyId, invitation.CalendarConnectionId,
                invitation.Provider, invitation.OrganizerEmail,
                lease.AccessToken, invitation.CalendarId);

            if (change.Operation == SalesMeetingChangeOperation.Reschedule)
            {
                string? browserLink=null;
                if(invitation.Conferencing==SalesMeetingConferencing.Browser)
                {
                    if(_browserMeetings==null)throw new InvalidOperationException("Browser scheduling is not configured.");
                    await _browserMeetings.ValidateRescheduleAsync(invitation.CompanyId,invitation.Id,change.StartsUtc!.Value,change.EndsUtc!.Value,cancellationToken);
                    browserLink=await _browserMeetings.DeliveryLinkAsync(invitation.CompanyId,invitation.Id,cancellationToken);
                }
                CalendarMeetingCreateResult? result=null;
                if(message.ReconcileOnly || browserLink!=null&&change.ExecutionAttemptCount>1)
                {
                    var observed=await provider.InspectMeetingAsync(context,invitation.Id,invitation.ExternalEventId,change.StartsUtc!.Value,change.EndsUtc!.Value,cancellationToken);
                    if(message.ReconcileOnly&&(observed==null||observed.Cancelled||observed.StartsUtc!=change.StartsUtc||observed.EndsUtc!=change.EndsUtc||observed.Title!=change.Title))
                        throw new CalendarProviderException("calendar_change_unconfirmed","The calendar does not confirm the requested change. No update was resent.",CalendarProviderFailureKind.Ambiguous);
                    if(observed!=null&&!observed.Cancelled&&observed.StartsUtc==change.StartsUtc&&observed.EndsUtc==change.EndsUtc&&observed.Title==change.Title)result=observed.Event;
                }
                if(result==null) result = await provider.UpdateMeetingAsync(
                    context,
                    new CalendarMeetingUpdateRequest(
                        change.Id, change.IdempotencyKey, invitation.ExternalEventId,
                        change.Title!, browserLink is null?change.Description!:$"{change.Description}\n\nJoin browser meeting: {browserLink}", change.StartsUtc!.Value,
                        change.EndsUtc!.Value, change.TimeZoneId!, change.Location,
                        invitation.AttendeeEmail, invitation.AttendeeName,
                        invitation.Conferencing==SalesMeetingConferencing.Browser?false:change.CreateOnlineMeeting ?? invitation.CreateOnlineMeeting),
                    cancellationToken);
                invitation.ApplyReschedule(
                    change.Title!, change.Description!, DateTime.SpecifyKind(change.StartsUtc.Value,DateTimeKind.Utc),
                    DateTime.SpecifyKind(change.EndsUtc.Value,DateTimeKind.Utc), change.TimeZoneId!, change.Location,
                    invitation.Conferencing==SalesMeetingConferencing.Browser?false:change.CreateOnlineMeeting ?? invitation.CreateOnlineMeeting,
                    result.ProviderWebUrl, browserLink is null?result.OnlineMeetingUrl:new Uri(browserLink).GetLeftPart(UriPartial.Path), DateTime.UtcNow);
                if(browserLink!=null)await _browserMeetings!.RescheduledAsync(invitation.CompanyId,invitation.Id,cancellationToken);
            }
            else
            {
                if(invitation.Conferencing==SalesMeetingConferencing.Browser)
                    await (_browserMeetings??throw new InvalidOperationException("Browser scheduling is not configured.")).CancelAsync(invitation.CompanyId,invitation.Id,change.Id,cancellationToken);
                var cancellationConfirmed=false;
                if(message.ReconcileOnly||invitation.Conferencing==SalesMeetingConferencing.Browser&&change.ExecutionAttemptCount>1)
                {
                    var observed=await provider.InspectMeetingAsync(context,invitation.Id,invitation.ExternalEventId,invitation.StartsUtc,invitation.EndsUtc,cancellationToken);
                    cancellationConfirmed=observed==null||observed.Cancelled;
                    if(!cancellationConfirmed&&message.ReconcileOnly)throw new CalendarProviderException("calendar_cancellation_unconfirmed","The calendar still shows this event. No cancellation was resent.",CalendarProviderFailureKind.Ambiguous);
                }
                if(!cancellationConfirmed) await provider.CancelMeetingAsync(
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
