using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class SalesMeetingProviderTranscript : ICompanyOwnedEntity
{
    private SalesMeetingProviderTranscript() { }

    public SalesMeetingProviderTranscript(Guid id, Guid companyId, Guid sessionId, Guid subscriptionId,
        string providerTranscriptId, string providerVersion, string contentHash, string metadataJson,
        DateTime providerCreatedUtc, DateTime fetchedUtc, DateTime retentionUntilUtc)
    {
        SalesMeetingTranscriptSegment.EnsureIds(companyId, sessionId, subscriptionId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SessionId = sessionId;
        SubscriptionId = subscriptionId;
        ProviderTranscriptId = Required(providerTranscriptId, nameof(providerTranscriptId), 512);
        ProviderVersion = Required(providerVersion, nameof(providerVersion), 512);
        ContentHash = Required(contentHash, nameof(contentHash), 128);
        MetadataJson = Required(metadataJson, nameof(metadataJson), 4000);
        ProviderCreatedUtc = SalesMeetingTranscriptSegment.Utc(providerCreatedUtc);
        FetchedUtc = SalesMeetingTranscriptSegment.Utc(fetchedUtc);
        RetentionUntilUtc = SalesMeetingTranscriptSegment.Utc(retentionUntilUtc);
        UpdatedUtc = FetchedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public string ProviderTranscriptId { get; private set; } = null!;
    public string ProviderVersion { get; private set; } = null!;
    public string ContentHash { get; private set; } = null!;
    public string MetadataJson { get; private set; } = null!;
    public DateTime ProviderCreatedUtc { get; private set; }
    public DateTime FetchedUtc { get; private set; }
    public DateTime RetentionUntilUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public SalesMeetingSession Session { get; private set; } = null!;
    public SalesMeetingTranscriptSubscription Subscription { get; private set; } = null!;

    public bool IsSameVersion(string providerVersion, string contentHash) =>
        string.Equals(ProviderVersion, providerVersion, StringComparison.Ordinal) &&
        string.Equals(ContentHash, contentHash, StringComparison.Ordinal);

    public void Refresh(string providerVersion, string contentHash, string metadataJson, DateTime fetchedUtc)
    {
        ProviderVersion = Required(providerVersion, nameof(providerVersion), 512);
        ContentHash = Required(contentHash, nameof(contentHash), 128);
        MetadataJson = Required(metadataJson, nameof(metadataJson), 4000);
        FetchedUtc = SalesMeetingTranscriptSegment.Utc(fetchedUtc);
        UpdatedUtc = FetchedUtc;
    }

    private static string Required(string? value, string name, int max) =>
        SalesMeetingTranscriptSegment.Required(value, name, max);
}
