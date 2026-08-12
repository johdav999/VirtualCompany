namespace VirtualCompany.Domain.Entities;

public sealed class CompanyOperatingLease : ICompanyOwnedEntity
{
    private CompanyOperatingLease() { }
    public CompanyOperatingLease(Guid id, Guid companyId)
    { Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = OperatingCycle.RequiredId(companyId, nameof(companyId)); Version = 1; UpdatedUtc = DateTime.UtcNow; }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTime? LeaseExpiresUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public int Version { get; private set; }
    public Company Company { get; private set; } = null!;
    public bool TryAcquire(string owner, DateTime nowUtc, TimeSpan duration)
    {
        if (LeaseExpiresUtc > nowUtc && !string.Equals(LeaseOwner, owner, StringComparison.Ordinal)) return false;
        LeaseOwner = OperatingCycle.Text(owner, nameof(owner), 128); LeaseExpiresUtc = nowUtc.Add(duration); UpdatedUtc = nowUtc; Version++; return true;
    }
    public void Release(string owner, DateTime nowUtc) { if (LeaseOwner != owner) return; LeaseOwner = null; LeaseExpiresUtc = null; UpdatedUtc = nowUtc; Version++; }
}
