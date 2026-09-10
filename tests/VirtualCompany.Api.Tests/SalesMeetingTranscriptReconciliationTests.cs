using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class SalesMeetingTranscriptReconciliationTests
{
    [Fact]
    public async Task Reconciliation_merges_equivalent_corrects_unreviewed_adds_gaps_and_preserves_reviewed_conflicts()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Provider.Document = new(
            new("transcript-1", "provider-v2", fixture.Now, fixture.Now.AddMinutes(1), "{\"id\":\"transcript-1\"}"),
            new string('a', 64),
            [
                new("cue-correct", "We need a faster close.", "Jordan", fixture.Now.AddSeconds(1), fixture.Now.AddSeconds(3)),
                new("cue-conflict", "Security review is required.", "Jordan", fixture.Now.AddSeconds(4), fixture.Now.AddSeconds(6)),
                new("cue-equivalent", "Procurement joins next week.", "Alex", fixture.Now.AddSeconds(7), fixture.Now.AddSeconds(9)),
                new("cue-gap", "The rollout starts in October.", "Jordan", fixture.Now.AddSeconds(10), fixture.Now.AddSeconds(12))
            ]);

        await fixture.Dispatcher.DispatchAsync(new(fixture.CompanyId, fixture.IngestionId, "ingestion-key", "corr"), CancellationToken.None);

        var ingestion = await fixture.Db.SalesMeetingTranscriptIngestions.SingleAsync();
        Assert.Equal(SalesMeetingTranscriptIngestionStatus.Completed, ingestion.Status);
        Assert.Equal(1, ingestion.SpeakerCorrectionCount);
        Assert.Equal(1, ingestion.ConflictCount);
        Assert.Equal(1, ingestion.EquivalentCount);
        Assert.Equal(1, ingestion.AddedCount);

        var segments = await fixture.Db.SalesMeetingTranscriptSegments.OrderBy(x => x.Sequence).ToListAsync();
        Assert.Equal(4, segments.Count);
        Assert.Equal("Jordan", segments.Single(x => x.Content == "We need a faster close.").SpeakerLabel);
        Assert.Equal("Host review", segments.Single(x => x.Content == "Security is required.").SpeakerLabel);
        Assert.Contains(segments, x => x.Content == "The rollout starts in October." &&
            x.InputSource == SalesMeetingInputSource.TranscriptAdapter);

        var provenance = await fixture.Db.SalesMeetingTranscriptProvenance.ToListAsync();
        Assert.Equal(4, provenance.Count);
        Assert.Contains(provenance, x => x.MatchKind == SalesMeetingTranscriptMatchKind.ReviewConflict &&
            x.RequiresReview && x.BeforeContent == "Security is required." &&
            x.ProviderContent == "Security review is required." && x.BeforeSpeakerLabel == "Host review");
        Assert.Contains(provenance, x => x.MatchKind == SalesMeetingTranscriptMatchKind.Equivalent);

        var session = await fixture.Db.SalesMeetingSessions.SingleAsync();
        Assert.Equal(1, session.TranscriptReconciliationVersion);
        Assert.Equal(1, session.CaptureVersion);
        Assert.True((await fixture.Db.SalesMeetingMinutes.SingleAsync()).IsEvidenceStale);
        Assert.True((await fixture.Db.SalesMeetingInternalIntelligence.SingleAsync()).IsEvidenceStale);
    }

    [Fact]
    public async Task Replayed_provider_version_does_not_duplicate_evidence_or_advance_reconciliation_version()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Provider.Document = new(
            new("transcript-1", "provider-v1", fixture.Now, null, "{}"), new string('b', 64),
            [new("cue-equivalent", "Procurement joins next week.", "Alex", fixture.Now.AddSeconds(7), fixture.Now.AddSeconds(9))]);

        await fixture.Dispatcher.DispatchAsync(new(fixture.CompanyId, fixture.IngestionId, "ingestion-key", null), CancellationToken.None);
        var replay = new SalesMeetingTranscriptIngestion(Guid.NewGuid(), fixture.CompanyId, fixture.SessionId,
            fixture.SubscriptionId, "provider-meeting", "transcript-1", "older-notification", "replay-key",
            fixture.Now.AddMinutes(1), fixture.Now.AddDays(30));
        fixture.Db.SalesMeetingTranscriptIngestions.Add(replay);
        await fixture.Db.SaveChangesAsync();

        await fixture.Dispatcher.DispatchAsync(new(fixture.CompanyId, replay.Id, "replay-key", null), CancellationToken.None);

        Assert.Equal(3, await fixture.Db.SalesMeetingTranscriptSegments.CountAsync());
        Assert.Equal(SalesMeetingTranscriptIngestionStatus.Ignored, replay.Status);
        Assert.Equal("provider_version_already_reconciled", replay.FailureCode);
        Assert.Equal(0, (await fixture.Db.SalesMeetingSessions.SingleAsync()).TranscriptReconciliationVersion);
    }

    [Fact]
    public async Task Retryable_provider_failure_is_left_recoverable_for_the_background_executor()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Provider.Failure = new MeetingTranscriptProviderException("TooManyRequests", "Graph is temporarily unavailable.",
            MeetingTranscriptProviderFailureKind.Retryable, TimeSpan.FromSeconds(10));

        await Assert.ThrowsAsync<HttpRequestException>(() => fixture.Dispatcher.DispatchAsync(
            new(fixture.CompanyId, fixture.IngestionId, "ingestion-key", null), CancellationToken.None));

        var ingestion = await fixture.Db.SalesMeetingTranscriptIngestions.SingleAsync();
        Assert.Equal(SalesMeetingTranscriptIngestionStatus.RetryPending, ingestion.Status);
        Assert.Equal("TooManyRequests", ingestion.FailureCode);
        Assert.Equal(1, ingestion.AttemptCount);
    }

    [Fact]
    public async Task Graph_reconciliation_never_rewrites_or_claims_browser_evidence()
    {
        await using var f = await Fixture.CreateAsync();
        var browser = new SalesMeetingTranscriptSegment(Guid.NewGuid(), f.CompanyId, f.SessionId, Guid.NewGuid(), 4,
            SalesMeetingSpeakerType.Customer, "Browser participant", SalesMeetingInputSource.BrowserRoom, "Browser-only evidence.",
            f.Now.AddSeconds(30), f.Now.AddSeconds(32), null, SalesMeetingReviewState.Unreviewed, Guid.NewGuid(), Guid.NewGuid(), f.Now);
        f.Db.SalesMeetingTranscriptSegments.Add(browser);
        await f.Db.SaveChangesAsync();
        f.Provider.Document = new(new("transcript-1", "provider-browser-overlap", f.Now, null, "{}"), new string('d', 64),
            [new("browser-overlap", "Browser-only evidence.", "Graph speaker", f.Now.AddSeconds(30), f.Now.AddSeconds(32))]);
        await f.Dispatcher.DispatchAsync(new(f.CompanyId, f.IngestionId, "ingestion-key", null), default);
        Assert.Equal("Browser participant", browser.SpeakerLabel);
        Assert.Equal(SalesMeetingInputSource.BrowserRoom, browser.InputSource);
        Assert.DoesNotContain(await f.Db.SalesMeetingTranscriptProvenance.ToListAsync(), p => p.TranscriptSegmentId == browser.Id);
        Assert.Equal(2, await f.Db.SalesMeetingTranscriptSegments.CountAsync(s => s.Content == "Browser-only evidence."));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(VirtualCompanyDbContext db, Guid companyId, Guid sessionId, Guid subscriptionId,
            Guid ingestionId, DateTime now, RecordingProvider provider, SalesMeetingTranscriptIngestionDispatcher dispatcher)
        {
            Db = db; CompanyId = companyId; SessionId = sessionId; SubscriptionId = subscriptionId;
            IngestionId = ingestionId; Now = now; Provider = provider; Dispatcher = dispatcher;
        }
        public VirtualCompanyDbContext Db { get; }
        public Guid CompanyId { get; }
        public Guid SessionId { get; }
        public Guid SubscriptionId { get; }
        public Guid IngestionId { get; }
        public DateTime Now { get; }
        public RecordingProvider Provider { get; }
        public SalesMeetingTranscriptIngestionDispatcher Dispatcher { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var companyId = Guid.NewGuid(); var userId = Guid.NewGuid(); var sessionId = Guid.NewGuid();
            var invitationId = Guid.NewGuid(); var connectionId = Guid.NewGuid(); var subscriptionId = Guid.NewGuid();
            var ingestionId = Guid.NewGuid(); var now = new DateTime(2026, 9, 4, 8, 0, 0, DateTimeKind.Utc);
            var db = new VirtualCompanyDbContext(
                new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseInMemoryDatabase($"transcript-reconcile-{Guid.NewGuid():N}").Options,
                new TestContext(companyId, userId));
            db.SalesMeetingInvitations.Add(new SalesMeetingInvitation(invitationId, companyId, Guid.NewGuid(), null,
                null, connectionId, ExternalAccountProvider.Microsoft365, "organizer@example.com", "customer@example.com",
                "Jordan", "Demo", "Confirm fit", now, now.AddMinutes(30), "UTC", null, true, userId, now));
            db.SalesMeetingSessions.Add(new SalesMeetingSession(sessionId, companyId, invitationId, Guid.NewGuid(), null,
                null, Guid.NewGuid(), "Confirm fit", "Operations", 30, null, "provider-meeting",
                SalesMeetingConsentStatus.Granted, SalesMeetingRetentionPolicy.Standard, 365, now, userId, now));
            db.SalesMeetingTranscriptSubscriptions.Add(new SalesMeetingTranscriptSubscription(subscriptionId, companyId,
                sessionId, connectionId, "provider-meeting", "online-meeting", "graph-subscription",
                "communications/onlineMeetings/online-meeting/transcripts", new string('c', 64), now.AddHours(2),
                now.AddDays(365), userId, now));
            db.SalesMeetingTranscriptIngestions.Add(new SalesMeetingTranscriptIngestion(ingestionId, companyId,
                sessionId, subscriptionId, "provider-meeting", "transcript-1", "notification-v2", "ingestion-key",
                now, now.AddDays(365)));
            db.SalesMeetingTranscriptSegments.AddRange(
                Segment(1, "Unknown", "We need a faster close.", SalesMeetingReviewState.Unreviewed, now.AddSeconds(1)),
                Segment(2, "Host review", "Security is required.", SalesMeetingReviewState.Reviewed, now.AddSeconds(4)),
                Segment(3, "Alex", "Procurement joins next week.", SalesMeetingReviewState.Unreviewed, now.AddSeconds(7)));
            var minutesId = Guid.NewGuid(); var agentId = Guid.NewGuid();
            db.SalesMeetingMinutes.Add(new SalesMeetingMinutes(minutesId, companyId, sessionId, Guid.NewGuid(), null, 1,
                0, now, agentId, null, "deterministic-v1", "v1", now.AddDays(365), userId, now));
            db.SalesMeetingInternalIntelligence.Add(new SalesMeetingInternalIntelligence(Guid.NewGuid(), companyId,
                sessionId, minutesId, 1, 0, now, agentId, null, "deterministic-v1", "v1", now.AddDays(365), userId, now));
            await db.SaveChangesAsync();
            var provider = new RecordingProvider();
            var dispatcher = new SalesMeetingTranscriptIngestionDispatcher(db, new TokenLease(connectionId, companyId),
                provider, new FixedTime(now.AddMinutes(2)), NullLogger<SalesMeetingTranscriptIngestionDispatcher>.Instance);
            return new(db, companyId, sessionId, subscriptionId, ingestionId, now, provider, dispatcher);

            SalesMeetingTranscriptSegment Segment(long sequence, string speaker, string content,
                SalesMeetingReviewState reviewState, DateTime start) => new(Guid.NewGuid(), companyId, sessionId,
                Guid.NewGuid(), sequence, SalesMeetingSpeakerType.Unknown, speaker, SalesMeetingInputSource.HostMediated,
                content, start, start.AddSeconds(2), .8m, reviewState, userId, Guid.NewGuid(), now);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class RecordingProvider : IMeetingTranscriptProviderAdapter
    {
        public MeetingTranscriptDocument Document { get; set; } = null!;
        public MeetingTranscriptProviderException? Failure { get; set; }
        public string Provider => "test";
        public IReadOnlyCollection<string> RequiredScopes => ["transcript.read"];
        public Task<MeetingTranscriptDocument> FetchTranscriptAsync(MeetingTranscriptProviderContext context, string onlineMeetingId, string transcriptId, CancellationToken cancellationToken) =>
            Failure is null ? Task.FromResult(Document) : Task.FromException<MeetingTranscriptDocument>(Failure);
        public Task<MeetingTranscriptResolvedMeeting> ResolveMeetingAsync(MeetingTranscriptProviderContext context, string onlineMeetingUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MeetingTranscriptProviderSubscription> CreateSubscriptionAsync(MeetingTranscriptProviderContext context, string onlineMeetingId, Uri notificationUrl, Uri lifecycleNotificationUrl, string clientState, DateTime expiresUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MeetingTranscriptProviderSubscription> RenewSubscriptionAsync(MeetingTranscriptProviderContext context, string subscriptionId, string resource, DateTime expiresUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteSubscriptionAsync(MeetingTranscriptProviderContext context, string subscriptionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MeetingTranscriptDescriptorPage> ListTranscriptsAsync(MeetingTranscriptProviderContext context, string onlineMeetingId, string? continuationToken, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class TokenLease(Guid connectionId, Guid companyId) : ICalendarOAuthAccessTokenLeaseService
    {
        public Task<CalendarOAuthAccessTokenLease> AcquireAsync(Guid requestedCompanyId, Guid calendarConnectionId,
            IReadOnlyCollection<string> requiredScopes, CancellationToken cancellationToken) => Task.FromResult(
                new CalendarOAuthAccessTokenLease(connectionId, Guid.NewGuid(), companyId,
                    ExternalAccountProvider.Microsoft365, "organizer@example.com", "token", DateTime.UtcNow.AddHours(1),
                    requiredScopes, "primary"));
    }

    private sealed class FixedTime(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private sealed class TestContext(Guid companyId, Guid userId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId;
        public Guid? UserId { get; private set; } = userId;
        public bool IsResolved => true;
        public ResolvedCompanyMembershipContext? Membership { get; private set; }
        public void SetCompanyId(Guid? value) => CompanyId = value;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? value)
        {
            Membership = value; CompanyId = value?.CompanyId; UserId = value?.UserId;
        }
    }
}
