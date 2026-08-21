namespace VirtualCompany.Domain.Entities;

public sealed class LedgerPostingIdentity : ICompanyOwnedEntity
{
    private LedgerPostingIdentity() { }

    public LedgerPostingIdentity(Guid id, Guid companyId, Guid ledgerEntryId, string action, string sourceType, string sourceId, string sourceVersion, string idempotencyKey, string payloadHash, DateTime createdUtc)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (ledgerEntryId == Guid.Empty) throw new ArgumentException("LedgerEntryId is required.", nameof(ledgerEntryId));
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        LedgerEntryId = ledgerEntryId;
        Action = Normalize(action, nameof(action), 64).ToLowerInvariant();
        SourceType = Normalize(sourceType, nameof(sourceType), 64).ToLowerInvariant();
        SourceId = Normalize(sourceId, nameof(sourceId), 128);
        SourceVersion = Normalize(sourceVersion, nameof(sourceVersion), 128);
        IdempotencyKey = Normalize(idempotencyKey, nameof(idempotencyKey), 200);
        PayloadHash = Normalize(payloadHash, nameof(payloadHash), 64).ToLowerInvariant();
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid LedgerEntryId { get; private set; }
    public string Action { get; private set; } = null!;
    public string SourceType { get; private set; } = null!;
    public string SourceId { get; private set; } = null!;
    public string SourceVersion { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public string PayloadHash { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public LedgerEntry LedgerEntry { get; private set; } = null!;

    private static string Normalize(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        if (normalized.Length > maxLength) throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        return normalized;
    }
}
