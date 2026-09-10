namespace VirtualCompany.Domain.Entities;

public sealed class SalesRoomAgentTranscript : ICompanyOwnedEntity
{
    private SalesRoomAgentTranscript() { }
    public SalesRoomAgentTranscript(Guid companyId, Guid roomId, Guid participantId, long participantConsentVersion,
        string trackIdHash, long trackGeneration, DateTime startedUtc, DateTime endedUtc, bool overlapped, string text, DateTime createdUtc, Guid? stableId = null, Guid? transcriptSegmentId = null, long? agentGeneration = null, long? participantGeneration = null)
    {
        if (companyId == Guid.Empty || roomId == Guid.Empty || participantId == Guid.Empty || participantConsentVersion < 1 ||
            trackGeneration < 1 || endedUtc < startedUtc || string.IsNullOrWhiteSpace(trackIdHash) || string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("A complete retained transcript binding is required.");
        Id = stableId ?? Guid.NewGuid(); TranscriptSegmentId = transcriptSegmentId; AgentGeneration = agentGeneration; ParticipantGeneration = participantGeneration; CompanyId = companyId; RoomId = roomId; ParticipantId = participantId;
        ParticipantConsentVersion = participantConsentVersion; TrackIdHash = trackIdHash[..Math.Min(128, trackIdHash.Length)];
        TrackGeneration = trackGeneration; StartedUtc = startedUtc; EndedUtc = endedUtc; Overlapped = overlapped;
        Text = text.Trim()[..Math.Min(8000, text.Trim().Length)]; CreatedUtc = createdUtc;
    }
    public Guid Id { get; private set; }
    public Guid? TranscriptSegmentId { get; private set; }
    public long? AgentGeneration { get; private set; }
    public long? ParticipantGeneration { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid RoomId { get; private set; }
    public Guid ParticipantId { get; private set; }
    public long ParticipantConsentVersion { get; private set; }
    public string TrackIdHash { get; private set; } = null!;
    public long TrackGeneration { get; private set; }
    public DateTime StartedUtc { get; private set; }
    public DateTime EndedUtc { get; private set; }
    public bool Overlapped { get; private set; }
    public string Text { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
}
