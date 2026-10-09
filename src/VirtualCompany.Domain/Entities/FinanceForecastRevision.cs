namespace VirtualCompany.Domain.Entities;

/// <summary>Immutable assumptions/audit evidence for values in the native Forecast store.</summary>
public sealed class FinanceForecastRevision : ICompanyOwnedEntity
{
    private FinanceForecastRevision() { }
    public FinanceForecastRevision(Guid id, Guid company, Guid author, Guid request, string name, string nativeVersion,
        Guid? previous, DateTime saved, DateTime sourceAsOf, string payload, string checksum, string commandHash)
    {
        if (id == Guid.Empty || company == Guid.Empty || author == Guid.Empty || request == Guid.Empty ||
            string.IsNullOrWhiteSpace(name) || name.Length > 64 || nativeVersion.Length > 64 ||
            System.Text.Encoding.UTF8.GetByteCount(payload) > 1024 * 1024 || checksum.Length != 64 || commandHash.Length != 64)
            throw new ArgumentException("Invalid forecast revision or evidence exceeds one MiB.");
        Id = id; CompanyId = company; AuthorId = author; RequestId = request; Name = name; NativeVersion = nativeVersion;
        PreviousId = previous; SavedUtc = saved; SourceAsOfUtc = sourceAsOf; Payload = payload; Checksum = checksum; CommandHash = commandHash;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid AuthorId { get; private set; }
    public Guid RequestId { get; private set; }
    public string Name { get; private set; } = null!;
    public string NativeVersion { get; private set; } = null!;
    public Guid? PreviousId { get; private set; }
    public DateTime SavedUtc { get; private set; }
    public DateTime SourceAsOfUtc { get; private set; }
    public string Payload { get; private set; } = null!;
    public string Checksum { get; private set; } = null!;
    public string CommandHash { get; private set; } = null!;
}
