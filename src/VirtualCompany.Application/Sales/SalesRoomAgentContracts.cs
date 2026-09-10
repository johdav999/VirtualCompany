namespace VirtualCompany.Application.Sales;

public static class SalesRoomAgentProblemCodes
{
    public const string Disabled = "sales.room_agent.disabled", ConsentRequired = "sales.room_agent.consent_required",
        Conflict = "sales.room_agent.conflict", Unavailable = "sales.room_agent.unavailable",
        ReleaseRequired = "sales.room_agent.release_required", QuotaExceeded = "sales.room_agent.quota_exceeded",
        FloorConflict = "sales.room_agent.floor_conflict", FloorNotReady = "sales.room_agent.floor_not_ready",
        EmergencyDisabled = "sales.room_agent.emergency_disabled", Draining = "sales.room_agent.draining",
        ConcurrencyLimit = "sales.room_agent.concurrency_limit", SpendLimit = "sales.room_agent.spend_limit";
}
public sealed record StartSalesRoomAgent(Guid CommandId, long ExpectedVersion);
public sealed record StopSalesRoomAgent(Guid CommandId, long ExpectedVersion, string Reason = "host_stopped");
public sealed record InvokeSalesRoomNarration(Guid CommandId, long ExpectedVersion, Guid RevisionId, Guid SegmentId, int OffsetMilliseconds = 0);
public sealed record AskSalesRoomAgent(Guid CommandId, long ExpectedVersion, string Question);
public sealed record SpeakSalesRoomAnswer(Guid CommandId, long ExpectedVersion, Guid QuestionId, long ExpectedQuestionVersion);
public sealed record TakeOverSalesRoomAgent(Guid CommandId, long ExpectedVersion, long ExpectedFloorVersion);
public sealed record ResumeSalesRoomAgent(Guid CommandId, long ExpectedVersion, long ExpectedFloorVersion, long ExpectedPresentationVersion);
public sealed record ConfirmSalesRoomPendingTurn(Guid CommandId, long ExpectedVersion, long ExpectedFloorVersion);
public sealed record DismissSalesRoomPendingTurn(Guid CommandId, long ExpectedVersion, long ExpectedFloorVersion);
public sealed record AuthorizeSalesRoomCoHost(Guid CommandId, long ExpectedVersion, long ExpectedFloorVersion, Guid? ParticipantId);
public sealed record AddressSalesRoomAgent(Guid CommandId, long ExpectedParticipantVersion, string Question);
public sealed record AcknowledgeSalesRoomPlaybackStop(Guid StopId, long ResponseGeneration, string ConnectionId);
public sealed record SalesRoomAgentEvidenceView(string SourceId, string SourceType, string SourceTitle);
public sealed record SalesRoomAgentAnswerView(Guid QuestionId, string Question, string? Answer, string Status,
    string Visibility, long Version, IReadOnlyList<SalesRoomAgentEvidenceView> Evidence);
public sealed record SalesRoomAgentSpeechView(Guid Id, string Kind, string Status, int DurationMilliseconds,
    string? FailureCode, string? FailureSummary, DateTime CreatedUtc);
public sealed record SalesRoomPlaybackStopView(Guid? StopId, string State, int RequiredCount, int AcknowledgedCount,
    DateTime? RequestedUtc, DateTime? DeadlineUtc, int? ElapsedMilliseconds);
public sealed record SalesRoomFloorView(string State, string OwnerLabel, Guid? OwnerParticipantId, string ControlMode,
    Guid? PendingTurnId, Guid? PendingParticipantId, string? PendingParticipantLabel, Guid? PendingQuestionId,
    string PendingTurnState, bool AddressedAgent, bool Overlap, long TurnGeneration, long ResponseGeneration,
    long PresentationVersion, int SlideNumber, int TalkingPointIndex, string? ResumeMarker,
    int ResumeOffsetMilliseconds, Guid? PreauthorizedCoHostParticipantId, long Version,
    SalesRoomPlaybackStopView PlaybackStop);
public sealed record SalesRoomAgentStatusView(Guid RoomId, Guid? AgentId, string AgentName, string State,
    string VoiceHealth, long Generation, long TurnGeneration, int ConsentedParticipants, int RequiredParticipants,
    bool AllParticipantsConsented, DateTime? StartedUtc, DateTime? LeaseExpiresUtc,
    Guid? NarrationRevisionId, Guid? NarrationSegmentId, string NarrationState,
    long ReceivedAudioMilliseconds, long DetectedSpeechMilliseconds, long ForwardedAudioMilliseconds,
    long? ProviderBilledAudioMilliseconds, string BillingEvidenceState, long OutputAudioMilliseconds,
    int InputTokens, int OutputTokens, decimal EstimatedSpendUsd, string? LastErrorCode, string? LastErrorSummary,
    long RoomVersion, SalesRoomAgentAnswerView? LatestAnswer, IReadOnlyList<SalesRoomAgentSpeechView> RecentSpeech,
    SalesRoomFloorView? Floor = null);
public sealed record SalesRoomAgentWorkItem(Guid CompanyId, Guid RoomId, Guid LeaseOwnerId, long Generation, string Action);
public interface ISalesRoomAgentCommandSink { ValueTask SignalAsync(SalesRoomAgentWorkItem work, CancellationToken ct); }
public sealed record SalesRoomPlaybackStopRequest(Guid RoomId, Guid StopId, long ResponseGeneration, DateTime DeadlineUtc);
public sealed record SalesRoomPlaybackStartRequest(Guid RoomId, long ResponseGeneration);
public interface ISalesRoomFloorEventPublisher
{
    Task RequestPlaybackStopAsync(Guid companyId, Guid sessionId, SalesRoomPlaybackStopRequest request, CancellationToken ct);
    Task AllowPlaybackAsync(Guid companyId, Guid sessionId, SalesRoomPlaybackStartRequest request, CancellationToken ct);
}
public interface ISalesRoomAgentService
{
    Task<SalesRoomAgentStatusView> GetAsync(Guid companyId, Guid userId, Guid roomId, CancellationToken ct);
    Task<SalesRoomAgentStatusView> StartAsync(Guid companyId, Guid userId, Guid roomId, StartSalesRoomAgent command, CancellationToken ct);
    Task<SalesRoomAgentStatusView> StopAsync(Guid companyId, Guid userId, Guid roomId, StopSalesRoomAgent command, CancellationToken ct);
    Task<SalesRoomAgentStatusView> InvokeNarrationAsync(Guid companyId, Guid userId, Guid roomId, InvokeSalesRoomNarration command, CancellationToken ct);
    Task<SalesRoomAgentStatusView> AskAsync(Guid companyId, Guid userId, Guid roomId, AskSalesRoomAgent command, CancellationToken ct);
    Task<SalesRoomAgentStatusView> SpeakAnswerAsync(Guid companyId, Guid userId, Guid roomId, SpeakSalesRoomAnswer command, CancellationToken ct);
    Task<SalesRoomAgentStatusView> TakeOverAsync(Guid companyId, Guid userId, Guid roomId, TakeOverSalesRoomAgent command, CancellationToken ct);
    Task<SalesRoomAgentStatusView> ResumeAsync(Guid companyId, Guid userId, Guid roomId, ResumeSalesRoomAgent command, CancellationToken ct);
    Task<SalesRoomAgentStatusView> ConfirmPendingTurnAsync(Guid companyId, Guid userId, Guid roomId, ConfirmSalesRoomPendingTurn command, CancellationToken ct);
    Task<SalesRoomAgentStatusView> DismissPendingTurnAsync(Guid companyId, Guid userId, Guid roomId, DismissSalesRoomPendingTurn command, CancellationToken ct);
    Task<SalesRoomAgentStatusView> AuthorizeCoHostAsync(Guid companyId, Guid userId, Guid roomId, AuthorizeSalesRoomCoHost command, CancellationToken ct);
    Task AddressAsync(SalesBrowserPresentationAccessContext access, AddressSalesRoomAgent command, CancellationToken ct);
    Task AcknowledgePlaybackStopAsync(SalesBrowserPresentationAccessContext access, AcknowledgeSalesRoomPlaybackStop acknowledgement, CancellationToken ct);
}
public sealed class SalesRoomAgentException(string code, string message, int statusCode = 409) : Exception(message)
{ public string Code { get; } = code; public int StatusCode { get; } = statusCode; }
