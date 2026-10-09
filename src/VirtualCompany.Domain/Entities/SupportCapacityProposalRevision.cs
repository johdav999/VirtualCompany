namespace VirtualCompany.Domain.Entities;

public sealed class SupportCapacityProposalRevision : ICompanyOwnedEntity
{
    private SupportCapacityProposalRevision() { }
    public SupportCapacityProposalRevision(Guid id, Guid company, Guid owner, Guid request, Guid? previous,
        string name, int year, int month, int targetYear, int targetMonth, int arrivals, int backlog,
        decimal handling, decimal people, decimal hours, decimal utilization, int responseTarget,
        string payload, string checksum, string fingerprint, string requestHash, DateTime saved)
    {
        if (new[] { id, company, owner, request }.Contains(Guid.Empty)) throw new ArgumentException("Proposal identity is required.");
        if (payload.Length>1024*1024) throw new ArgumentException("Capacity evidence is too large.");
        Id=id; CompanyId=company; OwnerId=owner; RequestId=request; PreviousId=previous;
        Name=SupportEntityText.NormalizeRequired(name,nameof(name),120); Year=year; Month=month;
        TargetYear=targetYear; TargetMonth=targetMonth; ExpectedArrivals=arrivals; BacklogToClear=backlog;
        HandlingMinutes=handling; AvailablePeople=people; HoursPerBusinessDay=hours; UtilizationPercent=utilization;
        ResponseTargetMinutes=responseTarget; Payload=payload; Checksum=checksum; SourceFingerprint=fingerprint;
        RequestHash=requestHash; SavedUtc=saved;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid RequestId { get; private set; }
    public Guid? PreviousId { get; private set; }
    public string Name { get; private set; } = null!;
    public int Year { get; private set; }
    public int Month { get; private set; }
    public int TargetYear { get; private set; }
    public int TargetMonth { get; private set; }
    public int ExpectedArrivals { get; private set; }
    public int BacklogToClear { get; private set; }
    public decimal HandlingMinutes { get; private set; }
    public decimal AvailablePeople { get; private set; }
    public decimal HoursPerBusinessDay { get; private set; }
    public decimal UtilizationPercent { get; private set; }
    public int ResponseTargetMinutes { get; private set; }
    public string Payload { get; private set; } = null!;
    public string Checksum { get; private set; } = null!;
    public string SourceFingerprint { get; private set; } = null!;
    public string RequestHash { get; private set; } = null!;
    public DateTime SavedUtc { get; private set; }
}
