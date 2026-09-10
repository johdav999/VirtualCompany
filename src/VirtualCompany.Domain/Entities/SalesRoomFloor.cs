namespace VirtualCompany.Domain.Entities;

public static class SalesRoomFloorStates
{
    public const string Host = "host", Human = "human", Agent = "agent", Pending = "pending",
        Overlap = "overlap", Paused = "paused";
}
public static class SalesRoomPendingTurnStates
{
    public const string None = "none", HostInvocationRequired = "host_invocation_required",
        ConfirmationRequired = "confirmation_required", Authorized = "authorized",
        OverlapWaiting = "overlap_waiting", Dismissed = "dismissed";
}
public static class SalesRoomPlaybackStopStates
{
    public const string None = "none", Waiting = "waiting", Acknowledged = "acknowledged", TimedOut = "timed_out";
}

public sealed class SalesRoomFloor : ICompanyOwnedEntity
{
    private SalesRoomFloor() { }
    public SalesRoomFloor(Guid companyId, Guid roomId, Guid hostParticipantId, long turnGeneration,
        long presentationVersion, int slideNumber, int talkingPointIndex, string? resumeMarker,
        string controlMode, DateTime nowUtc)
    {
        if (companyId == Guid.Empty || roomId == Guid.Empty || hostParticipantId == Guid.Empty ||
            turnGeneration < 1 || presentationVersion < 1 || slideNumber < 1 || talkingPointIndex < 0)
            throw new ArgumentException("A complete floor binding is required.");
        Id = Guid.NewGuid(); CompanyId = companyId; RoomId = roomId; HostParticipantId = hostParticipantId;
        FloorOwnerParticipantId = hostParticipantId; State = SalesRoomFloorStates.Host;
        TurnGeneration = turnGeneration; ResponseGeneration = 1; PresentationVersion = presentationVersion;
        SlideNumber = slideNumber; TalkingPointIndex = talkingPointIndex; ResumeMarker = Limit(resumeMarker, 1000);
        ControlMode = Mode(controlMode); PendingTurnState = SalesRoomPendingTurnStates.None;
        PlaybackStopState = SalesRoomPlaybackStopStates.None; UpdatedUtc = nowUtc; Version = 1;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid RoomId { get; private set; }
    public Guid HostParticipantId { get; private set; }
    public Guid? PreauthorizedCoHostParticipantId { get; private set; }
    public string State { get; private set; } = SalesRoomFloorStates.Host;
    public Guid? FloorOwnerParticipantId { get; private set; }
    public Guid? PendingTurnId { get; private set; }
    public Guid? PendingParticipantId { get; private set; }
    public long? PendingParticipantGeneration { get; private set; }
    public Guid? PendingQuestionId { get; private set; }
    public string PendingTurnState { get; private set; } = SalesRoomPendingTurnStates.None;
    public bool PendingAddressedAgent { get; private set; }
    public bool Overlap { get; private set; }
    public long TurnGeneration { get; private set; }
    public long ResponseGeneration { get; private set; }
    public long PresentationVersion { get; private set; }
    public int SlideNumber { get; private set; }
    public int TalkingPointIndex { get; private set; }
    public string? ResumeMarker { get; private set; }
    public int ResumeOffsetMilliseconds { get; private set; }
    public string ControlMode { get; private set; } = "manual";
    public Guid? LastPlaybackStopId { get; private set; }
    public DateTime? PlaybackStopRequestedUtc { get; private set; }
    public DateTime? PlaybackStopDeadlineUtc { get; private set; }
    public int PlaybackStopRequiredCount { get; private set; }
    public int PlaybackStopAcknowledgedCount { get; private set; }
    public string PlaybackStopState { get; private set; } = SalesRoomPlaybackStopStates.None;
    public DateTime UpdatedUtc { get; private set; }
    public long Version { get; private set; }

    public void SetMode(string mode, DateTime nowUtc) { ControlMode = Mode(mode); Touch(nowUtc); }
    public void AuthorizeCoHost(Guid? participantId, DateTime nowUtc)
    { if (participantId == Guid.Empty || participantId == HostParticipantId) throw new ArgumentException("Choose another admitted member."); PreauthorizedCoHostParticipantId = participantId; Touch(nowUtc); }
    public void HumanStarted(Guid participantId, long participantGeneration, bool overlap, long turnGeneration,
        long presentationVersion, int slideNumber, int talkingPointIndex, string? resumeMarker, DateTime nowUtc)
    {
        if (participantId == Guid.Empty || participantGeneration < 1 || turnGeneration < TurnGeneration) throw new ArgumentException("Human floor identity is invalid.");
        State = overlap ? SalesRoomFloorStates.Overlap : SalesRoomFloorStates.Human; FloorOwnerParticipantId = participantId;
        Overlap = overlap; TurnGeneration = turnGeneration; ResponseGeneration++;
        PresentationVersion = presentationVersion; SlideNumber = slideNumber; TalkingPointIndex = talkingPointIndex;
        ResumeMarker = Limit(resumeMarker, 1000); ClearPending(); Touch(nowUtc);
    }
    public void ProposeTurn(Guid participantId, long participantGeneration, bool addressed, bool overlap,
        Guid? questionId, DateTime nowUtc)
    {
        PendingTurnId = Guid.NewGuid(); PendingParticipantId = participantId; PendingParticipantGeneration = participantGeneration;
        PendingQuestionId = questionId; PendingAddressedAgent = addressed; Overlap = overlap;
        PendingTurnState = overlap ? SalesRoomPendingTurnStates.OverlapWaiting :
            ControlMode == "autonomous" && addressed ? SalesRoomPendingTurnStates.Authorized :
            ControlMode == "assisted" && addressed ? SalesRoomPendingTurnStates.ConfirmationRequired :
            SalesRoomPendingTurnStates.HostInvocationRequired;
        State = overlap ? SalesRoomFloorStates.Overlap : SalesRoomFloorStates.Pending;
        ResponseGeneration++; Touch(nowUtc);
    }
    public void ApprovePending(Guid actorParticipantId, long expectedVersion, DateTime nowUtc)
    {
        RequireController(actorParticipantId); RequireVersion(expectedVersion);
        if (PendingTurnId is null || PendingTurnState is not (SalesRoomPendingTurnStates.ConfirmationRequired or SalesRoomPendingTurnStates.HostInvocationRequired))
            throw new InvalidOperationException("There is no pending turn to approve.");
        PendingTurnState = SalesRoomPendingTurnStates.Authorized; ResponseGeneration++; Touch(nowUtc);
    }
    public void DismissPending(Guid actorParticipantId, long expectedVersion, DateTime nowUtc)
    { RequireController(actorParticipantId); RequireVersion(expectedVersion); PendingTurnState = SalesRoomPendingTurnStates.Dismissed; State = SalesRoomFloorStates.Host; FloorOwnerParticipantId = actorParticipantId; ResponseGeneration++; Touch(nowUtc); }
    public void AgentClaim(long expectedResponseGeneration, long turnGeneration, DateTime nowUtc)
    {
        if (expectedResponseGeneration != ResponseGeneration || turnGeneration < TurnGeneration)
            throw new InvalidOperationException("The floor generation changed before the agent could speak.");
        if (PendingTurnId is not null && PendingTurnState != SalesRoomPendingTurnStates.Authorized)
            throw new InvalidOperationException("The pending turn is not authorized.");
        TurnGeneration = turnGeneration; State = SalesRoomFloorStates.Agent; FloorOwnerParticipantId = null; Overlap = false; Touch(nowUtc);
    }
    public void AuthorizeAgentResponse(Guid actorParticipantId, long turnGeneration, DateTime nowUtc)
    {
        RequireController(actorParticipantId);
        if (PendingTurnId is null || PendingQuestionId is null || PendingTurnState != SalesRoomPendingTurnStates.Authorized)
            throw new InvalidOperationException("The pending answer is not authorized.");
        TurnGeneration = turnGeneration; ResponseGeneration++; State = SalesRoomFloorStates.Agent;
        FloorOwnerParticipantId = null; Overlap = false; Touch(nowUtc);
    }
    public void AgentCompleted(Guid returnToParticipantId, DateTime nowUtc)
    { RequireController(returnToParticipantId); State = SalesRoomFloorStates.Host; FloorOwnerParticipantId = returnToParticipantId; ClearPending(); ResumeOffsetMilliseconds = 0; Touch(nowUtc); }
    public void AgentAdvanced(long presentationVersion, int slideNumber, int talkingPointIndex,
        string? resumeMarker, long turnGeneration, DateTime nowUtc)
    {
        if (ControlMode != "autonomous" || State != SalesRoomFloorStates.Agent ||
            presentationVersion < 1 || slideNumber < 1 || talkingPointIndex < 0 || turnGeneration < TurnGeneration)
            throw new InvalidOperationException("The autonomous floor changed before the next slide was ready.");
        PresentationVersion = presentationVersion; SlideNumber = slideNumber;
        TalkingPointIndex = talkingPointIndex; ResumeMarker = Limit(resumeMarker, 1000);
        ResumeOffsetMilliseconds = 0; TurnGeneration = turnGeneration; ResponseGeneration++;
        ClearPending(); Touch(nowUtc);
    }
    public void PauseAt(int resumeOffsetMilliseconds, long turnGeneration, DateTime nowUtc)
    { State = SalesRoomFloorStates.Paused; TurnGeneration = turnGeneration; ResumeOffsetMilliseconds = Math.Max(0, resumeOffsetMilliseconds); ResponseGeneration++; Touch(nowUtc); }
    public void Resume(Guid actorParticipantId, long expectedVersion, long currentPresentationVersion,
        int slideNumber, int talkingPointIndex, string? resumeMarker, long turnGeneration, DateTime nowUtc)
    {
        RequireController(actorParticipantId); RequireVersion(expectedVersion);
        if (currentPresentationVersion != PresentationVersion || slideNumber != SlideNumber || talkingPointIndex != TalkingPointIndex)
            throw new InvalidOperationException("The presentation moved after the interruption. Refresh before resuming.");
        PresentationVersion = currentPresentationVersion; ResumeMarker = Limit(resumeMarker, 1000);
        TurnGeneration = turnGeneration; State = SalesRoomFloorStates.Agent; FloorOwnerParticipantId = null;
        Overlap = false; ClearPending(); ResponseGeneration++; Touch(nowUtc);
    }
    public void TakeOver(Guid actorParticipantId, long expectedVersion, Guid stopId, int requiredAcks,
        DateTime deadlineUtc, long turnGeneration, long presentationVersion, int slideNumber,
        int talkingPointIndex, string? resumeMarker, DateTime nowUtc)
    {
        RequireController(actorParticipantId); RequireVersion(expectedVersion);
        State = SalesRoomFloorStates.Host; FloorOwnerParticipantId = actorParticipantId; Overlap = false;
        TurnGeneration = turnGeneration; ResponseGeneration++; PresentationVersion = presentationVersion;
        SlideNumber = slideNumber; TalkingPointIndex = talkingPointIndex; ResumeMarker = Limit(resumeMarker, 1000);
        ClearPending(); LastPlaybackStopId = stopId; PlaybackStopRequestedUtc = nowUtc;
        PlaybackStopDeadlineUtc = deadlineUtc; PlaybackStopRequiredCount = Math.Max(0, requiredAcks);
        PlaybackStopAcknowledgedCount = 0; PlaybackStopState = requiredAcks == 0
            ? SalesRoomPlaybackStopStates.Acknowledged : SalesRoomPlaybackStopStates.Waiting; Touch(nowUtc);
    }
    public void RequestPlaybackStop(Guid stopId, int requiredAcks, DateTime deadlineUtc, DateTime nowUtc)
    {
        LastPlaybackStopId = stopId; PlaybackStopRequestedUtc = nowUtc; PlaybackStopDeadlineUtc = deadlineUtc;
        PlaybackStopRequiredCount = Math.Max(0, requiredAcks); PlaybackStopAcknowledgedCount = 0;
        PlaybackStopState = requiredAcks == 0 ? SalesRoomPlaybackStopStates.Acknowledged : SalesRoomPlaybackStopStates.Waiting;
        Touch(nowUtc);
    }
    public void AcknowledgeStop(Guid stopId, DateTime nowUtc)
    {
        if (LastPlaybackStopId != stopId || PlaybackStopState != SalesRoomPlaybackStopStates.Waiting) return;
        PlaybackStopAcknowledgedCount = Math.Min(PlaybackStopRequiredCount, PlaybackStopAcknowledgedCount + 1);
        if (PlaybackStopAcknowledgedCount >= PlaybackStopRequiredCount) PlaybackStopState = SalesRoomPlaybackStopStates.Acknowledged;
        Touch(nowUtc);
    }
    public void ExpireStop(DateTime nowUtc)
    { if (PlaybackStopState == SalesRoomPlaybackStopStates.Waiting && PlaybackStopDeadlineUtc <= nowUtc) { PlaybackStopState = SalesRoomPlaybackStopStates.TimedOut; Touch(nowUtc); } }
    private void ClearPending() { PendingTurnId = null; PendingParticipantId = null; PendingParticipantGeneration = null; PendingQuestionId = null; PendingTurnState = SalesRoomPendingTurnStates.None; PendingAddressedAgent = false; }
    private void RequireController(Guid participantId) { if (participantId != HostParticipantId && participantId != PreauthorizedCoHostParticipantId) throw new UnauthorizedAccessException("Only the host or preauthorized co-host controls the floor."); }
    private void RequireVersion(long expected) { if (expected != Version) throw new InvalidOperationException("The floor changed. Refresh before continuing."); }
    private void Touch(DateTime nowUtc) { UpdatedUtc = nowUtc; Version++; }
    private static string Mode(string value) => value is "manual" or "assisted" or "autonomous" ? value : throw new ArgumentException("Invalid floor mode.");
    private static string? Limit(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
}

public sealed class SalesRoomPlaybackStopAcknowledgement : ICompanyOwnedEntity
{
    private SalesRoomPlaybackStopAcknowledgement() { }
    public SalesRoomPlaybackStopAcknowledgement(Guid companyId, Guid roomId, Guid stopId, Guid participantId,
        long participantGeneration, long responseGeneration, string connectionIdHash, DateTime acknowledgedUtc)
    {
        if (companyId == Guid.Empty || roomId == Guid.Empty || stopId == Guid.Empty || participantId == Guid.Empty ||
            participantGeneration < 1 || responseGeneration < 1 || string.IsNullOrWhiteSpace(connectionIdHash))
            throw new ArgumentException("A complete playback-stop acknowledgement is required.");
        Id = Guid.NewGuid(); CompanyId = companyId; RoomId = roomId; StopId = stopId; ParticipantId = participantId;
        ParticipantGeneration = participantGeneration; ResponseGeneration = responseGeneration;
        ConnectionIdHash = connectionIdHash; AcknowledgedUtc = acknowledgedUtc;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid RoomId { get; private set; }
    public Guid StopId { get; private set; }
    public Guid ParticipantId { get; private set; }
    public long ParticipantGeneration { get; private set; }
    public long ResponseGeneration { get; private set; }
    public string ConnectionIdHash { get; private set; } = null!;
    public DateTime AcknowledgedUtc { get; private set; }
}
