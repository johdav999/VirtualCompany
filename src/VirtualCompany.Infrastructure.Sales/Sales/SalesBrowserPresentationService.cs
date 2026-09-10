using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesBrowserPresentationService(
    VirtualCompanyDbContext db,
    ISalesPresentationRuntimeService runtime,
    ICompanyDocumentStorage storage,
    IOptions<SalesPresentationConductorOptions> configured,
    TimeProvider clock) : ISalesBrowserPresentationService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<SalesBrowserPresentationHostDto> GetHostAsync(
        Guid companyId, Guid userId, Guid roomId, CancellationToken cancellationToken)
    {
        var access = await ValidateHostAsync(companyId, userId, roomId, cancellationToken);
        var snapshot = await runtime.GetCurrentAsync(companyId, userId, access.SessionId, cancellationToken)
            ?? throw new SalesRoomAccessException("presentation_unavailable", 404);
        await CaptureAsync(access, snapshot.Stage, cancellationToken);
        return new(roomId, access.ParticipantId, access.ParticipantGeneration, snapshot,
            await ReadinessAsync(access.CompanyId, roomId, snapshot.Stage, cancellationToken));
    }

    public async Task<SalesBrowserPresentationPublicDto> GetGuestAsync(
        string credential, Guid roomId, CancellationToken cancellationToken)
    {
        var access = await ValidateGuestAsync(credential, roomId, cancellationToken);
        var stage = await PublicSnapshotAsync(access.CompanyId, access.SessionId, cancellationToken);
        await CaptureAsync(access, stage, cancellationToken);
        return new(roomId, access.ParticipantId, access.ParticipantGeneration, stage);
    }

    public async Task<SalesBrowserPresentationAccessContext> ValidateHostAsync(
        Guid companyId, Guid userId, Guid roomId, CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty || userId == Guid.Empty || roomId == Guid.Empty)
            throw new SalesRoomAccessException("room_not_found", 404);
        var authorized = await db.CompanyMemberships.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.CompanyId == companyId && x.UserId == userId && x.Status == CompanyMembershipStatus.Active,
            cancellationToken);
        var room = await db.SalesBrowserRooms.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.Id == roomId, cancellationToken);
        if (!authorized || room is null) throw new SalesRoomAccessException("room_not_found", 404);
        if (room.OrganizerUserId != userId) throw new SalesRoomAccessException("organizer_required", 403);
        EnsureOpen(room);
        if (!room.MeetingSessionId.HasValue) throw new SalesRoomAccessException("presentation_unavailable", 404);
        var participant = await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().SingleAsync(x =>
            x.CompanyId == companyId && x.RoomId == roomId && x.MemberUserId == userId, cancellationToken);
        if (participant.State != SalesRoomParticipantStates.Admitted)
            throw new SalesRoomAccessException("admission_required", 403);
        return new(companyId, roomId, room.MeetingSessionId.Value, participant.Id, participant.Generation, true);
    }

    public async Task<SalesBrowserPresentationAccessContext> ValidateGuestAsync(
        string credential, Guid roomId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(credential) || credential.Length is < 40 or > 100 || roomId == Guid.Empty)
            throw new SalesRoomAccessException("guest_session_invalid", 401);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credential)));
        var participant = await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.RoomId == roomId && x.SessionHash == hash, cancellationToken)
            ?? throw new SalesRoomAccessException("guest_session_invalid", 401);
        var room = await db.SalesBrowserRooms.IgnoreQueryFilters().AsNoTracking().SingleAsync(x =>
            x.CompanyId == participant.CompanyId && x.Id == roomId, cancellationToken);
        EnsureOpen(room);
        if (participant.ExpiresUtc <= Now || participant.State != SalesRoomParticipantStates.Admitted)
            throw new SalesRoomAccessException("admission_required", 403);
        var organizerActive = await db.CompanyMemberships.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.CompanyId == room.CompanyId && x.UserId == room.OrganizerUserId &&
            x.Status == CompanyMembershipStatus.Active, cancellationToken);
        if (!organizerActive || !room.MeetingSessionId.HasValue)
            throw new SalesRoomAccessException("room_unavailable", 410);
        return new(room.CompanyId, roomId, room.MeetingSessionId.Value, participant.Id,
            participant.Generation, false);
    }

    public async Task<SalesPresentationSlideAsset> OpenHostSlideAsync(
        Guid companyId, Guid userId, Guid roomId, Guid deckId, int deckVersion, int slideNumber,
        CancellationToken cancellationToken) =>
        await OpenSlideAsync(await ValidateHostAsync(companyId, userId, roomId, cancellationToken),
            deckId, deckVersion, slideNumber, cancellationToken);

    public async Task<SalesPresentationSlideAsset> OpenGuestSlideAsync(
        string credential, Guid roomId, Guid deckId, int deckVersion, int slideNumber,
        CancellationToken cancellationToken) =>
        await OpenSlideAsync(await ValidateGuestAsync(credential, roomId, cancellationToken),
            deckId, deckVersion, slideNumber, cancellationToken);

    public async Task<SalesPresentationCommandResultDto> ExecuteHostAsync(
        Guid companyId, Guid userId, Guid roomId, string toolName, SalesPresentationCommandRequest request,
        string? correlationId, CancellationToken cancellationToken)
    {
        var access = await ValidateHostAsync(companyId, userId, roomId, cancellationToken);
        ValidateActor(access, request.ActorType, request.ActorId, request.ActorGeneration);
        var result = await runtime.ExecuteAsync(companyId, userId, access.SessionId, toolName, request,
            correlationId, cancellationToken) ?? throw new SalesRoomAccessException("presentation_unavailable", 404);
        await CaptureAsync(access, result.Snapshot.Stage, cancellationToken);
        return result;
    }

    public async Task<SalesPresentationControlModeDto> SetControlModeAsync(
        Guid companyId, Guid userId, Guid roomId, SetSalesBrowserPresentationControlModeRequest request,
        string? correlationId, CancellationToken cancellationToken)
    {
        var access = await ValidateHostAsync(companyId, userId, roomId, cancellationToken);
        ValidateActor(access, SalesPresentationCommandActorTypes.Human, request.ActorId, request.ActorGeneration);
        var result = await runtime.SetControlModeAsync(companyId, userId, access.SessionId,
            new(request.Mode, request.ExpectedVersion), correlationId, cancellationToken)
            ?? throw new SalesRoomAccessException("presentation_unavailable", 404);
        var floor = await db.SalesRoomFloors.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.RoomId == roomId, cancellationToken);
        if (floor is not null)
        {
            floor.SetMode(result.Mode, Now);
            await db.SaveChangesAsync(cancellationToken);
        }
        return result;
    }

    public async Task<SalesBrowserPresentationReadinessDto> ConnectAsync(
        SalesBrowserPresentationAccessContext access, CancellationToken cancellationToken)
    {
        var stage = await PublicSnapshotAsync(access.CompanyId, access.SessionId, cancellationToken);
        await CaptureAsync(access, stage, cancellationToken);
        var row = await ExactRowAsync(access, stage, cancellationToken);
        row?.Connect();
        if (row is not null && db.Entry(row).State == EntityState.Modified) await db.SaveChangesAsync(cancellationToken);
        return await ReadinessAsync(access.CompanyId, access.RoomId, stage, cancellationToken);
    }

    public async Task<SalesBrowserPresentationReadinessDto> AcknowledgeAsync(
        SalesBrowserPresentationAccessContext access,
        SalesPresentationRenderAcknowledgement acknowledgement,
        CancellationToken cancellationToken)
    {
        if (acknowledgement.CompanyId != access.CompanyId || acknowledgement.SessionId != access.SessionId)
            throw new SalesRoomAccessException("presentation_acknowledgement_invalid", 403);
        var stage = await PublicSnapshotAsync(access.CompanyId, access.SessionId, cancellationToken);
        if (stage.DeckId != acknowledgement.DeckId || stage.DeckVersion != acknowledgement.DeckVersion ||
            stage.SlideNumber != acknowledgement.SlideNumber ||
            stage.Sequence != acknowledgement.PresentationSequence ||
            stage.Version != acknowledgement.PresentationVersion)
            throw new SalesRoomAccessException("presentation_acknowledgement_stale");
        await CaptureAsync(access, stage, cancellationToken);
        var row = await ExactRowAsync(access, stage, cancellationToken)
            ?? throw new SalesRoomAccessException("presentation_audience_not_captured");
        if (row.ParticipantGeneration != access.ParticipantGeneration)
            throw new SalesRoomAccessException("participant_generation_stale", 403);
        row.Render(NormalizeUtc(acknowledgement.RenderedUtc));
        await db.SaveChangesAsync(cancellationToken);
        return await ReadinessAsync(access.CompanyId, access.RoomId, stage, cancellationToken);
    }

    public async Task<SalesBrowserPresentationReadinessDto> DisconnectAsync(
        SalesBrowserPresentationAccessContext access, CancellationToken cancellationToken)
    {
        var stage = await PublicSnapshotAsync(access.CompanyId, access.SessionId, cancellationToken);
        var row = await ExactRowAsync(access, stage, cancellationToken);
        row?.Disconnect(Now);
        if (row is not null && db.Entry(row).State == EntityState.Modified) await db.SaveChangesAsync(cancellationToken);
        return await ReadinessAsync(access.CompanyId, access.RoomId, stage, cancellationToken);
    }

    public async Task<SalesBrowserPresentationReadinessDto> OverrideAsync(
        Guid companyId, Guid userId, Guid roomId, long expectedPresentationVersion,
        CancellationToken cancellationToken)
    {
        var access = await ValidateHostAsync(companyId, userId, roomId, cancellationToken);
        var stage = await PublicSnapshotAsync(companyId, access.SessionId, cancellationToken);
        if (stage.Version != expectedPresentationVersion)
            throw new SalesRoomAccessException("version_conflict");
        await CaptureAsync(access, stage, cancellationToken);
        var rows = await CurrentRowsAsync(companyId, roomId, stage, cancellationToken);
        foreach (var row in rows) row.Override(Now);
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), companyId, AuditActorTypes.User, userId,
            "sales.browser_room.presentation_render_override", "sales_browser_room", roomId.ToString("D"),
            AuditEventOutcomes.Succeeded, "The organizer continued after reviewing audience render readiness.",
            ["presentation audience acknowledgements"], new Dictionary<string, string?>
            {
                ["presentationVersion"] = stage.Version.ToString(),
                ["pendingCount"] = rows.Count(x => x.State == SalesRoomPresentationAudienceStates.Overridden).ToString()
            }, null, Now));
        await db.SaveChangesAsync(cancellationToken);
        return await ReadinessAsync(companyId, roomId, stage, cancellationToken);
    }

    private async Task CaptureAsync(
        SalesBrowserPresentationAccessContext access, SalesPresentationStageSnapshotDto stage,
        CancellationToken cancellationToken)
    {
        var exists = await db.SalesRoomPresentationAudience.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.CompanyId == access.CompanyId && x.RoomId == access.RoomId &&
            x.PresentationVersion == stage.Version, cancellationToken);
        if (exists) return;
        var audience = await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == access.CompanyId && x.RoomId == access.RoomId &&
                        x.State == SalesRoomParticipantStates.Admitted)
            .Select(x => new { x.Id, x.Generation }).ToListAsync(cancellationToken);
        var deadline = Now.AddMilliseconds(Math.Clamp(configured.Value.RenderTimeoutMilliseconds, 250, 10_000));
        db.SalesRoomPresentationAudience.AddRange(audience.Select(x => new SalesRoomPresentationAudience(
            access.CompanyId, access.RoomId, x.Id, x.Generation, stage.DeckId, stage.DeckVersion,
            stage.SlideNumber, stage.Sequence, stage.Version, Now, deadline)));
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (!await db.SalesRoomPresentationAudience.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                    x.CompanyId == access.CompanyId && x.RoomId == access.RoomId &&
                    x.PresentationVersion == stage.Version, cancellationToken)) throw;
        }
    }

    private async Task<SalesBrowserPresentationReadinessDto> ReadinessAsync(
        Guid companyId, Guid roomId, SalesPresentationStageSnapshotDto stage, CancellationToken cancellationToken)
    {
        var rows = await db.SalesRoomPresentationAudience.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.RoomId == roomId && x.PresentationVersion == stage.Version)
            .Join(db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking(),
                row => new { row.CompanyId, Id = row.ParticipantId },
                participant => new { participant.CompanyId, participant.Id },
                (row, participant) => new { Row = row, Participant = participant })
            .OrderBy(x => x.Participant.DisplayName).ToListAsync(cancellationToken);
        var deadline = rows.Count == 0 ? Now : rows.Min(x => x.Row.DeadlineUtc);
        var audience = rows.Select(x => new SalesBrowserPresentationAudienceMemberDto(
            x.Participant.Id, x.Participant.DisplayName, x.Participant.Connected,
            ProjectState(x.Row, x.Participant, deadline), x.Row.RenderedUtc)).ToArray();
        return new(roomId, stage.DeckId, stage.DeckVersion, stage.SlideNumber, stage.Sequence, stage.Version,
            deadline, rows.Any(x => x.Row.State == SalesRoomPresentationAudienceStates.Overridden), audience);
    }

    private string ProjectState(SalesRoomPresentationAudience row, SalesRoomParticipant participant, DateTime deadline)
    {
        if (row.State != SalesRoomPresentationAudienceStates.Pending) return row.State;
        if (participant.State != SalesRoomParticipantStates.Admitted) return "revoked";
        if (row.DisconnectedUtc.HasValue || !participant.Connected) return "disconnected";
        return Now >= deadline ? "slow" : SalesRoomPresentationAudienceStates.Pending;
    }

    private Task<List<SalesRoomPresentationAudience>> CurrentRowsAsync(
        Guid companyId, Guid roomId, SalesPresentationStageSnapshotDto stage, CancellationToken cancellationToken) =>
        db.SalesRoomPresentationAudience.IgnoreQueryFilters().Where(x =>
            x.CompanyId == companyId && x.RoomId == roomId && x.PresentationVersion == stage.Version &&
            x.DeckId == stage.DeckId && x.DeckVersion == stage.DeckVersion &&
            x.SlideNumber == stage.SlideNumber && x.PresentationSequence == stage.Sequence).ToListAsync(cancellationToken);

    private Task<SalesRoomPresentationAudience?> ExactRowAsync(
        SalesBrowserPresentationAccessContext access, SalesPresentationStageSnapshotDto stage,
        CancellationToken cancellationToken) =>
        db.SalesRoomPresentationAudience.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == access.CompanyId && x.RoomId == access.RoomId &&
            x.ParticipantId == access.ParticipantId && x.PresentationVersion == stage.Version &&
            x.DeckId == stage.DeckId && x.DeckVersion == stage.DeckVersion &&
            x.SlideNumber == stage.SlideNumber && x.PresentationSequence == stage.Sequence, cancellationToken);

    private async Task<SalesPresentationSlideAsset> OpenSlideAsync(
        SalesBrowserPresentationAccessContext access, Guid deckId, int deckVersion, int slideNumber,
        CancellationToken cancellationToken)
    {
        var stage = await PublicSnapshotAsync(access.CompanyId, access.SessionId, cancellationToken);
        if (stage.DeckId != deckId || stage.DeckVersion != deckVersion || stage.SlideNumber != slideNumber)
            throw new SalesRoomAccessException("presentation_slide_not_allowed", 403);
        var deck = await ActiveDeckAsync(access.CompanyId, access.SessionId, cancellationToken)
            ?? throw new SalesRoomAccessException("presentation_unavailable", 404);
        var slide = await db.SalesPresentationSlides.IgnoreQueryFilters().AsNoTracking().SingleAsync(x =>
            x.CompanyId == access.CompanyId && x.DeckId == deck.Id &&
            x.ProcessingVersion == deck.ProcessingVersion && x.SlideNumber == slideNumber,
            cancellationToken);
        var content = await storage.OpenReadAsync(slide.ImageStorageKey, cancellationToken);
        return new(content, ContentType(slide.ImageStorageKey), slide.ContentHash,
            slide.ImageWidthPixels, slide.ImageHeightPixels);
    }

    private async Task<SalesPresentationStageSnapshotDto> PublicSnapshotAsync(
        Guid companyId, Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await db.SalesMeetingSessions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.Id == sessionId, cancellationToken)
            ?? throw new SalesRoomAccessException("presentation_unavailable", 404);
        var deck = await ActiveDeckAsync(companyId, sessionId, cancellationToken)
            ?? throw new SalesRoomAccessException("presentation_unavailable", 404);
        var slideNumber = Math.Clamp(session.CurrentSlideIndex < 1 ? 1 : session.CurrentSlideIndex, 1, deck.SlideCount);
        var slide = await db.SalesPresentationSlides.IgnoreQueryFilters().AsNoTracking().SingleAsync(x =>
            x.CompanyId == companyId && x.DeckId == deck.Id &&
            x.ProcessingVersion == deck.ProcessingVersion && x.SlideNumber == slideNumber,
            cancellationToken);
        return new(session.Id, session.Status.ToStorageValue(), session.LastPresentationSequence,
            session.ConcurrencyVersion, deck.Id, deck.Version, slide.SlideNumber, deck.SlideCount,
            slide.Title, slide.ExtractedText, null, slide.ImageWidthPixels, slide.ImageHeightPixels);
    }

    private Task<SalesPresentationDeck?> ActiveDeckAsync(Guid companyId, Guid sessionId, CancellationToken cancellationToken) =>
        db.SalesPresentationDecks.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.SessionId == sessionId && x.IsActive &&
            x.Status == SalesPresentationDeckStatus.Processed && x.SlideCount > 0, cancellationToken);

    private void EnsureOpen(SalesBrowserRoom room)
    {
        if (!room.AllowsAccess(Now)) throw new SalesRoomAccessException("room_unavailable", 410);
    }

    private static void ValidateActor(
        SalesBrowserPresentationAccessContext access, string actorType, Guid? actorId, long? actorGeneration)
    {
        if (actorType != SalesPresentationCommandActorTypes.Human ||
            actorId != access.ParticipantId || actorGeneration != access.ParticipantGeneration)
            throw new SalesRoomAccessException("presentation_actor_stale", 403);
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private static string ContentType(string key) => Path.GetExtension(key).ToLowerInvariant() switch
    {
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        _ => "application/octet-stream"
    };
}
