namespace VirtualCompany.Domain.Entities;

public static class SalesRoomPresentationAudienceStates
{
    public const string Pending = "pending";
    public const string Rendered = "rendered";
    public const string Overridden = "overridden";
}

public sealed class SalesRoomPresentationAudience : ICompanyOwnedEntity
{
    private SalesRoomPresentationAudience() { }

    public SalesRoomPresentationAudience(
        Guid companyId,
        Guid roomId,
        Guid participantId,
        long participantGeneration,
        Guid deckId,
        int deckVersion,
        int slideNumber,
        long presentationSequence,
        long presentationVersion,
        DateTime capturedUtc,
        DateTime deadlineUtc)
    {
        Id = Guid.NewGuid();
        CompanyId = companyId;
        RoomId = roomId;
        ParticipantId = participantId;
        ParticipantGeneration = participantGeneration;
        DeckId = deckId;
        DeckVersion = deckVersion;
        SlideNumber = slideNumber;
        PresentationSequence = presentationSequence;
        PresentationVersion = presentationVersion;
        CapturedUtc = capturedUtc;
        DeadlineUtc = deadlineUtc;
        State = SalesRoomPresentationAudienceStates.Pending;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid RoomId { get; private set; }
    public Guid ParticipantId { get; private set; }
    public long ParticipantGeneration { get; private set; }
    public Guid DeckId { get; private set; }
    public int DeckVersion { get; private set; }
    public int SlideNumber { get; private set; }
    public long PresentationSequence { get; private set; }
    public long PresentationVersion { get; private set; }
    public string State { get; private set; } = SalesRoomPresentationAudienceStates.Pending;
    public DateTime CapturedUtc { get; private set; }
    public DateTime DeadlineUtc { get; private set; }
    public DateTime? RenderedUtc { get; private set; }
    public DateTime? OverrideUtc { get; private set; }
    public DateTime? DisconnectedUtc { get; private set; }
    public long Version { get; private set; }

    public void Render(DateTime renderedUtc)
    {
        if (State == SalesRoomPresentationAudienceStates.Rendered) return;
        if (State == SalesRoomPresentationAudienceStates.Overridden)
            throw new InvalidOperationException("An overridden audience acknowledgement cannot be rewritten.");
        State = SalesRoomPresentationAudienceStates.Rendered;
        RenderedUtc = renderedUtc;
        Version++;
    }

    public void Connect()
    {
        if (!DisconnectedUtc.HasValue) return;
        DisconnectedUtc = null;
        Version++;
    }

    public void Disconnect(DateTime utc)
    {
        if (State != SalesRoomPresentationAudienceStates.Pending || DisconnectedUtc.HasValue) return;
        DisconnectedUtc = utc;
        Version++;
    }

    public void Override(DateTime utc)
    {
        if (State != SalesRoomPresentationAudienceStates.Pending) return;
        State = SalesRoomPresentationAudienceStates.Overridden;
        OverrideUtc = utc;
        Version++;
    }
}
