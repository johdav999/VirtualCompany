namespace VirtualCompany.Domain.Entities;

public static class SalesBrowserRoomStates
{
    public const string Provisioning = "provisioning", Lobby = "lobby", Live = "live", Ending = "ending", Ended = "ended", Reconciliation = "reconciliation_required";
}
public sealed class SalesBrowserRoom : ICompanyOwnedEntity
{
    private SalesBrowserRoom() { }
    public SalesBrowserRoom(Guid companyId, Guid meetingId, Guid organizer, DateTime expiresUtc, DateTime now)
    { Id = Guid.NewGuid(); CompanyId = companyId; MeetingSessionId = meetingId; OrganizerUserId = organizer; ExpiresUtc = expiresUtc; CreatedUtc = now; Version = 1; }
    public static SalesBrowserRoom ForInvitation(Guid company,Guid invitation,Guid organizer,DateTime expires,DateTime now)
    {return new SalesBrowserRoom{Id=Guid.NewGuid(),CompanyId=company,InvitationId=invitation,OrganizerUserId=organizer,ExpiresUtc=expires,CreatedUtc=now,Version=1};}
    public Guid? InvitationId {get;private set;}
    public void AttachSession(Guid session) {if(MeetingSessionId.HasValue&&MeetingSessionId!=session)throw new InvalidOperationException("Room already bound to a session.");MeetingSessionId=session;Touch();}
    public void Reschedule(DateTime expires) {if(LiveStartedUtc.HasValue||State is SalesBrowserRoomStates.Ended or SalesBrowserRoomStates.Ending)throw new InvalidOperationException("A started or ended browser room cannot be rescheduled.");ExpiresUtc=expires;Touch();}
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? MeetingSessionId { get; private set; }
    public Guid OrganizerUserId { get; private set; }
    public string State { get; private set; } = SalesBrowserRoomStates.Provisioning;
    public string AgentHealth { get; private set; } = "not_started";
    public string? ProviderReference { get; private set; }
    public Guid ProvisionOperationId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime ExpiresUtc { get; private set; }
    public DateTime? LiveStartedUtc { get; private set; }
    public long Version { get; private set; }
    public bool AllowsAccess(DateTime now) => ExpiresUtc > now && State is SalesBrowserRoomStates.Lobby or SalesBrowserRoomStates.Live;
    public void Touch() => Version++;
    public void BindOperation(Guid id) { ProvisionOperationId = id; }
    public void Provisioned(string reference) { if (State is SalesBrowserRoomStates.Provisioning or SalesBrowserRoomStates.Reconciliation) { ProviderReference = reference; State = SalesBrowserRoomStates.Lobby; Touch(); } }
    public void Start(DateTime now, int minutes) { if (!AllowsAccess(now)) throw new InvalidOperationException("Room unavailable."); if (State == SalesBrowserRoomStates.Live) return; State = SalesBrowserRoomStates.Live; LiveStartedUtc = now; ExpiresUtc = new[] { ExpiresUtc, now.AddMinutes(minutes) }.Min(); Touch(); }
    public void End() { if (State == SalesBrowserRoomStates.Ended) return; State = SalesBrowserRoomStates.Ending; AgentHealth = "stopped"; Touch(); }
    public void Ended() { State = SalesBrowserRoomStates.Ended; AgentHealth = "stopped"; Touch(); }
    public void Reconcile() { if (State == SalesBrowserRoomStates.Provisioning) State = SalesBrowserRoomStates.Reconciliation; Touch(); }
}
