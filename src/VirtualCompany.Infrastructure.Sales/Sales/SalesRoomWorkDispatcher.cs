using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;
public sealed class SalesRoomWorkDispatcher(VirtualCompanyDbContext db, ICompanyOutboxEnqueuer outbox,
    ISalesRoomMediaTransport media, ISalesRoomProviderInspection inspection, IOptionsMonitor<SalesRoomLifecycleOptions> options, TimeProvider clock) : ISalesRoomWorkDispatcher
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public async Task DispatchAsync(SalesRoomWorkItem work, CancellationToken ct)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var op = await db.SalesRoomOperations.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == work.CompanyId && x.RoomId == work.RoomId && x.Id == work.OperationId, ct);
        if (op == null || op.State is "completed" or "needs_review") return;
        if (op.LeaseUntilUtc > Now) throw new SalesRoomAccessException("operation_already_running");
        var room = await db.SalesBrowserRooms.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == work.CompanyId && x.Id == work.RoomId, ct);
        if (op.Action == "expire" && room.ExpiresUtc > Now) throw new SalesRoomAccessException("expiry_not_due");
        op.Claim(Now);
        outbox.Enqueue(room.CompanyId, SalesBrowserRoomService.Topic, work, availableAtUtc: op.LeaseUntilUtc,
            idempotencyKey: $"room:{op.Id:N}:lease:{op.Attempts}");
        await db.SaveChangesAsync(ct);
        var dispatchToken = ct;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(90)); ct = deadline.Token;
        // Outbox owns durable dispatch. A separate optimistic receipt lease fences duplicate effects,
        // including deliveries recovered after process loss. Reads always precede retryable writes.
        try
        {
            var scope = new SalesRoomMediaScope(room.CompanyId, room.Id);
            if (op.Action == "provision")
            {
                if (room.State is SalesBrowserRoomStates.Ending or SalesBrowserRoomStates.Ended || room.ExpiresUtc <= Now)
                { room.End(); await End(room, scope, ct); }
                else
                {
                    if (!options.CurrentValue.Enabled || options.CurrentValue.DrainEnabled ||
                        !await db.CompanyMemberships.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == room.CompanyId && x.UserId == room.OrganizerUserId && x.Status == CompanyMembershipStatus.Active, ct))
                        throw new SalesRoomMediaException("organizer_or_route_unavailable");
                    if(room.InvitationId.HasValue&&!await db.SalesMeetingInvitations.IgnoreQueryFilters().AnyAsync(x=>x.CompanyId==room.CompanyId&&x.Id==room.InvitationId&&x.Status!=SalesMeetingInvitationStatus.Cancelled&&db.ApprovalRequests.IgnoreQueryFilters().Any(a=>a.CompanyId==room.CompanyId&&a.Id==x.ApprovalRequestId&&a.Status==ApprovalRequestStatus.Approved),ct))
                        throw new SalesRoomMediaException("invitation_approval_unavailable");
                    var result = await media.EnsureRoomAsync(scope, room.ProvisionOperationId, options.CurrentValue.MaximumParticipants + 1, ct);
                    // Refresh after the external effect: an organizer may have ended the room meanwhile.
                    await db.Entry(room).ReloadAsync(ct);
                    if (room.State is SalesBrowserRoomStates.Ending or SalesBrowserRoomStates.Ended || room.ExpiresUtc <= Now)
                    { room.End(); await End(room, scope, ct); }
                    else room.Provisioned(result.Reference);
                }
            }
            else if (op.Action is "end" or "expire")
            {
                room.End(); await db.SaveChangesAsync(ct); await End(room, scope, ct);
            }
            else if (op.Action is "remove" or "deny")
            {
                var p = await db.SalesRoomParticipants.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == room.CompanyId && x.RoomId == room.Id && x.Id == op.TargetId, ct);
                if (p != null && p.MemberUserId == null)
                {
                    // Cloud revocation is needed even for an already disconnected participant.
                    // Inspect first so a recovered operation never blindly repeats an unknown write.
                    await inspection.ParticipantsAsync(scope, ct);
                    await media.RemoveParticipantAsync(scope, new(p.Id, false, p.Generation), ct);
                    if ((await inspection.ParticipantsAsync(scope, ct)).Contains(LiveKitSalesRoomMediaTransport.Identity(new(p.Id, false, p.Generation))))
                        throw new SalesRoomMediaException("participant_removal_unconfirmed", true);
                    p.Removed(); room.Touch();
                }
            }
            else if (op.Action == "stop_agents")
            {
                foreach (var identity in await inspection.ParticipantsAsync(scope, ct))
                {
                    var parts = identity.Split('-');
                    if (parts.Length == 3 && parts[0] == "agent" && Guid.TryParseExact(parts[1], "N", out var id) && long.TryParse(parts[2], out var generation))
                        await media.RemoveParticipantAsync(scope, new(id, true, generation), ct);
                }
            }
            else if (op.Action == "inspect")
            {
                var provider = await media.InspectRoomAsync(scope, ct);
                await db.Entry(room).ReloadAsync(ct);
                if (provider == null && room.State is not (SalesBrowserRoomStates.Ending or SalesBrowserRoomStates.Ended))
                { room.End(); await End(room, scope, ct); }
            }
            else throw new SalesRoomMediaException("unsupported_room_operation");
            op.Complete();
            db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), room.CompanyId, "system", (Guid?)null, "sales.browser_room.provider_" + op.Action,
                "sales_room_operation", op.Id.ToString("D"), "succeeded", metadata: new Dictionary<string, string?> { { "initiatingActorId", op.ActorId?.ToString("D") } }, occurredUtc: Now));
            await db.SaveChangesAsync(ct);
            SalesRoomBenchmarkTelemetry.RecordLifecycle(op.Action + "_completed");
            SalesRoomBenchmarkTelemetry.RecordLatency("lifecycle_" + op.Action,
                System.Diagnostics.Stopwatch.GetElapsedTime(started));
        }
        catch (DbUpdateConcurrencyException) { throw; }
        catch (OperationCanceledException) when (dispatchToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            ct = dispatchToken;
            // Safe codes only; never persist raw SDK exceptions, signed payloads or tokens.
            await db.Entry(room).ReloadAsync(ct);
            op.Retry(error is SalesRoomMediaException mediaError ? mediaError.Code : "provider_operation_failed"); room.Reconcile();
            SalesRoomBenchmarkTelemetry.RecordLifecycle(op.State == "needs_review" ? "ambiguity_needs_review" : "retry_scheduled");
            if (op.State != "needs_review") outbox.Enqueue(room.CompanyId, SalesBrowserRoomService.Topic, work,
                availableAtUtc: Now.AddSeconds(Math.Min(120, 15 * Math.Pow(2, op.Attempts))), idempotencyKey: $"room:{op.Id:N}:{op.Attempts}");
            await db.SaveChangesAsync(ct);
        }
    }
    private async Task End(SalesBrowserRoom room, SalesRoomMediaScope scope, CancellationToken ct)
    {
        if (room.MeetingSessionId is Guid meetingId)
        {
            var meeting = await db.SalesMeetingSessions.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == room.CompanyId && x.Id == meetingId, ct);
            meeting.BeginBrowserClosing(room.OrganizerUserId, Now);
        }
        if (room.State == SalesBrowserRoomStates.Ended)
        {
            // A delayed provision can leave an orphan after termination; inspect and remove it.
            if (await media.InspectRoomAsync(scope, ct) != null) await media.DeleteRoomAsync(scope, ct);
            if (await media.InspectRoomAsync(scope, ct) != null) throw new SalesRoomMediaException("room_termination_unconfirmed", true);
            return;
        }
        var participants = await db.SalesRoomParticipants.IgnoreQueryFilters().Where(x => x.CompanyId == room.CompanyId && x.RoomId == room.Id).ToListAsync(ct);
        var grants = await db.SalesRoomInvitationGrants.IgnoreQueryFilters().Where(x => x.CompanyId == room.CompanyId && x.RoomId == room.Id).ToListAsync(ct);
        foreach (var grant in grants) grant.Revoke();
        foreach (var participant in participants) participant.Revoke();
        await db.SaveChangesAsync(ct);
        await inspection.ParticipantsAsync(scope, ct);
        foreach (var p in participants.Where(x => x.LastTokenExpiresUtc != null)) await media.RemoveParticipantAsync(scope, new(p.Id, false, p.Generation), ct);
        // Remove any agent identities too; no room can be silently resurrected by provider callbacks.
        foreach (var identity in await inspection.ParticipantsAsync(scope, ct))
        {
            var parts = identity.Split('-');
            if (parts.Length == 3 && parts[0] == "agent" && Guid.TryParseExact(parts[1], "N", out var id) && long.TryParse(parts[2], out var generation))
                await media.RemoveParticipantAsync(scope, new(id, true, generation), ct);
        }
        await media.DeleteRoomAsync(scope, ct);
        if (await media.InspectRoomAsync(scope, ct) != null) throw new SalesRoomMediaException("room_termination_unconfirmed", true);
        foreach (var p in participants) p.Removed(); room.Ended();
    }
}
