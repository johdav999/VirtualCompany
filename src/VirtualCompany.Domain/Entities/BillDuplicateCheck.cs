using System.Text.Json;

namespace VirtualCompany.Domain.Entities;

public sealed class BillDuplicateCheck : ICompanyOwnedEntity
{
    private BillDuplicateCheck()
    {
    }

    public BillDuplicateCheck(
        Guid id,
        Guid companyId,
        string? supplierName,
        string? supplierOrgNumber,
        string? invoiceNumber,
        decimal? totalAmount,
        string? currency,
        bool isDuplicate,
        IReadOnlyCollection<Guid> matchedBillIds,
        string criteriaSummary,
        string? sourceEmailId = null,
        string? sourceAttachmentId = null,
        DateTime? checkedUtc = null,
        string? resultStatus = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SupplierName = NormalizeOptional(supplierName, nameof(supplierName), 200);
        SupplierOrgNumber = NormalizeOptional(supplierOrgNumber, nameof(supplierOrgNumber), 64);
        InvoiceNumber = NormalizeOptional(invoiceNumber, nameof(invoiceNumber), 64);
        TotalAmount = totalAmount;
        Currency = NormalizeOptional(currency, nameof(currency), 3)?.ToUpperInvariant();
        IsDuplicate = isDuplicate;
        ResultStatus = NormalizeResultStatus(resultStatus ?? (isDuplicate ? "duplicate" : "not_duplicate"));
        MatchedBillIdsJson = JsonSerializer.Serialize((matchedBillIds ?? []).Where(x => x != Guid.Empty).Distinct().ToArray());
        CriteriaSummary = NormalizeRequired(criteriaSummary, nameof(criteriaSummary), 1000);
        SourceEmailId = NormalizeOptional(sourceEmailId, nameof(sourceEmailId), 512);
        SourceAttachmentId = NormalizeOptional(sourceAttachmentId, nameof(sourceAttachmentId), 512);
        CheckedUtc = EntityTimestampNormalizer.NormalizeUtc(checkedUtc ?? DateTime.UtcNow, nameof(checkedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string? SupplierName { get; private set; }
    public string? SupplierOrgNumber { get; private set; }
    public string? InvoiceNumber { get; private set; }
    public decimal? TotalAmount { get; private set; }
    public string? Currency { get; private set; }
    public bool IsDuplicate { get; private set; }
    public string ResultStatus { get; private set; } = "not_duplicate";
    public string MatchedBillIdsJson { get; private set; } = "[]";
    public string CriteriaSummary { get; private set; } = null!;
    public string? SourceEmailId { get; private set; }
    public string? SourceAttachmentId { get; private set; }
    public DateTime CheckedUtc { get; private set; }
    public Company Company { get; private set; } = null!;

    public IReadOnlyList<Guid> GetMatchedBillIds()
    {
        try
        {
            return JsonSerializer.Deserialize<Guid[]>(MatchedBillIdsJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string NormalizeResultStatus(string value)
    {
        var normalized = NormalizeRequired(value, nameof(value), 32).ToLowerInvariant();
        return normalized is "pending" or "not_duplicate" or "duplicate" or "inconclusive"
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported bill duplicate check status.");
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
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}
