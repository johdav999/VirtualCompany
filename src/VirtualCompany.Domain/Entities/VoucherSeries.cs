namespace VirtualCompany.Domain.Entities;

public sealed class VoucherSeries : ICompanyOwnedEntity
{
    private VoucherSeries() { }

    public VoucherSeries(Guid id, Guid companyId, string code, string displayName, string numberPrefix, bool isActive, DateTime createdUtc)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Code = Normalize(code, nameof(code), 32).ToUpperInvariant();
        DisplayName = Normalize(displayName, nameof(displayName), 128);
        NumberPrefix = Normalize(numberPrefix, nameof(numberPrefix), 16).ToUpperInvariant();
        IsActive = isActive;
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Code { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string NumberPrefix { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;

    private static string Normalize(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        if (normalized.Length > maxLength) throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        return normalized;
    }
}
