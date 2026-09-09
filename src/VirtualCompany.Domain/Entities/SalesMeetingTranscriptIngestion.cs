using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class SalesMeetingTranscriptIngestion : ICompanyOwnedEntity
{
    private SalesMeetingTranscriptIngestion() { }

    public SalesMeetingTranscriptIngestion(Guid id, Guid companyId, Guid sessionId, Guid subscriptionId,
        string providerMeetingId, string providerTranscriptId, string providerVersion,
        string idempotencyKey, DateTime receivedUtc, DateTime retentionUntilUtc)
    {
        SalesMeetingTranscriptSegment.EnsureIds(companyId, sessionId, subscriptionId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SessionId = sessionId;
        SubscriptionId = subscriptionId;
        ProviderMeetingId = Required(providerMeetingId, nameof(providerMeetingId), 512);
        ProviderTranscriptId = Required(providerTranscriptId, nameof(providerTranscriptId), 512);
        ProviderVersion = Required(providerVersion, nameof(providerVersion), 512);
        IdempotencyKey = Required(idempotencyKey, nameof(idempotencyKey), 128);
        ReceivedUtc = SalesMeetingTranscriptSegment.Utc(receivedUtc);
        RetentionUntilUtc = SalesMeetingTranscriptSegment.Utc(retentionUntilUtc);
        Status = SalesMeetingTranscriptIngestionStatus.Pending;
        UpdatedUtc = ReceivedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public string ProviderMeetingId { get; private set; } = null!;
    public string ProviderTranscriptId { get; private set; } = null!;
    public string ProviderVersion { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public SalesMeetingTranscriptIngestionStatus Status { get; private set; }
    public DateTime ReceivedUtc { get; private set; }
    public DateTime RetentionUntilUtc { get; private set; }
    public int AttemptCount { get; private set; }
    public int EquivalentCount { get; private set; }
    public int AddedCount { get; private set; }
    public int SpeakerCorrectionCount { get; private set; }
    public int ConflictCount { get; private set; }
    public bool MateriallyChanged { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public DateTime? ProcessingStartedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public SalesMeetingSession Session { get; private set; } = null!;
    public SalesMeetingTranscriptSubscription Subscription { get; private set; } = null!;

    public void Begin(DateTime nowUtc)
    {
        if (Status is SalesMeetingTranscriptIngestionStatus.Completed or SalesMeetingTranscriptIngestionStatus.Ignored) return;
        AttemptCount++;
        Status = SalesMeetingTranscriptIngestionStatus.Processing;
        ProcessingStartedUtc = SalesMeetingTranscriptSegment.Utc(nowUtc);
        FailureCode = null;
        FailureSummary = null;
        UpdatedUtc = ProcessingStartedUtc.Value;
    }

    public void Complete(int equivalent, int added, int speakerCorrections, int conflicts, DateTime nowUtc)
    {
        EquivalentCount = Math.Max(0, equivalent);
        AddedCount = Math.Max(0, added);
        SpeakerCorrectionCount = Math.Max(0, speakerCorrections);
        ConflictCount = Math.Max(0, conflicts);
        MateriallyChanged = added > 0 || speakerCorrections > 0 || conflicts > 0;
        Status = SalesMeetingTranscriptIngestionStatus.Completed;
        CompletedUtc = SalesMeetingTranscriptSegment.Utc(nowUtc);
        UpdatedUtc = CompletedUtc.Value;
    }

    public void MarkRetry(string code, string summary, DateTime nowUtc) =>
        MarkFailure(SalesMeetingTranscriptIngestionStatus.RetryPending, code, summary, nowUtc);

    public void MarkPermanentFailure(string code, string summary, DateTime nowUtc) =>
        MarkFailure(SalesMeetingTranscriptIngestionStatus.PermanentFailure, code, summary, nowUtc);

    public void Ignore(string code, string summary, DateTime nowUtc) =>
        MarkFailure(SalesMeetingTranscriptIngestionStatus.Ignored, code, summary, nowUtc);

    private void MarkFailure(SalesMeetingTranscriptIngestionStatus status, string code, string summary, DateTime nowUtc)
    {
        Status = status;
        FailureCode = Required(code, nameof(code), 120);
        FailureSummary = Required(summary, nameof(summary), 1000);
        UpdatedUtc = SalesMeetingTranscriptSegment.Utc(nowUtc);
    }

    private static string Required(string? value, string name, int max) =>
        SalesMeetingTranscriptSegment.Required(value, name, max);
}
