using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;
public sealed class SalesBrowserMeetingScheduling(VirtualCompanyDbContext db, ICompanyOutboxEnqueuer outbox,
    IDataProtectionProvider protection, IOptions<SalesRoomLifecycleOptions> lifecycle, IOptions<SalesRoomMediaOptions> media, TimeProvider clock) : ISalesBrowserMeetingScheduling
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public SalesBrowserMeetingReadiness Readiness()
    {
        if (!lifecycle.Value.Enabled || !media.Value.Enabled || media.Value.ConfigurationProblem != null)
            return new(false, "Browser meetings are not enabled or their media connection is not ready.");
        if (!Uri.TryCreate(lifecycle.Value.PublicOrigin, UriKind.Absolute, out var origin) || origin.Scheme != "https" || origin.AbsolutePath != "/" || !string.IsNullOrEmpty(origin.UserInfo) || !string.IsNullOrEmpty(origin.Query) || !string.IsNullOrEmpty(origin.Fragment))
            return new(false, "Configure the secure browser meeting address before scheduling.");
        if (!new Uri(media.Value.Url).Host.EndsWith(".livekit.cloud", StringComparison.OrdinalIgnoreCase)) return new(false, "Browser invitations require LiveKit Cloud revocation support.");
        return new(true, null);
    }
    public void ValidateWindow(DateTime starts, DateTime ends)
    {
        var ready = Readiness(); if (!ready.Ready) throw Failure("browser_meeting_unavailable", ready.Reason!, false);
        if (starts <= Now.AddMinutes(5) || ends <= starts || ends - starts > TimeSpan.FromMinutes(media.Value.MaximumSessionMinutes) || ends.AddHours(1) > Now.AddDays(7))
            throw Failure("browser_meeting_window_invalid", "Browser meetings must be within the next seven days and no longer than the configured call limit.", false);
    }
    private static CalendarProviderException Failure(string code, string summary, bool retry) => new(code, summary, retry ? CalendarProviderFailureKind.Retryable : CalendarProviderFailureKind.Permanent);
    private IDataProtector Protector(SalesMeetingInvitation invitation) => protection.CreateProtector("SalesBrowserMeetingInvitation.v1", invitation.CompanyId.ToString("N"), invitation.Id.ToString("N"));
    private async Task<SalesMeetingInvitation> Invitation(Guid company, Guid id, CancellationToken ct) => await db.SalesMeetingInvitations.SingleOrDefaultAsync(x => x.CompanyId == company && x.Id == id, ct) ?? throw new KeyNotFoundException("Invitation not found.");
    private async Task Approved(SalesMeetingInvitation invitation, CancellationToken ct)
    {
        if (invitation.Conferencing != SalesMeetingConferencing.Browser || invitation.Status is SalesMeetingInvitationStatus.Cancelled or SalesMeetingInvitationStatus.Rejected ||
            !await db.ApprovalRequests.AnyAsync(x => x.CompanyId == invitation.CompanyId && x.Id == invitation.ApprovalRequestId && x.Status == ApprovalRequestStatus.Approved, ct))
            throw Failure("browser_invitation_not_approved", "The browser invitation needs an active approval.", false);
        if (!await db.CompanyMemberships.AnyAsync(x => x.CompanyId == invitation.CompanyId && x.UserId == invitation.CreatedByUserId && x.Status == CompanyMembershipStatus.Active, ct))
            throw Failure("browser_organizer_unavailable", "The meeting organizer no longer has active company access.", false);
    }
    private void Queue(SalesBrowserRoom room, string action, Guid command, DateTime? due = null)
    {
        var op = new SalesRoomOperation(room.CompanyId, room.Id, command, action, null, room.OrganizerUserId, "", Now, true);
        db.SalesRoomOperations.Add(op); if (action == "provision") room.BindOperation(op.Id);
        outbox.Enqueue(room.CompanyId, CompanyOutboxTopics.SalesBrowserRoomWorkRequested, new SalesRoomWorkItem(room.CompanyId, room.Id, op.Id), availableAtUtc: due, idempotencyKey: $"room:{op.Id:N}:0");
    }
    public async Task<string> PrepareAsync(Guid company, Guid id, CancellationToken ct)
    {
        var attempt = 0;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            if (attempt++ > 0) db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var invitation = await Invitation(company, id, ct); await Approved(invitation, ct);
            if (!invitation.BrowserRoomId.HasValue)
            {
                ValidateWindow(DateTime.SpecifyKind(invitation.StartsUtc, DateTimeKind.Utc), DateTime.SpecifyKind(invitation.EndsUtc, DateTimeKind.Utc));
                if (await db.SalesBrowserRooms.CountAsync(x => x.CompanyId == company && x.State != SalesBrowserRoomStates.Ended, ct) >= lifecycle.Value.MaximumRoomsPerCompany)
                    throw Failure("browser_room_capacity", "The company has reached its browser room limit.", false);
                var room = SalesBrowserRoom.ForInvitation(company, id, invitation.CreatedByUserId, invitation.EndsUtc.AddHours(1), Now);
                var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                db.SalesBrowserRooms.Add(room);
                db.SalesRoomInvitationGrants.Add(new(company, room.Id, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))), room.ExpiresUtc, invitation.CreatedByUserId));
                db.SalesRoomParticipants.Add(new(company, room.Id, "Organizer", null, room.ExpiresUtc, invitation.CreatedByUserId));
                invitation.BindBrowserRoom(room.Id, Protector(invitation).Protect($"{lifecycle.Value.PublicOrigin.TrimEnd('/')}/sales/rooms/{room.Id:D}#invite={secret}"));
                Queue(room, "provision", Guid.NewGuid()); Queue(room, "expire", Guid.NewGuid(), room.ExpiresUtc);
                db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), company, "system", null, "sales.browser_room.scheduled", "sales_meeting_invitation", id.ToString("D"), "requested", metadata: new Dictionary<string, string?> { { "organizerUserId", invitation.CreatedByUserId.ToString("D") }, { "approvalRequestId", invitation.ApprovalRequestId?.ToString("D") } }, occurredUtc: Now));
                await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
        });
        return await DeliveryLinkAsync(company, id, ct);
    }
    public async Task<string> DeliveryLinkAsync(Guid company, Guid id, CancellationToken ct)
    {
        var invitation = await Invitation(company, id, ct); await Approved(invitation, ct);
        var room = await db.SalesBrowserRooms.SingleOrDefaultAsync(x => x.CompanyId == company && x.Id == invitation.BrowserRoomId, ct);
        if (room == null || room.State is SalesBrowserRoomStates.Provisioning or SalesBrowserRoomStates.Reconciliation)
            throw Failure("browser_room_preparing", "The browser room is being prepared. Retry delivery after it is ready.", true);
        if (!room.AllowsAccess(Now)) throw Failure("browser_room_unavailable", "The browser room is no longer available. Create a new approved invitation.", false);
        var ready = Readiness(); if (!ready.Ready) throw Failure("browser_meeting_unavailable", ready.Reason!, false);
        try { return Protector(invitation).Unprotect(invitation.ProtectedBrowserInvitationLink!); }
        catch (CryptographicException) { throw Failure("browser_invitation_key_unavailable", "The invitation encryption key is unavailable. Restore the shared key ring before retrying.", false); }
    }
    public async Task<SalesBrowserMeetingLink> GetLinkAsync(Guid company, Guid actor, Guid id, CancellationToken ct)
    {
        var invitation = await Invitation(company, id, ct);
        if (invitation.CreatedByUserId != actor || !await db.CompanyMemberships.AnyAsync(x => x.CompanyId == company && x.UserId == actor && x.Status == CompanyMembershipStatus.Active, ct)) throw new SalesRoomAccessException("organizer_required", 403);
        if (invitation.Status != SalesMeetingInvitationStatus.Scheduled) throw new SalesRoomAccessException("invitation_not_scheduled");
        return new(await DeliveryLinkAsync(company, id, ct));
    }
    public async Task ValidateRescheduleAsync(Guid company, Guid id, DateTime starts, DateTime ends, CancellationToken ct)
    {
        ValidateWindow(DateTime.SpecifyKind(starts, DateTimeKind.Utc), DateTime.SpecifyKind(ends, DateTimeKind.Utc));
        var room = await db.SalesBrowserRooms.SingleOrDefaultAsync(x => x.CompanyId == company && x.InvitationId == id, ct);
        if (room == null || !room.AllowsAccess(Now) || room.LiveStartedUtc.HasValue) throw Failure("browser_room_not_reschedulable", "A started or ended browser meeting requires a new approved invitation.", false);
    }
    public async Task RescheduledAsync(Guid company, Guid id, CancellationToken ct)
    {
        var invitation = await Invitation(company, id, ct);
        var room = await db.SalesBrowserRooms.SingleAsync(x => x.CompanyId == company && x.InvitationId == id, ct);
        room.Reschedule(invitation.EndsUtc.AddHours(1));
        foreach (var grant in await db.SalesRoomInvitationGrants.Where(x => x.CompanyId == company && x.RoomId == room.Id && !x.Revoked).ToListAsync(ct)) grant.Reschedule(room.ExpiresUtc);
        foreach (var participant in await db.SalesRoomParticipants.Where(x => x.CompanyId == company && x.RoomId == room.Id && (x.SessionHash != null || x.MemberUserId != null)).ToListAsync(ct)) participant.Reschedule(room.ExpiresUtc);
        Queue(room, "expire", Guid.NewGuid(), room.ExpiresUtc);
    }
    public async Task RetryChangeAsync(Guid company,Guid actor,Guid id,Guid changeId,CancellationToken ct)
    {
        var invitation=await Invitation(company,id,ct);
        if(invitation.CreatedByUserId!=actor)throw new SalesRoomAccessException("organizer_required",403);
        await Approved(invitation,ct);
        var change=await db.SalesMeetingChangeRequests.SingleOrDefaultAsync(x=>x.CompanyId==company&&x.InvitationId==id&&x.Id==changeId,ct)??throw new KeyNotFoundException("Meeting change not found.");
        if(change.Status!=SalesMeetingChangeRequestStatus.Failed||!await db.ApprovalRequests.AnyAsync(x=>x.CompanyId==company&&x.Id==change.ApprovalRequestId&&x.Status==ApprovalRequestStatus.Approved,ct))throw new SalesRoomAccessException("approved_failed_change_required");
        change.QueueRetry();
        outbox.Enqueue(company,CompanyOutboxTopics.SalesMeetingChangeDeliveryRequested,new SalesMeetingChangeDeliveryRequestedMessage(company,change.Id,change.IdempotencyKey,null),idempotencyKey:$"{change.IdempotencyKey}:retry:{change.ExecutionAttemptCount+1}");
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(),company,"user",actor,"sales.meeting_change.retry_requested","sales_meeting_change_request",change.Id.ToString("D"),"requested",occurredUtc:Now));
        await db.SaveChangesAsync(ct);
    }
    public async Task ReconcileAsync(Guid company, Guid actor, Guid id, Guid? changeId, CancellationToken ct)
    {
        var invitation = await Invitation(company, id, ct);
        if (invitation.CreatedByUserId != actor) throw new SalesRoomAccessException("organizer_required", 403);
        await Approved(invitation, ct);
        if (changeId.HasValue)
        {
            var change = await db.SalesMeetingChangeRequests.SingleOrDefaultAsync(x => x.CompanyId == company && x.InvitationId == id && x.Id == changeId, ct) ?? throw new KeyNotFoundException("Meeting change not found.");
            if (change.Status is not (SalesMeetingChangeRequestStatus.ReconciliationRequired or SalesMeetingChangeRequestStatus.Executing)) throw new SalesRoomAccessException("change_not_reconcilable");
            outbox.Enqueue(company, CompanyOutboxTopics.SalesMeetingChangeDeliveryRequested, new SalesMeetingChangeDeliveryRequestedMessage(company, change.Id, change.IdempotencyKey, null, true), idempotencyKey: $"{change.IdempotencyKey}:inspect:{change.UpdatedUtc.Ticks}");
        }
        else
        {
            if (invitation.Status is not (SalesMeetingInvitationStatus.ReconciliationRequired or SalesMeetingInvitationStatus.Scheduling)) throw new SalesRoomAccessException("invitation_not_reconcilable");
            outbox.Enqueue(company, CompanyOutboxTopics.SalesMeetingInvitationDeliveryRequested, new SalesMeetingInvitationDeliveryRequestedMessage(company, id, invitation.IdempotencyKey, null, true), idempotencyKey: $"{invitation.IdempotencyKey}:inspect:{invitation.UpdatedUtc.Ticks}");
        }
        await db.SaveChangesAsync(ct);
    }
    public async Task CancelAsync(Guid company, Guid id, Guid changeId, CancellationToken ct)
    {
        var change = await db.SalesMeetingChangeRequests.SingleAsync(x => x.CompanyId == company && x.InvitationId == id && x.Id == changeId, ct);
        if (change.Operation != SalesMeetingChangeOperation.Cancel || !await db.ApprovalRequests.AnyAsync(x => x.CompanyId == company && x.Id == change.ApprovalRequestId && x.Status == ApprovalRequestStatus.Approved, ct)) throw new SalesRoomAccessException("approved_cancellation_required", 403);
        var room = await db.SalesBrowserRooms.SingleOrDefaultAsync(x => x.CompanyId == company && x.InvitationId == id, ct); if (room == null) return;
        if (room.State is not (SalesBrowserRoomStates.Ending or SalesBrowserRoomStates.Ended))
        { room.End(); Queue(room, "end", changeId); await db.SaveChangesAsync(ct); }
    }
}
