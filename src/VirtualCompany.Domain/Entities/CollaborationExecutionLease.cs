namespace VirtualCompany.Domain.Entities;

public sealed class CollaborationExecutionLease : ICompanyOwnedEntity
{
    private CollaborationExecutionLease() { }
    public CollaborationExecutionLease(Guid company, string key, string fingerprint)
    { Id = Guid.NewGuid(); CompanyId = OperatingCycle.RequiredId(company, nameof(company));
      Key = OperatingCycle.Text(key, nameof(key), 128); Fingerprint = fingerprint; }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Key { get; private set; } = null!;
    public string Fingerprint { get; private set; } = null!;
    public Guid? Token { get; private set; }
    public DateTime? ExpiresUtc { get; private set; }
    public long Version { get; private set; }
    public bool TryClaim(Guid token, DateTime now, TimeSpan duration)
    {
        if (Token.HasValue && ExpiresUtc > now) return false;
        Token = token; ExpiresUtc = now.Add(duration); Version++; return true;
    }
}
