using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class SalesMeetingTranscriptSubscription : ICompanyOwnedEntity
{
    private SalesMeetingTranscriptSubscription() { }

    public SalesMeetingTranscriptSubscription(Guid id, Guid companyId, Guid sessionId, Guid calendarConnectionId,
        string providerMeetingId, string providerOnlineMeetingId, string providerSubscriptionId,
        string providerResource, string clientStateHash, DateTime expiresUtc, DateTime retentionUntilUtc,
        Guid createdByUserId, DateTime nowUtc)
    {
        SalesMeetingTranscriptSegment.EnsureIds(companyId, sessionId, calendarConnectionId, createdByUserId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SessionId = sessionId;
        CalendarConnectionId = calendarConnectionId;
        ProviderMeetingId = Required(providerMeetingId, nameof(providerMeetingId), 512);
        ProviderOnlineMeetingId = Required(providerOnlineMeetingId, nameof(providerOnlineMeetingId), 512);
        ProviderSubscriptionId = Required(providerSubscriptionId, nameof(providerSubscriptionId), 256);
        ProviderResource = Required(providerResource, nameof(providerResource), 1000);
        ClientStateHash = Required(clientStateHash, nameof(clientStateHash), 128);
        ExpiresUtc = SalesMeetingTranscriptSegment.Utc(expiresUtc);
        RetentionUntilUtc = SalesMeetingTranscriptSegment.Utc(retentionUntilUtc);
        CreatedByUserId = createdByUserId;
        CreatedUtc = SalesMeetingTranscriptSegment.Utc(nowUtc);
        UpdatedUtc = CreatedUtc;
        Status = SalesMeetingTranscriptSubscriptionStatus.Active;
        ConcurrencyVersion = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid CalendarConnectionId { get; private set; }
    public string ProviderMeetingId { get; private set; } = null!;
    public string ProviderOnlineMeetingId { get; private set; } = null!;
    public string ProviderSubscriptionId { get; private set; } = null!;
    public string ProviderResource { get; private set; } = null!;
    public string ClientStateHash { get; private set; } = null!;
    public SalesMeetingTranscriptSubscriptionStatus Status { get; private set; }
    public DateTime ExpiresUtc { get; private set; }
    public DateTime RetentionUntilUtc { get; private set; }
    public DateTime? LastNotificationUtc { get; private set; }
    public DateTime? LastRenewedUtc { get; private set; }
    public int RenewalAttemptCount { get; private set; }
    public int AuthenticityFailureCount { get; private set; }
    public string? LastErrorCode { get; private set; }
    public string? LastErrorSummary { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long ConcurrencyVersion { get; private set; }
    public SalesMeetingSession Session { get; private set; } = null!;
    public CalendarConnection CalendarConnection { get; private set; } = null!;

    public void RecordNotification(DateTime nowUtc)
    {
        LastNotificationUtc = SalesMeetingTranscriptSegment.Utc(nowUtc);
        UpdatedUtc = LastNotificationUtc.Value;
        ConcurrencyVersion++;
    }

    public void RecordAuthenticityFailure(DateTime nowUtc)
    {
        AuthenticityFailureCount++;
        LastErrorCode = "graph_webhook_authenticity_failed";
        LastErrorSummary = "A Microsoft Graph transcript notification failed client-state validation.";
        UpdatedUtc = SalesMeetingTranscriptSegment.Utc(nowUtc);
        ConcurrencyVersion++;
    }

    public void Renew(string providerSubscriptionId, DateTime expiresUtc, DateTime nowUtc)
    {
        ProviderSubscriptionId = Required(providerSubscriptionId, nameof(providerSubscriptionId), 256);
        ExpiresUtc = SalesMeetingTranscriptSegment.Utc(expiresUtc);
        LastRenewedUtc = SalesMeetingTranscriptSegment.Utc(nowUtc);
        RenewalAttemptCount++;
        Status = SalesMeetingTranscriptSubscriptionStatus.Active;
        LastErrorCode = null;
        LastErrorSummary = null;
        UpdatedUtc = LastRenewedUtc.Value;
        ConcurrencyVersion++;
    }

    public void MarkRenewalRequired(string code, string summary, DateTime nowUtc) =>
        MarkFailure(SalesMeetingTranscriptSubscriptionStatus.RenewalRequired, code, summary, nowUtc);

    public void MarkPermissionRequired(string code, string summary, DateTime nowUtc) =>
        MarkFailure(SalesMeetingTranscriptSubscriptionStatus.PermissionRequired, code, summary, nowUtc);

    public void MarkExpired(DateTime nowUtc) =>
        MarkFailure(SalesMeetingTranscriptSubscriptionStatus.Expired, "graph_subscription_expired",
            "The Microsoft Graph transcript subscription expired and must be recreated.", nowUtc);

    public void Disable(string summary, DateTime nowUtc) =>
        MarkFailure(SalesMeetingTranscriptSubscriptionStatus.Disabled, "transcript_subscription_disabled", summary, nowUtc);

    public void MarkFailed(string code, string summary, DateTime nowUtc) =>
        MarkFailure(SalesMeetingTranscriptSubscriptionStatus.Failed, code, summary, nowUtc);

    private void MarkFailure(SalesMeetingTranscriptSubscriptionStatus status, string code, string summary, DateTime nowUtc)
    {
        Status = status;
        LastErrorCode = Required(code, nameof(code), 120);
        LastErrorSummary = Required(summary, nameof(summary), 1000);
        UpdatedUtc = SalesMeetingTranscriptSegment.Utc(nowUtc);
        ConcurrencyVersion++;
    }

    private static string Required(string? value, string name, int max) =>
        SalesMeetingTranscriptSegment.Required(value, name, max);
}
