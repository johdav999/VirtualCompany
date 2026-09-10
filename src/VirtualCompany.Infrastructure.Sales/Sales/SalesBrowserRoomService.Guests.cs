using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Sales;
public sealed partial class SalesBrowserRoomService
{
    private async Task<(SalesBrowserRoom Room, SalesRoomParticipant Participant)> Guest(string credential, Guid roomId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(credential) || credential.Length is < 40 or > 100) throw new SalesRoomAccessException("guest_session_invalid", 401);
        var hash = Hash(credential);
        // This capability lookup intentionally has no caller-supplied company scope. The secret's
        // unique hash resolves the participant; all subsequent queries use its persisted company.
        var participant = await db.SalesRoomParticipants.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.SessionHash == hash && x.RoomId == roomId, ct)
            ?? throw new SalesRoomAccessException("guest_session_invalid", 401);
        var room = await db.SalesBrowserRooms.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == participant.CompanyId && x.Id == participant.RoomId, ct);
        Open(room);
        if (participant.ExpiresUtc <= Now || participant.State is not (SalesRoomParticipantStates.Lobby or SalesRoomParticipantStates.Admitted))
            throw new SalesRoomAccessException("guest_session_invalid", 401);
        if (!await db.CompanyMemberships.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == room.CompanyId && x.UserId == room.OrganizerUserId && x.Status == CompanyMembershipStatus.Active, ct))
            throw new SalesRoomAccessException("room_unavailable", 410);
        return (room, participant);
    }
    private SalesRoomGuestView GuestView(SalesBrowserRoom room, SalesRoomParticipant p) => new(room.Id, p.Id, room.State, p.State, p.Version,
        room.ExpiresUtc < p.ExpiresUtc ? room.ExpiresUtc : p.ExpiresUtc, p.AiProcessingAllowed, p.TranscriptRetentionAllowed)
        { ConsentNoticeVersion = Options.NoticeVersion };
    public async Task<SalesRoomGuestView> GuestStatusAsync(string credential, Guid room, CancellationToken ct)
    {
        var result = await Guest(credential, room, ct);
        var view = GuestView(result.Room, result.Participant);
        // Lobby access does not grant audience access. Never return host operations or member IDs.
        if (result.Participant.State != SalesRoomParticipantStates.Admitted) return view;
        var participants = await db.SalesRoomParticipants.IgnoreQueryFilters()
            .Where(x => x.CompanyId == result.Room.CompanyId && x.RoomId == room && x.State == SalesRoomParticipantStates.Admitted)
            .OrderBy(x => x.Id).ToListAsync(ct);
        return view with { Participants = participants.Select(x => new SalesRoomPublicParticipantView(x.Id, x.DisplayName,
            LiveKitSalesRoomMediaTransport.Identity(new(x.Id, false, x.Generation)))).ToArray() };
    }
    public async Task<SalesRoomGuestSession> RedeemAsync(RedeemSalesRoomInvitation request, CancellationToken ct)
    {
        Enabled();
        if (string.IsNullOrEmpty(request.Secret) || request.Secret.Length != 43 || string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Length > 80 || request.DisplayName.Any(char.IsControl))
            throw new SalesRoomAccessException("invalid_invitation_request", 400);
        return await Transaction(async () =>
        {
            var hash = Hash(request.Secret);
            // High-entropy capability resolution, then authoritative company/room scope.
            var grant = await db.SalesRoomInvitationGrants.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.SecretHash == hash, ct)
                ?? throw new SalesRoomAccessException("invitation_unavailable", 401);
            var room = await db.SalesBrowserRooms.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == grant.CompanyId && x.Id == grant.RoomId, ct); Open(room);
            if (grant.Revoked || grant.ExpiresUtc <= Now || grant.RedeemedParticipantId != null) throw new SalesRoomAccessException("invitation_unavailable", 401);
            if (await db.SalesRoomParticipants.IgnoreQueryFilters().CountAsync(x => x.CompanyId == room.CompanyId && x.RoomId == room.Id &&
                (x.State == SalesRoomParticipantStates.Lobby || x.State == SalesRoomParticipantStates.Admitted), ct) >= Options.MaximumParticipants)
                throw new SalesRoomAccessException("participant_capacity_reached", 429);
            var credential = Secret() + Secret();
            var participant = new SalesRoomParticipant(room.CompanyId, room.Id, request.DisplayName.Trim(), Hash(credential), room.ExpiresUtc);
            db.SalesRoomParticipants.Add(participant); grant.Redeem(participant.Id, Now); room.Touch();
            Record(room, Guid.NewGuid(), "redeem", participant.Id, participant.Id, new { grant.Id }, actorType: "guest");
            return new SalesRoomGuestSession(credential, GuestView(room, participant));
        }, ct);
    }
    private async Task<SalesRoomMediaToken> Token(SalesBrowserRoom room, SalesRoomParticipant participant, CancellationToken ct)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        Enabled(); Open(room);
        if(room.InvitationId.HasValue)
        {
            var invitation=await db.SalesMeetingInvitations.IgnoreQueryFilters().SingleAsync(x=>x.CompanyId==room.CompanyId&&x.Id==room.InvitationId,ct);
            if(invitation.Status!=SalesMeetingInvitationStatus.Scheduled||invitation.StartsUtc>Now.AddMinutes(15))throw new SalesRoomAccessException("meeting_not_open",409);
        }
        if (participant.State != SalesRoomParticipantStates.Admitted) throw new SalesRoomAccessException("admission_required", 403);
        if (room.ExpiresUtc <= Now.AddSeconds(5)) throw new SalesRoomAccessException("room_expiring", 410);
        if (room.State != SalesBrowserRoomStates.Live)
        {
            if (await db.SalesBrowserRooms.IgnoreQueryFilters().CountAsync(x => x.CompanyId == room.CompanyId && x.State == SalesBrowserRoomStates.Live && x.ExpiresUtc > Now, ct) >= Options.MaximumLiveRoomsPerCompany)
                throw new SalesRoomAccessException("live_room_capacity_reached", 429);
            room.Start(Now, mediaOptions.Value.MaximumSessionMinutes); Expiry(room);
        }
        var token = media.IssueToken(new(room.CompanyId, room.Id), new(participant.Id, false, participant.Generation, new DateTimeOffset(DateTime.SpecifyKind(room.ExpiresUtc < participant.ExpiresUtc ? room.ExpiresUtc : participant.ExpiresUtc, DateTimeKind.Utc))));
        participant.TokenIssued(token.ExpiresAt.UtcDateTime); room.Touch();
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), room.CompanyId, participant.MemberUserId.HasValue ? "user" : "guest",
            participant.MemberUserId ?? participant.Id, "sales.browser_room.media_token_issued", "sales_room_participant", participant.Id.ToString("D"), "succeeded", occurredUtc: Now));
        SalesRoomBenchmarkTelemetry.RecordAdmission(participant.MemberUserId.HasValue ? "host_token_issued" : "guest_token_issued");
        SalesRoomBenchmarkTelemetry.RecordLatency("media_token", System.Diagnostics.Stopwatch.GetElapsedTime(started));
        return token;
    }
    public Task<SalesRoomMediaToken> GuestTokenAsync(string credential, Guid room, CancellationToken ct) => Transaction(async () =>
    { var current = await Guest(credential, room, ct); return await Token(current.Room, current.Participant, ct); }, ct);
    public Task<SalesRoomMediaToken> HostTokenAsync(Guid company, Guid actor, Guid room, CancellationToken ct) => Transaction(async () =>
    {
        var entity = await Host(company, actor, room, ct);
        var participant = await db.SalesRoomParticipants.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == company && x.RoomId == room && x.MemberUserId == actor, ct);
        return await Token(entity, participant, ct);
    }, ct);
    public Task<SalesRoomGuestView> ConsentAsync(string credential, Guid room, SetSalesRoomConsent request, CancellationToken ct) => Transaction(async () =>
    {
        var current = await Guest(credential, room, ct);
        return await Consent(current.Room, current.Participant, request, ct);
    }, ct);
    public Task<SalesRoomGuestView> HostConsentAsync(Guid company, Guid actor, Guid room, SetSalesRoomConsent request, CancellationToken ct) => Transaction(async () =>
    {
        var entity = await Host(company, actor, room, ct); Open(entity);
        var participant = await db.SalesRoomParticipants.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == company && x.RoomId == room && x.MemberUserId == actor, ct);
        return await Consent(entity, participant, request, ct);
    }, ct);
    private async Task<SalesRoomGuestView> Consent(SalesBrowserRoom room, SalesRoomParticipant participant, SetSalesRoomConsent request, CancellationToken ct)
    {
        var actor = participant.MemberUserId ?? participant.Id; var actorType = participant.MemberUserId.HasValue ? "user" : "guest";
        if (await Replay(room.CompanyId, room.Id, actor, request.CommandId, "consent", request, ct)) return GuestView(room, participant);
        if (participant.Version != request.ExpectedVersion) throw new SalesRoomAccessException("version_conflict");
        if (request.Purpose is not ("ai_processing" or "retained_transcript") || request.NoticeVersion != Options.NoticeVersion)
            throw new SalesRoomAccessException("consent_notice_or_purpose_invalid", 400);
        participant.Consent(request.Purpose, request.Granted); room.Touch();
        db.SalesRoomConsents.Add(new SalesRoomConsent(participant, request.Purpose, request.Granted, request.NoticeVersion, Now));
        Record(room, request.CommandId, "consent", participant.Id, actor, request, actorType: actorType);
        if (!request.Granted && request.Purpose == "ai_processing")
        {
            room.StopAgent("consent_withdrawn", "AI stopped immediately because a participant withdrew processing consent.", Now);
            Record(room, Guid.NewGuid(), "stop_agents", null, actor, new { request.Purpose }, true, actorType);
        }
        return GuestView(room, participant);
    }
    public async Task LeaveAsync(string credential, Guid roomId, CancellationToken ct) => await Transaction(async () =>
    {
        var current = await Guest(credential, roomId, ct); current.Participant.Revoke(); current.Room.Touch();
        Record(current.Room, Guid.NewGuid(), "remove", current.Participant.Id, current.Participant.Id, new { current.Participant.Id }, true, "guest"); return true;
    }, ct);
    public async Task AcceptWebhookAsync(string body, string authorization, CancellationToken ct)
    {
        var message = webhooks.Verify(body, authorization);
        await Transaction(async () =>
        {
            if (await db.SalesRoomProviderEvents.IgnoreQueryFilters().AnyAsync(x => x.ProviderEventId == message.EventId, ct)) return true;
            // Only a verified provider reference can resolve an otherwise anonymous webhook.
            var room = await db.SalesBrowserRooms.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProviderReference == message.RoomReference, ct);
            if (room == null) return true;
            db.SalesRoomProviderEvents.Add(new SalesRoomProviderEvent(room.CompanyId, room.Id, message.EventId, message.EventType, message.ParticipantIdentity, message.OccurredUnixSeconds));
            if (room.State is SalesBrowserRoomStates.Ending or SalesBrowserRoomStates.Ended) return true;
            if (message.EventType == "room_finished")
            {
                // Provider timestamps/order are not room authority. Re-read provider state via outbox.
                Record(room, Guid.NewGuid(), "inspect", null, null, new { message.EventId }, true, "provider"); return true;
            }
            if (message.ParticipantIdentity != null)
            {
                var participants = await db.SalesRoomParticipants.IgnoreQueryFilters().Where(x => x.CompanyId == room.CompanyId && x.RoomId == room.Id).ToListAsync(ct);
                var p = participants.SingleOrDefault(x => LiveKitSalesRoomMediaTransport.Identity(new(x.Id, false, x.Generation)) == message.ParticipantIdentity);
                if (p != null && p.State == SalesRoomParticipantStates.Admitted)
                { p.Observe(message.EventType == "participant_joined", message.OccurredUnixSeconds); room.Touch(); }
            }
            return true;
        }, ct);
    }
}
