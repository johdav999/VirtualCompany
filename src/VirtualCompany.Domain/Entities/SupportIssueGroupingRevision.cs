namespace VirtualCompany.Domain.Entities;

public sealed class SupportIssueGroupingRevision : ICompanyOwnedEntity
{
    private SupportIssueGroupingRevision() { }
    public SupportIssueGroupingRevision(Guid id, Guid company, Guid caseId, Guid actor, Guid request,
        Guid? previous, string group, string reason, string fingerprint, string requestHash, DateTime saved)
    {
        if (new[] { id, company, caseId, actor, request }.Contains(Guid.Empty)) throw new ArgumentException("Grouping identity is required.");
        Id=id; CompanyId=company; SupportCaseId=caseId; ActorId=actor; RequestId=request; PreviousId=previous;
        Group=SupportEntityText.NormalizeRequired(group,nameof(group),80);
        Reason=SupportEntityText.NormalizeRequired(reason,nameof(reason),1000);
        SourceFingerprint=fingerprint; RequestHash=requestHash; SavedUtc=saved;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SupportCaseId { get; private set; }
    public Guid ActorId { get; private set; }
    public Guid RequestId { get; private set; }
    public Guid? PreviousId { get; private set; }
    public string Group { get; private set; } = null!;
    public string Reason { get; private set; } = null!;
    public string SourceFingerprint { get; private set; } = null!;
    public string RequestHash { get; private set; } = null!;
    public DateTime SavedUtc { get; private set; }
}
