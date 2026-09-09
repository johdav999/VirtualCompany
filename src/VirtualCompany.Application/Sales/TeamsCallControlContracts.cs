namespace VirtualCompany.Application.Sales;

public static class TeamsCallControlProblemCodes
{
    public const string Disabled = "teams_call.disabled";
    public const string NotReady = "teams_call.not_ready";
    public const string ConsentRequired = "teams_call.consent_required";
    public const string OrganizerRequired = "teams_call.organizer_required";
    public const string MeetingUnavailable = "teams_call.meeting_unavailable";
    public const string CapacityReached = "teams_call.capacity_reached";
    public const string VersionConflict = "teams_call.version_conflict";
    public const string ProviderRejected = "teams_call.provider_rejected";
    public const string ProviderAmbiguous = "teams_call.provider_ambiguous";
}

public sealed class TeamsCallControlException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed record TeamsMeetingCallDto(Guid Id, Guid MeetingSessionId, string State, string? ProviderState,
    string? SafeProviderReference, string Action, long ActionVersion, string MediaHostInstanceId,
    string? FailureCode, string? FailureSummary, DateTime RequestedUtc, DateTime? AdmittedUtc,
    DateTime? ConnectedUtc, DateTime? EndingUtc, DateTime? EndedUtc, DateTime UpdatedUtc, long Version,
    bool WaitingForOrganizerAdmission,
    bool MediaStartAuthorized = false,
    Guid? MediaStartAuthorizedByUserId = null,
    DateTime? MediaStartAuthorizedUtc = null);

public sealed record RequestTeamsCallJoin(long? ExpectedVersion = null);
public sealed record RequestTeamsCallLeave(long ExpectedVersion, string? Reason = null);
public sealed record RequestTeamsCallReconciliation(long ExpectedVersion);
public sealed record RequestTeamsCallMediaStart(long ExpectedVersion);
public sealed record RequestTeamsCallMediaStop(long ExpectedVersion, string Reason = "organizer_stopped_audio");
public sealed record RequestTeamsCallConsentRevocation(long ExpectedVersion);

public interface ITeamsCallControlService
{
    Task<TeamsMeetingCallDto?> GetAsync(Guid companyId, Guid actorUserId, Guid meetingSessionId, CancellationToken cancellationToken);
    Task<TeamsMeetingCallDto> RequestJoinAsync(Guid companyId, Guid actorUserId, Guid meetingSessionId, RequestTeamsCallJoin request, string? correlationId, CancellationToken cancellationToken);
    Task<TeamsMeetingCallDto?> RequestLeaveAsync(Guid companyId, Guid actorUserId, Guid meetingSessionId, RequestTeamsCallLeave request, string? correlationId, CancellationToken cancellationToken);
    Task<TeamsMeetingCallDto?> RequestReconciliationAsync(Guid companyId, Guid actorUserId, Guid meetingSessionId, RequestTeamsCallReconciliation request, string? correlationId, CancellationToken cancellationToken);
    Task<TeamsMeetingCallDto> AuthorizeMediaStartAsync(Guid companyId, Guid actorUserId, Guid meetingSessionId, RequestTeamsCallMediaStart request, string? correlationId, CancellationToken cancellationToken);
    Task<TeamsMeetingCallDto> StopMediaAsync(Guid companyId, Guid actorUserId, Guid meetingSessionId, RequestTeamsCallMediaStop request, string? correlationId, CancellationToken cancellationToken);
    Task<TeamsMeetingCallDto> RevokeConsentAsync(Guid companyId, Guid actorUserId, Guid meetingSessionId, RequestTeamsCallConsentRevocation request, string? correlationId, CancellationToken cancellationToken);
}

public sealed record TeamsCallControlWorkItem(Guid CompanyId, Guid CallId, long ActionVersion, string Action, string? CorrelationId);
public sealed record TeamsCallCallbackWorkItem(Guid CompanyId, Guid CallId, Guid ReceiptId, string? CorrelationId);

public interface ITeamsCallControlDispatcher
{
    Task DispatchAsync(TeamsCallControlWorkItem message, CancellationToken cancellationToken);
    Task ProcessCallbackAsync(TeamsCallCallbackWorkItem message, CancellationToken cancellationToken);
}

public enum TeamsCallProviderOutcome { Succeeded, NotFound, Rejected, Ambiguous, RetryableFailure, PermanentFailure }
public sealed record TeamsCallProviderResult(TeamsCallProviderOutcome Outcome, string? ProviderCallId = null,
    string? ProviderState = null, string? SafeProviderReference = null, string? ErrorCode = null, string? ErrorSummary = null);
public sealed record TeamsCallJoinContext(Guid CompanyId, Guid CallId, Guid EntraTenantId,
    IReadOnlyCollection<string> RequiredPermissions, string JoinWebUrl, Uri CallbackUri, string IdempotencyKey,
    string MediaHostInstanceId = "");
public sealed record TeamsCallProviderContext(Guid CompanyId, Guid CallId, Guid EntraTenantId,
    IReadOnlyCollection<string> RequiredPermissions, string ProviderCallId, string IdempotencyKey,
    string MediaHostInstanceId = "");

public interface ITeamsCallControlAdapter
{
    Task<TeamsCallProviderResult> JoinAsync(TeamsCallJoinContext request, CancellationToken cancellationToken);
    Task<TeamsCallProviderResult> AcceptAsync(TeamsCallProviderContext request, CancellationToken cancellationToken);
    Task<TeamsCallProviderResult> LeaveAsync(TeamsCallProviderContext request, CancellationToken cancellationToken);
    Task<TeamsCallProviderResult> GetAsync(TeamsCallProviderContext request, CancellationToken cancellationToken);
}

public sealed record TeamsCallMediaPreparation(string ODataType, string? ConfigurationBlob);

public interface ITeamsCallMediaPreparationProvider
{
    Task<TeamsCallMediaPreparation> PrepareAsync(Guid localCallId, string mediaHostInstanceId, CancellationToken cancellationToken);
    Task BindProviderCallAsync(Guid localCallId, string providerCallId, CancellationToken cancellationToken);
    Task ReleaseAsync(Guid localCallId, CancellationToken cancellationToken);
}

public sealed record TeamsCallCallbackReceiveResult(bool RequiresGraphProtocol, int Accepted, int Duplicates, int Ignored);
public interface ITeamsCallCallbackReceiver
{
    Task<TeamsCallCallbackReceiveResult> ReceiveAsync(TeamsCallbackIdentity identity, Guid? localCallId, long? joinGeneration, Stream body, CancellationToken cancellationToken);
}
