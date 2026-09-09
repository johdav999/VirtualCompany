namespace VirtualCompany.Domain.Entities;

public sealed class SalesRoomProviderEvent : ICompanyOwnedEntity
{
    private SalesRoomProviderEvent() { }
    public SalesRoomProviderEvent(Guid company, Guid room, string eventId, string type, string? identity, long occurred)
    { Id = Guid.NewGuid(); CompanyId = company; RoomId = room; ProviderEventId = eventId; EventType = type; ParticipantIdentity = identity; OccurredUnixSeconds = occurred; }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid RoomId { get; private set; }
    public string ProviderEventId { get; private set; } = "";
    public string EventType { get; private set; } = "";
    public string? ParticipantIdentity { get; private set; }
    public long OccurredUnixSeconds { get; private set; }
}
