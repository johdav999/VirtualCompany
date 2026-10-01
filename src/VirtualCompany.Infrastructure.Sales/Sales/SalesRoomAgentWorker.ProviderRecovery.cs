using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Sales;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Sales;

internal sealed partial class SalesRoomAgentWorker
{
    private sealed class ProviderSessionRecovery : Exception;

    internal async Task<bool> ProviderRecoveryAllowedAsync(SalesRoomAgentWorkItem work, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var room = await db.SalesBrowserRooms.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == work.CompanyId && x.Id == work.RoomId, ct);
        if (room is null || !Options.HybridConversationEnabled || !Options.SemanticConversationInputEnabled ||
            AdmissionBlocked() || !room.IsAgentOwner(work.LeaseOwnerId, work.Generation, Now) ||
            room.State != SalesBrowserRoomStates.Live || room.ExpiresUtc <= Now ||
            room.AgentStartedUtc <= Now.AddMinutes(-Options.MaximumSessionMinutes) ||
            room.AgentLastErrorCode == "host_takeover" ||
            SalesRoomOperationsPolicy.AudioLimitProblem(room, Options) is not null ||
            SalesRoomOperationsPolicy.EstimatedSpend(room, Options) >= Options.MaximumSpendPerCallUsd ||
            await CompanyMonthlySpendAsync(room.CompanyId, ct) >= Options.MaximumMonthlySpendPerCompanyUsd ||
            !await HasAllConsentAsync(room, ct)) return false;
        if (!await db.CompanyMemberships.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.CompanyId == work.CompanyId && x.UserId == room.OrganizerUserId && x.Status == CompanyMembershipStatus.Active, ct) ||
            !await db.Agents.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.CompanyId == work.CompanyId && x.Id == room.AgentId && x.Status == AgentStatus.Active, ct) ||
            !await db.SalesRoomFloors.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.CompanyId == work.CompanyId && x.RoomId == room.Id && x.ControlMode == "autonomous", ct) ||
            !await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.CompanyId == work.CompanyId && x.RoomId == room.Id && x.MemberUserId == room.OrganizerUserId &&
                x.State == SalesRoomParticipantStates.Admitted && x.ExpiresUtc > Now, ct)) return false;
        // Reuse the same configured presenter/tool authorization as Start agent. No new grants.
        await using var scope = captureScopes.CreateAsyncScope();
        try
        {
            var presenter = await scope.ServiceProvider.GetRequiredService<ITeamsMeetingPresenterService>()
                .ResolveAsync(room.CompanyId, room.MeetingSessionId!.Value, ct);
            return presenter.AgentId == room.AgentId && presenter.Tools.Count > 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or TeamsCallControlException or KeyNotFoundException)
        { return false; }
    }

    internal async Task<bool> PrepareProviderRecoveryAsync(SalesRoomAgentWorkItem work, CancellationToken ct)
    {
        if (!await ProviderRecoveryAllowedAsync(work, ct)) return false;
        var room = await RoomAsync(work, ct);
        room.AgentReconnecting(work.LeaseOwnerId, work.Generation);
        var floor = await db.SalesRoomFloors.IgnoreQueryFilters().SingleAsync(x =>
            x.CompanyId == work.CompanyId && x.RoomId == work.RoomId, ct);
        floor.AgentCompleted(floor.HostParticipantId, Now, preserveNarrationCheckpoint: true);
        floor.PauseAt(floor.ResumeOffsetMilliseconds, room.AgentTurnGeneration, Now);
        foreach (var pending in await db.SalesRoomAgentSpeech.IgnoreQueryFilters().Where(x =>
            x.CompanyId == work.CompanyId && x.RoomId == work.RoomId && x.AgentGeneration == work.Generation &&
            (x.Status == SalesRoomAgentSpeechStates.Queued || x.Status == SalesRoomAgentSpeechStates.Processing)).ToListAsync(ct))
            pending.Fail(SalesRoomAgentSpeechStates.Interrupted, "provider_session_replaced",
                "The voice session changed. This pending speech will not replay; ask again or explicitly resume.", Now);
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), work.CompanyId, AuditActorTypes.System, null,
            "sales.browser_room.provider_recovery", "sales_browser_room", room.Id.ToString("D"),
            AuditEventOutcomes.Succeeded, "Voice recovery discarded pending speech and preserved usage and the paused checkpoint.",
            occurredUtc: Now));
        await db.SaveChangesAsync(ct);
        return true;
    }
}
