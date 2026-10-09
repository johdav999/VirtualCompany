namespace VirtualCompany.Application.Finance;
public sealed record StatutoryIssuedDocumentDto(Guid Id, string DocumentType, string Authority, string DocumentNumber, Guid SourceRecordId, long SourceVersion, Guid? SeriesId, string? FiscalYearKey, long? SequenceNumber, Guid StatutoryProfileId, long StatutoryProfileVersion, string PolicyPackKey, string PolicyPackVersion, string PolicyPackDefinitionHash, string SnapshotHash, Guid? OriginalIssuedDocumentId, DateTime IssuedUtc, bool IsImmutable, IReadOnlyList<Guid> ApprovalIds, string? RenderedEvidenceReference, string? DeliveryEvidenceReference, long EvidenceVersion)
{
    public Guid Id { get; set; } = Id;
    public string DocumentType { get; set; } = DocumentType;
    public string Authority { get; set; } = Authority;
    public string DocumentNumber { get; set; } = DocumentNumber;
    public Guid SourceRecordId { get; set; } = SourceRecordId;
    public long SourceVersion { get; set; } = SourceVersion;
    public Guid? SeriesId { get; set; } = SeriesId;
    public string? FiscalYearKey { get; set; } = FiscalYearKey;
    public long? SequenceNumber { get; set; } = SequenceNumber;
    public Guid StatutoryProfileId { get; set; } = StatutoryProfileId;
    public long StatutoryProfileVersion { get; set; } = StatutoryProfileVersion;
    public string PolicyPackKey { get; set; } = PolicyPackKey;
    public string PolicyPackVersion { get; set; } = PolicyPackVersion;
    public string PolicyPackDefinitionHash { get; set; } = PolicyPackDefinitionHash;
    public string SnapshotHash { get; set; } = SnapshotHash;
    public Guid? OriginalIssuedDocumentId { get; set; } = OriginalIssuedDocumentId;
    public DateTime IssuedUtc { get; set; } = IssuedUtc;
    public bool IsImmutable { get; set; } = IsImmutable;
    public IReadOnlyList<Guid> ApprovalIds { get; set; } = ApprovalIds;
    public string? RenderedEvidenceReference { get; set; } = RenderedEvidenceReference;
    public string? DeliveryEvidenceReference { get; set; } = DeliveryEvidenceReference;
    public long EvidenceVersion { get; set; } = EvidenceVersion;

    public StatutoryIssuedDocumentDto() : this(default !, string.Empty, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, default !, string.Empty, string.Empty, string.Empty, string.Empty, default !, default !, default !, [], default !, default !, default !)
    {
    }
}

public sealed record StatutoryDocumentAllocationDto(Guid Id, Guid SeriesId, string SeriesCode, string FiscalYearKey, long Number, string FormattedNumber, string Status, string? GapReason, string BusinessKey, long SourceVersion, Guid? IssuedDocumentId, Guid ActorUserId, DateTime AllocatedUtc)
{
    public Guid Id { get; set; } = Id;
    public Guid SeriesId { get; set; } = SeriesId;
    public string SeriesCode { get; set; } = SeriesCode;
    public string FiscalYearKey { get; set; } = FiscalYearKey;
    public long Number { get; set; } = Number;
    public string FormattedNumber { get; set; } = FormattedNumber;
    public string Status { get; set; } = Status;
    public string? GapReason { get; set; } = GapReason;
    public string BusinessKey { get; set; } = BusinessKey;
    public long SourceVersion { get; set; } = SourceVersion;
    public Guid? IssuedDocumentId { get; set; } = IssuedDocumentId;
    public Guid ActorUserId { get; set; } = ActorUserId;
    public DateTime AllocatedUtc { get; set; } = AllocatedUtc;

    public StatutoryDocumentAllocationDto() : this(default !, default !, string.Empty, string.Empty, default !, string.Empty, string.Empty, default !, string.Empty, default !, default !, default !, default !)
    {
    }
}

public sealed record StatutoryDocumentSeriesDto(Guid Id, string Code, string DocumentType, DateOnly FiscalYearStart, DateOnly FiscalYearEnd, string Prefix, int NumberWidth, long NextNumber, bool IsActive, long Version, DateTime CreatedUtc, DateTime UpdatedUtc)
{
    public Guid Id { get; set; } = Id;
    public string Code { get; set; } = Code;
    public string DocumentType { get; set; } = DocumentType;
    public DateOnly FiscalYearStart { get; set; } = FiscalYearStart;
    public DateOnly FiscalYearEnd { get; set; } = FiscalYearEnd;
    public string Prefix { get; set; } = Prefix;
    public int NumberWidth { get; set; } = NumberWidth;
    public long NextNumber { get; set; } = NextNumber;
    public bool IsActive { get; set; } = IsActive;
    public long Version { get; set; } = Version;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;

    public StatutoryDocumentSeriesDto() : this(default !, string.Empty, string.Empty, default !, default !, string.Empty, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record StatutoryDocumentPolicyDecisionDto(bool IsAllowed, IReadOnlyList<StatutoryDocumentPolicyIssueDto> Issues)
{
    public bool IsAllowed { get; set; } = IsAllowed;
    public IReadOnlyList<StatutoryDocumentPolicyIssueDto> Issues { get; set; } = Issues;

    public StatutoryDocumentPolicyDecisionDto() : this(default !, [])
    {
    }
}

public sealed record StatutoryDocumentPolicyIssueDto(string ReasonCode, string Explanation, string? Field = null)
{
    public string ReasonCode { get; set; } = ReasonCode;
    public string Explanation { get; set; } = Explanation;
    public string? Field { get; set; } = Field;

    public StatutoryDocumentPolicyIssueDto() : this(string.Empty, string.Empty, default !)
    {
    }
}
