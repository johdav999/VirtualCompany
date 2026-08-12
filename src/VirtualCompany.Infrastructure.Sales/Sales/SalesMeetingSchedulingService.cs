using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed partial class SalesMeetingSchedulingService : ISalesMeetingSchedulingService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IApprovalRequestService _approvalService;
    private readonly ICalendarOAuthAccessTokenLeaseService _tokenLeaseService;
    private readonly ICalendarProviderRegistry _providerRegistry;

    public SalesMeetingSchedulingService(
        VirtualCompanyDbContext dbContext,
        IApprovalRequestService approvalService,
        ICalendarOAuthAccessTokenLeaseService tokenLeaseService,
        ICalendarProviderRegistry providerRegistry)
    {
        _dbContext = dbContext;
        _approvalService = approvalService;
        _tokenLeaseService = tokenLeaseService;
        _providerRegistry = providerRegistry;
    }

    public async Task<IReadOnlyList<SalesCalendarConnectionResponse>> ListCalendarConnectionsAsync(
        Guid companyId, CancellationToken cancellationToken)
    {
        var connections = await _dbContext.CalendarConnections
            .AsNoTracking()
            .Include(x => x.ExternalAccountConnection)
            .Where(x => x.CompanyId == companyId &&
                x.Status != ExternalConnectionStatus.Disconnected)
            .OrderBy(x => x.AccountEmail)
            .ToListAsync(cancellationToken);

        return connections.Select(connection =>
        {
            var requiredScopes = _providerRegistry.Resolve(connection.Provider).RequiredScopes;
            var hasPermission = requiredScopes.All(scope =>
                connection.ExternalAccountConnection.GrantedScopes.Contains(scope, StringComparer.OrdinalIgnoreCase));
            return new SalesCalendarConnectionResponse(
                connection.Id,
                connection.Provider.ToStorageValue(),
                connection.AccountEmail,
                connection.DisplayName,
                connection.Status.ToStorageValue(),
                hasPermission,
                !hasPermission || connection.Status != ExternalConnectionStatus.Active);
        }).ToArray();
    }

    public async Task<IReadOnlyList<SalesMeetingInvitationResponse>> ListForLeadAsync(
        Guid companyId, Guid leadId, CancellationToken cancellationToken) =>
        (await _dbContext.SalesMeetingInvitations
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.LeadId == leadId)
            .OrderByDescending(x => x.CreatedUtc)
            .ToListAsync(cancellationToken))
        .Select(ToResponse)
        .ToArray();

    public async Task<SalesMeetingInvitationResponse?> GetAsync(
        Guid companyId, Guid invitationId, CancellationToken cancellationToken)
    {
        var invitation = await _dbContext.SalesMeetingInvitations
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == invitationId, cancellationToken);
        return invitation is null ? null : ToResponse(invitation);
    }

    public async Task<SalesMeetingInvitationResponse> CreateForLeadAsync(
        Guid companyId, Guid userId, Guid leadId,
        CreateSalesMeetingInvitationRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        var lead = await _dbContext.Leads
            .Include(x => x.PrimaryContact)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == leadId, cancellationToken)
            ?? throw new KeyNotFoundException("Sales lead not found.");
        if (lead.Status is not (SalesStatuses.Qualified or SalesStatuses.Converted))
            throw Validation(nameof(leadId), "Qualify the lead before preparing a meeting invitation.");

        var attendeeEmail = lead.PrimaryContact?.Email ?? lead.WebsiteSubmissionEmail;
        if (string.IsNullOrWhiteSpace(attendeeEmail))
            throw Validation(nameof(leadId), "Add a contact email before preparing a meeting invitation.");

        var connection = await _dbContext.CalendarConnections
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == request.CalendarConnectionId, cancellationToken)
            ?? throw Validation(nameof(request.CalendarConnectionId), "Select a connected Google or Microsoft 365 calendar.");
        var provider = _providerRegistry.Resolve(connection.Provider);
        await _tokenLeaseService.AcquireAsync(companyId, connection.Id, provider.RequiredScopes, cancellationToken);

        var invitation = new SalesMeetingInvitation(
            Guid.NewGuid(),
            companyId,
            lead.Id,
            lead.ConvertedDealId,
            lead.PrimaryContactId,
            connection.Id,
            connection.Provider,
            connection.AccountEmail,
            attendeeEmail,
            lead.PrimaryContact?.FullName,
            request.Title,
            request.Description,
            request.StartsUtc,
            request.EndsUtc,
            request.TimeZoneId,
            request.Location,
            request.CreateOnlineMeeting,
            userId);
        _dbContext.SalesMeetingInvitations.Add(invitation);
        await _dbContext.SaveChangesAsync(cancellationToken);

        ApprovalRequestDto approval;
        try
        {
            approval = await _approvalService.CreateAsync(
                companyId,
                new CreateApprovalRequestCommand(
                    ApprovalTargetEntityType.SalesMeetingInvitation.ToStorageValue(),
                    invitation.Id,
                    "user",
                    userId,
                    SalesMeetingApprovalTypes.SendInvitation,
                    new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["title"] = JsonValue.Create(invitation.Title),
                        ["organizer"] = JsonValue.Create(invitation.OrganizerEmail),
                        ["attendee"] = JsonValue.Create(invitation.AttendeeEmail),
                        ["startsUtc"] = JsonValue.Create(invitation.StartsUtc),
                        ["endsUtc"] = JsonValue.Create(invitation.EndsUtc),
                        ["timeZoneId"] = JsonValue.Create(invitation.TimeZoneId),
                        ["provider"] = JsonValue.Create(invitation.Provider.ToStorageValue())
                    },
                    RequiredRole: "owner"),
                cancellationToken);
        }
        catch
        {
            _dbContext.SalesMeetingInvitations.Remove(invitation);
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }

        invitation.SubmitForApproval(approval.Id);
        _dbContext.SalesActivities.Add(new SalesActivity(
            Guid.NewGuid(),
            companyId,
            "meeting invitation",
            $"Meeting invitation prepared for {invitation.AttendeeEmail} and submitted for approval.",
            DateTime.UtcNow,
            leadId: lead.Id,
            dealId: lead.ConvertedDealId,
            contactId: lead.PrimaryContactId,
            customerCompanyId: lead.CustomerCompanyId,
            status: SalesStatuses.Pending));
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(invitation);
    }

    public async Task<SalesMeetingAvailabilityResponse> GetAvailabilityAsync(
        Guid companyId, SalesMeetingAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CalendarConnectionId == Guid.Empty)
            throw Validation(nameof(request.CalendarConnectionId), "Select a calendar.");
        var fromUtc = NormalizeUtc(request.FromUtc);
        var toUtc = NormalizeUtc(request.ToUtc);
        if (toUtc <= fromUtc || toUtc - fromUtc > TimeSpan.FromDays(31))
            throw Validation(nameof(request.ToUtc), "Choose an availability window of no more than 31 days.");
        if (request.DurationMinutes is < 15 or > 240 || request.DurationMinutes % 15 != 0)
            throw Validation(nameof(request.DurationMinutes), "Choose a duration from 15 to 240 minutes in 15-minute increments.");
        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw Validation(nameof(request.TimeZoneId), "Choose a valid time zone.");
        }

        var connection = await _dbContext.CalendarConnections
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == request.CalendarConnectionId, cancellationToken)
            ?? throw new KeyNotFoundException("Calendar connection not found.");
        var provider = _providerRegistry.Resolve(connection.Provider);
        var lease = await _tokenLeaseService.AcquireAsync(companyId, connection.Id, provider.RequiredScopes, cancellationToken);
        var busy = await provider.GetBusyWindowsAsync(
            new CalendarProviderContext(companyId, connection.Id, connection.Provider, connection.AccountEmail, lease.AccessToken, connection.CalendarId),
            fromUtc, toUtc, request.TimeZoneId, cancellationToken);
        var suggestedSlots = BuildSuggestedSlots(
            fromUtc, toUtc, request.DurationMinutes, timeZone, busy);
        return new SalesMeetingAvailabilityResponse(
            connection.Id, connection.Provider.ToStorageValue(), busy, suggestedSlots);
    }

    internal static IReadOnlyList<CalendarAvailableSlot> BuildSuggestedSlots(
        DateTime fromUtc, DateTime toUtc, int durationMinutes, TimeZoneInfo timeZone,
        IReadOnlyList<CalendarBusyWindow> busyWindows)
    {
        var result = new List<CalendarAvailableSlot>(12);
        var duration = TimeSpan.FromMinutes(durationMinutes);
        var localFrom = TimeZoneInfo.ConvertTimeFromUtc(fromUtc, timeZone);
        var localTo = TimeZoneInfo.ConvertTimeFromUtc(toUtc, timeZone);
        var date = localFrom.Date;
        var latestStartUtc = DateTime.UtcNow.AddMinutes(5);

        while (date <= localTo.Date && result.Count < 12)
        {
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                var cursor = date.AddHours(9);
                var businessDayEnd = date.AddHours(17);
                while (cursor + duration <= businessDayEnd && result.Count < 12)
                {
                    if (!timeZone.IsInvalidTime(cursor) && !timeZone.IsAmbiguousTime(cursor))
                    {
                        var startsUtc = TimeZoneInfo.ConvertTimeToUtc(
                            DateTime.SpecifyKind(cursor, DateTimeKind.Unspecified), timeZone);
                        var endsUtc = startsUtc + duration;
                        var overlapsBusy = busyWindows.Any(window =>
                            NormalizeUtc(window.StartsUtc) < endsUtc &&
                            NormalizeUtc(window.EndsUtc) > startsUtc);
                        if (startsUtc >= fromUtc && endsUtc <= toUtc &&
                            startsUtc > latestStartUtc && !overlapsBusy)
                        {
                            result.Add(new CalendarAvailableSlot(startsUtc, endsUtc));
                        }
                    }
                    cursor = cursor.AddMinutes(30);
                }
            }
            date = date.AddDays(1);
        }

        return result;
    }

    private static void Validate(CreateSalesMeetingInvitationRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (request.CalendarConnectionId == Guid.Empty) errors[nameof(request.CalendarConnectionId)] = ["Select a calendar."];
        if (request.StartsUtc == default || request.EndsUtc <= request.StartsUtc) errors[nameof(request.EndsUtc)] = ["Choose a valid meeting time."];
        if (NormalizeUtc(request.StartsUtc) <= DateTime.UtcNow.AddMinutes(5)) errors[nameof(request.StartsUtc)] = ["Choose a meeting time at least five minutes from now."];
        if (request.EndsUtc - request.StartsUtc > TimeSpan.FromHours(8)) errors[nameof(request.EndsUtc)] = ["A sales meeting cannot be longer than eight hours."];
        if (string.IsNullOrWhiteSpace(request.TimeZoneId)) errors[nameof(request.TimeZoneId)] = ["Choose a time zone."];
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 200) errors[nameof(request.Title)] = ["Enter a title of 200 characters or fewer."];
        if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Trim().Length > 4000) errors[nameof(request.Description)] = ["Enter an agenda of 4,000 characters or fewer."];
        if (request.Location?.Trim().Length > 500) errors[nameof(request.Location)] = ["Location must be 500 characters or fewer."];
        if (errors.Count > 0) throw new SalesValidationException(errors);
    }

    private static SalesValidationException Validation(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    internal static SalesMeetingInvitationResponse ToResponse(SalesMeetingInvitation x) =>
        new(x.Id, x.LeadId, x.DealId, x.ContactId, x.CalendarConnectionId,
            x.Provider.ToStorageValue(), x.OrganizerEmail, x.AttendeeEmail, x.AttendeeName,
            x.Title, x.Description, x.StartsUtc, x.EndsUtc, x.TimeZoneId, x.Location,
            x.CreateOnlineMeeting, x.Status.ToStorageValue(), x.ApprovalRequestId,
            x.ExternalEventId, x.ProviderWebUrl, x.OnlineMeetingUrl,
            x.ExecutionAttemptCount, x.LastErrorCode, x.LastErrorSummary,
            x.CreatedUtc, x.UpdatedUtc, x.ScheduledUtc,
            x.ConfirmationStatus.ToStorageValue(), x.ConfirmationMailboxConnectionId,
            x.ConfirmationProviderMessageId, x.ConfirmationProviderThreadId,
            x.ConfirmationThreadingMode.ToStorageValue(), x.ConfirmationAttemptCount, x.ConfirmationErrorCode,
            x.ConfirmationErrorSummary, x.ConfirmationSentUtc);
}

public sealed class SalesMeetingInvitationDeliveryDispatcher : ISalesMeetingInvitationDeliveryDispatcher
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICalendarOAuthAccessTokenLeaseService _tokenLeaseService;
    private readonly ICalendarProviderRegistry _providerRegistry;
    private readonly ICompanyOutboxEnqueuer _outbox;

    public SalesMeetingInvitationDeliveryDispatcher(
        VirtualCompanyDbContext dbContext,
        ICalendarOAuthAccessTokenLeaseService tokenLeaseService,
        ICalendarProviderRegistry providerRegistry,
        ICompanyOutboxEnqueuer outbox)
    {
        _dbContext = dbContext;
        _tokenLeaseService = tokenLeaseService;
        _providerRegistry = providerRegistry;
        _outbox = outbox;
    }

    public async Task DispatchAsync(
        SalesMeetingInvitationDeliveryRequestedMessage message,
        CancellationToken cancellationToken)
    {
        var invitation = await _dbContext.SalesMeetingInvitations
            .SingleOrDefaultAsync(x => x.CompanyId == message.CompanyId && x.Id == message.InvitationId, cancellationToken)
            ?? throw new InvalidOperationException("Meeting invitation delivery target was not found.");
        if (!string.Equals(invitation.IdempotencyKey, message.IdempotencyKey, StringComparison.Ordinal))
            throw new InvalidOperationException("Meeting invitation idempotency key does not match.");
        if (invitation.Status == SalesMeetingInvitationStatus.Scheduled) return;
        if (!invitation.ApprovalRequestId.HasValue)
            throw new InvalidOperationException("Meeting invitation has no approval request.");

        var approved = await _dbContext.ApprovalRequests
            .AsNoTracking()
            .AnyAsync(x => x.CompanyId == message.CompanyId &&
                x.Id == invitation.ApprovalRequestId &&
                x.Status == ApprovalRequestStatus.Approved,
                cancellationToken);
        if (!approved)
            throw new InvalidOperationException("Meeting invitation is not approved.");

        var provider = _providerRegistry.Resolve(invitation.Provider);
        try
        {
            var lease = await _tokenLeaseService.AcquireAsync(
                invitation.CompanyId, invitation.CalendarConnectionId,
                provider.RequiredScopes, cancellationToken);
            invitation.BeginScheduling();
            await _dbContext.SaveChangesAsync(cancellationToken);

            var result = await provider.CreateMeetingAsync(
                new CalendarProviderContext(
                    invitation.CompanyId, invitation.CalendarConnectionId,
                    invitation.Provider, invitation.OrganizerEmail,
                    lease.AccessToken, invitation.CalendarId),
                new CalendarMeetingCreateRequest(
                    invitation.Id, invitation.IdempotencyKey, invitation.Title,
                    invitation.Description, invitation.StartsUtc, invitation.EndsUtc,
                    invitation.TimeZoneId, invitation.Location, invitation.AttendeeEmail,
                    invitation.AttendeeName, invitation.CreateOnlineMeeting),
                cancellationToken);
            invitation.MarkScheduled(
                result.ExternalEventId, result.ExternalICalUid,
                result.ProviderWebUrl, result.OnlineMeetingUrl, DateTime.UtcNow);
            invitation.QueueConfirmation();
            _outbox.Enqueue(
                invitation.CompanyId,
                CompanyOutboxTopics.SalesMeetingConfirmationDeliveryRequested,
                new SalesMeetingConfirmationDeliveryRequestedMessage(
                    invitation.CompanyId, invitation.Id,
                    invitation.ConfirmationIdempotencyKey, message.CorrelationId),
                correlationId: message.CorrelationId,
                idempotencyKey: invitation.ConfirmationIdempotencyKey,
                causationId: invitation.ApprovalRequestId?.ToString("D"));
            _dbContext.SalesActivities.Add(new SalesActivity(
                Guid.NewGuid(), invitation.CompanyId, "meeting",
                $"Meeting invitation sent to {invitation.AttendeeEmail}.",
                DateTime.UtcNow, invitation.LeadId, invitation.DealId, invitation.ContactId));
            AddDeliveryAudit(
                invitation,
                message.CorrelationId,
                "sales.meeting_invitation.sent",
                AuditEventOutcomes.Succeeded,
                "The approved calendar invitation was created and sent by the connected calendar provider.");
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (CalendarProviderException ex)
        {
            if (ex.Kind == CalendarProviderFailureKind.Ambiguous)
                invitation.MarkReconciliationRequired(ex.Code, ex.Message);
            else
                invitation.MarkFailed(ex.Code, ex.Message);
            AddDeliveryAudit(
                invitation,
                message.CorrelationId,
                ex.Kind == CalendarProviderFailureKind.Ambiguous
                    ? "sales.meeting_invitation.reconciliation_required"
                    : "sales.meeting_invitation.delivery_failed",
                ex.Kind == CalendarProviderFailureKind.Ambiguous
                    ? AuditEventOutcomes.Blocked
                    : AuditEventOutcomes.Failed,
                ex.Message);
            await _dbContext.SaveChangesAsync(cancellationToken);
            if (ex.Kind == CalendarProviderFailureKind.Retryable) throw;
        }
        catch (InvalidOperationException ex)
        {
            invitation.MarkFailed("calendar_connection_unavailable", ex.Message);
            AddDeliveryAudit(
                invitation,
                message.CorrelationId,
                "sales.meeting_invitation.delivery_failed",
                AuditEventOutcomes.Failed,
                ex.Message);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private void AddDeliveryAudit(
        SalesMeetingInvitation invitation,
        string? correlationId,
        string action,
        string outcome,
        string rationale)
    {
        _dbContext.AuditEvents.Add(new AuditEvent(
            Guid.NewGuid(),
            invitation.CompanyId,
            AuditActorTypes.System,
            actorId: null,
            action,
            "sales_meeting_invitation",
            invitation.Id.ToString("D"),
            outcome,
            rationale,
            dataSources: ["calendar provider", "approval request"],
            metadata: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["approvalRequestId"] = invitation.ApprovalRequestId?.ToString("D"),
                ["provider"] = invitation.Provider.ToStorageValue(),
                ["organizer"] = invitation.OrganizerEmail,
                ["attendee"] = invitation.AttendeeEmail,
                ["externalEventId"] = invitation.ExternalEventId,
                ["errorCode"] = invitation.LastErrorCode
            },
            correlationId: correlationId));
    }
}
