using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesRoomCaptureService(VirtualCompanyDbContext db, TimeProvider clock) : ISalesRoomCaptureService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public static Guid SegmentId(Guid room, Guid participant, string track, long generation, DateTime started) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"{room:N}:{participant:N}:{track}:{generation}:{started.ToUniversalTime().Ticks}")).AsSpan(0, 16));

    public async Task<Guid?> RetainAsync(RetainSalesRoomTranscript input, CancellationToken ct)
    {
        if (!input.RetentionAllowedAtSubmission || string.IsNullOrWhiteSpace(input.Text)) return null;
        // Serializable reads fence both consent withdrawal and end against the atomic evidence commit.
        // Callers use a dedicated scope so no unrelated worker mutations can enter this transaction.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var room = await db.SalesBrowserRooms.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                x.CompanyId == input.CompanyId && x.Id == input.RoomId, ct);
            if (room?.MeetingSessionId is not Guid sessionId || room.State != SalesBrowserRoomStates.Live ||
                !room.IsAgentOwner(input.LeaseOwnerId, input.AgentGeneration, Now)) return null;
            var participant = await db.SalesRoomParticipants.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                x.CompanyId == input.CompanyId && x.RoomId == input.RoomId && x.Id == input.ParticipantId, ct);
            if (participant is not { AiProcessingAllowed: true, TranscriptRetentionAllowed: true } ||
                participant.State != SalesRoomParticipantStates.Admitted || participant.Version != input.ConsentVersion ||
                participant.ExpiresUtc <= Now) return null;
            var consent = await db.SalesRoomConsents.IgnoreQueryFilters().AsNoTracking().Where(x =>
                x.CompanyId == input.CompanyId && x.RoomId == input.RoomId && x.ParticipantId == input.ParticipantId &&
                x.Purpose == "retained_transcript").OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
            if (consent is not { Granted: true } || consent.OccurredUtc > input.StartedUtc ||
                consent.Version > input.ConsentVersion) return null;
            var session = await db.SalesMeetingSessions.IgnoreQueryFilters().SingleAsync(x =>
                x.CompanyId == input.CompanyId && x.Id == sessionId, ct);
            if (session.RetentionUntilUtc <= Now || session.EndedUtc.HasValue) return null;
            var id = SegmentId(room.Id, participant.Id, input.TrackId, input.TrackGeneration, input.StartedUtc);
            if (await db.SalesRoomAgentTranscripts.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == input.CompanyId && x.Id == id, ct))
                return null; // Duplicate callbacks must not propose another question/turn.
            var sequence = (await db.SalesMeetingTranscriptSegments.IgnoreQueryFilters().Where(x =>
                x.CompanyId == room.CompanyId && x.SessionId == sessionId).MaxAsync(x => (long?)x.Sequence, ct) ?? 0) + 1;
            var text = input.Text.Trim();
            if (text.Length > 8000 || input.TrackGeneration < 1 || input.EndedUtc < input.StartedUtc)
                throw new SalesRoomAccessException("invalid_capture", 400);
            db.SalesMeetingTranscriptSegments.Add(new(id, room.CompanyId, sessionId, id, sequence,
                participant.MemberUserId.HasValue ? SalesMeetingSpeakerType.Host : SalesMeetingSpeakerType.Customer,
                participant.DisplayName, SalesMeetingInputSource.BrowserRoom, text, input.StartedUtc, input.EndedUtc,
                null, SalesMeetingReviewState.Unreviewed, room.OrganizerUserId, id, Now));
            db.SalesRoomAgentTranscripts.Add(new(room.CompanyId, room.Id, participant.Id, participant.Version,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input.TrackId))), input.TrackGeneration,
                input.StartedUtc, input.EndedUtc, input.Overlapped, text, Now, id, id, input.AgentGeneration, participant.Generation));
            session.ApplyCaptureBatch(id, session.CaptureVersion, room.OrganizerUserId, Now);
            db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), room.CompanyId, "system", null,
                "sales.browser_room.capture_retained", "sales_room_agent_transcript", id.ToString("D"), "succeeded",
                metadata: new Dictionary<string, string?> { ["participantId"] = participant.Id.ToString("D"),
                    ["consentVersion"] = participant.Version.ToString() }, occurredUtc: Now));
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return (Guid?)id;
        });
    }

    public async Task<SalesRoomCaptureReview> GetReviewAsync(Guid companyId, Guid userId, Guid roomId, CancellationToken ct)
    {
        if (!await db.CompanyMemberships.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.UserId == userId &&
                x.Status == CompanyMembershipStatus.Active && x.Role != CompanyMembershipRole.Accountant, ct))
            throw new SalesRoomAccessException("active_membership_required", 403);
        var room = await db.SalesBrowserRooms.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.Id == roomId, ct) ?? throw new SalesRoomAccessException("room_not_found", 404);
        if (room.OrganizerUserId != userId) throw new SalesRoomAccessException("organizer_required", 403);
        if (room.MeetingSessionId is not Guid sessionId) throw new SalesRoomAccessException("meeting_not_bound");
        var session = await db.SalesMeetingSessions.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.CompanyId == companyId && x.Id == sessionId, ct);
        var capture = session.RetentionUntilUtc > Now ? await SalesMeetingCaptureService.LoadSnapshotAsync(db, companyId, sessionId, ct) : null;
        var minutes = session.RetentionUntilUtc > Now ? await db.SalesMeetingMinutes.AsNoTracking().Include(x => x.Items)
            .Where(x => x.CompanyId == companyId && x.SessionId == sessionId).OrderByDescending(x => x.ArtifactVersion).FirstOrDefaultAsync(ct) : null;
        var intelligence = minutes is null ? null : await db.SalesMeetingInternalIntelligence.AsNoTracking().Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.MinutesId == minutes.Id, ct);
        return new(roomId, sessionId, room.AgentId ?? session.PresenterAgentId, room.State, session.ConcurrencyVersion, session.CaptureVersion,
            session.LastCaptureBatchId, session.RetentionUntilUtc,
            session.RetentionUntilUtc <= Now ? "expired" : "partial",
            capture?.TranscriptSegments.Where(x => x.InputSource == "browser_room").ToArray() ?? [],
            minutes is not null && intelligence is not null ? new(SalesMeetingClosingService.ToMinutesDto(minutes), SalesMeetingClosingService.ToInternalDto(intelligence)) : null,
            await db.SalesMeetingArtifacts.AsNoTracking().Where(x => x.CompanyId == companyId && x.SessionId == sessionId)
                .OrderBy(x => x.CreatedUtc).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct), session.Status.ToStorageValue());
    }

    public async Task<int> PurgeExpiredAsync(CancellationToken ct)
    {
        var now = Now;
        var rooms = await db.SalesBrowserRooms.IgnoreQueryFilters().AsNoTracking().Where(r =>
            r.State == SalesBrowserRoomStates.Ended && db.SalesMeetingSessions.IgnoreQueryFilters().Any(s =>
                s.CompanyId == r.CompanyId && s.Id == r.MeetingSessionId && s.RetentionUntilUtc <= now))
            .Where(r => db.SalesRoomAgentTranscripts.IgnoreQueryFilters().Any(t => t.CompanyId == r.CompanyId && t.RoomId == r.Id) ||
                db.SalesRoomAgentSpeech.IgnoreQueryFilters().Any(s => s.CompanyId == r.CompanyId && s.RoomId == r.Id &&
                    (s.ReleasedText != null || s.EvidenceJson != null || s.FailureSummary != null)) ||
                db.SalesMeetingQuestions.IgnoreQueryFilters().Any(q => q.CompanyId == r.CompanyId && q.SessionId == r.MeetingSessionId &&
                    q.InputSource == SalesMeetingInputSource.BrowserRoom && q.QuestionText != "[Expired meeting evidence]") ||
                db.SalesMeetingMinutes.IgnoreQueryFilters().Any(m => m.CompanyId == r.CompanyId && m.SessionId == r.MeetingSessionId &&
                    m.PromptVersion == "sales-browser-room-closing-v1" && m.Items.Any()) ||
                db.SalesMeetingInternalIntelligence.IgnoreQueryFilters().Any(m => m.CompanyId == r.CompanyId && m.SessionId == r.MeetingSessionId &&
                    m.PromptVersion == "sales-browser-room-closing-v1" && m.Items.Any()))
            .Where(r => !db.SalesMeetingMinutes.IgnoreQueryFilters().Any(m => m.CompanyId == r.CompanyId && m.SessionId == r.MeetingSessionId && m.RetentionUntilUtc > now) &&
                !db.SalesMeetingInternalIntelligence.IgnoreQueryFilters().Any(m => m.CompanyId == r.CompanyId && m.SessionId == r.MeetingSessionId && m.RetentionUntilUtc > now))
            .OrderBy(r => r.Id).Take(100).ToListAsync(ct);
        var count = 0;
        foreach (var room in rooms)
        {
            count += await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var session = await db.SalesMeetingSessions.IgnoreQueryFilters().SingleAsync(s => s.CompanyId == room.CompanyId && s.Id == room.MeetingSessionId, ct);
                if (session.RetentionUntilUtc > now || await db.SalesMeetingMinutes.IgnoreQueryFilters().AnyAsync(m => m.CompanyId == room.CompanyId && m.SessionId == session.Id && m.RetentionUntilUtc > now, ct) ||
                    await db.SalesMeetingInternalIntelligence.IgnoreQueryFilters().AnyAsync(m => m.CompanyId == room.CompanyId && m.SessionId == session.Id && m.RetentionUntilUtc > now, ct)) return 0;
                var rows = await db.SalesRoomAgentTranscripts.IgnoreQueryFilters().Where(x => x.CompanyId == room.CompanyId && x.RoomId == room.Id).ToListAsync(ct);
                var ids = rows.Where(x => x.TranscriptSegmentId.HasValue).Select(x => x.TranscriptSegmentId!.Value).ToArray();
                // The browser source and exact retained IDs exclude all Graph/Teams transcript records.
                var questions = await db.SalesMeetingQuestions.IgnoreQueryFilters().Include(x => x.Evidence).Where(x =>
                    x.CompanyId == room.CompanyId && x.SessionId == session.Id && x.InputSource == SalesMeetingInputSource.BrowserRoom).ToListAsync(ct);
                var runIds = questions.Where(x => x.AiRunId.HasValue).Select(x => x.AiRunId!.Value).ToArray();
                foreach (var question in questions.Where(q => q.QuestionText != "[Expired meeting evidence]" || q.AnswerText != null || q.Evidence.Count > 0)) question.ExpireBrowserContent(now);
                foreach (var speech in await db.SalesRoomAgentSpeech.IgnoreQueryFilters().Where(x =>
                    x.CompanyId == room.CompanyId && x.RoomId == room.Id && (x.ReleasedText != null || x.EvidenceJson != null || x.FailureSummary != null)).ToListAsync(ct))
                    speech.ExpireContent();
                foreach (var run in await db.AgentOrchestrationRuns.IgnoreQueryFilters().Where(x =>
                    x.CompanyId == room.CompanyId && (runIds.Contains(x.Id) || x.CorrelationId == "browser-closing:" + session.Id.ToString("N")) &&
                    (x.ResultJson != null || x.Summary != null || x.FailureMessage != null)).ToListAsync(ct))
                    run.ExpireContent();
                var browserMinutes = await db.SalesMeetingMinutes.IgnoreQueryFilters().Include(x => x.Items).Where(x =>
                    x.CompanyId == room.CompanyId && x.SessionId == session.Id && x.PromptVersion == "sales-browser-room-closing-v1" &&
                    x.RetentionUntilUtc <= now).ToListAsync(ct);
                foreach (var minutes in browserMinutes) minutes.Items.Clear();
                var browserInternal = await db.SalesMeetingInternalIntelligence.IgnoreQueryFilters().Include(x => x.Items).Where(x =>
                    x.CompanyId == room.CompanyId && x.SessionId == session.Id && x.PromptVersion == "sales-browser-room-closing-v1" &&
                    x.RetentionUntilUtc <= now).ToListAsync(ct);
                foreach (var intelligence in browserInternal) intelligence.Items.Clear();
                db.SalesMeetingActionItems.RemoveRange(await db.SalesMeetingActionItems.IgnoreQueryFilters().Where(x =>
                    x.CompanyId == room.CompanyId && x.SessionId == session.Id && ids.Contains(x.ClientItemId) &&
                    x.SourceReference != null && x.SourceReference.StartsWith("transcript:")).ToListAsync(ct));
                db.SalesRoomAgentTranscripts.RemoveRange(rows);
                await db.SaveChangesAsync(ct);
                db.SalesMeetingTranscriptSegments.RemoveRange(await db.SalesMeetingTranscriptSegments.IgnoreQueryFilters().Where(x =>
                    x.CompanyId == room.CompanyId && x.SessionId == session.Id && ids.Contains(x.Id) && x.InputSource == SalesMeetingInputSource.BrowserRoom).ToListAsync(ct));
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return rows.Count;
            });
        }
        return count;
    }
}
