namespace VirtualCompany.Domain.Entities;

public static class TeamsMeetingCallStates
{
    public const string Requested = "requested";
    public const string Joining = "joining";
    public const string WaitingInLobby = "waiting_in_lobby";
    public const string Admitted = "admitted";
    public const string Connected = "connected";
    public const string LeaveRequested = "leave_requested";
    public const string Ending = "ending";
    public const string Ended = "ended";
    public const string Rejected = "rejected";
    public const string Failed = "failed";
    public const string ReconciliationRequired = "reconciliation_required";

    public static bool IsActive(string state) => state is Requested or Joining or WaitingInLobby or Admitted or Connected or LeaveRequested or Ending or ReconciliationRequired;
}

public sealed class TeamsMeetingCall : ICompanyOwnedEntity
{
    private TeamsMeetingCall() { }

    public TeamsMeetingCall(Guid id, Guid companyId, Guid meetingSessionId, Guid registrationId,
        Guid organizerUserId, string meetingReferenceHash, long consentVersion, long policyVersion,
        string mediaHostInstanceId, DateTime nowUtc)
    {
        if (companyId == Guid.Empty || meetingSessionId == Guid.Empty || registrationId == Guid.Empty || organizerUserId == Guid.Empty)
            throw new ArgumentException("Company, meeting, registration, and organizer identifiers are required.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        MeetingSessionId = meetingSessionId;
        RegistrationId = registrationId;
        OrganizerUserId = organizerUserId;
        MeetingReferenceHash = Required(meetingReferenceHash, 128);
        MediaHostInstanceId = Required(mediaHostInstanceId, 120);
        ConsentEvidenceVersion = consentVersion;
        PolicyEvidenceVersion = policyVersion;
        State = TeamsMeetingCallStates.Requested;
        Action = "join";
        ActionVersion = 1;
        JoinGeneration = 1;
        JoinIdempotencyKey = Key(companyId, meetingSessionId, "join", ActionVersion);
        RequestedUtc = Utc(nowUtc);
        CreatedUtc = RequestedUtc;
        UpdatedUtc = RequestedUtc;
        ConcurrencyVersion = 1;
    }

    public Guid? PresenterAgentId { get; private set; }
    public DateTime? FirstUatAuthorizedUtc { get; private set; }
    public void BindPresenter(Guid id, DateTime? firstUatAuthorizedUtc = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Presenter is required.");
        if (State != TeamsMeetingCallStates.Requested) throw new InvalidOperationException("Bind a presenter only to a new call request.");
        PresenterAgentId = id; FirstUatAuthorizedUtc = firstUatAuthorizedUtc;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid MeetingSessionId { get; private set; }
    public Guid RegistrationId { get; private set; }
    public Guid OrganizerUserId { get; private set; }
    public string? ProviderCallId { get; private set; }
    public string MeetingReferenceHash { get; private set; } = null!;
    public string? SafeProviderReference { get; private set; }
    public string State { get; private set; } = null!;
    public string? ProviderState { get; private set; }
    public string Action { get; private set; } = null!;
    public long ActionVersion { get; private set; }
    public long JoinGeneration { get; private set; }
    public string JoinIdempotencyKey { get; private set; } = null!;
    public long LastCallbackSequence { get; private set; }
    public string? LastCallbackVersion { get; private set; }
    public string MediaHostInstanceId { get; private set; } = null!;
    public long ConsentEvidenceVersion { get; private set; }
    public long PolicyEvidenceVersion { get; private set; }
    public int RetryCount { get; private set; }
    public int ReconciliationCount { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public DateTime RequestedUtc { get; private set; }
    public DateTime? AdmittedUtc { get; private set; }
    public DateTime? ConnectedUtc { get; private set; }
    public DateTime? EndingUtc { get; private set; }
    public DateTime? EndedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long ConcurrencyVersion { get; private set; }
    public Guid? MediaStartAuthorizedByUserId { get; private set; }
    public DateTime? MediaStartAuthorizedUtc { get; private set; }

    public void BeginJoin(DateTime nowUtc) => Transition(TeamsMeetingCallStates.Joining, "joining", nowUtc);
    public void RecordProviderCall(string providerCallId, string? safeReference, string providerState, DateTime nowUtc)
    {
        BindProviderCall(providerCallId, safeReference, nowUtc);
        ApplyProviderState(providerState, 0, null, nowUtc);
    }

    public void BindProviderCall(string providerCallId, string? safeReference, DateTime nowUtc)
    {
        var normalized = Required(providerCallId, 512);
        if (ProviderCallId is not null && !ProviderCallId.Equals(normalized, StringComparison.Ordinal))
            throw new InvalidOperationException("The callback provider call does not match the durable call.");
        ProviderCallId = normalized;
        SafeProviderReference ??= Optional(safeReference, 160);
        Touch(nowUtc);
    }

    public bool ApplyProviderState(string providerState, long sequence, string? version, DateTime nowUtc)
    {
        if (sequence > 0 && sequence <= LastCallbackSequence) return false;
        var normalized = providerState.Trim().ToLowerInvariant();
        var target = normalized switch
        {
            "incoming" or "establishing" or "ringing" => TeamsMeetingCallStates.WaitingInLobby,
            "established" => TeamsMeetingCallStates.Connected,
            "terminated" => TeamsMeetingCallStates.Ended,
            "rejected" => TeamsMeetingCallStates.Rejected,
            "terminating" => TeamsMeetingCallStates.Ending,
            _ => State
        };
        if (State == TeamsMeetingCallStates.Ended && target != TeamsMeetingCallStates.Ended) return false;
        if (State != TeamsMeetingCallStates.ReconciliationRequired && !IsTerminal(target) && Rank(target) < Rank(State)) return false;
        ProviderState = Optional(providerState, 64);
        if (sequence > 0) LastCallbackSequence = sequence;
        LastCallbackVersion = Optional(version, 160);
        Transition(target, normalized, nowUtc);
        return true;
    }

    public string RequestLeave(DateTime nowUtc, bool forced = false)
    {
        if (!TeamsMeetingCallStates.IsActive(State)) return Key(CompanyId, MeetingSessionId, "leave", ActionVersion);
        Action = forced ? "terminate" : "leave";
        ActionVersion++;
        State = TeamsMeetingCallStates.LeaveRequested;
        EndingUtc = Utc(nowUtc);
        Touch(nowUtc);
        return Key(CompanyId, MeetingSessionId, Action, ActionVersion);
    }

    public string RequestJoin(string meetingReferenceHash, long consentVersion, long policyVersion, string hostId, DateTime nowUtc)
    {
        if (TeamsMeetingCallStates.IsActive(State)) return JoinIdempotencyKey;
        ProviderCallId = null; SafeProviderReference = null; ProviderState = null;
        FailureCode = null; FailureSummary = null; LastCallbackSequence = 0; LastCallbackVersion = null;
        AdmittedUtc = null; ConnectedUtc = null; EndingUtc = null; EndedUtc = null;
        MediaStartAuthorizedByUserId = null; MediaStartAuthorizedUtc = null;
        MeetingReferenceHash = Required(meetingReferenceHash, 128); ConsentEvidenceVersion = consentVersion;
        PolicyEvidenceVersion = policyVersion; MediaHostInstanceId = Required(hostId, 120);
        Action = "join"; ActionVersion++; State = TeamsMeetingCallStates.Requested;
        JoinGeneration++;
        JoinIdempotencyKey = Key(CompanyId, MeetingSessionId, Action, ActionVersion);
        RequestedUtc = Utc(nowUtc); Touch(nowUtc); return JoinIdempotencyKey;
    }

    public string RequestReconciliation(DateTime nowUtc)
    {
        Action = "reconcile";
        ActionVersion++;
        ReconciliationCount++;
        State = TeamsMeetingCallStates.ReconciliationRequired;
        Touch(nowUtc);
        return Key(CompanyId, MeetingSessionId, Action, ActionVersion);
    }

    public void AuthorizeMediaStart(Guid actorUserId, long expectedVersion, DateTime nowUtc)
    {
        if (actorUserId == Guid.Empty) throw new ArgumentException("An organizer is required.", nameof(actorUserId));
        if (OrganizerUserId != actorUserId) throw new UnauthorizedAccessException("Only the meeting organizer can start Alex audio.");
        if (ConcurrencyVersion != expectedVersion) throw new InvalidOperationException("The Teams call changed after it was opened.");
        if (State != TeamsMeetingCallStates.Connected) throw new InvalidOperationException("Alex must be admitted and connected before audio can start.");
        MediaStartAuthorizedByUserId = actorUserId;
        MediaStartAuthorizedUtc = Utc(nowUtc);
        Touch(nowUtc);
    }

    public void StopMedia(Guid actorUserId, long expectedVersion, DateTime nowUtc)
    {
        if (actorUserId == Guid.Empty) throw new ArgumentException("An organizer is required.", nameof(actorUserId));
        if (OrganizerUserId != actorUserId) throw new UnauthorizedAccessException("Only the meeting organizer can stop Alex audio.");
        if (ConcurrencyVersion != expectedVersion) throw new InvalidOperationException("The Teams call changed after it was opened.");
        MediaStartAuthorizedByUserId = null;
        MediaStartAuthorizedUtc = null;
        Touch(nowUtc);
    }

    public void AssignMediaHost(string mediaHostInstanceId, DateTime nowUtc)
    {
        if (State != TeamsMeetingCallStates.Requested || ProviderCallId is not null)
            throw new InvalidOperationException("A Teams media host can only be assigned before the provider join starts.");
        if (MediaHostInstanceId is not ("pending" or "unassigned") &&
            !MediaHostInstanceId.Equals(mediaHostInstanceId, StringComparison.Ordinal))
            throw new InvalidOperationException("The Teams media call is already pinned to another host.");
        MediaHostInstanceId = Required(mediaHostInstanceId, 120);
        Touch(nowUtc);
    }

    public void MarkAmbiguous(string code, string summary, DateTime nowUtc)
    {
        FailureCode = Required(code, 120);
        FailureSummary = Required(summary, 1000);
        RetryCount++;
        State = TeamsMeetingCallStates.ReconciliationRequired;
        Touch(nowUtc);
    }

    public void MarkFailed(string code, string summary, DateTime nowUtc)
    {
        FailureCode = Required(code, 120);
        FailureSummary = Required(summary, 1000);
        State = TeamsMeetingCallStates.Failed;
        MediaStartAuthorizedByUserId = null;
        MediaStartAuthorizedUtc = null;
        EndedUtc = Utc(nowUtc);
        Touch(nowUtc);
    }

    public void MarkEnded(string reason, DateTime nowUtc)
    {
        ProviderState = Optional(reason, 64);
        State = TeamsMeetingCallStates.Ended;
        MediaStartAuthorizedByUserId = null;
        MediaStartAuthorizedUtc = null;
        EndedUtc = Utc(nowUtc);
        Touch(nowUtc);
    }

    private void Transition(string target, string providerState, DateTime nowUtc)
    {
        State = target;
        ProviderState = Optional(providerState, 64);
        if (target is TeamsMeetingCallStates.Admitted or TeamsMeetingCallStates.Connected) AdmittedUtc ??= Utc(nowUtc);
        if (target == TeamsMeetingCallStates.Connected) ConnectedUtc ??= Utc(nowUtc);
        if (target == TeamsMeetingCallStates.Ending) EndingUtc ??= Utc(nowUtc);
        if (target is TeamsMeetingCallStates.Ended or TeamsMeetingCallStates.Rejected or TeamsMeetingCallStates.Failed) EndedUtc ??= Utc(nowUtc);
        Touch(nowUtc);
    }

    private void Touch(DateTime nowUtc) { UpdatedUtc = Utc(nowUtc); ConcurrencyVersion++; }
    private static bool IsTerminal(string state) => state is TeamsMeetingCallStates.Ended or TeamsMeetingCallStates.Rejected or TeamsMeetingCallStates.Failed;
    private static int Rank(string state) => state switch
    {
        TeamsMeetingCallStates.Requested => 0,
        TeamsMeetingCallStates.Joining => 1,
        TeamsMeetingCallStates.WaitingInLobby => 2,
        TeamsMeetingCallStates.Admitted => 3,
        TeamsMeetingCallStates.Connected => 4,
        TeamsMeetingCallStates.LeaveRequested => 5,
        TeamsMeetingCallStates.ReconciliationRequired => 5,
        TeamsMeetingCallStates.Ending => 6,
        _ => 7
    };
    private static string Key(Guid companyId, Guid meetingId, string action, long version) => $"teams-call:{companyId:N}:{meetingId:N}:{action}:v{version}";
    private static string Required(string? value, int max) => string.IsNullOrWhiteSpace(value) || value.Trim().Length > max ? throw new ArgumentException("A bounded value is required.") : value.Trim();
    private static string? Optional(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : Required(value, max);
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}

public sealed class TeamsCallNotificationReceipt : ICompanyOwnedEntity
{
    private TeamsCallNotificationReceipt() { }
    public TeamsCallNotificationReceipt(Guid id, Guid companyId, Guid callId, string eventKey, string resourceVersion,
        long sequence, string normalizedState, DateTime receivedUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; CallId = callId;
        EventKey = eventKey; ResourceVersion = resourceVersion; Sequence = sequence; NormalizedState = normalizedState;
        ReceivedUtc = receivedUtc.Kind == DateTimeKind.Utc ? receivedUtc : receivedUtc.ToUniversalTime();
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CallId { get; private set; }
    public string EventKey { get; private set; } = null!;
    public string ResourceVersion { get; private set; } = null!;
    public long Sequence { get; private set; }
    public string NormalizedState { get; private set; } = null!;
    public DateTime ReceivedUtc { get; private set; }
    public DateTime? ProcessedUtc { get; private set; }
    public bool Ignored { get; private set; }
    public void Complete(bool ignored, DateTime nowUtc) { Ignored = ignored; ProcessedUtc = nowUtc.Kind == DateTimeKind.Utc ? nowUtc : nowUtc.ToUniversalTime(); }
}
