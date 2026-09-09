using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

internal sealed class TeamsCallControlService(
    VirtualCompanyDbContext db, ICompanyOutboxEnqueuer outbox, IAuditEventWriter audit,
    IOptions<TeamsPresenterOptions> configured, TimeProvider timeProvider,
    ITeamsPresenterRolloutPolicy rolloutPolicy,
    ITeamsMeetingMediaCoordinator mediaCoordinator, ITeamsMeetingPresenterService presenters) : ITeamsCallControlService
{
    public async Task<TeamsMeetingCallDto?> GetAsync(Guid companyId, Guid actorUserId, Guid meetingSessionId, CancellationToken ct)
    {
        await RequireMemberAsync(companyId, actorUserId, ct);
        var call = await db.TeamsMeetingCalls.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.MeetingSessionId == meetingSessionId, ct);
        return call is null ? null : Map(call);
    }

    public async Task<TeamsMeetingCallDto> RequestJoinAsync(Guid companyId, Guid actorUserId, Guid meetingSessionId,
        RequestTeamsCallJoin request, string? correlationId, CancellationToken ct)
    {
        var context = await ValidateAsync(companyId, actorUserId, meetingSessionId, ct);
        await EnsureRolloutAsync(companyId, actorUserId, context.Registration.EntraTenantId, ct, meetingSessionId);
        var existing = await db.TeamsMeetingCalls.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.MeetingSessionId == meetingSessionId, ct);
        if (existing is not null)
        {
            if (request.ExpectedVersion.HasValue && request.ExpectedVersion != existing.ConcurrencyVersion) throw Conflict();
            if (TeamsMeetingCallStates.IsActive(existing.State)) return Map(existing);
            if (!request.ExpectedVersion.HasValue) throw new TeamsCallControlException(TeamsCallControlProblemCodes.VersionConflict,
                "Confirm the terminal call version before asking Alex to rejoin.");
            var key = existing.RequestJoin(Hash(context.Invitation.OnlineMeetingUrl!), context.Meeting.ConcurrencyVersion,
                context.Registration.ConcurrencyVersion, "pending", Now());
            existing.BindPresenter(context.Meeting.PresenterAgentId!.Value, configured.Value.LiveUatApproved ? null : context.Registration.FirstUatAuthorizedUtc);
            Enqueue(existing, "join", existing.ActionVersion, correlationId, key);
            await audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Human, actorUserId,
                "sales.teams_call.rejoin_requested", "teams_meeting_call", existing.Id.ToString("D"), AuditEventOutcomes.Started,
                "The meeting organizer confirmed a new durable Alex call generation.", CorrelationId: correlationId), ct);
            await db.SaveChangesAsync(ct); TeamsCallTelemetry.JoinRequests.Add(1); return Map(existing);
        }
        var now = Now();
        var call = new TeamsMeetingCall(Guid.NewGuid(), companyId, meetingSessionId, context.Registration.Id,
            actorUserId, Hash(context.Invitation.OnlineMeetingUrl!), context.Meeting.ConcurrencyVersion,
            context.Registration.ConcurrencyVersion, "pending", now);
        call.BindPresenter(context.Meeting.PresenterAgentId!.Value, configured.Value.LiveUatApproved ? null : context.Registration.FirstUatAuthorizedUtc);
        db.TeamsMeetingCalls.Add(call);
        Enqueue(call, "join", call.ActionVersion, correlationId);
        await audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Human, actorUserId,
            "sales.teams_call.join_requested", "teams_meeting_call", call.Id.ToString("D"), AuditEventOutcomes.Started,
            "The meeting organizer requested durable Teams call admission for Alex.", CorrelationId: correlationId), ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var winner = await db.TeamsMeetingCalls.AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.MeetingSessionId == meetingSessionId, ct);
            if (winner is null) throw;
            return Map(winner);
        }
        TeamsCallTelemetry.JoinRequests.Add(1);
        return Map(call);
    }

    public async Task<TeamsMeetingCallDto?> RequestLeaveAsync(Guid companyId, Guid actorUserId, Guid meetingSessionId,
        RequestTeamsCallLeave request, string? correlationId, CancellationToken ct)
    {
        await RequireMemberAsync(companyId, actorUserId, ct);
        var call = await db.TeamsMeetingCalls.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.MeetingSessionId == meetingSessionId, ct);
        if (call is null) return null;
        EnsureOrganizer(call, actorUserId); EnsureVersion(call, request.ExpectedVersion);
        if (!TeamsMeetingCallStates.IsActive(call.State)) return Map(call);
        var key = call.RequestLeave(Now());
        Enqueue(call, "leave", call.ActionVersion, correlationId, key);
        await audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Human, actorUserId,
            "sales.teams_call.leave_requested", "teams_meeting_call", call.Id.ToString("D"), AuditEventOutcomes.Started,
            "The organizer requested that Alex leave the Teams meeting.", CorrelationId: correlationId), ct);
        await db.SaveChangesAsync(ct); TeamsCallTelemetry.LeaveRequests.Add(1); return Map(call);
    }

    public async Task<TeamsMeetingCallDto?> RequestReconciliationAsync(Guid companyId, Guid actorUserId, Guid meetingSessionId,
        RequestTeamsCallReconciliation request, string? correlationId, CancellationToken ct)
    {
        await RequireMemberAsync(companyId, actorUserId, ct);
        var call = await db.TeamsMeetingCalls.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.MeetingSessionId == meetingSessionId, ct);
        if (call is null) return null;
        EnsureOrganizer(call, actorUserId); EnsureVersion(call, request.ExpectedVersion);
        if (string.IsNullOrWhiteSpace(call.ProviderCallId)) throw new TeamsCallControlException(TeamsCallControlProblemCodes.ProviderAmbiguous, "The provider call identity is not yet known; automatic reconciliation remains pending.");
        var key = call.RequestReconciliation(Now()); Enqueue(call, "reconcile", call.ActionVersion, correlationId, key);
        await db.SaveChangesAsync(ct); TeamsCallTelemetry.Reconciliations.Add(1); return Map(call);
    }

    public async Task<TeamsMeetingCallDto> AuthorizeMediaStartAsync(Guid companyId, Guid actorUserId,
        Guid meetingSessionId, RequestTeamsCallMediaStart request, string? correlationId, CancellationToken ct)
    {
        var context = await ValidateAsync(companyId, actorUserId, meetingSessionId, ct);
        await EnsureRolloutAsync(companyId, actorUserId, context.Registration.EntraTenantId, ct, meetingSessionId);
        var call = await RequiredCallAsync(companyId, meetingSessionId, ct);
        EnsureOrganizer(call, actorUserId);
        if (call.MediaStartAuthorizedUtc.HasValue) return Map(call);
        try { call.AuthorizeMediaStart(actorUserId, request.ExpectedVersion, Now()); }
        catch (InvalidOperationException exception) { throw new TeamsCallControlException(
            TeamsCallControlProblemCodes.VersionConflict, exception.Message); }
        await audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Human, actorUserId,
            "sales.teams_call.media_start_authorized", "teams_meeting_call", call.Id.ToString("D"),
            AuditEventOutcomes.Succeeded, "The organizer explicitly authorized Alex to hear and speak in this meeting.",
            CorrelationId: correlationId), ct);
        await db.SaveChangesAsync(ct);
        await mediaCoordinator.StartIfReadyAsync(companyId, call.Id, ct);
        return Map(call);
    }

    public async Task<TeamsMeetingCallDto> StopMediaAsync(Guid companyId, Guid actorUserId,
        Guid meetingSessionId, RequestTeamsCallMediaStop request, string? correlationId, CancellationToken ct)
    {
        await RequireMemberAsync(companyId, actorUserId, ct);
        var call = await RequiredCallAsync(companyId, meetingSessionId, ct);
        EnsureOrganizer(call, actorUserId);
        if (!call.MediaStartAuthorizedUtc.HasValue)
        {
            await mediaCoordinator.StopAsync(call.Id, request.Reason, ct);
            return Map(call);
        }
        try { call.StopMedia(actorUserId, request.ExpectedVersion, Now()); }
        catch (InvalidOperationException exception) { throw new TeamsCallControlException(
            TeamsCallControlProblemCodes.VersionConflict, exception.Message); }
        await audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Human, actorUserId,
            "sales.teams_call.media_stopped", "teams_meeting_call", call.Id.ToString("D"),
            AuditEventOutcomes.Succeeded, "The organizer stopped Alex meeting audio; typed controls remain available.",
            CorrelationId: correlationId), ct);
        await db.SaveChangesAsync(ct);
        await mediaCoordinator.StopAsync(call.Id, request.Reason, ct);
        return Map(call);
    }

    public async Task<TeamsMeetingCallDto> RevokeConsentAsync(Guid companyId, Guid actorUserId,
        Guid meetingSessionId, RequestTeamsCallConsentRevocation request, string? correlationId, CancellationToken ct)
    {
        await RequireMemberAsync(companyId, actorUserId, ct);
        var call = await RequiredCallAsync(companyId, meetingSessionId, ct);
        EnsureOrganizer(call, actorUserId);
        var meeting = await db.SalesMeetingSessions.SingleAsync(x => x.CompanyId == companyId && x.Id == meetingSessionId, ct);
        if (meeting.ConsentStatus != SalesMeetingConsentStatus.Revoked)
        {
            EnsureVersion(call, request.ExpectedVersion);
            meeting.RevokeMediaConsent(actorUserId, Now());
            if (call.MediaStartAuthorizedUtc.HasValue)
                call.StopMedia(actorUserId, request.ExpectedVersion, Now());
            await audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Human, actorUserId,
                "sales.teams_call.consent_revoked", "sales_meeting_session", meeting.Id.ToString("D"),
                AuditEventOutcomes.Succeeded,
                "The organizer revoked meeting-media consent. Audio stopped immediately; retained artifacts follow the selected retention policy.",
                CorrelationId: correlationId), ct);
            await db.SaveChangesAsync(ct);
        }
        await mediaCoordinator.StopAsync(call.Id, "consent_revoked", ct);
        return Map(call);
    }

    private async Task<(SalesMeetingSession Meeting, SalesMeetingInvitation Invitation, TeamsTenantRegistration Registration)> ValidateAsync(Guid companyId, Guid actorUserId, Guid meetingSessionId, CancellationToken ct)
    {
        await RequireMemberAsync(companyId, actorUserId, ct);
        var meeting = await db.SalesMeetingSessions.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == meetingSessionId, ct)
            ?? throw new KeyNotFoundException("The sales meeting session was not found.");
        var invitation = await db.SalesMeetingInvitations.SingleAsync(x => x.CompanyId == companyId && x.Id == meeting.InvitationId, ct);
        if (invitation.CreatedByUserId != actorUserId || meeting.CreatedByUserId != actorUserId)
            throw new TeamsCallControlException(TeamsCallControlProblemCodes.OrganizerRequired, "Only the company meeting organizer can command Alex to join.");
        if (meeting.ConsentStatus != SalesMeetingConsentStatus.Granted) throw new TeamsCallControlException(TeamsCallControlProblemCodes.ConsentRequired, "Explicit meeting-media consent is required.");
        if (meeting.RetentionUntilUtc <= Now() || invitation.Status != SalesMeetingInvitationStatus.Scheduled || string.IsNullOrWhiteSpace(invitation.OnlineMeetingUrl))
            throw new TeamsCallControlException(TeamsCallControlProblemCodes.MeetingUnavailable, "A current scheduled Teams meeting with retained consent is required.");
        var registration = await db.TeamsTenantRegistrations.SingleOrDefaultAsync(x => x.CompanyId == companyId, ct)
            ?? throw new TeamsCallControlException(TeamsCallControlProblemCodes.NotReady, "The company has no Teams tenant registration.");
        if (registration.Status != TeamsTenantRegistrationStates.Ready || !configured.Value.Enabled || !configured.Value.CallControlEnabled)
            throw new TeamsCallControlException(TeamsCallControlProblemCodes.NotReady, "Teams call control is not ready for this company.");
        await presenters.ResolveAsync(companyId, meetingSessionId, ct);
        return (meeting, invitation, registration);
    }

    private async Task RequireMemberAsync(Guid companyId, Guid userId, CancellationToken ct)
    {
        if (!await db.CompanyMemberships.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.UserId == userId && x.Status == CompanyMembershipStatus.Active, ct))
            throw new UnauthorizedAccessException("An active company membership is required.");
    }
    private async Task<TeamsMeetingCall> RequiredCallAsync(Guid companyId, Guid meetingSessionId, CancellationToken ct) =>
        await db.TeamsMeetingCalls.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.MeetingSessionId == meetingSessionId, ct)
        ?? throw new KeyNotFoundException("The Teams meeting call was not found.");
    private async Task EnsureRolloutAsync(Guid companyId, Guid actorUserId, Guid tenantId, CancellationToken ct, Guid meetingSessionId)
    {
        var decision = await rolloutPolicy.EvaluateAsync(companyId, actorUserId, tenantId, ct, meetingSessionId);
        if (!decision.Allowed) throw new TeamsCallControlException(decision.ReasonCode, decision.Message);
    }
    private void Enqueue(TeamsMeetingCall c, string action, long version, string? correlationId, string? key = null) =>
        outbox.Enqueue(c.CompanyId, CompanyOutboxTopics.TeamsCallControlRequested,
            new TeamsCallControlWorkItem(c.CompanyId, c.Id, version, action, correlationId), correlationId,
            idempotencyKey: key ?? c.JoinIdempotencyKey, messageType: nameof(TeamsCallControlWorkItem));
    private static void EnsureOrganizer(TeamsMeetingCall c, Guid actor) { if (c.OrganizerUserId != actor) throw new TeamsCallControlException(TeamsCallControlProblemCodes.OrganizerRequired, "Only the meeting organizer can control this call."); }
    private static void EnsureVersion(TeamsMeetingCall c, long version) { if (c.ConcurrencyVersion != version) throw Conflict(); }
    private static TeamsCallControlException Conflict() => new(TeamsCallControlProblemCodes.VersionConflict, "The Teams call changed after it was opened.");
    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static TeamsMeetingCallDto Map(TeamsMeetingCall c) => new(c.Id, c.MeetingSessionId, c.State, c.ProviderState,
        c.SafeProviderReference, c.Action, c.ActionVersion, c.MediaHostInstanceId, c.FailureCode, c.FailureSummary,
        c.RequestedUtc, c.AdmittedUtc, c.ConnectedUtc, c.EndingUtc, c.EndedUtc, c.UpdatedUtc, c.ConcurrencyVersion,
        c.State == TeamsMeetingCallStates.WaitingInLobby,
        c.MediaStartAuthorizedUtc.HasValue,
        c.MediaStartAuthorizedByUserId,
        c.MediaStartAuthorizedUtc);
}

internal static class TeamsCallTelemetry
{
    private static readonly Meter Meter = new("VirtualCompany.Teams.CallControl", "1.0.0");
    public static readonly Counter<long> JoinRequests = Meter.CreateCounter<long>("teams.call.join.requests");
    public static readonly Counter<long> LeaveRequests = Meter.CreateCounter<long>("teams.call.leave.requests");
    public static readonly Counter<long> Callbacks = Meter.CreateCounter<long>("teams.call.callbacks");
    public static readonly Counter<long> CallbackDuplicates = Meter.CreateCounter<long>("teams.call.callback.duplicates");
    public static readonly Counter<long> Failures = Meter.CreateCounter<long>("teams.call.failures");
    public static readonly Counter<long> Reconciliations = Meter.CreateCounter<long>("teams.call.reconciliations");
    public static readonly Histogram<double> ProviderLatency = Meter.CreateHistogram<double>("teams.call.provider.latency", "ms");
}
