using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;
public sealed partial class SalesBrowserRoomService(VirtualCompanyDbContext db, ICompanyOutboxEnqueuer outbox,
    ISalesRoomMediaTransport media, ISalesRoomWebhookVerifier webhooks, IOptions<SalesRoomLifecycleOptions> configured,
    IOptions<SalesRoomMediaOptions> mediaOptions, TimeProvider clock) : ISalesBrowserRoomService
{
    internal const string Topic = CompanyOutboxTopics.SalesBrowserRoomWorkRequested;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private SalesRoomLifecycleOptions Options => configured.Value;
    private void Enabled()
    {
        if (!Options.Enabled || Options.MaximumParticipants is < 2 or > 6 || Options.MaximumRoomsPerCompany is < 1 or > 1000 ||
            Options.MaximumLiveRoomsPerCompany is < 1 or > 100 || Options.MaximumInvitationsPerRoom is < 1 or > 100 ||
            string.IsNullOrWhiteSpace(Options.NoticeVersion) || Options.NoticeVersion.Length > 80)
            throw new SalesRoomAccessException("room_feature_unavailable", 503);
        // Initial guest release relies on LiveKit Cloud's token revocation semantics.
        if (!Uri.TryCreate(mediaOptions.Value.Url, UriKind.Absolute, out var url) || !url.Host.EndsWith(".livekit.cloud", StringComparison.OrdinalIgnoreCase))
            throw new SalesRoomAccessException("cloud_revocation_required", 503);
    }
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static string Secret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static void ValidCommand(Guid id) { if (id == Guid.Empty) throw new SalesRoomAccessException("command_id_required", 400); }
    private async Task<T> Transaction<T>(Func<Task<T>> action, CancellationToken ct)
    {
        var attempt = 0;
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            if (attempt++ > 0) db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try { var result = await action(); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return result; }
            catch (DbUpdateConcurrencyException) { throw new SalesRoomAccessException("version_conflict"); }
        });
    }
    private async Task Member(Guid company, Guid actor, CancellationToken ct)
    {
        if (company == Guid.Empty || actor == Guid.Empty || !await db.CompanyMemberships.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == company && x.UserId == actor && x.Status == CompanyMembershipStatus.Active, ct))
            throw new SalesRoomAccessException("active_membership_required", 403);
    }
    private async Task<SalesBrowserRoom> Host(Guid company, Guid actor, Guid room, CancellationToken ct)
    {
        await Member(company, actor, ct);
        var entity = await db.SalesBrowserRooms.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == company && x.Id == room, ct)
            ?? throw new SalesRoomAccessException("room_not_found", 404);
        if (entity.OrganizerUserId != actor) throw new SalesRoomAccessException("organizer_required", 403);
        return entity;
    }
    private static void Version(SalesBrowserRoom room, long expected)
    { if (room.Version != expected) throw new SalesRoomAccessException("version_conflict"); }
    private void Open(SalesBrowserRoom room)
    { if (!room.AllowsAccess(Now)) throw new SalesRoomAccessException("room_unavailable", 410); }
    private async Task<bool> Replay(Guid company, Guid room, Guid actor, Guid command, string action, object payload, CancellationToken ct)
    {
        ValidCommand(command);
        var operation = await db.SalesRoomOperations.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == company && x.CommandId == command, ct);
        if (operation == null) return false;
        if (operation.RoomId != room || operation.ActorId != actor || operation.Action != action || operation.RequestHash != Hash(JsonSerializer.Serialize(payload)))
            throw new SalesRoomAccessException("command_reused");
        return true;
    }
    private SalesRoomOperation Record(SalesBrowserRoom room, Guid command, string action, Guid? target, Guid? actor, object payload, bool queued = false, string actorType = "user")
    {
        ValidCommand(command);
        var operation = new SalesRoomOperation(room.CompanyId, room.Id, command, action, target, actor, Hash(JsonSerializer.Serialize(payload)), Now, queued);
        db.SalesRoomOperations.Add(operation);
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), room.CompanyId, actorType, actor, "sales.browser_room." + action,
            "sales_browser_room", room.Id.ToString("D"), queued ? "requested" : "succeeded", occurredUtc: Now));
        if (queued) Queue(operation);
        return operation;
    }
    private void Queue(SalesRoomOperation operation, DateTime? due = null) => outbox.Enqueue(operation.CompanyId, Topic,
        new SalesRoomWorkItem(operation.CompanyId, operation.RoomId, operation.Id), availableAtUtc: due,
        idempotencyKey: $"room:{operation.Id:N}:{operation.Attempts}");
    private void Expiry(SalesBrowserRoom room)
    {
        var operation = new SalesRoomOperation(room.CompanyId, room.Id, Guid.NewGuid(), "expire", null, null, "", Now, true);
        db.SalesRoomOperations.Add(operation); Queue(operation, room.ExpiresUtc);
    }
    private async Task<SalesBrowserRoomView> View(SalesBrowserRoom room, CancellationToken ct) => new(room.Id, room.MeetingSessionId, room.State, room.AgentHealth, room.Version, room.ExpiresUtc,
        await db.SalesRoomParticipants.IgnoreQueryFilters().Where(x => x.CompanyId == room.CompanyId && x.RoomId == room.Id).OrderBy(x => x.Id)
            .Select(x => new SalesRoomParticipantView(x.Id, x.DisplayName, x.State, x.Version, x.Connected)).ToListAsync(ct),
        await db.SalesRoomOperations.IgnoreQueryFilters().Where(x => x.CompanyId == room.CompanyId && x.RoomId == room.Id && x.State != "completed").OrderByDescending(x => x.CreatedUtc).Take(20)
            .Select(x => new SalesRoomOperationView(x.Id, x.Action, x.State, x.Attempts, x.ProblemCode)).ToListAsync(ct));
    public async Task<SalesBrowserRoomView> GetAsync(Guid company, Guid actor, Guid room, CancellationToken ct) => await View(await Host(company, actor, room, ct), ct);
    public async Task<SalesBrowserRoomView> CreateAsync(Guid company, Guid actor, Guid meeting, CreateSalesBrowserRoom request, CancellationToken ct)
    {
        Enabled(); ValidCommand(request.CommandId); await Member(company, actor, ct);
        var room = await Transaction(async () =>
        {
            var session = await db.SalesMeetingSessions.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == company && x.Id == meeting, ct)
                ?? throw new SalesRoomAccessException("meeting_not_found", 404);
            if (session.Status is SalesMeetingSessionStatus.Completed or SalesMeetingSessionStatus.Cancelled or SalesMeetingSessionStatus.Failed) throw new SalesRoomAccessException("meeting_finished", 410);
            if (session.CreatedByUserId != actor) throw new SalesRoomAccessException("organizer_required", 403);
            var existing = await db.SalesBrowserRooms.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == company && x.MeetingSessionId == meeting, ct);
            if (existing != null)
            {
                if (await Replay(company, existing.Id, actor, request.CommandId, "provision", request, ct)) return existing;
                throw new SalesRoomAccessException("meeting_already_has_room");
            }
            if (await db.SalesRoomOperations.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == company && x.CommandId == request.CommandId, ct)) throw new SalesRoomAccessException("command_reused");
            if (request.ExpiresUtc.Kind != DateTimeKind.Utc || request.ExpiresUtc <= Now || request.ExpiresUtc > Now.AddDays(7)) throw new SalesRoomAccessException("invalid_expiry", 400);
            if (await db.SalesBrowserRooms.IgnoreQueryFilters().CountAsync(x => x.CompanyId == company && x.State != SalesBrowserRoomStates.Ended, ct) >= Options.MaximumRoomsPerCompany)
                throw new SalesRoomAccessException("room_capacity_reached", 429);
            var created = new SalesBrowserRoom(company, meeting, actor, request.ExpiresUtc, Now); db.SalesBrowserRooms.Add(created);
            var operation = Record(created, request.CommandId, "provision", null, actor, request, true); created.BindOperation(operation.Id);
            db.SalesRoomParticipants.Add(new SalesRoomParticipant(company, created.Id, "Organizer", null, created.ExpiresUtc, actor));
            Expiry(created); return created;
        }, ct);
        return await View(room, ct);
    }
    public async Task<SalesBrowserRoomView> RetryAsync(Guid company, Guid actor, Guid roomId, Guid operationId, SalesRoomCommand request, CancellationToken ct)
    {
        var room = await Transaction(async () =>
        {
            var room = await Host(company, actor, roomId, ct);
            if (await Replay(company, roomId, actor, request.CommandId, "retry", new { request, operationId }, ct)) return room;
            Version(room, request.ExpectedVersion);
            var failed = await db.SalesRoomOperations.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == company && x.RoomId == roomId && x.Id == operationId, ct)
                ?? throw new SalesRoomAccessException("operation_not_found", 404);
            if (failed.State != "needs_review") throw new SalesRoomAccessException("operation_not_retryable");
            // Preserve the original operation and link a fresh bounded retry budget to its identifier.
            failed.Complete(); room.Touch();
            Record(room, request.CommandId, "retry", operationId, actor, new { request, operationId });
            Record(room, Guid.NewGuid(), failed.Action, failed.TargetId, actor, new { retryOf = operationId }, true);
            return room;
        }, ct);
        return await View(room, ct);
    }
    public async Task<SalesRoomInvitationResult> InviteAsync(Guid company, Guid actor, Guid roomId, CreateSalesRoomInvitation request, CancellationToken ct)
    {
        Enabled(); return await Transaction(async () =>
        {
            var room = await Host(company, actor, roomId, ct); Open(room);
            if (await Replay(company, roomId, actor, request.CommandId, "invite", request, ct)) throw new SalesRoomAccessException("invitation_already_issued");
            Version(room, request.ExpectedVersion);
            if (request.ExpiresUtc.Kind != DateTimeKind.Utc || request.ExpiresUtc <= Now || request.ExpiresUtc > room.ExpiresUtc) throw new SalesRoomAccessException("invalid_expiry", 400);
            if (await db.SalesRoomInvitationGrants.IgnoreQueryFilters().CountAsync(x => x.CompanyId == company && x.RoomId == roomId, ct) >= Options.MaximumInvitationsPerRoom)
                throw new SalesRoomAccessException("invitation_capacity_reached", 429);
            var secret = Secret(); var grant = new SalesRoomInvitationGrant(company, roomId, Hash(secret), request.ExpiresUtc, actor);
            db.SalesRoomInvitationGrants.Add(grant); room.Touch(); Record(room, request.CommandId, "invite", grant.Id, actor, request);
            return new SalesRoomInvitationResult(grant.Id, secret, grant.ExpiresUtc);
        }, ct);
    }
    public async Task<SalesBrowserRoomView> DecideAsync(Guid company, Guid actor, Guid roomId, Guid participantId, string decision, SalesRoomCommand request, CancellationToken ct)
    {
        if (decision is not ("admit" or "deny" or "remove")) throw new SalesRoomAccessException("invalid_decision", 400);
        var room = await Transaction(async () =>
        {
            var room = await Host(company, actor, roomId, ct);
            if (await Replay(company, roomId, actor, request.CommandId, decision, new { request, participantId }, ct)) return room;
            Open(room); Version(room, request.ExpectedVersion);
            var participant = await db.SalesRoomParticipants.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == company && x.RoomId == roomId && x.Id == participantId, ct)
                ?? throw new SalesRoomAccessException("participant_not_found", 404);
            if (participant.MemberUserId != null) throw new SalesRoomAccessException("cannot_remove_organizer");
            if (decision == "admit")
            {
                Enabled(); if (participant.State != SalesRoomParticipantStates.Lobby) throw new SalesRoomAccessException("participant_not_in_lobby");
                if (await db.SalesRoomParticipants.IgnoreQueryFilters().CountAsync(x => x.CompanyId == company && x.RoomId == roomId && x.State == SalesRoomParticipantStates.Admitted, ct) >= Options.MaximumParticipants)
                    throw new SalesRoomAccessException("participant_capacity_reached", 429);
                participant.Admit();
            }
            else participant.Revoke(decision == "deny");
            room.Touch(); Record(room, request.CommandId, decision, participantId, actor, new { request, participantId }, decision != "admit"); return room;
        }, ct); return await View(room, ct);
    }
    public async Task<SalesBrowserRoomView> EndAsync(Guid company, Guid actor, Guid roomId, SalesRoomCommand request, CancellationToken ct)
    {
        var room = await Transaction(async () =>
        {
            var room = await Host(company, actor, roomId, ct);
            if (await Replay(company, roomId, actor, request.CommandId, "end", request, ct)) return room;
            Version(room, request.ExpectedVersion); room.End();
            Record(room, request.CommandId, "end", null, actor, request, true); return room;
        }, ct); return await View(room, ct);
    }
    public async Task<SalesBrowserRoomView> RevokeInvitationAsync(Guid company, Guid actor, Guid roomId, Guid invitation, SalesRoomCommand request, CancellationToken ct)
    {
        var room = await Transaction(async () =>
        {
            var room = await Host(company, actor, roomId, ct);
            if (await Replay(company, roomId, actor, request.CommandId, "revoke_invitation", new { request, invitation }, ct)) return room;
            Version(room, request.ExpectedVersion);
            var grant = await db.SalesRoomInvitationGrants.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == company && x.RoomId == roomId && x.Id == invitation, ct)
                ?? throw new SalesRoomAccessException("invitation_not_found", 404);
            grant.Revoke();
            if (grant.RedeemedParticipantId is { } participantId)
            {
                var p = await db.SalesRoomParticipants.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == company && x.RoomId == roomId && x.Id == participantId, ct); p.Revoke();
                Record(room, Guid.NewGuid(), "remove", p.Id, actor, new { invitation }, true);
            }
            room.Touch(); Record(room, request.CommandId, "revoke_invitation", invitation, actor, new { request, invitation }); return room;
        }, ct); return await View(room, ct);
    }
}
