using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class SalesMeetingActionItem : ICompanyOwnedEntity
{
    private SalesMeetingActionItem() { }
    public SalesMeetingActionItem(Guid id, Guid companyId, Guid sessionId, Guid clientItemId, long sequence,
        string title, string? details, string? ownerLabel, DateTime? dueUtc, SalesMeetingActionItemStatus status,
        decimal? confidence, string? sourceReference, SalesMeetingReviewState reviewState,
        Guid createdByUserId, Guid clientBatchId, DateTime nowUtc)
    {
        SalesMeetingTranscriptSegment.EnsureIds(companyId, sessionId, clientItemId, createdByUserId, clientBatchId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; SessionId = sessionId; ClientItemId = clientItemId;
        Sequence = SalesMeetingTranscriptSegment.Positive(sequence, nameof(sequence)); Title = SalesMeetingTranscriptSegment.Required(title, nameof(title), 500);
        Details = SalesMeetingTranscriptSegment.Optional(details, 4000); OwnerLabel = SalesMeetingTranscriptSegment.Optional(ownerLabel, 160);
        DueUtc = dueUtc.HasValue ? SalesMeetingTranscriptSegment.Utc(dueUtc.Value) : null; Status = status; _ = status.ToStorageValue();
        Confidence = SalesMeetingTranscriptSegment.ConfidenceValue(confidence); SourceReference = SalesMeetingTranscriptSegment.Optional(sourceReference, 500);
        ReviewState = reviewState; _ = reviewState.ToStorageValue(); CreatedByUserId = createdByUserId; LastClientBatchId = clientBatchId;
        CreatedUtc = SalesMeetingTranscriptSegment.Utc(nowUtc); UpdatedUtc = CreatedUtc; ConcurrencyVersion = 1;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid ClientItemId { get; private set; }
    public long Sequence { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Details { get; private set; }
    public string? OwnerLabel { get; private set; }
    public DateTime? DueUtc { get; private set; }
    public SalesMeetingActionItemStatus Status { get; private set; }
    public decimal? Confidence { get; private set; }
    public string? SourceReference { get; private set; }
    public SalesMeetingReviewState ReviewState { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid LastClientBatchId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long ConcurrencyVersion { get; private set; }
    public SalesMeetingSession Session { get; private set; } = null!;
    public void Update(long expectedVersion, long sequence, string title, string? details, string? ownerLabel,
        DateTime? dueUtc, SalesMeetingActionItemStatus status, decimal? confidence, string? sourceReference,
        SalesMeetingReviewState reviewState, Guid batchId, DateTime nowUtc)
    {
        if (expectedVersion != ConcurrencyVersion) throw new InvalidOperationException("The action item changed after it was opened.");
        Sequence = SalesMeetingTranscriptSegment.Positive(sequence, nameof(sequence)); Title = SalesMeetingTranscriptSegment.Required(title, nameof(title), 500);
        Details = SalesMeetingTranscriptSegment.Optional(details, 4000); OwnerLabel = SalesMeetingTranscriptSegment.Optional(ownerLabel, 160);
        DueUtc = dueUtc.HasValue ? SalesMeetingTranscriptSegment.Utc(dueUtc.Value) : null; Status = status; _ = status.ToStorageValue();
        Confidence = SalesMeetingTranscriptSegment.ConfidenceValue(confidence); SourceReference = SalesMeetingTranscriptSegment.Optional(sourceReference, 500);
        ReviewState = reviewState; _ = reviewState.ToStorageValue(); LastClientBatchId = batchId;
        UpdatedUtc = SalesMeetingTranscriptSegment.Utc(nowUtc); ConcurrencyVersion++;
    }
}
