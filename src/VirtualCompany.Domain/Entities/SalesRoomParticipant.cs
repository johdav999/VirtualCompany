namespace VirtualCompany.Domain.Entities;

public static class SalesRoomParticipantStates
{ public const string Lobby = "lobby", Admitted = "admitted", Denied = "denied", RemovalPending = "removal_pending", Removed = "removed"; }
public sealed class SalesRoomParticipant : ICompanyOwnedEntity
{
    private SalesRoomParticipant() { }
    public SalesRoomParticipant(Guid company, Guid room, string name, string? sessionHash, DateTime expires, Guid? member = null)
    { Id = Guid.NewGuid(); CompanyId = company; RoomId = room; DisplayName = name; SessionHash = sessionHash; ExpiresUtc = expires; MemberUserId = member; State = member.HasValue ? SalesRoomParticipantStates.Admitted : SalesRoomParticipantStates.Lobby; Version = 1; }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid RoomId { get; private set; }
    public Guid? MemberUserId { get; private set; }
    public string DisplayName { get; private set; } = "";
    public string? SessionHash { get; private set; }
    public DateTime ExpiresUtc { get; private set; }
    public string State { get; private set; } = SalesRoomParticipantStates.Lobby;
    public long Generation { get; private set; } = 1;
    public long Version { get; private set; }
    public bool AiProcessingAllowed { get; private set; }
    public bool TranscriptRetentionAllowed { get; private set; }
    public DateTime? LastTokenExpiresUtc { get; private set; }
    public bool Connected { get; private set; }
    public long LastProviderEventUtc { get; private set; }
    public void Admit() { if (State != SalesRoomParticipantStates.Lobby) throw new InvalidOperationException("Participant is not in the lobby."); State = SalesRoomParticipantStates.Admitted; Version++; }
    public void Revoke(bool deny = false) { State = deny ? SalesRoomParticipantStates.Denied : SalesRoomParticipantStates.RemovalPending; SessionHash = null; AiProcessingAllowed = false; TranscriptRetentionAllowed = false; Version++; }
    public void Removed() { State = SalesRoomParticipantStates.Removed; Connected = false; Version++; }
    public void Consent(string purpose, bool allowed) { if (purpose == "ai_processing") AiProcessingAllowed = allowed; else if (purpose == "retained_transcript") TranscriptRetentionAllowed = allowed; else throw new ArgumentException("Unsupported consent purpose."); Version++; }
    public void TokenIssued(DateTime expires) { LastTokenExpiresUtc = expires; Version++; }
    public void Observe(bool connected, long timestamp) { if (timestamp <= LastProviderEventUtc) return; LastProviderEventUtc = timestamp; Connected = connected; Version++; }
    public void Reschedule(DateTime expires) {ExpiresUtc=expires;Version++;}

}
