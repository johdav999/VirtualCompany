namespace VirtualCompany.Application.Sales;

public static class SalesRoomMediaRoutes
{
    // Do not reinterpret the existing single-user browser_webrtc route.
    public const string LiveKit = "browser_livekit_room";
}

public sealed record SalesRoomMediaScope(Guid CompanyId, Guid RoomId)
{
    public void Validate()
    {
        if (CompanyId == Guid.Empty || RoomId == Guid.Empty)
            throw new ArgumentException("A company and room scope are required.");
    }
}
public sealed record SalesRoomMediaReadiness(string Route, bool Enabled, bool Configured,
    bool NativeAvailable, string State, string? ReasonCode);
public sealed record SalesRoomProviderRoom(string Reference, string OperationId, int MaximumParticipants);
public sealed record SalesRoomMediaParticipant(Guid ParticipantId, bool IsAgent, long Generation = 1, DateTimeOffset? ExpiresAt = null);
public sealed record SalesRoomMediaToken(string Url, string Token, string Identity, DateTimeOffset ExpiresAt)
{
    public override string ToString() => $"SalesRoomMediaToken {{ Identity = {Identity}, Token = [redacted] }}";
}
public sealed record SalesRoomAudioFrame(Guid ParticipantId, string TrackId, long TrackGeneration,
    long Sequence, DateTimeOffset ReceivedAt, int SampleRate, ReadOnlyMemory<short> Samples);
public sealed record SalesRoomMediaStatistics(string State, long ReceivedFrames, long DroppedFrames,
    long SentFrames, int Reconnects, long TurnGeneration);

// Infrastructure boundary, not a guest/member authorization surface. Owning room use cases must
// authorize admission/consent and persist operation identities before invoking provider effects.
public interface ISalesRoomMediaTransport
{
    SalesRoomMediaReadiness GetReadiness(bool probeNative = false);
    Task<SalesRoomProviderRoom> EnsureRoomAsync(SalesRoomMediaScope scope, Guid operationId,
        int maximumParticipants, CancellationToken cancellationToken);
    Task<SalesRoomProviderRoom?> InspectRoomAsync(SalesRoomMediaScope scope, CancellationToken cancellationToken);
    Task DeleteRoomAsync(SalesRoomMediaScope scope, CancellationToken cancellationToken);
    SalesRoomMediaToken IssueToken(SalesRoomMediaScope scope, SalesRoomMediaParticipant participant);
    Task RemoveParticipantAsync(SalesRoomMediaScope scope, SalesRoomMediaParticipant participant,
        CancellationToken cancellationToken);
    Task<ISalesRoomMediaConnection> ConnectAgentAsync(SalesRoomMediaScope scope,
        SalesRoomMediaParticipant agent, IReadOnlyCollection<SalesRoomMediaParticipant> consentedHumans,
        CancellationToken cancellationToken);
}
public interface ISalesRoomMediaConnection : IAsyncDisposable
{
    IAsyncEnumerable<SalesRoomAudioFrame> ReceiveAsync(CancellationToken cancellationToken);
    Task<bool> SendAsync(long turnGeneration, int sampleRate, ReadOnlyMemory<short> samples,
        CancellationToken cancellationToken);
    Task<bool> CompleteSpeechAsync(long turnGeneration, CancellationToken cancellationToken);
    Task<long> CancelSpeechAsync(CancellationToken cancellationToken);
    Task RevokeInputAsync(Guid participantId, CancellationToken cancellationToken);
    SalesRoomMediaStatistics GetStatistics();
}
public sealed class SalesRoomMediaException(string code, bool requiresReconciliation = false)
    : Exception("Browser room media operation failed: " + code)
{
    public string Code { get; } = code;
    public bool RequiresReconciliation { get; } = requiresReconciliation;
}
