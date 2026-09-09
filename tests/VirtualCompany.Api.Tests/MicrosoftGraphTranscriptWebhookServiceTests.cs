using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Companies;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class MicrosoftGraphTranscriptWebhookServiceTests
{
    [Fact]
    public async Task Authenticated_duplicate_notification_is_acknowledged_once_and_never_trusts_payload_company_id()
    {
        await using var fixture = await Fixture.CreateAsync(SalesMeetingConsentStatus.Granted);
        var attackerCompanyId = Guid.NewGuid();
        var payload = fixture.Notification(companyId: attackerCompanyId);

        var first = await fixture.Service.ReceiveAsync(payload, "corr", CancellationToken.None);
        var duplicate = await fixture.Service.ReceiveAsync(payload, "corr", CancellationToken.None);

        Assert.Equal(1, first.Accepted);
        Assert.Equal(1, duplicate.Duplicates);
        var ingestion = Assert.Single(await fixture.Db.SalesMeetingTranscriptIngestions.ToListAsync());
        Assert.Equal(fixture.CompanyId, ingestion.CompanyId);
        Assert.Single(fixture.Outbox.Messages);
        Assert.Equal(CompanyOutboxTopics.SalesMeetingTranscriptIngestionRequested, fixture.Outbox.Messages[0].Topic);
    }

    [Fact]
    public async Task Invalid_client_state_is_observable_and_does_not_enqueue_content_ingestion()
    {
        await using var fixture = await Fixture.CreateAsync(SalesMeetingConsentStatus.Granted);

        var receipt = await fixture.Service.ReceiveAsync(fixture.Notification(clientState: "wrong-secret"), null, CancellationToken.None);

        Assert.Equal(1, receipt.Rejected);
        Assert.Empty(await fixture.Db.SalesMeetingTranscriptIngestions.ToListAsync());
        Assert.Empty(fixture.Outbox.Messages);
        Assert.Equal(1, (await fixture.Db.SalesMeetingTranscriptSubscriptions.SingleAsync()).AuthenticityFailureCount);
    }

    [Theory]
    [InlineData(SalesMeetingConsentStatus.Pending)]
    [InlineData(SalesMeetingConsentStatus.Denied)]
    [InlineData(SalesMeetingConsentStatus.Revoked)]
    public async Task Missing_consent_is_rejected_before_durable_ingestion(SalesMeetingConsentStatus consent)
    {
        await using var fixture = await Fixture.CreateAsync(consent);

        var receipt = await fixture.Service.ReceiveAsync(fixture.Notification(), null, CancellationToken.None);

        Assert.Equal(1, receipt.Rejected);
        Assert.Empty(await fixture.Db.SalesMeetingTranscriptIngestions.ToListAsync());
        Assert.Empty(fixture.Outbox.Messages);
        Assert.Equal(SalesMeetingTranscriptSubscriptionStatus.Disabled,
            (await fixture.Db.SalesMeetingTranscriptSubscriptions.SingleAsync()).Status);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private const string ClientState = "test-client-state-with-enough-entropy";
        private Fixture(VirtualCompanyDbContext db, Guid companyId, string providerSubscriptionId,
            RecordingOutbox outbox, MicrosoftGraphTranscriptWebhookService service)
        {
            Db = db; CompanyId = companyId; ProviderSubscriptionId = providerSubscriptionId;
            Outbox = outbox; Service = service;
        }

        public VirtualCompanyDbContext Db { get; }
        public Guid CompanyId { get; }
        public string ProviderSubscriptionId { get; }
        public RecordingOutbox Outbox { get; }
        public MicrosoftGraphTranscriptWebhookService Service { get; }

        public string Notification(string clientState = ClientState, Guid? companyId = null)
        {
            var notification = new Dictionary<string, object?>
            {
                ["subscriptionId"] = ProviderSubscriptionId,
                ["clientState"] = clientState,
                ["resource"] = "communications/onlineMeetings/meeting/transcripts/transcript-1",
                ["resourceData"] = new Dictionary<string, string>
                {
                    ["id"] = "transcript-1",
                    ["@odata.etag"] = "v1"
                }
            };
            if (companyId.HasValue) notification["companyId"] = companyId.Value;
            return JsonSerializer.Serialize(new { value = new[] { notification } });
        }

        public static async Task<Fixture> CreateAsync(SalesMeetingConsentStatus consent)
        {
            var companyId = Guid.NewGuid(); var userId = Guid.NewGuid(); var sessionId = Guid.NewGuid();
            var db = new VirtualCompanyDbContext(
                new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseInMemoryDatabase($"graph-webhook-{Guid.NewGuid():N}").Options,
                new TestContext(companyId, userId));
            var now = DateTime.UtcNow;
            var session = new SalesMeetingSession(sessionId, companyId, Guid.NewGuid(), Guid.NewGuid(), null, null,
                Guid.NewGuid(), "Confirm fit", "Operations", 30, null, "provider-meeting",
                consent, SalesMeetingRetentionPolicy.Standard, 365, now, userId, now);
            var providerSubscriptionId = $"graph-sub-{Guid.NewGuid():N}";
            db.SalesMeetingSessions.Add(session);
            db.SalesMeetingTranscriptSubscriptions.Add(new SalesMeetingTranscriptSubscription(Guid.NewGuid(), companyId,
                sessionId, Guid.NewGuid(), "provider-meeting", "online-meeting", providerSubscriptionId,
                "communications/onlineMeetings/online-meeting/transcripts", Hash(ClientState), now.AddHours(2),
                now.AddDays(365), userId, now));
            await db.SaveChangesAsync();
            var outbox = new RecordingOutbox();
            var service = new MicrosoftGraphTranscriptWebhookService(db, outbox,
                Options.Create(new SalesMeetingTranscriptOptions { MaximumNotificationsPerRequest = 100 }),
                TimeProvider.System, NullLogger<MicrosoftGraphTranscriptWebhookService>.Instance);
            return new(db, companyId, providerSubscriptionId, outbox, service);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
        private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private sealed class RecordingOutbox : ICompanyOutboxEnqueuer
    {
        public List<(Guid CompanyId, string Topic)> Messages { get; } = [];
        public void Enqueue(Guid companyId, string topic, object payload, string? correlationId = null,
            DateTime? availableAtUtc = null, string? idempotencyKey = null, string? messageType = null,
            string? causationId = null, IReadOnlyDictionary<string, string?>? headers = null) =>
            Messages.Add((companyId, topic));
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
