namespace VirtualCompany.Domain.Entities;

public static class SalesBrowserRoomStates
{
    public const string Provisioning = "provisioning", Lobby = "lobby", Live = "live", Ending = "ending", Ended = "ended", Reconciliation = "reconciliation_required";
}
public static class SalesRoomAgentHealthStates
{
    public const string NotStarted = "not_started", Starting = "starting", Ready = "ready", Speaking = "speaking",
        Paused = "paused", Stopped = "stopped", Unavailable = "unavailable";
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
    public string AgentHealth { get; private set; } = SalesRoomAgentHealthStates.NotStarted;
    public string AgentVoiceHealth { get; private set; } = "not_connected";
    public Guid? AgentId { get; private set; }
    public Guid? AgentStartedByUserId { get; private set; }
    public Guid? AgentLeaseOwnerId { get; private set; }
    public DateTime? AgentLeaseExpiresUtc { get; private set; }
    public DateTime? AgentStartedUtc { get; private set; }
    public DateTime? AgentStoppedUtc { get; private set; }
    public long AgentGeneration { get; private set; }
    public long AgentTurnGeneration { get; private set; } = 1;
    public long AgentReceivedAudioMilliseconds { get; private set; }
    public long AgentDetectedSpeechMilliseconds { get; private set; }
    public long AgentForwardedAudioMilliseconds { get; private set; }
    public long AgentProviderBilledAudioMilliseconds { get; private set; }
    public long AgentOutputAudioMilliseconds { get; private set; }
    public int AgentInputTokens { get; private set; }
    public int AgentOutputTokens { get; private set; }
    public string? AgentLastErrorCode { get; private set; }
    public string? AgentLastErrorSummary { get; private set; }
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
    public void End() { if (State == SalesBrowserRoomStates.Ended) return; State = SalesBrowserRoomStates.Ending; StopAgent("room_ending", null, DateTime.UtcNow); Touch(); }
    public void Ended() { State = SalesBrowserRoomStates.Ended; StopAgent("room_ended", null, DateTime.UtcNow); Touch(); }
    public void Reconcile() { if (State == SalesBrowserRoomStates.Provisioning) State = SalesBrowserRoomStates.Reconciliation; Touch(); }

    public void StartAgent(Guid agentId, Guid actorId, Guid leaseOwnerId, DateTime leaseExpiresUtc, DateTime nowUtc)
    {
        if (agentId == Guid.Empty || actorId == Guid.Empty || leaseOwnerId == Guid.Empty) throw new ArgumentException("Agent ownership identifiers are required.");
        if (actorId != OrganizerUserId) throw new UnauthorizedAccessException("Only the organizer can start the room agent.");
        if (State != SalesBrowserRoomStates.Live || ExpiresUtc <= nowUtc) throw new InvalidOperationException("The room must be live before its agent starts.");
        if (AgentLeaseExpiresUtc > nowUtc && AgentHealth is SalesRoomAgentHealthStates.Starting or SalesRoomAgentHealthStates.Ready or SalesRoomAgentHealthStates.Speaking)
            throw new InvalidOperationException("The room already has an active agent owner.");
        AgentId = agentId; AgentStartedByUserId = actorId; AgentLeaseOwnerId = leaseOwnerId;
        AgentLeaseExpiresUtc = leaseExpiresUtc; AgentStartedUtc = nowUtc; AgentStoppedUtc = null;
        AgentGeneration++; AgentTurnGeneration++; AgentHealth = SalesRoomAgentHealthStates.Starting;
        AgentVoiceHealth = "connecting"; AgentLastErrorCode = null; AgentLastErrorSummary = null; Touch();
    }
    public bool RenewAgentLease(Guid ownerId, long generation, DateTime expiresUtc, DateTime nowUtc)
    {
        if (AgentLeaseOwnerId != ownerId || AgentGeneration != generation || AgentLeaseExpiresUtc <= nowUtc ||
            AgentHealth is SalesRoomAgentHealthStates.Stopped or SalesRoomAgentHealthStates.Unavailable) return false;
        AgentLeaseExpiresUtc = expiresUtc; Touch(); return true;
    }
    public bool IsAgentOwner(Guid ownerId, long generation, DateTime nowUtc) => AgentLeaseOwnerId == ownerId &&
        AgentGeneration == generation && AgentLeaseExpiresUtc > nowUtc &&
        AgentHealth is SalesRoomAgentHealthStates.Starting or SalesRoomAgentHealthStates.Ready or SalesRoomAgentHealthStates.Speaking or SalesRoomAgentHealthStates.Paused;
    public void AgentReady(Guid ownerId, long generation)
    { RequireAgentOwner(ownerId, generation); AgentHealth = SalesRoomAgentHealthStates.Ready; AgentVoiceHealth = "healthy"; AgentLastErrorCode = null; AgentLastErrorSummary = null; Touch(); }
    public void AgentSpeaking(Guid ownerId, long generation)
    { RequireAgentOwner(ownerId, generation); AgentHealth = SalesRoomAgentHealthStates.Speaking; Touch(); }
    public void AgentSpeechCompleted(Guid ownerId, long generation, long outputMilliseconds)
    { RequireAgentOwner(ownerId, generation); AgentOutputAudioMilliseconds += Math.Max(0, outputMilliseconds); AgentHealth = SalesRoomAgentHealthStates.Ready; Touch(); }
    public void RecordAgentAudio(Guid ownerId, long generation, long received, long detected, long forwarded, long billed, int inputTokens, int outputTokens)
    {
        RequireAgentOwner(ownerId, generation); AgentReceivedAudioMilliseconds += Math.Max(0, received);
        AgentDetectedSpeechMilliseconds += Math.Max(0, detected); AgentForwardedAudioMilliseconds += Math.Max(0, forwarded);
        AgentProviderBilledAudioMilliseconds += Math.Max(0, billed); AgentInputTokens += Math.Max(0, inputTokens);
        AgentOutputTokens += Math.Max(0, outputTokens); Touch();
    }
    public void PreemptAgent(Guid ownerId, long generation, string reason)
    { RequireAgentOwner(ownerId, generation); AgentTurnGeneration++; AgentHealth = SalesRoomAgentHealthStates.Paused; AgentLastErrorCode = "human_speaking"; AgentLastErrorSummary = reason; Touch(); }
    public void TakeOverAgent(string reason)
    {
        AgentTurnGeneration++;
        if (AgentHealth is SalesRoomAgentHealthStates.Starting or SalesRoomAgentHealthStates.Ready or SalesRoomAgentHealthStates.Speaking)
            AgentHealth = SalesRoomAgentHealthStates.Paused;
        AgentLastErrorCode = "host_takeover"; AgentLastErrorSummary = reason; Touch();
    }
    public void ResumeAgent(Guid ownerId, long generation)
    { RequireAgentOwner(ownerId, generation); AgentTurnGeneration++; AgentHealth = SalesRoomAgentHealthStates.Ready; AgentLastErrorCode = null; AgentLastErrorSummary = null; Touch(); }
    public void PauseAgent(Guid ownerId, long generation, string code, string summary, string voiceHealth = "degraded")
    { RequireAgentOwner(ownerId, generation); AgentTurnGeneration++; AgentHealth = SalesRoomAgentHealthStates.Paused; AgentVoiceHealth = voiceHealth; AgentLastErrorCode = code; AgentLastErrorSummary = summary; Touch(); }
    public void StopAgent(string? code, string? summary, DateTime nowUtc)
    {
        AgentTurnGeneration++; AgentHealth = SalesRoomAgentHealthStates.Stopped; AgentVoiceHealth = "stopped";
        AgentLeaseOwnerId = null; AgentLeaseExpiresUtc = null; AgentStoppedUtc = nowUtc;
        AgentLastErrorCode = code; AgentLastErrorSummary = summary; Touch();
    }
    private void RequireAgentOwner(Guid ownerId, long generation)
    {
        if (AgentLeaseOwnerId != ownerId || AgentGeneration != generation) throw new InvalidOperationException("The room agent lease was fenced.");
    }
}
