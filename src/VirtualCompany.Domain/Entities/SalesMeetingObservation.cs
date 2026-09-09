using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class SalesMeetingObservation : ICompanyOwnedEntity
{
    private SalesMeetingObservation() { }
    public SalesMeetingObservation(Guid id, Guid companyId, Guid sessionId, Guid clientItemId, long sequence,
        SalesMeetingObservationCategory category, string content, decimal? confidence, string? sourceReference,
        SalesMeetingReviewState reviewState, Guid createdByUserId, Guid clientBatchId, DateTime nowUtc)
    {
        SalesMeetingTranscriptSegment.EnsureIds(companyId, sessionId, clientItemId, createdByUserId, clientBatchId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; SessionId = sessionId; ClientItemId = clientItemId;
        Sequence = SalesMeetingTranscriptSegment.Positive(sequence, nameof(sequence)); Category = category; _ = category.ToStorageValue();
        Content = SalesMeetingTranscriptSegment.Required(content, nameof(content), 4000);
        Confidence = SalesMeetingTranscriptSegment.ConfidenceValue(confidence); SourceReference = SalesMeetingTranscriptSegment.Optional(sourceReference, 500);
        ReviewState = reviewState; _ = reviewState.ToStorageValue(); CreatedByUserId = createdByUserId; LastClientBatchId = clientBatchId;
        CreatedUtc = SalesMeetingTranscriptSegment.Utc(nowUtc); UpdatedUtc = CreatedUtc; ConcurrencyVersion = 1;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid ClientItemId { get; private set; }
    public long Sequence { get; private set; }
    public SalesMeetingObservationCategory Category { get; private set; }
    public string Content { get; private set; } = null!;
    public decimal? Confidence { get; private set; }
    public string? SourceReference { get; private set; }
    public SalesMeetingReviewState ReviewState { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid LastClientBatchId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long ConcurrencyVersion { get; private set; }
    public SalesMeetingSession Session { get; private set; } = null!;
    public void Update(long expectedVersion, long sequence, SalesMeetingObservationCategory category, string content,
        decimal? confidence, string? sourceReference, SalesMeetingReviewState reviewState, Guid batchId, DateTime nowUtc)
    {
        if (expectedVersion != ConcurrencyVersion) throw new InvalidOperationException("The observation changed after it was opened.");
        Sequence = SalesMeetingTranscriptSegment.Positive(sequence, nameof(sequence)); Category = category; _ = category.ToStorageValue();
        Content = SalesMeetingTranscriptSegment.Required(content, nameof(content), 4000);
        Confidence = SalesMeetingTranscriptSegment.ConfidenceValue(confidence); SourceReference = SalesMeetingTranscriptSegment.Optional(sourceReference, 500);
        ReviewState = reviewState; _ = reviewState.ToStorageValue(); LastClientBatchId = batchId;
        UpdatedUtc = SalesMeetingTranscriptSegment.Utc(nowUtc); ConcurrencyVersion++;
    }
}
