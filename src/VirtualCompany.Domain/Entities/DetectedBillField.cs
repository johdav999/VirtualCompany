namespace VirtualCompany.Domain.Entities;

public sealed class DetectedBillField : ICompanyOwnedEntity
{
    private DetectedBillField()
    {
    }

    public DetectedBillField(
        Guid id,
        Guid companyId,
        Guid detectedBillId,
        string fieldName,
        string? rawValue,
        string? normalizedValue,
        string sourceDocument,
        string? sourceDocumentType,
        string? pageReference,
        string? sectionReference,
        string? textSpan,
        string? locator,
        string extractionMethod,
        decimal? fieldConfidence,
        string? snippet = null,
        DateTime? createdUtc = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (detectedBillId == Guid.Empty)
        {
            throw new ArgumentException("DetectedBillId is required.", nameof(detectedBillId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        DetectedBillId = detectedBillId;
        FieldName = NormalizeFieldName(fieldName);
        RawValue = NormalizeOptional(rawValue, nameof(rawValue), 2000);
        NormalizedValue = NormalizeOptional(normalizedValue, nameof(normalizedValue), 2000);
        SourceDocument = NormalizeRequired(sourceDocument, nameof(sourceDocument), 512);
        SourceDocumentType = NormalizeOptional(sourceDocumentType, nameof(sourceDocumentType), 64);
        PageReference = NormalizeOptional(pageReference, nameof(pageReference), 128);
        SectionReference = NormalizeOptional(sectionReference, nameof(sectionReference), 128);
        TextSpan = NormalizeOptional(textSpan, nameof(textSpan), 128);
        Locator = NormalizeOptional(locator, nameof(locator), 512);
        ExtractionMethod = NormalizeRequired(extractionMethod, nameof(extractionMethod), 64);
        FieldConfidence = NormalizeScore(fieldConfidence, nameof(fieldConfidence));
        Snippet = NormalizeOptional(snippet, nameof(snippet), 2000);
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid DetectedBillId { get; private set; }
    public string FieldName { get; private set; } = null!;
    public string? RawValue { get; private set; }
    public string? NormalizedValue { get; private set; }
    public string SourceDocument { get; private set; } = null!;
    public string? SourceDocumentType { get; private set; }
    public string? PageReference { get; private set; }
    public string? SectionReference { get; private set; }
    public string? TextSpan { get; private set; }
    public string? Locator { get; private set; }
    public string ExtractionMethod { get; private set; } = null!;
    public decimal? FieldConfidence { get; private set; }
    public string? Snippet { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public DetectedBill DetectedBill { get; private set; } = null!;

    private static string NormalizeFieldName(string value)
    {
        var normalized = NormalizeRequired(value, nameof(value), 64);
        return normalized.Length == 0 ? throw new ArgumentException("FieldName is required.", nameof(value)) : normalized;
    }

    private static decimal? NormalizeScore(decimal? value, string name)
    {
        if (value is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be between 0 and 1.");
        }

        return value;
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }
}
