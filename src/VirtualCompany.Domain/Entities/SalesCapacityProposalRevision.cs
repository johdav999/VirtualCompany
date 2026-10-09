namespace VirtualCompany.Domain.Entities;

public sealed class SalesCapacityProposalRevision : ICompanyOwnedEntity
{
    private SalesCapacityProposalRevision() { }
    public SalesCapacityProposalRevision(Guid company, Guid user, Guid request, Guid series, int revision, Guid? previous,
        int year,int month,string? currency,DateTime saved,string payload,string checksum)
    {
        if(company==Guid.Empty || user==Guid.Empty || request==Guid.Empty || series==Guid.Empty || revision<1 ||
            year is <2000 or >2100 || month is <1 or >12 || string.IsNullOrWhiteSpace(payload) ||
            System.Text.Encoding.UTF8.GetByteCount(payload)>512*1024 || checksum.Length!=64) throw new ArgumentException("Invalid proposal revision.");
        Id=Guid.NewGuid();CompanyId=company;AccountableUserId=user;RequestId=request;SeriesId=series;Revision=revision;PreviousId=previous;
        Year=year;Month=month;Currency=currency;SavedAtUtc=saved;Payload=payload;Checksum=checksum;
    }
    public Guid Id {get;private set;} public Guid CompanyId {get;private set;} public Guid AccountableUserId {get;private set;}
    public Guid RequestId {get;private set;} public Guid SeriesId {get;private set;} public int Revision {get;private set;}
    public Guid? PreviousId {get;private set;} public int Year {get;private set;} public int Month {get;private set;}
    public string? Currency {get;private set;} public DateTime SavedAtUtc {get;private set;}
    public string Payload {get;private set;}=null!; public string Checksum {get;private set;}=null!;
    public Company Company {get;private set;}=null!;
}
