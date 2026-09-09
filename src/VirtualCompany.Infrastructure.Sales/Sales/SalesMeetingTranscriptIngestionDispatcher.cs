using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesMeetingTranscriptIngestionDispatcher(
    VirtualCompanyDbContext db,
    ICalendarOAuthAccessTokenLeaseService tokenLeases,
    IMeetingTranscriptProviderAdapter provider,
    TimeProvider timeProvider,
    ILogger<SalesMeetingTranscriptIngestionDispatcher> logger) : ISalesMeetingTranscriptIngestionDispatcher
{
    private static readonly Meter Meter = new("VirtualCompany.Sales.MeetingTranscripts");
    private static readonly Counter<long> Reconciliations = Meter.CreateCounter<long>("sales_meeting_transcript_reconciliations");
    private static readonly Counter<long> Conflicts = Meter.CreateCounter<long>("sales_meeting_transcript_conflicts");

    public async Task DispatchAsync(SalesMeetingTranscriptIngestionRequestedMessage message,
        CancellationToken cancellationToken)
    {
        EnsureIds(message.CompanyId, message.IngestionId);
        var ingestion = await db.SalesMeetingTranscriptIngestions
            .SingleOrDefaultAsync(x => x.CompanyId == message.CompanyId && x.Id == message.IngestionId, cancellationToken);
        if (ingestion is null) throw new InvalidOperationException("The transcript ingestion request no longer exists.");
        if (!string.Equals(ingestion.IdempotencyKey, message.IdempotencyKey, StringComparison.Ordinal))
            throw new InvalidOperationException("The transcript ingestion idempotency identity does not match.");
        if (ingestion.Status is SalesMeetingTranscriptIngestionStatus.Completed or SalesMeetingTranscriptIngestionStatus.Ignored) return;

        var session = await db.SalesMeetingSessions.SingleAsync(x => x.CompanyId == message.CompanyId && x.Id == ingestion.SessionId, cancellationToken);
        var subscription = await db.SalesMeetingTranscriptSubscriptions.SingleAsync(x => x.CompanyId == message.CompanyId && x.Id == ingestion.SubscriptionId, cancellationToken);
        var invitation = await db.SalesMeetingInvitations.AsNoTracking().SingleAsync(x => x.CompanyId == message.CompanyId && x.Id == session.InvitationId, cancellationToken);
        var now = UtcNow();
        if (session.ConsentStatus != SalesMeetingConsentStatus.Granted)
        {
            ingestion.MarkPermanentFailure("meeting_consent_required", "Transcript ingestion stopped because meeting consent is not granted.", now);
            await db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("Transcript ingestion requires recorded meeting consent.");
        }
        if (session.RetentionUntilUtc <= now || ingestion.RetentionUntilUtc <= now)
        {
            ingestion.Ignore("meeting_retention_expired", "Transcript ingestion was discarded because the meeting retention period expired.", now);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        ingestion.Begin(now);
        await db.SaveChangesAsync(cancellationToken);
        try
        {
            var lease = await tokenLeases.AcquireAsync(message.CompanyId, subscription.CalendarConnectionId,
                provider.RequiredScopes, cancellationToken);
            var context = new MeetingTranscriptProviderContext(lease.AccessToken, invitation.OrganizerEmail, invitation.StartsUtc);
            var document = await provider.FetchTranscriptAsync(context, subscription.ProviderOnlineMeetingId,
                ingestion.ProviderTranscriptId, cancellationToken);
            await ReconcileAsync(session, subscription, ingestion, document, message.CorrelationId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (MeetingTranscriptProviderException e)
        {
            await RecordProviderFailureAsync(ingestion, subscription, e, cancellationToken);
            if (e.Kind == MeetingTranscriptProviderFailureKind.Retryable)
                throw new HttpRequestException(e.Message, e);
            throw e.Kind == MeetingTranscriptProviderFailureKind.AuthenticationRequired
                ? new UnauthorizedAccessException(e.Message, e)
                : new InvalidOperationException(e.Message, e);
        }
        catch (InvalidOperationException e) when (e.InnerException is null)
        {
            ingestion.MarkPermanentFailure("transcript_ingestion_policy_failure", Safe(e.Message), UtcNow());
            await db.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    private async Task ReconcileAsync(SalesMeetingSession session, SalesMeetingTranscriptSubscription subscription,
        SalesMeetingTranscriptIngestion ingestion, MeetingTranscriptDocument document, string? correlationId,
        CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var providerTranscript = await db.SalesMeetingProviderTranscripts.SingleOrDefaultAsync(
            x => x.CompanyId == session.CompanyId && x.SessionId == session.Id &&
                 x.ProviderTranscriptId == document.Descriptor.TranscriptId, cancellationToken);
        if (providerTranscript is not null && providerTranscript.IsSameVersion(document.Descriptor.Version, document.ContentHash))
        {
            ingestion.Ignore("provider_version_already_reconciled", "This provider transcript version was already reconciled.", now);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }
        if (providerTranscript is null)
        {
            providerTranscript = new SalesMeetingProviderTranscript(Guid.NewGuid(), session.CompanyId, session.Id,
                subscription.Id, document.Descriptor.TranscriptId, document.Descriptor.Version,
                document.ContentHash, document.Descriptor.MetadataJson, document.Descriptor.CreatedUtc,
                now, session.RetentionUntilUtc);
            db.SalesMeetingProviderTranscripts.Add(providerTranscript);
        }
        else providerTranscript.Refresh(document.Descriptor.Version, document.ContentHash, document.Descriptor.MetadataJson, now);

        var live = await db.SalesMeetingTranscriptSegments.Where(x => x.CompanyId == session.CompanyId && x.SessionId == session.Id)
            .OrderBy(x => x.Sequence).ToListAsync(cancellationToken);
        var priorProviderSegmentIds = await db.SalesMeetingTranscriptProvenance
            .Where(x => x.CompanyId == session.CompanyId && x.ProviderTranscriptId == providerTranscript.Id &&
                        x.ProviderVersion == document.Descriptor.Version)
            .Select(x => x.ProviderSegmentId).ToListAsync(cancellationToken);
        var prior = priorProviderSegmentIds.ToHashSet(StringComparer.Ordinal);
        var equivalent = 0;
        var added = 0;
        var corrected = 0;
        var conflicts = 0;
        var nextSequence = live.Count == 0 ? 1 : live.Max(x => x.Sequence) + 1;
        foreach (var providerSegment in document.Segments.Where(x => !prior.Contains(x.SegmentId)))
        {
            var contentHash = Hash(NormalizeText(providerSegment.Content));
            var match = live.FirstOrDefault(x => Equivalent(x, providerSegment));
            SalesMeetingTranscriptMatchKind kind;
            string? conflict = null;
            string? beforeContent = match?.Content;
            string? beforeSpeaker = match?.SpeakerLabel;
            if (match is not null)
            {
                var speakerChanged = !string.IsNullOrWhiteSpace(providerSegment.SpeakerLabel) &&
                    !string.Equals(NormalizeLabel(match.SpeakerLabel), NormalizeLabel(providerSegment.SpeakerLabel), StringComparison.OrdinalIgnoreCase);
                if (speakerChanged && match.ReviewState == SalesMeetingReviewState.Reviewed)
                {
                    kind = SalesMeetingTranscriptMatchKind.ReviewConflict;
                    conflict = "Microsoft Graph reported different speaker attribution for user-reviewed meeting evidence.";
                    conflicts++;
                }
                else if (speakerChanged)
                {
                    match.Update(match.ConcurrencyVersion, match.Sequence, match.SpeakerType,
                        providerSegment.SpeakerLabel, match.InputSource, match.Content, match.StartedUtc,
                        match.EndedUtc, match.Confidence, match.ReviewState, ingestion.Id, now);
                    kind = SalesMeetingTranscriptMatchKind.SpeakerCorrected;
                    corrected++;
                }
                else
                {
                    kind = SalesMeetingTranscriptMatchKind.Equivalent;
                    equivalent++;
                }
            }
            else
            {
                var overlap = live.FirstOrDefault(x => TimeOverlaps(x.StartedUtc, x.EndedUtc, providerSegment.StartedUtc, providerSegment.EndedUtc));
                if (overlap is not null && overlap.ReviewState == SalesMeetingReviewState.Reviewed)
                {
                    match = overlap;
                    kind = SalesMeetingTranscriptMatchKind.ReviewConflict;
                    conflict = "Microsoft Graph reported different content during the same period as user-reviewed meeting evidence.";
                    beforeContent = overlap.Content;
                    beforeSpeaker = overlap.SpeakerLabel;
                    conflicts++;
                }
                else
                {
                    match = new SalesMeetingTranscriptSegment(Guid.NewGuid(), session.CompanyId, session.Id,
                        DeterministicGuid($"{providerTranscript.Id:N}|{providerSegment.SegmentId}"), nextSequence++,
                        SalesMeetingSpeakerType.Unknown, providerSegment.SpeakerLabel,
                        SalesMeetingInputSource.TranscriptAdapter, providerSegment.Content,
                        providerSegment.StartedUtc, providerSegment.EndedUtc, null,
                        SalesMeetingReviewState.Unreviewed, session.CreatedByUserId, ingestion.Id, now);
                    db.SalesMeetingTranscriptSegments.Add(match);
                    live.Add(match);
                    kind = SalesMeetingTranscriptMatchKind.GapAdded;
                    added++;
                }
            }
            db.SalesMeetingTranscriptProvenance.Add(new SalesMeetingTranscriptProvenance(Guid.NewGuid(),
                session.CompanyId, session.Id, providerTranscript.Id, match.Id, providerSegment.SegmentId,
                document.Descriptor.Version, contentHash, providerSegment.Content, providerSegment.SpeakerLabel,
                providerSegment.StartedUtc, providerSegment.EndedUtc, kind, beforeContent, beforeSpeaker, conflict, now));
        }

        var materiallyChanged = added > 0 || corrected > 0 || conflicts > 0;
        if (materiallyChanged)
        {
            session.ApplyTranscriptReconciliation(ingestion.Id, now);
            const string staleReason = "Meeting transcript evidence changed after this artifact version was generated.";
            foreach (var minutes in await db.SalesMeetingMinutes.Where(x => x.CompanyId == session.CompanyId && x.SessionId == session.Id && !x.IsEvidenceStale).ToListAsync(cancellationToken))
                minutes.MarkEvidenceStale(staleReason, now);
            foreach (var intelligence in await db.SalesMeetingInternalIntelligence.Where(x => x.CompanyId == session.CompanyId && x.SessionId == session.Id && !x.IsEvidenceStale).ToListAsync(cancellationToken))
                intelligence.MarkEvidenceStale(staleReason, now);
        }
        ingestion.Complete(equivalent, added, corrected, conflicts, now);
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), session.CompanyId, AuditActorTypes.System, null,
            AuditEventActions.SalesMeetingTranscriptReconciled, "sales_meeting_transcript_ingestion",
            ingestion.Id.ToString("D"), AuditEventOutcomes.Succeeded,
            conflicts > 0
                ? "Microsoft Graph transcript evidence was reconciled with conflicts retained for human review."
                : "Microsoft Graph transcript evidence was reconciled without overwriting reviewed evidence.",
            ["microsoft graph transcript", "live meeting transcript"], new Dictionary<string, string?>
            {
                ["sessionId"] = session.Id.ToString("D"), ["equivalentCount"] = equivalent.ToString(),
                ["addedCount"] = added.ToString(), ["speakerCorrectionCount"] = corrected.ToString(),
                ["conflictCount"] = conflicts.ToString(), ["materiallyChanged"] = materiallyChanged.ToString()
            }, NormalizeCorrelation(correlationId), now));
        await db.SaveChangesAsync(cancellationToken);
        Reconciliations.Add(1, new KeyValuePair<string, object?>("outcome", conflicts > 0 ? "review_required" : "completed"));
        if (conflicts > 0) Conflicts.Add(conflicts);
        logger.LogInformation("Reconciled Microsoft Graph transcript ingestion {IngestionId}. Added {Added}; corrected {Corrected}; conflicts {Conflicts}.", ingestion.Id, added, corrected, conflicts);
    }

    private async Task RecordProviderFailureAsync(SalesMeetingTranscriptIngestion ingestion,
        SalesMeetingTranscriptSubscription subscription, MeetingTranscriptProviderException exception,
        CancellationToken cancellationToken)
    {
        var now = UtcNow();
        if (exception.Kind == MeetingTranscriptProviderFailureKind.Retryable)
            ingestion.MarkRetry(exception.Code, Safe(exception.Message), now);
        else ingestion.MarkPermanentFailure(exception.Code, Safe(exception.Message), now);
        if (exception.Kind == MeetingTranscriptProviderFailureKind.AuthenticationRequired)
            subscription.MarkPermissionRequired(exception.Code, Safe(exception.Message), now);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static bool Equivalent(SalesMeetingTranscriptSegment stored, MeetingTranscriptNormalizedSegment incoming) =>
        string.Equals(NormalizeText(stored.Content), NormalizeText(incoming.Content), StringComparison.Ordinal) &&
        (TimeOverlaps(stored.StartedUtc, stored.EndedUtc, incoming.StartedUtc, incoming.EndedUtc) ||
         Math.Abs((stored.StartedUtc - incoming.StartedUtc).TotalSeconds) <= 2);

    private static bool TimeOverlaps(DateTime leftStart, DateTime? leftEnd, DateTime rightStart, DateTime? rightEnd)
    {
        var aEnd = leftEnd ?? leftStart.AddSeconds(3);
        var bEnd = rightEnd ?? rightStart.AddSeconds(3);
        return leftStart <= bEnd && rightStart <= aEnd;
    }

    private static string NormalizeText(string value) => string.Join(' ', value.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static string? NormalizeLabel(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static Guid DeterministicGuid(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));
    private static string Safe(string value) => value.Trim()[..Math.Min(1000, value.Trim().Length)];
    private static string? NormalizeCorrelation(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(128, value.Trim().Length)];
    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
    private static void EnsureIds(params Guid[] ids) { if (ids.Any(x => x == Guid.Empty)) throw new ArgumentException("Required identifiers cannot be empty."); }
}
