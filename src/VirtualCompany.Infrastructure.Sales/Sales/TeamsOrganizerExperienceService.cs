using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

internal sealed class TeamsOrganizerExperienceService(
    VirtualCompanyDbContext db,
    ITeamsPresenterReadinessService readinessService,
    ITeamsPresenterRolloutPolicy rolloutPolicy) : ITeamsOrganizerExperienceService
{
    public async Task<TeamsOrganizerPresenterStateDto> GetAsync(Guid companyId, Guid actorUserId,
        Guid meetingSessionId, Guid? requestEntraTenantId, CancellationToken ct)
    {
        if (!await db.CompanyMemberships.AsNoTracking().AnyAsync(member => member.CompanyId == companyId &&
                member.UserId == actorUserId && member.Status == CompanyMembershipStatus.Active, ct))
            throw new UnauthorizedAccessException("An active company membership is required.");

        var meeting = await db.SalesMeetingSessions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == meetingSessionId, ct)
            ?? throw new KeyNotFoundException("The sales meeting session was not found.");
        var invitation = await db.SalesMeetingInvitations.AsNoTracking()
            .SingleAsync(item => item.CompanyId == companyId && item.Id == meeting.InvitationId, ct);
        var call = await db.TeamsMeetingCalls.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.MeetingSessionId == meetingSessionId, ct);
        var registration = await db.TeamsTenantRegistrations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == companyId, ct);
        var rollout = await rolloutPolicy.EvaluateAsync(companyId, actorUserId, requestEntraTenantId, ct, meetingSessionId);
        var readiness = await readinessService.GetReadinessAsync(companyId, ct);
        // Company-wide readiness intentionally cannot authorize a first test. Apply the
        // meeting-scoped rollout decision only to this authorized organizer's response.
        readiness = readiness with
        {
            Rollout = rollout,
            LiveCallingReady = rollout.Allowed && readiness.EffectiveCapabilities.CallControl &&
                (!readiness.ConfiguredCapabilities.Audio || readiness.EffectiveCapabilities.Audio),
            Checks = readiness.Checks.Select(c => c.Name == TeamsPresenterReadinessCheckNames.Rollout
                ? c with { Ready = rollout.Allowed, State = rollout.Allowed ? "ready" : "blocked", ReasonCode = rollout.ReasonCode, Message = rollout.Message } : c).ToArray()
        };
        var isOrganizer = meeting.CreatedByUserId == actorUserId && invitation.CreatedByUserId == actorUserId;
        var guidance = Guidance(call, rollout, registration, isOrganizer);

        return new TeamsOrganizerPresenterStateDto(
            companyId, meetingSessionId, isOrganizer,
            meeting.ConsentStatus.ToStorageValue(), meeting.ConsentRecordedUtc, meeting.RetentionUntilUtc,
            meeting.PresentationControlMode, meeting.PresentationControlUpdatedByUserId,
            meeting.PresentationControlUpdatedUtc, invitation.OnlineMeetingUrl,
            call is null ? null : TeamsCallControlService.Map(call), readiness, rollout, guidance,
            "The selected presenter is an AI assistant. It can present the approved deck, hear and speak only after meeting consent, and cannot make commitments for your company.",
            "Revoking consent stops presenter meeting media immediately. Existing notes and artifacts remain governed by this meeting's retention policy until its displayed retention date.");
    }

    private static TeamsOrganizerGuidanceDto Guidance(TeamsMeetingCall? call,
        TeamsPresenterRolloutDto rollout, TeamsTenantRegistration? registration, bool isOrganizer)
    {
        if (!isOrganizer) return new("organizer_required", "Organizer access required",
            "Only the person who organized this meeting can invite, start, stop, or remove presenter.", [], false);
        if (!rollout.Allowed) return new(rollout.ReasonCode, "presenter is not ready for this meeting",
            rollout.Message, ["Use the typed presentation controls while an administrator resolves the rollout gate."], false);
        if (registration is null) return new("tenant_registration_missing", "Connect the Teams tenant",
            "A platform administrator must associate and approve this company's Teams tenant.", [], false);
        if (call is null) return new("request_join", "Invite presenter",
            "Request presenter to join. Microsoft Teams may place the bot in the lobby according to the meeting policy.",
            ["Select Request presenter to join.", "Wait for the provider state shown here before starting audio."], false);
        if (call.State == TeamsMeetingCallStates.WaitingInLobby) return new("native_admission_required", "Admit presenter in Teams",
            "Teams requires a human organizer to admit presenter. Virtual Company will not automate the Teams participant controls.",
            ["Open People in the Teams meeting.", "Find the configured Teams bot under Waiting in the lobby and select Admit.", "Return here and wait until the provider state changes to Connected."], true);
        if (call.State == TeamsMeetingCallStates.Connected && !call.MediaStartAuthorizedUtc.HasValue)
            return new("audio_authorization_required", "Start presenter audio when attendees are ready",
                "presenter is connected but cannot hear or speak until you explicitly start audio.",
                ["Confirm the consent indicator is Granted.", "Tell attendees presenter is an AI assistant.", "Select Start presenter audio."], false);
        if (call.State == TeamsMeetingCallStates.ReconciliationRequired)
            return new("reconciliation_required", "Confirm the provider state",
                "The last provider outcome is ambiguous. Reconcile before retrying or reporting success.",
                ["Select Reconcile state.", "If the state remains ambiguous, use Teams to remove presenter and contact support."], true);
        return new("native_stage_control", "Use verified Teams controls",
            "Share the Blazor presentation from this side panel. Teams shows Stop presenting only to the participant who started the share.",
            ["Select Share to meeting and confirm Start sharing in Teams.", "If Teams blocks sharing, verify the meeting app policy and organizer role.", "Use Teams Stop presenting to end the shared stage."], true);
    }
}
