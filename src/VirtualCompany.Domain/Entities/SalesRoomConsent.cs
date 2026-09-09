namespace VirtualCompany.Domain.Entities;

public sealed class SalesRoomConsent : ICompanyOwnedEntity
{
    private SalesRoomConsent() { }
    public SalesRoomConsent(SalesRoomParticipant participant, string purpose, bool granted, string notice, DateTime now)
    { Id = Guid.NewGuid(); CompanyId = participant.CompanyId; RoomId = participant.RoomId; ParticipantId = participant.Id; Purpose = purpose; Granted = granted; NoticeVersion = notice; Version = participant.Version; OccurredUtc = now; }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid RoomId { get; private set; }
    public Guid ParticipantId { get; private set; }
    public string Purpose { get; private set; } = "";
    public bool Granted { get; private set; }
    public string NoticeVersion { get; private set; } = "";
    public long Version { get; private set; }
    public DateTime OccurredUtc { get; private set; }
}
