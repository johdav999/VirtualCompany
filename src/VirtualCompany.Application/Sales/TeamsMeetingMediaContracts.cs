namespace VirtualCompany.Application.Sales;

public static class TeamsMeetingMediaStates
{
    public const string Attaching = "attaching";
    public const string Active = "active";
    public const string Suspended = "suspended";
    public const string Reconnecting = "reconnecting";
    public const string Degraded = "degraded";
    public const string Stopped = "stopped";
}

public static class TeamsMeetingMediaProblemCodes
{
    public const string NotReady = "teams_media.not_ready";
    public const string BindingRejected = "teams_media.binding_rejected";
    public const string ConsentRequired = "teams_media.consent_required";
    public const string HostMismatch = "teams_media.host_mismatch";
    public const string InvalidFrame = "teams_media.invalid_frame";
    public const string ReorderedFrame = "teams_media.reordered_frame";
    public const string Backpressure = "teams_media.backpressure";
    public const string QuotaExceeded = "teams_media.quota_exceeded";
    public const string SessionNotFound = "teams_media.session_not_found";
}

public sealed record TeamsAudioFormat(int SampleRateHertz, int BitsPerSample, int Channels, int FrameDurationMilliseconds)
{
    public static TeamsAudioFormat Pcm16KMono20Ms { get; } = new(16_000, 16, 1, 20);
    public int ExpectedFrameBytes => checked(SampleRateHertz * (BitsPerSample / 8) * Channels * FrameDurationMilliseconds / 1000);

    public bool IsSupported => SampleRateHertz == 16_000 && BitsPerSample == 16 && Channels == 1 && FrameDurationMilliseconds == 20;
}

public sealed record TeamsMeetingMediaBinding(
    Guid CompanyId,
    Guid MeetingSessionId,
    Guid TeamsCallId,
    Guid VoiceSessionId,
    Guid AgentId,
    long ConsentVersion,
    string MediaHostInstanceId,
    string ProviderCallId);

public sealed record TeamsMeetingMediaAttachRequest(
    TeamsMeetingMediaBinding Binding,
    TeamsAudioFormat Format,
    int MaximumBufferedFrames,
    TimeSpan MaximumDuration);

public sealed record TeamsAudioFrame(
    Guid MediaSessionId,
    long Sequence,
    DateTime TimestampUtc,
    TeamsAudioFormat Format,
    ReadOnlyMemory<byte> Data,
    bool IsSilence,
    string? ParticipantId = null,
    string? ParticipantDisplayName = null);

public sealed record TeamsMeetingMediaHealth(
    bool Enabled,
    bool Approved,
    bool Available,
    string Route,
    string Status,
    string? ReasonCode = null,
    string? Message = null,
    string? SdkVersion = null);

public sealed record TeamsMeetingMediaSession(
    Guid Id,
    TeamsMeetingMediaBinding Binding,
    string Route,
    string State,
    TeamsAudioFormat Format,
    DateTime CreatedUtc,
    DateTime ExpiresUtc,
    long ReceivedFrames,
    long SentFrames,
    long DroppedFrames,
    int ReconnectCount,
    string? FailureCode = null,
    string? FailureSummary = null);

public sealed record TeamsMediaWriteResult(bool Accepted, bool Backpressured, string? ReasonCode = null);

public interface ITeamsMeetingMediaAdapter
{
    Task<TeamsMeetingMediaHealth> GetHealthAsync(CancellationToken cancellationToken);
    Task<TeamsMeetingMediaSession> AttachAsync(TeamsMeetingMediaAttachRequest request, CancellationToken cancellationToken);
    IAsyncEnumerable<TeamsAudioFrame> ReceiveAudioAsync(Guid mediaSessionId, CancellationToken cancellationToken);
    Task<TeamsMediaWriteResult> SendAudioAsync(TeamsAudioFrame frame, CancellationToken cancellationToken);
    Task SuspendAsync(Guid mediaSessionId, string reason, CancellationToken cancellationToken);
    Task CancelResponseAsync(Guid mediaSessionId, CancellationToken cancellationToken);
    Task<TeamsMeetingMediaSession> ReconnectAsync(Guid mediaSessionId, CancellationToken cancellationToken);
    Task<TeamsMeetingMediaSession?> GetStatusAsync(Guid mediaSessionId, CancellationToken cancellationToken);
    Task TerminateAsync(Guid mediaSessionId, string reason, CancellationToken cancellationToken);
}

// Called only by the approved provider transport on the pinned media host.
public interface ITeamsMeetingMediaIngress
{
    Task<TeamsMediaWriteResult> ReceiveProviderAudioAsync(TeamsAudioFrame frame, CancellationToken cancellationToken);
}

public interface ITeamsMeetingMediaBindingAuthorizer
{
    Task AuthorizeAsync(TeamsMeetingMediaBinding binding, CancellationToken cancellationToken);
}

public interface ITeamsRealtimeAudioBridge
{
    Task RunAsync(TeamsMeetingMediaBinding binding, string realtimeProviderSessionId,
        CancellationToken cancellationToken);
}

public interface ITeamsMeetingMediaCoordinator
{
    Task StartIfReadyAsync(Guid companyId, Guid teamsCallId, CancellationToken cancellationToken);
    Task StopAsync(Guid teamsCallId, string reason, CancellationToken cancellationToken);
    Task ReconcileHostAsync(string mediaHostInstanceId, CancellationToken cancellationToken);
}

public sealed class TeamsMeetingMediaException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
