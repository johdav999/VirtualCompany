using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesMeetingPresentationPreparationQuery(
    VirtualCompanyDbContext db,
    IOptions<SalesPresentationOptions> presentationOptions)
    : ISalesMeetingPresentationPreparationQuery
{
    public async Task<SalesMeetingPresentationPreparationResponse?> GetAsync(
        Guid companyId,
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        EnsureId(companyId, nameof(companyId));
        EnsureId(invitationId, nameof(invitationId));

        var invitation = await db.SalesMeetingInvitations.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId && x.Id == invitationId,
                cancellationToken);
        if (invitation is null) return null;

        var leadContext = await db.Leads.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Id == invitation.LeadId)
            .Select(x => new { x.Title, x.CustomerCompanyId })
            .SingleOrDefaultAsync(cancellationToken);
        var customerCompanyName = leadContext?.CustomerCompanyId is Guid customerCompanyId
            ? await db.CustomerCompanies.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Id == customerCompanyId)
                .Select(x => x.Name)
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        var session = await db.SalesMeetingSessions.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId && x.InvitationId == invitationId,
                cancellationToken);

        var agentEntities = await db.Agents.AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.Status == AgentStatus.Active &&
                        x.Department == "Sales")
            .OrderBy(x => x.DisplayName)
            .ThenBy(x => x.Id)
            .ToArrayAsync(cancellationToken);
        var agents = agentEntities
            .Select(x => new SalesMeetingPreparationAgentDto(
                x.Id, x.DisplayName, x.RoleName, x.TemplateId, x.Department,
                x.Status.ToStorageValue(), x.AvatarUrl))
            .ToArray();

        var deckEntities = session is null
            ? []
            : await db.SalesPresentationDecks.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.SessionId == session.Id)
                .OrderByDescending(x => x.Version)
                .ThenBy(x => x.Id)
                .ToArrayAsync(cancellationToken);
        var decks = deckEntities
            .Select(x => new SalesMeetingPreparationDeckDto(
                x.Id, x.AgentId, x.Version, x.Title, x.OriginalFileName,
                x.Status.ToStorageValue(), x.ProcessingVersion,
                x.ProcessingAttemptCount, x.SlideCount, x.FailureCode,
                x.FailureSummary, x.CanRetry, x.IsActive, x.CreatedUtc,
                x.UpdatedUtc, x.ProcessedUtc, x.FailedUtc, x.ActivatedUtc,
                x.ConcurrencyVersion))
            .ToArray();

        var hasProviderEvent = !string.IsNullOrWhiteSpace(invitation.ExternalEventId);
        var invitationEligible =
            invitation.Status == SalesMeetingInvitationStatus.Scheduled && hasProviderEvent;
        var actions = new List<string>();
        var blockers = new List<SalesMeetingPreparationBlockerDto>();
        var readiness = SalesMeetingPreparationReadinessStates.Blocked;
        Guid? activeDeckId = null;
        var activeDeckSlideCount = 0;
        var canOpenPresenter = false;

        if (session is null)
        {
            if (leadContext?.CustomerCompanyId is null)
            {
                blockers.Add(new(
                    SalesMeetingPreparationReasonCodes.CustomerCompanyMissing,
                    "Associate this lead with a customer company before creating its presentation session."));
            }

            if (invitation.Status != SalesMeetingInvitationStatus.Scheduled)
            {
                blockers.Add(new(
                    SalesMeetingPreparationReasonCodes.InvitationNotScheduled,
                    invitation.Status switch
                    {
                        SalesMeetingInvitationStatus.WaitingForApproval =>
                            "Review and approve this invitation, then wait for the calendar event to be confirmed before creating its presentation session.",
                        SalesMeetingInvitationStatus.Failed =>
                            "Retry this invitation from the sales lead, then wait for the calendar event to be confirmed before creating its presentation session.",
                        SalesMeetingInvitationStatus.Rejected =>
                            "This invitation was not approved. Prepare a new invitation before creating a presentation session.",
                        SalesMeetingInvitationStatus.Cancelled =>
                            "This invitation was cancelled. Prepare a new invitation before creating a presentation session.",
                        _ =>
                            "Finish scheduling this invitation before creating its presentation session."
                    }));
            }
            else if (!hasProviderEvent)
            {
                blockers.Add(new(
                    SalesMeetingPreparationReasonCodes.ProviderEventMissing,
                    "The scheduled invitation has no provider event yet. Reconcile the calendar event before creating a presentation session."));
            }

            if (agents.Length == 0)
            {
                blockers.Add(new(
                    SalesMeetingPreparationReasonCodes.EligibleSalesAgentMissing,
                    "Add or activate a Sales agent before preparing this presentation."));
            }

            if (invitationEligible && agents.Length > 0 && leadContext?.CustomerCompanyId is not null)
            {
                actions.Add(SalesMeetingPreparationActionValues.CreateSession);
                blockers.Add(new(
                    SalesMeetingPreparationReasonCodes.SessionMissing,
                    "Create the meeting session to configure the presentation and upload a deck."));
                readiness = SalesMeetingPreparationReadinessStates.SessionRequired;
            }
        }
        else
        {
            if (session.Status == SalesMeetingSessionStatus.Ready)
            {
                actions.Add(SalesMeetingPreparationActionValues.UpdateSession);
            }

            if (agents.Length > 0)
            {
                actions.Add(SalesMeetingPreparationActionValues.UploadDeck);
            }
            else
            {
                blockers.Add(new(
                    SalesMeetingPreparationReasonCodes.EligibleSalesAgentMissing,
                    "Add or activate a Sales agent before uploading or presenting a deck."));
            }

            if (decks.Any(x => x.Status == "failed" && x.CanRetry))
            {
                actions.Add(SalesMeetingPreparationActionValues.RetryProcessing);
            }

            if (decks.Any(x => x.Status == "processed" && !x.IsActive))
            {
                actions.Add(SalesMeetingPreparationActionValues.ActivateDeck);
            }

            var activeDeck = decks.SingleOrDefault(x => x.IsActive);
            if (activeDeck is not null && activeDeck.Status == "processed")
            {
                activeDeckId = activeDeck.Id;
                activeDeckSlideCount = activeDeck.SlideCount;
                if (agents.Any(x => x.Id == activeDeck.AgentId))
                {
                    canOpenPresenter = true;
                    actions.Add(SalesMeetingPreparationActionValues.OpenPresenter);
                    readiness = SalesMeetingPreparationReadinessStates.Ready;
                }
                else
                {
                    blockers.Add(new(
                        SalesMeetingPreparationReasonCodes.ActiveDeckAgentIneligible,
                        "The active deck is assigned to an agent who is no longer eligible to present. Upload or activate a deck assigned to an active Sales agent."));
                }
            }

            if (!canOpenPresenter &&
                (activeDeck is null || activeDeck.Status != "processed"))
            {
                AddDeckBlocker(decks, blockers, ref readiness);
            }
        }

        return new SalesMeetingPresentationPreparationResponse(
            companyId, invitation.Id, invitation.LeadId,
            leadContext?.Title ?? invitation.Title, customerCompanyName, session?.Id,
            presentationOptions.Value.MaximumUploadBytes,
            new SalesMeetingPreparationInvitationDto(
                invitation.Id, invitation.LeadId, invitation.DealId, invitation.ContactId,
                invitation.Title, invitation.StartsUtc, invitation.EndsUtc,
                invitation.TimeZoneId, invitation.Location, invitation.CreateOnlineMeeting,
                invitation.Provider.ToStorageValue(), invitation.Status.ToStorageValue(),
                hasProviderEvent, invitationEligible),
            session is null ? null : MapSession(session),
            agents, decks, activeDeckId, activeDeckSlideCount, readiness,
            canOpenPresenter, blockers, actions);
    }

    private static void AddDeckBlocker(
        IReadOnlyCollection<SalesMeetingPreparationDeckDto> decks,
        ICollection<SalesMeetingPreparationBlockerDto> blockers,
        ref string readiness)
    {
        if (decks.Count == 0)
        {
            blockers.Add(new(
                SalesMeetingPreparationReasonCodes.DeckMissing,
                "Upload a PowerPoint deck before opening the presenter."));
            readiness = SalesMeetingPreparationReadinessStates.DeckRequired;
            return;
        }

        if (decks.Any(x => x.Status is "pending_scan" or "processing"))
        {
            blockers.Add(new(
                SalesMeetingPreparationReasonCodes.DeckProcessing,
                "The presentation deck is still being processed. Wait for processing to finish."));
            readiness = SalesMeetingPreparationReadinessStates.Processing;
            return;
        }

        if (decks.Any(x => x.Status == "processed" && !x.IsActive))
        {
            blockers.Add(new(
                SalesMeetingPreparationReasonCodes.ActiveDeckMissing,
                "Activate a processed deck before opening the presenter."));
            readiness = SalesMeetingPreparationReadinessStates.ActivationRequired;
            return;
        }

        var failed = decks.FirstOrDefault(x => x.Status == "failed");
        if (failed is not null)
        {
            blockers.Add(new(
                SalesMeetingPreparationReasonCodes.DeckProcessingFailed,
                failed.CanRetry
                    ? "Deck processing failed and can be retried."
                    : "Deck processing failed. Upload a corrected PowerPoint deck."));
            readiness = SalesMeetingPreparationReadinessStates.Blocked;
            return;
        }

        blockers.Add(new(
            SalesMeetingPreparationReasonCodes.DeckProcessingBlocked,
            "Deck processing was blocked. Review the safe failure summary and upload a corrected PowerPoint deck."));
        readiness = SalesMeetingPreparationReadinessStates.Blocked;
    }

    private static SalesMeetingSessionResponse MapSession(SalesMeetingSession session) =>
        new(
            session.Id, session.CompanyId, session.InvitationId, session.LeadId,
            session.DealId, session.ContactId, session.CustomerCompanyId,
            session.MeetingGoal, session.IntendedAudience, session.PlannedDurationMinutes,
            session.DemoScenario, session.ProviderMeetingId, session.Status.ToStorageValue(),
            session.CurrentSlideIndex, session.CurrentTalkingPointIndex, session.ResumeMarker,
            session.ConsentStatus.ToStorageValue(), session.ConsentRecordedUtc,
            session.ConsentRecordedByUserId, session.RetentionPolicy.ToStorageValue(),
            session.RetentionDays, session.RetentionStartsUtc, session.RetentionUntilUtc,
            session.StatusReason, session.EndedUtc, session.CreatedByUserId,
            session.UpdatedByUserId, session.CreatedUtc, session.UpdatedUtc,
            session.ConcurrencyVersion);

    private static void EnsureId(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("A non-empty identifier is required.", parameterName);
    }
}
