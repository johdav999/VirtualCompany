using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed record MicrosoftGraphWebhookReceipt(int Accepted, int Duplicates, int Rejected);

public interface IMicrosoftGraphTranscriptWebhookService
{
    Task<MicrosoftGraphWebhookReceipt> ReceiveAsync(string payload, string? correlationId, CancellationToken cancellationToken);
    Task<MicrosoftGraphWebhookReceipt> ReceiveLifecycleAsync(string payload, CancellationToken cancellationToken);
}

public sealed class MicrosoftGraphTranscriptWebhookService(
    VirtualCompanyDbContext db,
    ICompanyOutboxEnqueuer outbox,
    IOptions<SalesMeetingTranscriptOptions> options,
    TimeProvider timeProvider,
    ILogger<MicrosoftGraphTranscriptWebhookService> logger) : IMicrosoftGraphTranscriptWebhookService
{
    public async Task<MicrosoftGraphWebhookReceipt> ReceiveAsync(string payload, string? correlationId,
        CancellationToken cancellationToken)
    {
        var notifications = Parse(payload, options.Value.MaximumNotificationsPerRequest);
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var accepted = 0;
        var duplicates = 0;
        var rejected = 0;
        foreach (var notification in notifications)
        {
            var subscription = await db.SalesMeetingTranscriptSubscriptions.IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.ProviderSubscriptionId == notification.SubscriptionId, cancellationToken);
            if (subscription is null)
            {
                rejected++;
                logger.LogWarning("Ignored a Microsoft Graph transcript notification for an unknown subscription.");
                continue;
            }
            if (!FixedEquals(subscription.ClientStateHash, Sha256(notification.ClientState)))
            {
                subscription.RecordAuthenticityFailure(UtcNow());
                rejected++;
                continue;
            }
            var sessionPolicy = await db.SalesMeetingSessions.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == subscription.CompanyId && x.Id == subscription.SessionId)
                .Select(x => new { x.ConsentStatus, x.RetentionUntilUtc })
                .SingleOrDefaultAsync(cancellationToken);
            if (sessionPolicy is null || sessionPolicy.ConsentStatus != SalesMeetingConsentStatus.Granted ||
                sessionPolicy.RetentionUntilUtc <= UtcNow() || subscription.RetentionUntilUtc <= UtcNow())
            {
                subscription.Disable("Transcript notification ignored because meeting consent or retention policy no longer permits ingestion.", UtcNow());
                rejected++;
                continue;
            }

            var version = string.IsNullOrWhiteSpace(notification.ProviderVersion)
                ? $"created:{notification.TranscriptId}" : notification.ProviderVersion;
            var idempotencyKey = Sha256($"{subscription.CompanyId:N}|{subscription.ProviderMeetingId}|{notification.TranscriptId}|{version}");
            var exists = await db.SalesMeetingTranscriptIngestions.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.CompanyId == subscription.CompanyId && x.IdempotencyKey == idempotencyKey, cancellationToken);
            if (exists)
            {
                duplicates++;
                subscription.RecordNotification(UtcNow());
                continue;
            }

            var ingestion = new SalesMeetingTranscriptIngestion(Guid.NewGuid(), subscription.CompanyId,
                subscription.SessionId, subscription.Id, subscription.ProviderMeetingId,
                notification.TranscriptId, version, idempotencyKey, UtcNow(), subscription.RetentionUntilUtc);
            db.SalesMeetingTranscriptIngestions.Add(ingestion);
            subscription.RecordNotification(UtcNow());
            outbox.Enqueue(subscription.CompanyId, CompanyOutboxTopics.SalesMeetingTranscriptIngestionRequested,
                new SalesMeetingTranscriptIngestionRequestedMessage(subscription.CompanyId, ingestion.Id,
                    idempotencyKey, correlationId), correlationId, idempotencyKey: idempotencyKey);
            db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), subscription.CompanyId, AuditActorTypes.System, null,
                AuditEventActions.SalesMeetingTranscriptNotificationAccepted, "sales_meeting_transcript_ingestion",
                ingestion.Id.ToString("D"), AuditEventOutcomes.Succeeded,
                "An authenticated Microsoft Graph transcript notification was durably queued.",
                ["microsoft graph notification"], new Dictionary<string, string?>
                {
                    ["sessionId"] = subscription.SessionId.ToString("D"),
                    ["subscriptionId"] = subscription.Id.ToString("D")
                }, Normalize(correlationId), UtcNow()));
            accepted++;
        }
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return new(accepted, duplicates, rejected);
    }

    public async Task<MicrosoftGraphWebhookReceipt> ReceiveLifecycleAsync(string payload,
        CancellationToken cancellationToken)
    {
        var notifications = ParseLifecycle(payload, options.Value.MaximumNotificationsPerRequest);
        var accepted = 0;
        var rejected = 0;
        foreach (var notification in notifications)
        {
            var subscription = await db.SalesMeetingTranscriptSubscriptions.IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.ProviderSubscriptionId == notification.SubscriptionId, cancellationToken);
            if (subscription is null || !FixedEquals(subscription.ClientStateHash, Sha256(notification.ClientState)))
            {
                if (subscription is not null) subscription.RecordAuthenticityFailure(UtcNow());
                rejected++;
                continue;
            }
            if (notification.Event.Equals("reauthorizationRequired", StringComparison.OrdinalIgnoreCase) ||
                notification.Event.Equals("missed", StringComparison.OrdinalIgnoreCase))
                subscription.MarkRenewalRequired("graph_subscription_reauthorization_required",
                    "Microsoft Graph requires transcript subscription reauthorization.", UtcNow());
            else if (notification.Event.Equals("subscriptionRemoved", StringComparison.OrdinalIgnoreCase))
                subscription.MarkExpired(UtcNow());
            else
            {
                rejected++;
                continue;
            }
            accepted++;
        }
        await db.SaveChangesAsync(cancellationToken);
        return new(accepted, 0, rejected);
    }

    private static IReadOnlyList<Notification> Parse(string payload, int maximum)
    {
        if (string.IsNullOrWhiteSpace(payload)) return [];
        try
        {
            using var json = JsonDocument.Parse(payload);
            if (!json.RootElement.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array) return [];
            var result = new List<Notification>();
            foreach (var value in values.EnumerateArray().Take(Math.Clamp(maximum, 1, 500)))
            {
                var subscriptionId = BoundedText(value, "subscriptionId", 256);
                var clientState = BoundedText(value, "clientState", 255);
                var resource = BoundedText(value, "resource", 1000);
                if (subscriptionId is null || clientState is null || resource is null ||
                    !value.TryGetProperty("resourceData", out var data)) continue;
                var transcriptId = BoundedText(data, "id", 512);
                if (transcriptId is null) continue;
                result.Add(new(subscriptionId, clientState, transcriptId,
                    BoundedText(data, "@odata.etag", 512) ?? BoundedText(data, "changeKey", 512)));
            }
            return result;
        }
        catch (JsonException) { return []; }
    }

    private static IReadOnlyList<LifecycleNotification> ParseLifecycle(string payload, int maximum)
    {
        if (string.IsNullOrWhiteSpace(payload)) return [];
        try
        {
            using var json = JsonDocument.Parse(payload);
            if (!json.RootElement.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array) return [];
            return values.EnumerateArray().Take(Math.Clamp(maximum, 1, 500))
                .Select(x => new LifecycleNotification(BoundedText(x, "subscriptionId", 256) ?? string.Empty,
                    BoundedText(x, "clientState", 255) ?? string.Empty,
                    BoundedText(x, "lifecycleEvent", 100) ?? string.Empty))
                .Where(x => x.SubscriptionId.Length > 0 && x.ClientState.Length > 0 && x.Event.Length > 0).ToArray();
        }
        catch (JsonException) { return []; }
    }

    private static string? Text(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() : null;
    private static string? BoundedText(JsonElement value, string name, int maximumLength)
    {
        var text = Text(value, name);
        return string.IsNullOrWhiteSpace(text) || text.Length > maximumLength ? null : text;
    }
    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static bool FixedEquals(string left, string right) => CryptographicOperations.FixedTimeEquals(
        Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(128, value.Trim().Length)];
    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
    private sealed record Notification(string SubscriptionId, string ClientState, string TranscriptId, string? ProviderVersion);
    private sealed record LifecycleNotification(string SubscriptionId, string ClientState, string Event);
}
