using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesMeetingSessionService(
    VirtualCompanyDbContext dbContext,
    TimeProvider timeProvider) : ISalesMeetingSessionService
{
    public async Task<SalesMeetingSessionResponse?> GetByInvitationAsync(
        Guid companyId,
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        EnsureId(companyId, nameof(companyId));
        EnsureId(invitationId, nameof(invitationId));
        var session = await dbContext.SalesMeetingSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.CompanyId == companyId && item.InvitationId == invitationId,
                cancellationToken);
        return session is null ? null : ToResponse(session);
    }

    public async Task<SalesMeetingSessionResponse?> GetAsync(
        Guid companyId,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        EnsureId(companyId, nameof(companyId));
        EnsureId(sessionId, nameof(sessionId));
        var session = await dbContext.SalesMeetingSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.CompanyId == companyId && item.Id == sessionId,
                cancellationToken);
        return session is null ? null : ToResponse(session);
    }

    public async Task<SalesMeetingSessionResponse> CreateOrUpdateAsync(
        Guid companyId,
        Guid actorUserId,
        Guid invitationId,
        CreateOrUpdateSalesMeetingSessionRequest request,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        EnsureId(companyId, nameof(companyId));
        EnsureId(actorUserId, nameof(actorUserId));
        EnsureId(invitationId, nameof(invitationId));
        ArgumentNullException.ThrowIfNull(request);
        await EnsureActiveMemberAsync(companyId, actorUserId, cancellationToken);
        var consentStatus = ParseConsentStatus(request.ConsentStatus);
        var retentionPolicy = ParseRetentionPolicy(request.RetentionPolicy);
        ValidatePreparationRequest(request, retentionPolicy);

        var existing = await dbContext.SalesMeetingSessions
            .SingleOrDefaultAsync(
                item => item.CompanyId == companyId && item.InvitationId == invitationId,
                cancellationToken);
        if (existing is not null)
        {
            if (existing.HasPreparation(
                    request.MeetingGoal,
                    request.IntendedAudience,
                    request.PlannedDurationMinutes,
                    request.DemoScenario,
                    consentStatus,
                    retentionPolicy,
                    request.RetentionDays))
            {
                return ToResponse(existing);
            }

            if (!request.ExpectedVersion.HasValue)
            {
                throw Conflict("The meeting session already exists. Supply its current version before changing preparation details.");
            }

            EnsureExpectedVersion(existing, request.ExpectedVersion.Value);
            var previousConsent = existing.ConsentStatus.ToStorageValue();
            var previousRetention = existing.RetentionPolicy.ToStorageValue();
            try
            {
                existing.UpdatePreparation(
                    request.MeetingGoal,
                    request.IntendedAudience,
                    request.PlannedDurationMinutes,
                    request.DemoScenario,
                    consentStatus,
                    retentionPolicy,
                    request.RetentionDays,
                    actorUserId,
                    UtcNow());
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                throw Conflict(exception.Message);
            }

            AddAudit(
                existing,
                actorUserId,
                AuditEventActions.SalesMeetingSessionPreparationUpdated,
                "Sales meeting preparation details were updated.",
                correlationId,
                new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["previousConsentStatus"] = previousConsent,
                    ["newConsentStatus"] = existing.ConsentStatus.ToStorageValue(),
                    ["previousRetentionPolicy"] = previousRetention,
                    ["newRetentionPolicy"] = existing.RetentionPolicy.ToStorageValue()
                });
            await SaveWithConcurrencyMappingAsync(cancellationToken);
            return ToResponse(existing);
        }

        if (request.ExpectedVersion.HasValue)
        {
            throw Conflict("The meeting session does not exist, so an expected version cannot be applied.");
        }

        var invitation = await dbContext.SalesMeetingInvitations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.CompanyId == companyId && item.Id == invitationId,
                cancellationToken)
            ?? throw new KeyNotFoundException("Sales meeting invitation not found.");

        if (invitation.Status != SalesMeetingInvitationStatus.Scheduled ||
            string.IsNullOrWhiteSpace(invitation.ExternalEventId))
        {
            throw Conflict("Only a scheduled meeting invitation with a provider event can start a meeting session.");
        }

        var relationships = await ResolveRelationshipsAsync(companyId, invitation, cancellationToken);
        var now = UtcNow();
        var session = new SalesMeetingSession(
            Guid.NewGuid(),
            companyId,
            invitation.Id,
            invitation.LeadId,
            invitation.DealId,
            invitation.ContactId,
            relationships.CustomerCompanyId,
            request.MeetingGoal,
            request.IntendedAudience,
            request.PlannedDurationMinutes,
            request.DemoScenario,
            invitation.ExternalEventId,
            consentStatus,
            retentionPolicy,
            request.RetentionDays,
            NormalizeStoredUtc(invitation.EndsUtc),
            actorUserId,
            now);

        dbContext.SalesMeetingSessions.Add(session);
        if(invitation.BrowserRoomId.HasValue)
        {
            var browserRoom=await dbContext.SalesBrowserRooms.SingleAsync(x=>x.CompanyId==companyId&&x.Id==invitation.BrowserRoomId&&x.InvitationId==invitation.Id,cancellationToken);
            browserRoom.AttachSession(session.Id);
        }
        AddAudit(
            session,
            actorUserId,
            AuditEventActions.SalesMeetingSessionCreated,
            "A durable sales meeting session was created from the scheduled invitation.",
            correlationId,
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["previousStatus"] = null,
                ["newStatus"] = session.Status.ToStorageValue()
            });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            var raced = await dbContext.SalesMeetingSessions
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.CompanyId == companyId && item.InvitationId == invitationId,
                    cancellationToken);
            if (raced is not null)
            {
                return ToResponse(raced);
            }

            throw;
        }

        return ToResponse(session);
    }

    public async Task<SalesMeetingSessionResponse?> TransitionAsync(
        Guid companyId,
        Guid actorUserId,
        Guid sessionId,
        TransitionSalesMeetingSessionRequest request,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        EnsureId(companyId, nameof(companyId));
        EnsureId(actorUserId, nameof(actorUserId));
        EnsureId(sessionId, nameof(sessionId));
        ArgumentNullException.ThrowIfNull(request);
        await EnsureActiveMemberAsync(companyId, actorUserId, cancellationToken);
        var targetStatus = ParseSessionStatus(request.TargetStatus);
        if (request.CurrentSlideIndex < 0 || request.CurrentTalkingPointIndex < 0)
        {
            throw Validation(nameof(request.CurrentSlideIndex), "Slide and talking-point indexes cannot be negative.");
        }

        var session = await dbContext.SalesMeetingSessions
            .SingleOrDefaultAsync(
                item => item.CompanyId == companyId && item.Id == sessionId,
                cancellationToken);
        if (session is null) return null;

        EnsureExpectedVersion(session, request.ExpectedVersion);
        var previousStatus = session.Status.ToStorageValue();
        try
        {
            session.TransitionTo(
                targetStatus,
                request.CurrentSlideIndex,
                request.CurrentTalkingPointIndex,
                request.ResumeMarker,
                request.Reason,
                actorUserId,
                UtcNow());
        }
        catch (ArgumentException exception)
        {
            throw Validation(nameof(request.TargetStatus), exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            throw new SalesMeetingSessionConflictException(
                SalesMeetingSessionProblemCodes.InvalidTransition,
                exception.Message);
        }

        AddAudit(
            session,
            actorUserId,
            AuditEventActions.SalesMeetingSessionTransitioned,
            $"Sales meeting session moved from {previousStatus} to {session.Status.ToStorageValue()}.",
            correlationId,
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["previousStatus"] = previousStatus,
                ["newStatus"] = session.Status.ToStorageValue(),
                ["currentSlideIndex"] = session.CurrentSlideIndex.ToString(),
                ["currentTalkingPointIndex"] = session.CurrentTalkingPointIndex.ToString()
            });
        await SaveWithConcurrencyMappingAsync(cancellationToken);
        return ToResponse(session);
    }

    private async Task<ResolvedRelationships> ResolveRelationshipsAsync(
        Guid companyId,
        SalesMeetingInvitation invitation,
        CancellationToken cancellationToken)
    {
        var lead = await dbContext.Leads.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == invitation.LeadId, cancellationToken)
            ?? throw InvalidRelationship("The invitation's sales lead is unavailable in this company.");

        Deal? deal = null;
        if (invitation.DealId.HasValue)
        {
            deal = await dbContext.Deals.AsNoTracking()
                .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == invitation.DealId.Value, cancellationToken)
                ?? throw InvalidRelationship("The invitation's deal is unavailable in this company.");
            if (deal.SourceLeadId.HasValue && deal.SourceLeadId != lead.Id)
            {
                throw InvalidRelationship("The invitation's deal does not belong to its sales lead.");
            }
        }

        Contact? contact = null;
        if (invitation.ContactId.HasValue)
        {
            contact = await dbContext.Contacts.AsNoTracking()
                .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == invitation.ContactId.Value, cancellationToken)
                ?? throw InvalidRelationship("The invitation's contact is unavailable in this company.");
        }

        var customerIds = new[] { lead.CustomerCompanyId, deal?.CustomerCompanyId, contact?.CustomerCompanyId }
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .Distinct()
            .ToArray();
        if (customerIds.Length != 1)
        {
            throw InvalidRelationship(customerIds.Length == 0
                ? "Associate the meeting lead, deal, or contact with a customer company before creating a session."
                : "The invitation's sales records resolve to different customer companies.");
        }

        var customerExists = await dbContext.CustomerCompanies.AsNoTracking()
            .AnyAsync(item => item.CompanyId == companyId && item.Id == customerIds[0], cancellationToken);
        if (!customerExists)
        {
            throw InvalidRelationship("The invitation's customer company is unavailable in this company.");
        }

        return new ResolvedRelationships(customerIds[0]);
    }

    private async Task EnsureActiveMemberAsync(Guid companyId, Guid actorUserId, CancellationToken cancellationToken)
    {
        var isMember = await dbContext.CompanyMemberships
            .AsNoTracking()
            .AnyAsync(
                membership => membership.CompanyId == companyId &&
                              membership.UserId == actorUserId &&
                              membership.Status == CompanyMembershipStatus.Active,
                cancellationToken);
        if (!isMember)
        {
            throw new UnauthorizedAccessException("An active company membership is required.");
        }
    }

    private void AddAudit(
        SalesMeetingSession session,
        Guid actorUserId,
        string action,
        string rationale,
        string? correlationId,
        IReadOnlyDictionary<string, string?> extraMetadata)
    {
        var metadata = new Dictionary<string, string?>(extraMetadata, StringComparer.OrdinalIgnoreCase)
        {
            ["invitationId"] = session.InvitationId.ToString("D"),
            ["leadId"] = session.LeadId.ToString("D"),
            ["dealId"] = session.DealId?.ToString("D"),
            ["contactId"] = session.ContactId?.ToString("D"),
            ["customerCompanyId"] = session.CustomerCompanyId.ToString("D"),
            ["concurrencyVersion"] = session.ConcurrencyVersion.ToString()
        };
        dbContext.AuditEvents.Add(new AuditEvent(
            Guid.NewGuid(),
            session.CompanyId,
            AuditActorTypes.User,
            actorUserId,
            action,
            "sales_meeting_session",
            session.Id.ToString("D"),
            AuditEventOutcomes.Succeeded,
            rationale,
            ["sales meeting invitation", "sales records"],
            metadata,
            NormalizeCorrelationId(correlationId),
            UtcNow()));
    }

    private async Task SaveWithConcurrencyMappingAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new SalesMeetingSessionConflictException(
                SalesMeetingSessionProblemCodes.Conflict,
                "The meeting session changed after it was opened. Refresh it before trying again.");
        }
    }

    private static void EnsureExpectedVersion(SalesMeetingSession session, long expectedVersion)
    {
        if (expectedVersion <= 0 || session.ConcurrencyVersion != expectedVersion)
        {
            throw Conflict("The meeting session changed after it was opened. Refresh it before trying again.");
        }
    }

    private static void ValidatePreparationRequest(
        CreateOrUpdateSalesMeetingSessionRequest request,
        SalesMeetingRetentionPolicy retentionPolicy)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        AddRequired(errors, nameof(request.MeetingGoal), request.MeetingGoal, 1000);
        AddRequired(errors, nameof(request.IntendedAudience), request.IntendedAudience, 1000);
        AddOptional(errors, nameof(request.DemoScenario), request.DemoScenario, 4000);
        if (request.PlannedDurationMinutes is < 5 or > 480)
            errors[nameof(request.PlannedDurationMinutes)] = ["Planned duration must be between 5 and 480 minutes."];
        if (request.RetentionDays is < 1 or > 3650)
            errors[nameof(request.RetentionDays)] = ["Retention must be between 1 and 3650 days."];
        else if (retentionPolicy == SalesMeetingRetentionPolicy.Standard && request.RetentionDays != 365)
            errors[nameof(request.RetentionDays)] = ["The standard meeting retention policy is 365 days."];
        if (errors.Count > 0) throw new SalesValidationException(errors);
    }

    private static SalesMeetingConsentStatus ParseConsentStatus(string value)
    {
        try { return SalesMeetingConsentStatusValues.Parse(value); }
        catch (ArgumentOutOfRangeException)
        {
            throw Validation(nameof(CreateOrUpdateSalesMeetingSessionRequest.ConsentStatus), "Choose a supported consent status.");
        }
    }

    private static SalesMeetingRetentionPolicy ParseRetentionPolicy(string value)
    {
        try { return SalesMeetingRetentionPolicyValues.Parse(value); }
        catch (ArgumentOutOfRangeException)
        {
            throw Validation(nameof(CreateOrUpdateSalesMeetingSessionRequest.RetentionPolicy), "Choose a supported retention policy.");
        }
    }

    private static SalesMeetingSessionStatus ParseSessionStatus(string value)
    {
        try { return SalesMeetingSessionStatusValues.Parse(value); }
        catch (ArgumentOutOfRangeException)
        {
            throw Validation(nameof(TransitionSalesMeetingSessionRequest.TargetStatus), "Choose a supported meeting lifecycle status.");
        }
    }

    private static SalesValidationException Validation(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { [field] = [message] });

    private static SalesMeetingSessionConflictException Conflict(string message) =>
        new(SalesMeetingSessionProblemCodes.Conflict, message);

    private static SalesMeetingSessionConflictException InvalidRelationship(string message) =>
        new(SalesMeetingSessionProblemCodes.InvalidRelationship, message);

    private static void AddRequired(Dictionary<string, string[]> errors, string field, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) errors[field] = [$"{field} is required."];
        else if (value.Trim().Length > maxLength) errors[field] = [$"{field} must be {maxLength} characters or fewer."];
    }

    private static void AddOptional(Dictionary<string, string[]> errors, string field, string? value, int maxLength)
    {
        if (!string.IsNullOrWhiteSpace(value) && value.Trim().Length > maxLength)
            errors[field] = [$"{field} must be {maxLength} characters or fewer."];
    }

    private static void EnsureId(Guid id, string name)
    {
        if (id == Guid.Empty) throw new ArgumentException($"{name} is required.", name);
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private static DateTime NormalizeStoredUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static string? NormalizeCorrelationId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 128)];

    internal static SalesMeetingSessionResponse ToResponse(SalesMeetingSession session) =>
        new(
            session.Id,
            session.CompanyId,
            session.InvitationId,
            session.LeadId,
            session.DealId,
            session.ContactId,
            session.CustomerCompanyId,
            session.MeetingGoal,
            session.IntendedAudience,
            session.PlannedDurationMinutes,
            session.DemoScenario,
            session.ProviderMeetingId,
            session.Status.ToStorageValue(),
            session.CurrentSlideIndex,
            session.CurrentTalkingPointIndex,
            session.ResumeMarker,
            session.ConsentStatus.ToStorageValue(),
            session.ConsentRecordedUtc,
            session.ConsentRecordedByUserId,
            session.RetentionPolicy.ToStorageValue(),
            session.RetentionDays,
            session.RetentionStartsUtc,
            session.RetentionUntilUtc,
            session.StatusReason,
            session.EndedUtc,
            session.CreatedByUserId,
            session.UpdatedByUserId,
            session.CreatedUtc,
            session.UpdatedUtc,
            session.ConcurrencyVersion);

    private sealed record ResolvedRelationships(Guid CustomerCompanyId);
}
