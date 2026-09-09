using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class SalesMeetingTranscriptSegment : ICompanyOwnedEntity
{
    private SalesMeetingTranscriptSegment() { }

    public SalesMeetingTranscriptSegment(Guid id, Guid companyId, Guid sessionId, Guid clientItemId, long sequence,
        SalesMeetingSpeakerType speakerType, string? speakerLabel, SalesMeetingInputSource inputSource,
        string content, DateTime startedUtc, DateTime? endedUtc, decimal? confidence,
        SalesMeetingReviewState reviewState, Guid createdByUserId, Guid clientBatchId, DateTime nowUtc)
    {
        EnsureIds(companyId, sessionId, clientItemId, createdByUserId, clientBatchId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId; SessionId = sessionId; ClientItemId = clientItemId;
        Sequence = Positive(sequence, nameof(sequence));
        SpeakerType = speakerType; _ = speakerType.ToStorageValue();
        SpeakerLabel = Optional(speakerLabel, 160); InputSource = inputSource; _ = inputSource.ToStorageValue();
        Content = Required(content, nameof(content), 8000);
        StartedUtc = Utc(startedUtc); EndedUtc = endedUtc.HasValue ? Utc(endedUtc.Value) : null;
        if (EndedUtc < StartedUtc) throw new ArgumentException("A transcript segment cannot end before it starts.");
        Confidence = ConfidenceValue(confidence); ReviewState = reviewState; _ = reviewState.ToStorageValue();
        CreatedByUserId = createdByUserId; LastClientBatchId = clientBatchId;
        CreatedUtc = Utc(nowUtc); UpdatedUtc = CreatedUtc; ConcurrencyVersion = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid ClientItemId { get; private set; }
    public long Sequence { get; private set; }
    public SalesMeetingSpeakerType SpeakerType { get; private set; }
    public string? SpeakerLabel { get; private set; }
    public SalesMeetingInputSource InputSource { get; private set; }
    public string Content { get; private set; } = null!;
    public DateTime StartedUtc { get; private set; }
    public DateTime? EndedUtc { get; private set; }
    public decimal? Confidence { get; private set; }
    public SalesMeetingReviewState ReviewState { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid LastClientBatchId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long ConcurrencyVersion { get; private set; }
    public SalesMeetingSession Session { get; private set; } = null!;

    public void Update(long expectedVersion, long sequence, SalesMeetingSpeakerType speakerType, string? speakerLabel,
        SalesMeetingInputSource inputSource, string content, DateTime startedUtc, DateTime? endedUtc,
        decimal? confidence, SalesMeetingReviewState reviewState, Guid batchId, DateTime nowUtc)
    {
        EnsureVersion(expectedVersion); Sequence = Positive(sequence, nameof(sequence));
        SpeakerType = speakerType; _ = speakerType.ToStorageValue(); SpeakerLabel = Optional(speakerLabel, 160);
        InputSource = inputSource; _ = inputSource.ToStorageValue(); Content = Required(content, nameof(content), 8000);
        StartedUtc = Utc(startedUtc); EndedUtc = endedUtc.HasValue ? Utc(endedUtc.Value) : null;
        if (EndedUtc < StartedUtc) throw new ArgumentException("A transcript segment cannot end before it starts.");
        Confidence = ConfidenceValue(confidence); ReviewState = reviewState; _ = reviewState.ToStorageValue();
        LastClientBatchId = batchId; UpdatedUtc = Utc(nowUtc); ConcurrencyVersion++;
    }

    private void EnsureVersion(long value) { if (value != ConcurrencyVersion) throw new InvalidOperationException("The transcript segment changed after it was opened."); }
    internal static string Required(string? value, string name, int max) { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name); var text = value.Trim(); return text.Length <= max ? text : throw new ArgumentOutOfRangeException(name); }
    internal static string? Optional(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : Required(value, nameof(value), max);
    internal static decimal? ConfidenceValue(decimal? value) => value is < 0 or > 1 ? throw new ArgumentOutOfRangeException(nameof(value)) : value;
    internal static long Positive(long value, string name) => value > 0 ? value : throw new ArgumentOutOfRangeException(name);
    internal static DateTime Utc(DateTime value) => value == default ? throw new ArgumentException("A timestamp is required.") : value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    internal static void EnsureIds(params Guid[] ids) { if (ids.Any(x => x == Guid.Empty)) throw new ArgumentException("Required identifiers cannot be empty."); }
}
