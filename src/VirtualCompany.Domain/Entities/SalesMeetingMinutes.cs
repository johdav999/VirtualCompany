using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class SalesMeetingMinutes : ICompanyOwnedEntity
{
    private SalesMeetingMinutes() { }
    public SalesMeetingMinutes(Guid id, Guid companyId, Guid sessionId, Guid generationRequestId,
        Guid? previousVersionId, int artifactVersion, long evidenceCaptureVersion, DateTime evidenceCutoffUtc,
        Guid generatorAgentId, Guid? aiRunId, string generatorVersion, string promptVersion,
        DateTime retentionUntilUtc, Guid createdByUserId, DateTime nowUtc)
    {
        SalesMeetingTranscriptSegment.EnsureIds(companyId, sessionId, generationRequestId, generatorAgentId, createdByUserId);
        if (previousVersionId == Guid.Empty) throw new ArgumentException("PreviousVersionId cannot be empty.", nameof(previousVersionId));
        if (artifactVersion < 1 || evidenceCaptureVersion < 0) throw new ArgumentOutOfRangeException(nameof(artifactVersion));
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; SessionId = sessionId;
        GenerationRequestId = generationRequestId; PreviousVersionId = previousVersionId; ArtifactVersion = artifactVersion;
        EvidenceCaptureVersion = evidenceCaptureVersion; EvidenceCutoffUtc = SalesMeetingTranscriptSegment.Utc(evidenceCutoffUtc);
        GeneratorAgentId = generatorAgentId; AiRunId = aiRunId == Guid.Empty ? null : aiRunId;
        GeneratorVersion = SalesMeetingTranscriptSegment.Required(generatorVersion, nameof(generatorVersion), 100);
        PromptVersion = SalesMeetingTranscriptSegment.Required(promptVersion, nameof(promptVersion), 100);
        RetentionUntilUtc = SalesMeetingTranscriptSegment.Utc(retentionUntilUtc); CreatedByUserId = createdByUserId;
        Status = SalesMeetingClosingArtifactStatus.Draft; CreatedUtc = SalesMeetingTranscriptSegment.Utc(nowUtc);
        UpdatedUtc = CreatedUtc; ConcurrencyVersion = 1;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid GenerationRequestId { get; private set; }
    public Guid? PreviousVersionId { get; private set; }
    public int ArtifactVersion { get; private set; }
    public SalesMeetingClosingArtifactStatus Status { get; private set; }
    public long EvidenceCaptureVersion { get; private set; }
    public DateTime EvidenceCutoffUtc { get; private set; }
    public Guid GeneratorAgentId { get; private set; }
    public Guid? AiRunId { get; private set; }
    public string GeneratorVersion { get; private set; } = null!;
    public string PromptVersion { get; private set; } = null!;
    public DateTime RetentionUntilUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public DateTime? ReviewedUtc { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTime? ApprovedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long ConcurrencyVersion { get; private set; }
    public bool IsEvidenceStale { get; private set; }
    public DateTime? EvidenceStaleUtc { get; private set; }
    public string? EvidenceStaleReason { get; private set; }
    public SalesMeetingSession Session { get; private set; } = null!;
    public SalesMeetingMinutes? PreviousVersion { get; private set; }
    public ICollection<SalesMeetingMinutesItem> Items { get; } = new List<SalesMeetingMinutesItem>();
    public void EnsureEditable(long expectedVersion)
    {
        if (expectedVersion != ConcurrencyVersion) throw new InvalidOperationException("The meeting minutes changed after they were opened.");
        if (Status != SalesMeetingClosingArtifactStatus.Draft) throw new InvalidOperationException("Only draft meeting minutes can be edited.");
    }
    public void Touch(DateTime nowUtc) { UpdatedUtc = SalesMeetingTranscriptSegment.Utc(nowUtc); ConcurrencyVersion++; }
    public void SubmitForReview(Guid reviewerUserId, long expectedVersion, DateTime nowUtc)
    {
        EnsureEditable(expectedVersion); SalesMeetingTranscriptSegment.EnsureIds(reviewerUserId);
        Status = SalesMeetingClosingArtifactStatus.InReview; ReviewedByUserId = reviewerUserId;
        ReviewedUtc = SalesMeetingTranscriptSegment.Utc(nowUtc); Touch(nowUtc);
    }
    public void Approve(Guid reviewerUserId, long expectedVersion, bool hasUnsupportedProductStatements, DateTime nowUtc)
    {
        if (expectedVersion != ConcurrencyVersion) throw new InvalidOperationException("The meeting minutes changed after they were opened.");
        if (Status != SalesMeetingClosingArtifactStatus.InReview) throw new InvalidOperationException("Meeting minutes must be under review before approval.");
        if (hasUnsupportedProductStatements) throw new InvalidOperationException("Unsupported product statements cannot be approved.");
        SalesMeetingTranscriptSegment.EnsureIds(reviewerUserId); Status = SalesMeetingClosingArtifactStatus.Approved;
        ApprovedByUserId = reviewerUserId; ApprovedUtc = SalesMeetingTranscriptSegment.Utc(nowUtc); Touch(nowUtc);
    }
    public void Supersede(long expectedVersion, DateTime nowUtc)
    {
        if (expectedVersion != ConcurrencyVersion) throw new InvalidOperationException("The meeting minutes changed after they were opened.");
        if (Status == SalesMeetingClosingArtifactStatus.Approved) return;
        Status = SalesMeetingClosingArtifactStatus.Superseded; Touch(nowUtc);
    }

    public void MarkEvidenceStale(string reason, DateTime nowUtc)
    {
        IsEvidenceStale = true;
        EvidenceStaleReason = SalesMeetingTranscriptSegment.Required(reason, nameof(reason), 500);
        EvidenceStaleUtc = SalesMeetingTranscriptSegment.Utc(nowUtc);
        Touch(nowUtc);
    }
}

public sealed class SalesMeetingMinutesItem : ICompanyOwnedEntity
{
    private SalesMeetingMinutesItem() { }
    public SalesMeetingMinutesItem(Guid id, Guid companyId, Guid minutesId, int order,
        SalesMeetingMinutesItemType itemType, string content, string? ownerLabel, DateTime? dueUtc,
        string sourceId, Guid? sourceArtifactId, bool requiresReview, DateTime createdUtc)
    {
        SalesMeetingTranscriptSegment.EnsureIds(companyId, minutesId); if (sourceArtifactId == Guid.Empty) throw new ArgumentException("SourceArtifactId cannot be empty.", nameof(sourceArtifactId));
        if (order < 0) throw new ArgumentOutOfRangeException(nameof(order));
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; MinutesId = minutesId; Order = order;
        ItemType = itemType; _ = itemType.ToStorageValue(); Content = SalesMeetingTranscriptSegment.Required(content, nameof(content), 4000);
        OwnerLabel = SalesMeetingTranscriptSegment.Optional(ownerLabel, 160); DueUtc = dueUtc.HasValue ? SalesMeetingTranscriptSegment.Utc(dueUtc.Value) : null;
        SourceId = SalesMeetingTranscriptSegment.Required(sourceId, nameof(sourceId), 500); SourceArtifactId = sourceArtifactId;
        RequiresReview = requiresReview; CreatedUtc = SalesMeetingTranscriptSegment.Utc(createdUtc);
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid MinutesId { get; private set; }
    public int Order { get; private set; }
    public SalesMeetingMinutesItemType ItemType { get; private set; }
    public string Content { get; private set; } = null!;
    public string? OwnerLabel { get; private set; }
    public DateTime? DueUtc { get; private set; }
    public string SourceId { get; private set; } = null!;
    public Guid? SourceArtifactId { get; private set; }
    public bool RequiresReview { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public SalesMeetingMinutes Minutes { get; private set; } = null!;
    public SalesMeetingArtifact? SourceArtifact { get; private set; }
}
