using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

internal sealed class TeamsCallControlDispatcher(
    VirtualCompanyDbContext db, ITeamsCallControlAdapter adapter, ICompanyOutboxEnqueuer outbox,
    IAuditEventWriter audit, IOptions<TeamsPresenterOptions> configured, TimeProvider timeProvider,
    ITeamsMediaHostRuntime mediaHostRuntime,
    ITeamsPresenterRolloutPolicy rolloutPolicy,
    ITeamsMeetingPresenterService presenters,
    ITeamsMeetingMediaCoordinator? mediaCoordinator = null) : ITeamsCallControlDispatcher
{
    public async Task DispatchAsync(TeamsCallControlWorkItem message, CancellationToken ct)
    {
        var call = await db.TeamsMeetingCalls.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == message.CompanyId && x.Id == message.CallId, ct);
        if (call is null || call.ActionVersion != message.ActionVersion) return;
        var context = await ContextAsync(call, ct);
        if (message.Action == "join")
        {
            await EnforceExecutionPolicyAsync(call, context, ct);
            var applicationHosted = string.Equals(configured.Value.MediaRoute, "teams_application_hosted", StringComparison.Ordinal);
            var mediaHost = applicationHosted
                ? mediaHostRuntime.GetStatus().HostInstanceId
                : configured.Value.CallControlHostId;
            if (call.MediaHostInstanceId is "pending" or "unassigned")
            {
                if (applicationHosted) await mediaHostRuntime.ReserveCallAsync(call.Id, ct);
                if (string.IsNullOrWhiteSpace(mediaHost) || mediaHost is "pending" or "unassigned")
                    throw new TeamsCallControlException(TeamsCallControlProblemCodes.NotReady,
                        "No approved call-control host is available.");
                call.AssignMediaHost(mediaHost, Now());
                await db.SaveChangesAsync(ct);
            }
            if (!string.Equals(call.MediaHostInstanceId, mediaHost, StringComparison.Ordinal))
                throw new TeamsMeetingMediaException(TeamsMeetingMediaProblemCodes.HostMismatch,
                    "The durable join is waiting for its pinned media host.");
            call.BeginJoin(Now()); await db.SaveChangesAsync(ct);
            var callbackUrl = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(configured.Value.BotCallingCallbackUrl, "callId", call.Id.ToString("D"));
            var callback = new Uri(Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(callbackUrl, "joinGeneration", call.JoinGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            var result = await adapter.JoinAsync(new TeamsCallJoinContext(call.CompanyId, call.Id, context.Registration.EntraTenantId,
                Permissions(context.Registration.RequiredPermissions), context.Invitation.OnlineMeetingUrl!, callback,
                call.JoinIdempotencyKey, call.MediaHostInstanceId), ct);
            await ApplyAsync(call, result, true, message.CorrelationId, ct);
            return;
        }
        if (string.IsNullOrWhiteSpace(call.ProviderCallId))
        {
            call.MarkAmbiguous(TeamsCallControlProblemCodes.ProviderAmbiguous, "The provider call identity is unavailable; operator reconciliation is required.", Now());
            await db.SaveChangesAsync(ct); return;
        }
        var providerContext = new TeamsCallProviderContext(call.CompanyId, call.Id, context.Registration.EntraTenantId,
            Permissions(context.Registration.RequiredPermissions), call.ProviderCallId,
            $"teams-call:{call.CompanyId:N}:{call.MeetingSessionId:N}:{message.Action}:v{message.ActionVersion}", call.MediaHostInstanceId);
        var providerResult = message.Action is "leave" or "terminate"
            ? await adapter.LeaveAsync(providerContext, ct)
            : await adapter.GetAsync(providerContext, ct);
        await ApplyAsync(call, providerResult, false, message.CorrelationId, ct);
    }

    public async Task ProcessCallbackAsync(TeamsCallCallbackWorkItem message, CancellationToken ct)
    {
        var receipt = await db.TeamsCallNotificationReceipts.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == message.CompanyId && x.Id == message.ReceiptId && x.CallId == message.CallId, ct);
        if (receipt is null || receipt.ProcessedUtc.HasValue) return;
        var call = await db.TeamsMeetingCalls.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == message.CompanyId && x.Id == message.CallId, ct);
        var applied = call.ApplyProviderState(receipt.NormalizedState, receipt.Sequence, receipt.ResourceVersion, Now());
        receipt.Complete(!applied, Now()); await db.SaveChangesAsync(ct);
        if (applied && call.State == TeamsMeetingCallStates.Connected && mediaCoordinator is not null)
            await mediaCoordinator.StartIfReadyAsync(call.CompanyId, call.Id, ct);
        else if (applied &&
                 call.State is (TeamsMeetingCallStates.Ended or TeamsMeetingCallStates.Rejected or TeamsMeetingCallStates.Failed) &&
                 mediaCoordinator is not null)
            await mediaCoordinator.StopAsync(call.Id, call.State, ct);
    }

    private async Task EnforceExecutionPolicyAsync(TeamsMeetingCall call,
        (SalesMeetingSession Meeting, SalesMeetingInvitation Invitation, TeamsTenantRegistration Registration) c, CancellationToken ct)
    {
        await presenters.ResolveAsync(call.CompanyId, call.MeetingSessionId, ct);
        if (call.PresenterAgentId != c.Meeting.PresenterAgentId) throw new TeamsCallControlException(TeamsCallControlProblemCodes.NotReady, "Meeting presenter changed before call execution.");
        if (!await db.CompanyMemberships.IgnoreQueryFilters().AnyAsync(m => m.CompanyId == call.CompanyId &&
            m.UserId == call.OrganizerUserId && m.Status == CompanyMembershipStatus.Active, ct))
            throw new TeamsCallControlException(TeamsCallControlProblemCodes.OrganizerRequired, "The organizer membership is no longer active.");
        var o = configured.Value;
        var rollout = await rolloutPolicy.EvaluateAsync(call.CompanyId, call.OrganizerUserId,
            c.Registration.EntraTenantId, ct, call.MeetingSessionId);
        if (!rollout.Allowed)
            throw new TeamsCallControlException(rollout.ReasonCode, rollout.Message);
        if (!o.Enabled || !o.CallControlEnabled || c.Registration.Status != TeamsTenantRegistrationStates.Ready ||
            c.Meeting.ConsentStatus != SalesMeetingConsentStatus.Granted || c.Meeting.RetentionUntilUtc <= Now() ||
            c.Invitation.Status != SalesMeetingInvitationStatus.Scheduled || string.IsNullOrWhiteSpace(c.Invitation.OnlineMeetingUrl))
            throw new TeamsCallControlException(TeamsCallControlProblemCodes.NotReady, "Teams call policy changed before provider execution.");
        var active = new[] { TeamsMeetingCallStates.Requested, TeamsMeetingCallStates.Joining, TeamsMeetingCallStates.WaitingInLobby,
            TeamsMeetingCallStates.Admitted, TeamsMeetingCallStates.Connected, TeamsMeetingCallStates.LeaveRequested, TeamsMeetingCallStates.Ending, TeamsMeetingCallStates.ReconciliationRequired };
        var companyCount = await db.TeamsMeetingCalls.IgnoreQueryFilters().CountAsync(x => x.CompanyId == call.CompanyId && x.Id != call.Id && active.Contains(x.State), ct);
        var hostCount = await db.TeamsMeetingCalls.IgnoreQueryFilters().CountAsync(x => x.Id != call.Id && x.MediaHostInstanceId == call.MediaHostInstanceId && active.Contains(x.State), ct);
        if (companyCount >= o.MaxActiveCallsPerCompany || hostCount >= o.MaxActiveCallsPerHost)
            throw new TeamsCallControlException(TeamsCallControlProblemCodes.CapacityReached, "The configured Teams call-control capacity has been reached.");
    }

    private async Task ApplyAsync(TeamsMeetingCall call, TeamsCallProviderResult result, bool joining, string? correlationId, CancellationToken ct)
    {
        switch (result.Outcome)
        {
            case TeamsCallProviderOutcome.Succeeded:
                if (joining && !string.IsNullOrWhiteSpace(result.ProviderCallId)) call.RecordProviderCall(result.ProviderCallId, result.SafeProviderReference, result.ProviderState ?? "establishing", Now());
                else if (result.ProviderState == "terminated" || result.Outcome == TeamsCallProviderOutcome.NotFound) call.MarkEnded("terminated", Now());
                else call.ApplyProviderState(result.ProviderState ?? "establishing", 0, null, Now());
                break;
            case TeamsCallProviderOutcome.NotFound:
                call.MarkEnded("not_found", Now()); break;
            case TeamsCallProviderOutcome.Ambiguous:
            case TeamsCallProviderOutcome.RetryableFailure:
                call.MarkAmbiguous(result.ErrorCode ?? "provider_ambiguous", result.ErrorSummary ?? "The provider outcome requires reconciliation.", Now());
                if (!string.IsNullOrWhiteSpace(call.ProviderCallId))
                {
                    var key = call.RequestReconciliation(Now());
                    outbox.Enqueue(call.CompanyId, CompanyOutboxTopics.TeamsCallControlRequested,
                        new TeamsCallControlWorkItem(call.CompanyId, call.Id, call.ActionVersion, "reconcile", correlationId), correlationId,
                        Now().AddSeconds(configured.Value.ReconciliationDelaySeconds), key, nameof(TeamsCallControlWorkItem));
                }
                TeamsCallTelemetry.Reconciliations.Add(1); break;
            default:
                call.MarkFailed(result.ErrorCode ?? "provider_rejected", result.ErrorSummary ?? "Microsoft Graph rejected the call-control request.", Now());
                TeamsCallTelemetry.Failures.Add(1); break;
        }
        await audit.WriteAsync(new AuditEventWriteRequest(call.CompanyId, "system", null,
            joining ? "sales.teams_call.join_dispatched" : "sales.teams_call.control_dispatched",
            "teams_meeting_call", call.Id.ToString("D"),
            result.Outcome == TeamsCallProviderOutcome.Succeeded ? AuditEventOutcomes.Succeeded :
            result.Outcome is TeamsCallProviderOutcome.Ambiguous or TeamsCallProviderOutcome.RetryableFailure ? AuditEventOutcomes.Started : AuditEventOutcomes.Failed,
            result.Outcome == TeamsCallProviderOutcome.Succeeded
                ? "The durable Teams call-control action completed."
                : "The durable Teams call-control action recorded a safe provider outcome.",
            Metadata: new Dictionary<string, string?> { ["state"] = call.State, ["failureCode"] = call.FailureCode },
            CorrelationId: correlationId), ct);
        await db.SaveChangesAsync(ct);
        if (call.State is (TeamsMeetingCallStates.Ended or TeamsMeetingCallStates.Rejected or TeamsMeetingCallStates.Failed) &&
            mediaCoordinator is not null)
            await mediaCoordinator.StopAsync(call.Id, call.State, ct);
    }

    private async Task<(SalesMeetingSession Meeting, SalesMeetingInvitation Invitation, TeamsTenantRegistration Registration)> ContextAsync(TeamsMeetingCall call, CancellationToken ct)
    {
        var meeting = await db.SalesMeetingSessions.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == call.CompanyId && x.Id == call.MeetingSessionId, ct);
        var invitation = await db.SalesMeetingInvitations.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == call.CompanyId && x.Id == meeting.InvitationId, ct);
        var registration = await db.TeamsTenantRegistrations.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == call.CompanyId && x.Id == call.RegistrationId, ct);
        return (meeting, invitation, registration);
    }
    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
    private static string[] Permissions(string value) => value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
