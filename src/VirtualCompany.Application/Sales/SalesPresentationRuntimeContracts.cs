namespace VirtualCompany.Application.Sales;

public static class SalesPresentationToolNames
{
    public const string GetCurrentSlide = "presentation.get_current_slide";
    public const string Next = "presentation.next";
    public const string Previous = "presentation.previous";
    public const string Goto = "presentation.goto";
    public const string SearchSlides = "presentation.search_slides";
    public const string Pause = "presentation.pause";
    public const string Resume = "presentation.resume";

    public static bool IsMutation(string value) => value is Next or Previous or Goto or Pause or Resume;
    public static bool IsRead(string value) => value is GetCurrentSlide or SearchSlides;
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        GetCurrentSlide, SearchSlides, Next, Previous, Goto, Pause, Resume
    };
}

public static class SalesPresentationControlModes
{
    public const string Manual = "manual";
    public const string Assisted = "assisted";
    public const string Autonomous = "autonomous";

    public static bool IsValid(string? value) => value is Manual or Assisted or Autonomous;
}

public static class SalesPresentationCommandActorTypes
{
    public const string Human = "human";
    public const string Agent = "agent";
}

public static class SalesPresentationRuntimeProblemCodes
{
    public const string Conflict = "sales.presentation_runtime.conflict";
    public const string OutOfOrder = "sales.presentation_runtime.out_of_order";
    public const string InvalidCommand = "sales.presentation_runtime.invalid_command";
    public const string Unavailable = "sales.presentation_runtime.unavailable";
}

public sealed record SalesPresentationCommandRequest(
    Guid CommandId,
    long Sequence,
    long ExpectedVersion,
    int? SlideNumber = null,
    int? TalkingPointIndex = null,
    string? ResumeMarker = null,
    string ActorType = SalesPresentationCommandActorTypes.Human,
    Guid? DeckId = null,
    Guid? ActorId = null,
    int? DeckVersion = null,
    long? ActorGeneration = null);

public sealed record SetSalesPresentationControlModeRequest(string Mode, long ExpectedVersion);

public sealed record SalesPresentationControlModeDto(
    Guid SessionId,
    string Mode,
    Guid UpdatedByUserId,
    DateTime UpdatedUtc,
    long Version);

public sealed record SalesPresentationStageSnapshotDto(
    Guid SessionId,
    string SessionStatus,
    long Sequence,
    long Version,
    Guid DeckId,
    int DeckVersion,
    int SlideNumber,
    int SlideCount,
    string? SlideTitle,
    string SlideText,
    string? ImageStorageUrl,
    int ImageWidthPixels,
    int ImageHeightPixels);

public sealed record SalesPresentationPrivateSnapshotDto(
    SalesPresentationStageSnapshotDto Stage,
    string ControlMode,
    int TalkingPointIndex,
    string? ResumeMarker,
    string? SpeakerNotes,
    string Objective,
    int ExpectedDurationSeconds,
    string TransitionText,
    IReadOnlyList<SalesPresentationArtifactDto> PlanArtifacts,
    Guid? ControlModeUpdatedByUserId = null,
    DateTime? ControlModeUpdatedUtc = null);

public sealed record SalesPresentationAuthoritativeSnapshotDto(
    SalesPresentationStageSnapshotDto Stage,
    SalesPresentationPrivateSnapshotDto Private);

public sealed record SalesPresentationSearchResultDto(
    int SlideNumber,
    string? Title,
    string Snippet);

public sealed record SalesPresentationCommandResultDto(
    string Disposition,
    string? ReasonCode,
    SalesPresentationAuthoritativeSnapshotDto Snapshot);

public sealed class SalesPresentationRuntimeConflictException(
    string code,
    string message,
    SalesPresentationAuthoritativeSnapshotDto snapshot) : Exception(message)
{
    public string Code { get; } = code;
    public SalesPresentationAuthoritativeSnapshotDto Snapshot { get; } = snapshot;
}

public interface ISalesPresentationRuntimeService
{
    Task<SalesPresentationAuthoritativeSnapshotDto?> GetCurrentAsync(Guid companyId, Guid userId, Guid sessionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SalesPresentationSearchResultDto>> SearchAsync(Guid companyId, Guid userId, Guid sessionId, string query, CancellationToken cancellationToken);
    Task<SalesPresentationCommandResultDto?> ExecuteAsync(Guid companyId, Guid userId, Guid sessionId, string toolName, SalesPresentationCommandRequest request, string? correlationId, CancellationToken cancellationToken);
    Task<SalesPresentationControlModeDto?> SetControlModeAsync(Guid companyId, Guid userId, Guid sessionId, SetSalesPresentationControlModeRequest request, string? correlationId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Presentation control modes are not implemented by this runtime.");
    Task RecordReconnectAsync(Guid companyId, Guid userId, Guid sessionId, string surface, CancellationToken cancellationToken);
}

public sealed record SalesPresentationStagePresence(
    Guid CompanyId,
    Guid SessionId,
    Guid DeckId,
    int DeckVersion,
    string ConnectionId,
    bool Active,
    DateTime ObservedUtc);

public sealed record SalesPresentationRenderAcknowledgement(
    Guid CompanyId,
    Guid SessionId,
    Guid DeckId,
    int DeckVersion,
    int SlideNumber,
    long PresentationSequence,
    long PresentationVersion,
    string ConnectionId,
    DateTime RenderedUtc);

public sealed record SalesPresentationRenderWaitResult(
    bool Acknowledged,
    string Status,
    SalesPresentationRenderAcknowledgement? Acknowledgement = null);

public static class SalesPresentationStageAccessProblemCodes
{
    public const string Disabled = "sales.presentation_stage.disabled";
    public const string OrganizerRequired = "sales.presentation_stage.organizer_required";
    public const string InvalidGrant = "sales.presentation_stage.invalid_grant";
    public const string ExpiredGrant = "sales.presentation_stage.expired_grant";
    public const string DeckChanged = "sales.presentation_stage.deck_changed";
    public const string SlideNotAllowed = "sales.presentation_stage.slide_not_allowed";
}

public sealed record SalesPresentationStageAccessGrantDto(
    string AccessToken,
    Guid SessionId,
    Guid DeckId,
    int DeckVersion,
    DateTime ExpiresUtc,
    string StagePath);

public sealed record SalesPresentationStageAccessContext(
    Guid CompanyId,
    Guid SessionId,
    Guid DeckId,
    int DeckVersion,
    Guid IssuedByUserId,
    DateTime ExpiresUtc);

public sealed record SalesPresentationSlideAsset(
    Stream Content,
    string ContentType,
    string ContentHash,
    int WidthPixels,
    int HeightPixels) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

public sealed class SalesPresentationStageAccessException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public interface ISalesPresentationStageAccessService
{
    Task<SalesPresentationStageAccessGrantDto> IssueAsync(Guid companyId, Guid organizerUserId,
        Guid sessionId, CancellationToken cancellationToken);
    Task<SalesPresentationStageAccessContext> ValidateAsync(Guid sessionId, string accessToken,
        CancellationToken cancellationToken);
    Task<SalesPresentationStageSnapshotDto> GetSnapshotAsync(Guid sessionId, string accessToken,
        CancellationToken cancellationToken);
    Task<SalesPresentationSlideAsset> OpenSlideAsync(Guid sessionId, string accessToken,
        Guid deckId, int deckVersion, int slideNumber, CancellationToken cancellationToken);
}

public interface ISalesPresentationStagePresenceService
{
    Task RegisterAsync(SalesPresentationStagePresence presence, CancellationToken cancellationToken);
    Task DisconnectAsync(Guid companyId, Guid sessionId, string connectionId, CancellationToken cancellationToken);
    Task AcknowledgeAsync(SalesPresentationRenderAcknowledgement acknowledgement, CancellationToken cancellationToken);
    Task<SalesPresentationRenderWaitResult> WaitForRenderAsync(SalesPresentationStageSnapshotDto expected, TimeSpan timeout, CancellationToken cancellationToken);
    Task<bool> IsConnectedAsync(Guid companyId, Guid sessionId, Guid deckId, int deckVersion, CancellationToken cancellationToken);
}

public sealed record SalesPresentationNarrationRequest(
    Guid CompanyId,
    Guid OrganizerUserId,
    Guid SessionId,
    string? ToolName,
    int? SlideNumber,
    int? TalkingPointIndex,
    string? ResumeMarker,
    string? SearchQuery,
    string? CorrelationId,
    Guid? AgentId = null);

public sealed record SalesPresentationNarrationPlan(
    string Disposition,
    string? ReasonCode,
    string ControlMode,
    SalesPresentationAuthoritativeSnapshotDto Snapshot,
    SalesPresentationRenderWaitResult Render,
    string Objective,
    IReadOnlyList<string> TalkingPoints,
    int TalkingPointIndex,
    string? ResumeMarker,
    string TransitionText,
    bool MayNarrate);

public interface ISalesMeetingPresentationConductor
{
    Task<SalesPresentationNarrationPlan?> PrepareAsync(SalesPresentationNarrationRequest request, CancellationToken cancellationToken);
    CancellationToken GetNarrationCancellation(Guid companyId, Guid sessionId, long presentationVersion);
    void Preempt(Guid companyId, Guid sessionId, long authoritativePresentationVersion);
}

public interface ISalesPresentationNarrationPreemption
{
    CancellationToken Bind(Guid companyId, Guid sessionId, long presentationVersion);
    void Preempt(Guid companyId, Guid sessionId, long authoritativePresentationVersion);
}

public interface ISalesPresentationEventPublisher
{
    Task PublishAsync(Guid companyId, Guid sessionId, SalesPresentationAuthoritativeSnapshotDto snapshot, CancellationToken cancellationToken);
}

public sealed record SalesBrowserPresentationAudienceMemberDto(
    Guid ParticipantId,
    string DisplayName,
    bool Connected,
    string State,
    DateTime? RenderedUtc);

public sealed record SalesBrowserPresentationReadinessDto(
    Guid RoomId,
    Guid DeckId,
    int DeckVersion,
    int SlideNumber,
    long PresentationSequence,
    long PresentationVersion,
    DateTime DeadlineUtc,
    bool OverrideApplied,
    IReadOnlyList<SalesBrowserPresentationAudienceMemberDto> Audience)
{
    public int RequiredCount => Audience.Count;
    public int RenderedCount => Audience.Count(x => x.State is "rendered" or "overridden");
    public bool Ready => RequiredCount == RenderedCount;
}

public sealed record SalesBrowserPresentationPublicDto(
    Guid RoomId,
    Guid ParticipantId,
    long ActorGeneration,
    SalesPresentationStageSnapshotDto Stage);

public sealed record SalesBrowserPresentationHostDto(
    Guid RoomId,
    Guid ParticipantId,
    long ActorGeneration,
    SalesPresentationAuthoritativeSnapshotDto Presentation,
    SalesBrowserPresentationReadinessDto Readiness);

public sealed record SetSalesBrowserPresentationControlModeRequest(
    string Mode,
    long ExpectedVersion,
    Guid ActorId,
    long ActorGeneration);

public sealed record SalesBrowserPresentationAccessContext(
    Guid CompanyId,
    Guid RoomId,
    Guid SessionId,
    Guid ParticipantId,
    long ParticipantGeneration,
    bool IsOrganizer);

public interface ISalesBrowserPresentationService
{
    Task<SalesBrowserPresentationHostDto> GetHostAsync(Guid companyId, Guid userId, Guid roomId, CancellationToken cancellationToken);
    Task<SalesBrowserPresentationPublicDto> GetGuestAsync(string credential, Guid roomId, CancellationToken cancellationToken);
    Task<SalesBrowserPresentationAccessContext> ValidateHostAsync(Guid companyId, Guid userId, Guid roomId, CancellationToken cancellationToken);
    Task<SalesBrowserPresentationAccessContext> ValidateGuestAsync(string credential, Guid roomId, CancellationToken cancellationToken);
    Task<SalesPresentationSlideAsset> OpenHostSlideAsync(Guid companyId, Guid userId, Guid roomId, Guid deckId, int deckVersion, int slideNumber, CancellationToken cancellationToken);
    Task<SalesPresentationSlideAsset> OpenGuestSlideAsync(string credential, Guid roomId, Guid deckId, int deckVersion, int slideNumber, CancellationToken cancellationToken);
    Task<SalesPresentationCommandResultDto> ExecuteHostAsync(Guid companyId, Guid userId, Guid roomId, string toolName, SalesPresentationCommandRequest request, string? correlationId, CancellationToken cancellationToken);
    Task<SalesPresentationControlModeDto> SetControlModeAsync(Guid companyId, Guid userId, Guid roomId, SetSalesBrowserPresentationControlModeRequest request, string? correlationId, CancellationToken cancellationToken);
    Task<SalesBrowserPresentationReadinessDto> ConnectAsync(SalesBrowserPresentationAccessContext access, CancellationToken cancellationToken);
    Task<SalesBrowserPresentationReadinessDto> AcknowledgeAsync(SalesBrowserPresentationAccessContext access, SalesPresentationRenderAcknowledgement acknowledgement, CancellationToken cancellationToken);
    Task<SalesBrowserPresentationReadinessDto> DisconnectAsync(SalesBrowserPresentationAccessContext access, CancellationToken cancellationToken);
    Task<SalesBrowserPresentationReadinessDto> OverrideAsync(Guid companyId, Guid userId, Guid roomId, long expectedPresentationVersion, CancellationToken cancellationToken);
}
