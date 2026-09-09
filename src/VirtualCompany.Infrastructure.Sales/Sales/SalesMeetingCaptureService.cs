using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesMeetingCaptureService(VirtualCompanyDbContext db, TimeProvider timeProvider) : ISalesMeetingCaptureService
{
    public async Task<SalesMeetingCaptureSnapshotDto?> GetAsync(Guid companyId, Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        await EnsureMemberAsync(companyId, userId, cancellationToken);
        return await LoadSnapshotAsync(db, companyId, sessionId, cancellationToken);
    }

    public async Task<SalesMeetingCaptureSaveResultDto?> AutosaveAsync(Guid companyId, Guid userId, Guid sessionId,
        AutosaveSalesMeetingCaptureRequest request, string? correlationId, CancellationToken cancellationToken)
    {
        EnsureIds(companyId, userId, sessionId, request.BatchId);
        var transcripts = request.TranscriptSegments ?? [];
        var observations = request.Observations ?? [];
        var actions = request.ActionItems ?? [];
        if (transcripts.Count + observations.Count + actions.Count == 0)
            throw Validation(nameof(request), "Autosave must contain at least one transcript segment, observation, or action item.");
        if (transcripts.Count > 200 || observations.Count > 100 || actions.Count > 100)
            throw Validation(nameof(request), "The autosave batch exceeds the supported item limit.");
        EnsureDistinct(transcripts.Select(x => x.ClientItemId), nameof(request.TranscriptSegments));
        EnsureDistinct(observations.Select(x => x.ClientItemId), nameof(request.Observations));
        EnsureDistinct(actions.Select(x => x.ClientItemId), nameof(request.ActionItems));
        await EnsureMemberAsync(companyId, userId, cancellationToken);

        var session = await db.SalesMeetingSessions.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == sessionId, cancellationToken);
        if (session is null) return null;
        if (session.LastCaptureBatchId == request.BatchId)
            return new("duplicate", (await LoadSnapshotAsync(db, companyId, sessionId, cancellationToken))!);

        var transcriptIds = transcripts.Select(x => x.ClientItemId).ToArray();
        var observationIds = observations.Select(x => x.ClientItemId).ToArray();
        var actionIds = actions.Select(x => x.ClientItemId).ToArray();
        var existingTranscripts = await db.SalesMeetingTranscriptSegments.Where(x => x.CompanyId == companyId && x.SessionId == sessionId && transcriptIds.Contains(x.ClientItemId)).ToDictionaryAsync(x => x.ClientItemId, cancellationToken);
        var existingObservations = await db.SalesMeetingObservations.Where(x => x.CompanyId == companyId && x.SessionId == sessionId && observationIds.Contains(x.ClientItemId)).ToDictionaryAsync(x => x.ClientItemId, cancellationToken);
        var existingActions = await db.SalesMeetingActionItems.Where(x => x.CompanyId == companyId && x.SessionId == sessionId && actionIds.Contains(x.ClientItemId)).ToDictionaryAsync(x => x.ClientItemId, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var changed = false;

        try
        {
            foreach (var item in transcripts)
            {
                var speaker = SalesMeetingCaptureEnumValues.ParseSpeakerType(item.SpeakerType);
                var source = SalesMeetingCaptureEnumValues.ParseInputSource(item.InputSource);
                var review = SalesMeetingCaptureEnumValues.ParseReviewState(item.ReviewState);
                if (existingTranscripts.TryGetValue(item.ClientItemId, out var existing))
                {
                    if (Same(existing, item, speaker, source, review)) continue;
                    existing.Update(item.ExpectedVersion ?? 0, item.Sequence, speaker, item.SpeakerLabel, source, item.Content, item.StartedUtc, item.EndedUtc, item.Confidence, review, request.BatchId, now);
                }
                else db.SalesMeetingTranscriptSegments.Add(new(Guid.NewGuid(), companyId, sessionId, item.ClientItemId, item.Sequence, speaker, item.SpeakerLabel, source, item.Content, item.StartedUtc, item.EndedUtc, item.Confidence, review, userId, request.BatchId, now));
                changed = true;
            }
            foreach (var item in observations)
            {
                var category = SalesMeetingCaptureEnumValues.ParseObservationCategory(item.Category);
                var review = SalesMeetingCaptureEnumValues.ParseReviewState(item.ReviewState);
                if (existingObservations.TryGetValue(item.ClientItemId, out var existing))
                {
                    if (Same(existing, item, category, review)) continue;
                    existing.Update(item.ExpectedVersion ?? 0, item.Sequence, category, item.Content, item.Confidence, item.SourceReference, review, request.BatchId, now);
                }
                else db.SalesMeetingObservations.Add(new(Guid.NewGuid(), companyId, sessionId, item.ClientItemId, item.Sequence, category, item.Content, item.Confidence, item.SourceReference, review, userId, request.BatchId, now));
                changed = true;
            }
            foreach (var item in actions)
            {
                var status = SalesMeetingCaptureEnumValues.ParseActionItemStatus(item.Status);
                var review = SalesMeetingCaptureEnumValues.ParseReviewState(item.ReviewState);
                if (existingActions.TryGetValue(item.ClientItemId, out var existing))
                {
                    if (Same(existing, item, status, review)) continue;
                    existing.Update(item.ExpectedVersion ?? 0, item.Sequence, item.Title, item.Details, item.OwnerLabel, item.DueUtc, status, item.Confidence, item.SourceReference, review, request.BatchId, now);
                }
                else db.SalesMeetingActionItems.Add(new(Guid.NewGuid(), companyId, sessionId, item.ClientItemId, item.Sequence, item.Title, item.Details, item.OwnerLabel, item.DueUtc, status, item.Confidence, item.SourceReference, review, userId, request.BatchId, now));
                changed = true;
            }
            if (!changed)
                return new("duplicate", (await LoadSnapshotAsync(db, companyId, sessionId, cancellationToken))!);
            session.ApplyCaptureBatch(request.BatchId, request.ExpectedCaptureVersion, userId, now);
        }
        catch (ArgumentException exception) { throw Validation(nameof(request), exception.Message); }
        catch (InvalidOperationException exception) { throw Conflict(exception.Message); }

        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), companyId, AuditActorTypes.User, userId,
            AuditEventActions.SalesMeetingCaptureAutosaved, "sales_meeting_session", sessionId.ToString("D"),
            AuditEventOutcomes.Succeeded, "Meeting-owned discussion records were autosaved.",
            ["meeting capture"], new Dictionary<string, string?>
            {
                ["batchId"] = request.BatchId.ToString("D"), ["captureVersion"] = session.CaptureVersion.ToString(),
                ["transcriptCount"] = transcripts.Count.ToString(), ["observationCount"] = observations.Count.ToString(), ["actionItemCount"] = actions.Count.ToString()
            }, Normalize(correlationId), now));
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw Conflict("The meeting capture changed after it was opened. Refresh before saving again."); }
        catch (DbUpdateException) { throw Conflict("The meeting capture order or idempotency key conflicts with a saved record. Refresh before saving again."); }
        return new("accepted", (await LoadSnapshotAsync(db, companyId, sessionId, cancellationToken))!);
    }

    internal static async Task<SalesMeetingCaptureSnapshotDto?> LoadSnapshotAsync(VirtualCompanyDbContext db, Guid companyId, Guid sessionId, CancellationToken ct)
    {
        var session = await db.SalesMeetingSessions.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == sessionId, ct);
        if (session is null) return null;
        var transcripts = await db.SalesMeetingTranscriptSegments.AsNoTracking().Where(x => x.CompanyId == companyId && x.SessionId == sessionId).OrderBy(x => x.Sequence).Select(x => new SalesMeetingTranscriptSegmentDto(x.Id, x.ClientItemId, x.Sequence, x.SpeakerType.ToStorageValue(), x.SpeakerLabel, x.InputSource.ToStorageValue(), x.Content, x.StartedUtc, x.EndedUtc, x.Confidence, x.ReviewState.ToStorageValue(), x.UpdatedUtc, x.ConcurrencyVersion)).ToListAsync(ct);
        var observations = await db.SalesMeetingObservations.AsNoTracking().Where(x => x.CompanyId == companyId && x.SessionId == sessionId).OrderBy(x => x.Sequence).Select(x => new SalesMeetingObservationDto(x.Id, x.ClientItemId, x.Sequence, x.Category.ToStorageValue(), x.Content, x.Confidence, x.SourceReference, x.ReviewState.ToStorageValue(), x.UpdatedUtc, x.ConcurrencyVersion)).ToListAsync(ct);
        var actions = await db.SalesMeetingActionItems.AsNoTracking().Where(x => x.CompanyId == companyId && x.SessionId == sessionId).OrderBy(x => x.Sequence).Select(x => new SalesMeetingActionItemDto(x.Id, x.ClientItemId, x.Sequence, x.Title, x.Details, x.OwnerLabel, x.DueUtc, x.Status.ToStorageValue(), x.Confidence, x.SourceReference, x.ReviewState.ToStorageValue(), x.UpdatedUtc, x.ConcurrencyVersion)).ToListAsync(ct);
        var questions = await db.SalesMeetingQuestions.AsNoTracking().Include(x => x.Evidence).Where(x => x.CompanyId == companyId && x.SessionId == sessionId).OrderBy(x => x.Sequence).ToListAsync(ct);
        return new(sessionId, session.CaptureVersion, session.LastCaptureBatchId, session.UpdatedUtc, transcripts, observations, actions, questions.Select(SalesMeetingQuestionAnsweringService.ToDto).ToArray());
    }

    private async Task EnsureMemberAsync(Guid companyId, Guid userId, CancellationToken ct)
    {
        EnsureIds(companyId, userId);
        if (!await db.CompanyMemberships.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.UserId == userId && x.Status == CompanyMembershipStatus.Active, ct))
            throw new UnauthorizedAccessException("An active company membership is required.");
    }
    private static bool Same(SalesMeetingTranscriptSegment x, SalesMeetingTranscriptSegmentDraft y, SalesMeetingSpeakerType s, SalesMeetingInputSource i, SalesMeetingReviewState r) => x.Sequence == y.Sequence && x.SpeakerType == s && x.SpeakerLabel == Clean(y.SpeakerLabel) && x.InputSource == i && x.Content == Clean(y.Content) && x.StartedUtc == ToUtc(y.StartedUtc) && x.EndedUtc == (y.EndedUtc.HasValue ? ToUtc(y.EndedUtc.Value) : null) && x.Confidence == y.Confidence && x.ReviewState == r;
    private static bool Same(SalesMeetingObservation x, SalesMeetingObservationDraft y, SalesMeetingObservationCategory c, SalesMeetingReviewState r) => x.Sequence == y.Sequence && x.Category == c && x.Content == Clean(y.Content) && x.Confidence == y.Confidence && x.SourceReference == Clean(y.SourceReference) && x.ReviewState == r;
    private static bool Same(SalesMeetingActionItem x, SalesMeetingActionItemDraft y, SalesMeetingActionItemStatus s, SalesMeetingReviewState r) => x.Sequence == y.Sequence && x.Title == Clean(y.Title) && x.Details == Clean(y.Details) && x.OwnerLabel == Clean(y.OwnerLabel) && x.DueUtc == (y.DueUtc.HasValue ? ToUtc(y.DueUtc.Value) : null) && x.Status == s && x.Confidence == y.Confidence && x.SourceReference == Clean(y.SourceReference) && x.ReviewState == r;
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static DateTime ToUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static void EnsureDistinct(IEnumerable<Guid> ids, string field) { var values = ids.ToArray(); if (values.Any(x => x == Guid.Empty) || values.Distinct().Count() != values.Length) throw Validation(field, "Client item IDs must be non-empty and unique within a batch."); }
    private static void EnsureIds(params Guid[] ids) { if (ids.Any(x => x == Guid.Empty)) throw new ArgumentException("Required identifiers cannot be empty."); }
    private static SalesMeetingCaptureValidationException Validation(string field, string message) => new(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { [field] = [message] });
    private static SalesMeetingCaptureConflictException Conflict(string message) => new(SalesMeetingCaptureProblemCodes.Conflict, message);
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 128)];
}
