namespace VirtualCompany.Domain.Entities;

public sealed class SalesRoomInvitationGrant : ICompanyOwnedEntity
{
    private SalesRoomInvitationGrant() { }
    public SalesRoomInvitationGrant(Guid company, Guid room, string hash, DateTime expires, Guid actor)
    { Id = Guid.NewGuid(); CompanyId = company; RoomId = room; SecretHash = hash; ExpiresUtc = expires; CreatedByUserId = actor; Version = 1; }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid RoomId { get; private set; }
    public string? SecretHash { get; private set; }
    public DateTime ExpiresUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid? RedeemedParticipantId { get; private set; }
    public bool Revoked { get; private set; }
    public long Version { get; private set; }
    public void Redeem(Guid participant, DateTime now) { if (Revoked || RedeemedParticipantId != null || ExpiresUtc <= now) throw new InvalidOperationException("Invitation unavailable."); RedeemedParticipantId = participant; SecretHash = null; Version++; }
    public void Revoke() { Revoked = true; SecretHash = null; Version++; }
    public void Reschedule(DateTime expires) {ExpiresUtc=expires;Version++;}

}
