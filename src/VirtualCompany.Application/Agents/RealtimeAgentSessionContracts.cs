namespace VirtualCompany.Application.Agents;

public static class RealtimeAgentEventTypes
{
    public const string Connected = "connected";
    public const string ParticipantSpeechStarted = "participant_speech_started";
    public const string ParticipantTranscriptCompleted = "participant_transcript_completed";
    public const string AgentTranscriptCompleted = "agent_transcript_completed";
    public const string ToolInvocation = "tool_invocation";
    public const string UsageUpdated = "usage_updated";
    public const string ResponseCancelled = "response_cancelled";
    public const string Disconnected = "disconnected";
    public const string ProviderError = "provider_error";
}

public sealed record RealtimeAgentToolDefinition(
    string Name,
    string Description,
    string ParametersJsonSchema,
    string ActionType);

public sealed record RealtimeAgentSessionCreateRequest(
    Guid CompanyId,
    Guid UserId,
    Guid AgentId,
    string Purpose,
    string OfferSdp,
    string Instructions,
    IReadOnlyList<RealtimeAgentToolDefinition> Tools,
    TimeSpan MaximumDuration,
    string? CorrelationId = null);

public sealed record RealtimeAgentSessionConnection(
    string Provider,
    string ProviderSessionId,
    string Model,
    string MediaTransport,
    string AnswerSdp,
    DateTime ExpiresUtc,
    string? EphemeralClientSecret = null);

public sealed record RealtimeAgentPcmSessionCreateRequest(
    Guid CompanyId,
    Guid UserId,
    Guid AgentId,
    string Purpose,
    string Instructions,
    IReadOnlyList<RealtimeAgentToolDefinition> Tools,
    TimeSpan MaximumDuration,
    string? CorrelationId = null);

public sealed record RealtimeAgentPcmSessionConnection(
    string Provider,
    string ProviderSessionId,
    string Model,
    int SampleRateHertz,
    DateTime ExpiresUtc);

public sealed record RealtimeAgentPcmOutput(
    string ProviderSessionId,
    long Sequence,
    DateTime TimestampUtc,
    ReadOnlyMemory<byte> Audio,
    string? ProviderEventId = null,
    string? ProviderEventJson = null);

public sealed record RealtimeAgentProviderEvent(
    string ProviderSessionId,
    string EventId,
    long Sequence,
    string PayloadJson);

public sealed record RealtimeAgentEvent(
    string EventId,
    long Sequence,
    string Type,
    string? Text = null,
    string? SpeakerId = null,
    string? SpeakerLabel = null,
    string? ToolCallId = null,
    string? ToolName = null,
    string? ToolArgumentsJson = null,
    string? ResponseId = null,
    int AudioDurationMilliseconds = 0,
    int InputTokens = 0,
    int OutputTokens = 0,
    string? ErrorCode = null,
    string? ErrorSummary = null);

public sealed record RealtimeAgentHealth(
    bool Enabled,
    bool Configured,
    bool Available,
    string Provider,
    string Model,
    string Status,
    string? ReasonCode = null,
    string? Message = null);

public sealed record RealtimeAgentControlResult(bool Accepted, string? ClientEventJson = null);

public interface IRealtimeAgentSessionGateway
{
    Task<RealtimeAgentHealth> GetHealthAsync(CancellationToken cancellationToken);
    Task<RealtimeAgentSessionConnection> CreateSessionAsync(RealtimeAgentSessionCreateRequest request, CancellationToken cancellationToken);
    Task<RealtimeAgentEvent> NormalizeEventAsync(RealtimeAgentProviderEvent providerEvent, CancellationToken cancellationToken);
    Task<RealtimeAgentControlResult> CancelResponseAsync(string providerSessionId, string? responseId, CancellationToken cancellationToken);
    Task TerminateSessionAsync(string providerSessionId, CancellationToken cancellationToken);
}

/// <summary>
/// Server-side PCM transport for approved telephony and meeting-media adapters. Implementations own
/// provider credentials and never expose them, SDP, or provider sockets to a browser.
/// PCM is signed 16-bit little-endian, 24 kHz, mono as required by the shared Realtime provider.
/// </summary>
public interface IRealtimeAgentPcmSessionGateway
{
    Task<RealtimeAgentPcmSessionConnection> CreatePcmSessionAsync(
        RealtimeAgentPcmSessionCreateRequest request,
        CancellationToken cancellationToken);
    Task SendInputAudioAsync(string providerSessionId, ReadOnlyMemory<byte> pcm24KhzMono,
        CancellationToken cancellationToken);
    IAsyncEnumerable<RealtimeAgentPcmOutput> ReceiveOutputAsync(string providerSessionId,
        CancellationToken cancellationToken);
    Task SendClientEventAsync(string providerSessionId, string clientEventJson,
        CancellationToken cancellationToken);
    Task<RealtimeAgentControlResult> CancelPcmResponseAsync(string providerSessionId, string? responseId,
        CancellationToken cancellationToken);
    Task TerminatePcmSessionAsync(string providerSessionId, CancellationToken cancellationToken);
}

public sealed class RealtimeAgentUnavailableException(string code, string message, int? retryAfterSeconds = null) : Exception(message)
{
    public string Code { get; } = code;
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}

public sealed class RealtimeAgentEventException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
