using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class SalesMeetingTranscriptProvenance : ICompanyOwnedEntity
{
    private SalesMeetingTranscriptProvenance() { }

    public SalesMeetingTranscriptProvenance(Guid id, Guid companyId, Guid sessionId, Guid providerTranscriptId,
        Guid transcriptSegmentId, string providerSegmentId, string providerVersion, string providerContentHash,
        string providerContent, string? providerSpeakerLabel, DateTime providerStartedUtc, DateTime? providerEndedUtc,
        SalesMeetingTranscriptMatchKind matchKind, string? beforeContent, string? beforeSpeakerLabel, string? conflictSummary,
        DateTime createdUtc)
    {
        SalesMeetingTranscriptSegment.EnsureIds(companyId, sessionId, providerTranscriptId, transcriptSegmentId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SessionId = sessionId;
        ProviderTranscriptId = providerTranscriptId;
        TranscriptSegmentId = transcriptSegmentId;
        ProviderSegmentId = Required(providerSegmentId, nameof(providerSegmentId), 256);
        ProviderVersion = Required(providerVersion, nameof(providerVersion), 512);
        ProviderContentHash = Required(providerContentHash, nameof(providerContentHash), 128);
        ProviderContent = Required(providerContent, nameof(providerContent), 8000);
        ProviderSpeakerLabel = Optional(providerSpeakerLabel, 160);
        ProviderStartedUtc = SalesMeetingTranscriptSegment.Utc(providerStartedUtc);
        ProviderEndedUtc = providerEndedUtc.HasValue ? SalesMeetingTranscriptSegment.Utc(providerEndedUtc.Value) : null;
        MatchKind = matchKind;
        _ = matchKind.ToStorageValue();
        BeforeContent = Optional(beforeContent, 8000);
        BeforeSpeakerLabel = Optional(beforeSpeakerLabel, 160);
        ConflictSummary = Optional(conflictSummary, 1000);
        RequiresReview = matchKind == SalesMeetingTranscriptMatchKind.ReviewConflict;
        CreatedUtc = SalesMeetingTranscriptSegment.Utc(createdUtc);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid ProviderTranscriptId { get; private set; }
    public Guid TranscriptSegmentId { get; private set; }
    public string ProviderSegmentId { get; private set; } = null!;
    public string ProviderVersion { get; private set; } = null!;
    public string ProviderContentHash { get; private set; } = null!;
    public string ProviderContent { get; private set; } = null!;
    public string? ProviderSpeakerLabel { get; private set; }
    public DateTime ProviderStartedUtc { get; private set; }
    public DateTime? ProviderEndedUtc { get; private set; }
    public SalesMeetingTranscriptMatchKind MatchKind { get; private set; }
    public string? BeforeContent { get; private set; }
    public string? BeforeSpeakerLabel { get; private set; }
    public string? ConflictSummary { get; private set; }
    public bool RequiresReview { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public SalesMeetingSession Session { get; private set; } = null!;
    public SalesMeetingProviderTranscript ProviderTranscript { get; private set; } = null!;
    public SalesMeetingTranscriptSegment TranscriptSegment { get; private set; } = null!;

    private static string Required(string? value, string name, int max) =>
        SalesMeetingTranscriptSegment.Required(value, name, max);
    private static string? Optional(string? value, int max) =>
        SalesMeetingTranscriptSegment.Optional(value, max);
}
