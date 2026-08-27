namespace VirtualCompany.Domain.Entities;

public static class StatutoryDocumentAllocationStatuses
{
    public const string Issued = "issued";
    public const string Gap = "gap";
}

public sealed class StatutoryDocumentSeries : ICompanyOwnedEntity
{
    private StatutoryDocumentSeries() { }

    public StatutoryDocumentSeries(Guid id, Guid companyId, string code, string documentType,
        DateOnly fiscalYearStart, DateOnly fiscalYearEnd, string prefix, int numberWidth,
        long firstNumber, Guid actorUserId, DateTime createdUtc)
    {
        if (companyId == Guid.Empty || actorUserId == Guid.Empty) throw new ArgumentException("Company and actor are required.");
        if (fiscalYearEnd < fiscalYearStart) throw new ArgumentException("Fiscal-year end must not precede its start.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Code = Required(code, nameof(code), 32).ToUpperInvariant();
        DocumentType = Required(documentType, nameof(documentType), 32).ToLowerInvariant();
        FiscalYearStart = fiscalYearStart;
        FiscalYearEnd = fiscalYearEnd;
        Prefix = Optional(prefix, nameof(prefix), 32) ?? string.Empty;
        NumberWidth = numberWidth is >= 1 and <= 12 ? numberWidth : throw new ArgumentOutOfRangeException(nameof(numberWidth));
        NextNumber = firstNumber > 0 ? firstNumber : throw new ArgumentOutOfRangeException(nameof(firstNumber));
        IsActive = true;
        CreatedByUserId = UpdatedByUserId = actorUserId;
        CreatedUtc = UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Code { get; private set; } = null!;
    public string DocumentType { get; private set; } = null!;
    public DateOnly FiscalYearStart { get; private set; }
    public DateOnly FiscalYearEnd { get; private set; }
    public string Prefix { get; private set; } = null!;
    public int NumberWidth { get; private set; }
    public long NextNumber { get; private set; }
    public bool IsActive { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long Version { get; private set; }
    public Company Company { get; private set; } = null!;

    public string FiscalYearKey => $"{FiscalYearStart:yyyyMMdd}-{FiscalYearEnd:yyyyMMdd}";
    public string Format(long number) => $"{Prefix}{number.ToString().PadLeft(NumberWidth, '0')}";

    public long Allocate(Guid actorUserId, DateTime allocatedUtc)
    {
        if (!IsActive) throw new InvalidOperationException("The document series is inactive.");
        var allocated = NextNumber;
        NextNumber = checked(NextNumber + 1);
        UpdatedByUserId = actorUserId;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(allocatedUtc, nameof(allocatedUtc));
        Version++;
        return allocated;
    }

    public void Update(string prefix, int numberWidth, bool isActive, Guid actorUserId, DateTime updatedUtc)
    {
        Prefix = Optional(prefix, nameof(prefix), 32) ?? string.Empty;
        NumberWidth = numberWidth is >= 1 and <= 12 ? numberWidth : throw new ArgumentOutOfRangeException(nameof(numberWidth));
        IsActive = isActive;
        UpdatedByUserId = actorUserId;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc, nameof(updatedUtc));
        Version++;
    }

    private static string Required(string value, string name, int max) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) :
        value.Trim().Length > max ? throw new ArgumentOutOfRangeException(name) : value.Trim();
    private static string? Optional(string? value, string name, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length > max ? throw new ArgumentOutOfRangeException(name) : value.Trim();
}

public sealed class StatutoryDocumentNumberAllocation : ICompanyOwnedEntity
{
    private StatutoryDocumentNumberAllocation() { }
    public StatutoryDocumentNumberAllocation(Guid id, Guid companyId, Guid seriesId, string fiscalYearKey,
        long number, string formattedNumber, string status, string? gapReason, string businessKey,
        long sourceVersion, Guid? issuedDocumentId, Guid actorUserId, DateTime allocatedUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SeriesId = seriesId;
        FiscalYearKey = fiscalYearKey;
        Number = number;
        FormattedNumber = formattedNumber;
        Status = status;
        GapReason = gapReason;
        BusinessKey = businessKey;
        SourceVersion = sourceVersion;
        IssuedDocumentId = issuedDocumentId;
        ActorUserId = actorUserId;
        AllocatedUtc = EntityTimestampNormalizer.NormalizeUtc(allocatedUtc, nameof(allocatedUtc));
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SeriesId { get; private set; }
    public string FiscalYearKey { get; private set; } = null!;
    public long Number { get; private set; }
    public string FormattedNumber { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string? GapReason { get; private set; }
    public string BusinessKey { get; private set; } = null!;
    public long SourceVersion { get; private set; }
    public Guid? IssuedDocumentId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public DateTime AllocatedUtc { get; private set; }
    public StatutoryDocumentSeries Series { get; private set; } = null!;
}

public sealed class IssuedStatutoryDocument : ICompanyOwnedEntity
{
    private IssuedStatutoryDocument() { }
    public IssuedStatutoryDocument(Guid id, Guid companyId, string documentType, string authority,
        string documentNumber, Guid sourceRecordId, long sourceVersion, Guid? seriesId,
        string? fiscalYearKey, long? sequenceNumber, Guid statutoryProfileId, long statutoryProfileVersion,
        string policyPackKey, string policyPackVersion, string policyPackDefinitionHash,
        string snapshotJson, string snapshotHash, string taxFactsJson, string approvalIdsJson,
        string businessKey, Guid? originalIssuedDocumentId, Guid actorUserId, DateTime issuedUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        DocumentType = documentType;
        Authority = authority;
        DocumentNumber = documentNumber;
        SourceRecordId = sourceRecordId;
        SourceVersion = sourceVersion;
        SeriesId = seriesId;
        FiscalYearKey = fiscalYearKey;
        SequenceNumber = sequenceNumber;
        StatutoryProfileId = statutoryProfileId;
        StatutoryProfileVersion = statutoryProfileVersion;
        PolicyPackKey = policyPackKey;
        PolicyPackVersion = policyPackVersion;
        PolicyPackDefinitionHash = policyPackDefinitionHash;
        SnapshotJson = snapshotJson;
        SnapshotHash = snapshotHash;
        TaxFactsJson = taxFactsJson;
        ApprovalIdsJson = approvalIdsJson;
        BusinessKey = businessKey;
        OriginalIssuedDocumentId = originalIssuedDocumentId;
        IssuedByUserId = actorUserId;
        IssuedUtc = EntityTimestampNormalizer.NormalizeUtc(issuedUtc, nameof(issuedUtc));
        EvidenceVersion = 1;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string DocumentType { get; private set; } = null!;
    public string Authority { get; private set; } = null!;
    public string DocumentNumber { get; private set; } = null!;
    public Guid SourceRecordId { get; private set; }
    public long SourceVersion { get; private set; }
    public Guid? SeriesId { get; private set; }
    public string? FiscalYearKey { get; private set; }
    public long? SequenceNumber { get; private set; }
    public Guid StatutoryProfileId { get; private set; }
    public long StatutoryProfileVersion { get; private set; }
    public string PolicyPackKey { get; private set; } = null!;
    public string PolicyPackVersion { get; private set; } = null!;
    public string PolicyPackDefinitionHash { get; private set; } = null!;
    public string SnapshotJson { get; private set; } = null!;
    public string SnapshotHash { get; private set; } = null!;
    public string TaxFactsJson { get; private set; } = null!;
    public string ApprovalIdsJson { get; private set; } = null!;
    public string BusinessKey { get; private set; } = null!;
    public Guid? OriginalIssuedDocumentId { get; private set; }
    public Guid IssuedByUserId { get; private set; }
    public DateTime IssuedUtc { get; private set; }
    public string? RenderedEvidenceReference { get; private set; }
    public string? DeliveryEvidenceReference { get; private set; }
    public long EvidenceVersion { get; private set; }
    public Company Company { get; private set; } = null!;

    public void AttachEvidence(string? renderedReference, string? deliveryReference)
    {
        if (string.IsNullOrWhiteSpace(renderedReference) && string.IsNullOrWhiteSpace(deliveryReference))
            throw new ArgumentException("At least one rendered or delivery evidence reference is required.");
        RenderedEvidenceReference = NormalizeReference(renderedReference);
        DeliveryEvidenceReference = NormalizeReference(deliveryReference);
        EvidenceVersion++;
    }

    private static string? NormalizeReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        return normalized.Length <= 512 ? normalized : throw new ArgumentOutOfRangeException(nameof(value), "Evidence references must be 512 characters or fewer.");
    }
}
