using VirtualCompany.Application.Agents;

namespace VirtualCompany.Application.Sales;

public static class SalesMeetingRealtimeToolNames
{
    public const string GetCurrentSlide = SalesPresentationToolNames.GetCurrentSlide;
    public const string AskGroundedQuestion = "ask_grounded_question";
    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        AskGroundedQuestion,
        SalesPresentationToolNames.GetCurrentSlide,
        SalesPresentationToolNames.SearchSlides,
        SalesPresentationToolNames.Next,
        SalesPresentationToolNames.Previous,
        SalesPresentationToolNames.Goto,
        SalesPresentationToolNames.Pause,
        SalesPresentationToolNames.Resume
    };
}

public static class SalesMeetingRealtimeProblemCodes
{
    public const string InvalidRequest = "sales.meeting_voice.invalid_request";
    public const string Disabled = "sales.meeting_voice.disabled";
    public const string ConsentRequired = "sales.meeting_voice.consent_required";
    public const string Unavailable = "sales.meeting_voice.unavailable";
    public const string QuotaExceeded = "sales.meeting_voice.quota_exceeded";
    public const string Conflict = "sales.meeting_voice.conflict";
    public const string ToolRejected = "sales.meeting_voice.tool_rejected";
}

public sealed record StartSalesMeetingRealtimeRequest(Guid AgentId, string OfferSdp);
public sealed record SubmitSalesMeetingRealtimeEventRequest(Guid VoiceSessionId, string EventId, long Sequence, string PayloadJson);
public sealed record CancelSalesMeetingRealtimeResponseRequest(Guid VoiceSessionId, string? ResponseId);
public sealed record StopSalesMeetingRealtimeRequest(long ExpectedVersion, string Reason = "ended");

public sealed record SalesMeetingRealtimeStatusDto(
    Guid? VoiceSessionId,
    Guid MeetingSessionId,
    bool FeatureEnabled,
    bool ConsentGranted,
    bool ProviderConfigured,
    bool MediaRouteApproved,
    bool VoiceAvailable,
    string State,
    string DegradedStatus,
    string? Provider,
    string? Model,
    string? MediaRoute,
    DateTime? ExpiresUtc,
    int ReconnectCount,
    int AudioDurationSeconds,
    int InputTokens,
    int OutputTokens,
    string? LastErrorCode,
    string? LastErrorSummary,
    long Version);

public sealed record SalesMeetingRealtimeStartResult(SalesMeetingRealtimeStatusDto Status, string AnswerSdp);
public sealed record SalesMeetingRealtimeEventResult(SalesMeetingRealtimeStatusDto Status, bool Duplicate,
    bool IgnoredAsReordered, string Outcome, Guid? QuestionId = null, string? ToolResultJson = null);
public sealed record SalesMeetingRealtimeCancelResult(SalesMeetingRealtimeStatusDto Status, string? ClientEventJson);

public sealed record MeetingMediaEvent(
    string EventId,
    long Sequence,
    string Type,
    string SpeakerType,
    string? SpeakerLabel,
    string? Text,
    string? ToolCallId,
    string? ToolName,
    string? ToolArgumentsJson,
    string? ResponseId,
    int AudioDurationMilliseconds,
    int InputTokens,
    int OutputTokens,
    string? ErrorCode,
    string? ErrorSummary);

public sealed record MeetingMediaAdapterHealth(bool Enabled, bool Approved, bool Available, string Route,
    string Status, string? ReasonCode = null, string? Message = null);

public interface IMeetingMediaAdapter
{
    Task<MeetingMediaAdapterHealth> GetHealthAsync(CancellationToken cancellationToken);
    MeetingMediaEvent Normalize(RealtimeAgentEvent realtimeEvent);
}

public interface ISalesMeetingRealtimeService
{
    Task<SalesMeetingRealtimeStatusDto?> GetStatusAsync(Guid companyId, Guid userId, Guid sessionId, CancellationToken cancellationToken);
    Task<SalesMeetingRealtimeStartResult?> StartAsync(Guid companyId, Guid userId, Guid sessionId,
        StartSalesMeetingRealtimeRequest request, string? correlationId, CancellationToken cancellationToken);
    Task<SalesMeetingRealtimeEventResult?> ProcessEventAsync(Guid companyId, Guid userId, Guid sessionId,
        SubmitSalesMeetingRealtimeEventRequest request, string? correlationId, CancellationToken cancellationToken);
    Task<SalesMeetingRealtimeCancelResult?> CancelResponseAsync(Guid companyId, Guid userId, Guid sessionId,
        CancelSalesMeetingRealtimeResponseRequest request, string? correlationId, CancellationToken cancellationToken);
    Task<SalesMeetingRealtimeStatusDto?> StopAsync(Guid companyId, Guid userId, Guid sessionId, Guid voiceSessionId,
        StopSalesMeetingRealtimeRequest request, string? correlationId, CancellationToken cancellationToken);
    Task<SalesMeetingRealtimeStatusDto?> RevokeConsentAsync(Guid companyId, Guid userId, Guid sessionId,
        Guid voiceSessionId, long expectedVersion, string? correlationId, CancellationToken cancellationToken);
}

public sealed class SalesMeetingRealtimeValidationException(IReadOnlyDictionary<string, string[]> errors) : Exception("The voice request is invalid.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class SalesMeetingRealtimeConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
