using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesMeetingTranscriptReconciliationQuery(VirtualCompanyDbContext db)
    : ISalesMeetingTranscriptReconciliationQuery
{
    public async Task<SalesMeetingTranscriptReconciliationStatusDto?> GetStatusAsync(Guid companyId, Guid userId,
        Guid sessionId, CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty || userId == Guid.Empty || sessionId == Guid.Empty) throw new ArgumentException("Required identifiers cannot be empty.");
        if (!await db.CompanyMemberships.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.UserId == userId && x.Status == CompanyMembershipStatus.Active, cancellationToken))
            throw new UnauthorizedAccessException("An active company membership is required.");
        var session = await db.SalesMeetingSessions.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == sessionId, cancellationToken);
        if (session is null) return null;
        var subscriptions = (await db.SalesMeetingTranscriptSubscriptions.AsNoTracking().Where(x => x.CompanyId == companyId && x.SessionId == sessionId).OrderByDescending(x => x.CreatedUtc).ToListAsync(cancellationToken)).Select(SalesMeetingTranscriptSubscriptionService.ToDto).ToArray();
        var ingestions = (await db.SalesMeetingTranscriptIngestions.AsNoTracking().Where(x => x.CompanyId == companyId && x.SessionId == sessionId).OrderByDescending(x => x.ReceivedUtc).Take(50).ToListAsync(cancellationToken)).Select(x => new SalesMeetingTranscriptIngestionDto(x.Id, x.Status.ToStorageValue(), x.ProviderTranscriptId, x.ProviderVersion, x.ReceivedUtc, x.AttemptCount, x.EquivalentCount, x.AddedCount, x.SpeakerCorrectionCount, x.ConflictCount, x.MateriallyChanged, x.FailureCode, x.FailureSummary, x.CompletedUtc)).ToArray();
        var conflicts = await db.SalesMeetingTranscriptProvenance.AsNoTracking().Where(x => x.CompanyId == companyId && x.SessionId == sessionId && x.RequiresReview).OrderByDescending(x => x.CreatedUtc).Take(100).Select(x => new SalesMeetingTranscriptConflictDto(x.Id, x.TranscriptSegmentId, x.ProviderSegmentId, x.BeforeContent, x.ProviderContent, x.BeforeSpeakerLabel, x.ProviderSpeakerLabel, x.ConflictSummary ?? "Transcript evidence requires review.", x.CreatedUtc)).ToListAsync(cancellationToken);
        var stale = await db.SalesMeetingMinutes.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.SessionId == sessionId && x.IsEvidenceStale, cancellationToken) || await db.SalesMeetingInternalIntelligence.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.SessionId == sessionId && x.IsEvidenceStale, cancellationToken);
        return new(sessionId, session.TranscriptReconciliationVersion, stale,
            ingestions.Count(x => x.Status is "pending" or "processing" or "retry_pending"), conflicts.Count,
            subscriptions, ingestions, conflicts);
    }
}
