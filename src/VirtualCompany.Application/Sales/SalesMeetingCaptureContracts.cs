namespace VirtualCompany.Application.Sales;

public static class SalesMeetingCaptureToolNames
{
    public const string ReadContext = "sales.meeting.read_context";
    public const string SearchApprovedKnowledge = "sales.meeting.search_approved_knowledge";
    public const string AnswerQuestion = "sales.meeting.answer_question";
}

public static class SalesMeetingCaptureProblemCodes
{
    public const string InvalidRequest = "sales.meeting_capture.invalid_request";
    public const string Conflict = "sales.meeting_capture.conflict";
    public const string AgentNotAuthorized = "sales.meeting_capture.agent_not_authorized";
}

public sealed record SalesMeetingTranscriptSegmentDraft(Guid ClientItemId, long Sequence, string SpeakerType,
    string? SpeakerLabel, string InputSource, string Content, DateTime StartedUtc, DateTime? EndedUtc,
    decimal? Confidence = null, string ReviewState = "unreviewed", long? ExpectedVersion = null);

public sealed record SalesMeetingObservationDraft(Guid ClientItemId, long Sequence, string Category, string Content,
    decimal? Confidence = null, string? SourceReference = null, string ReviewState = "unreviewed", long? ExpectedVersion = null);

public sealed record SalesMeetingActionItemDraft(Guid ClientItemId, long Sequence, string Title, string? Details = null,
    string? OwnerLabel = null, DateTime? DueUtc = null, string Status = "open", decimal? Confidence = null,
    string? SourceReference = null, string ReviewState = "unreviewed", long? ExpectedVersion = null);

public sealed record AutosaveSalesMeetingCaptureRequest(Guid BatchId, long ExpectedCaptureVersion,
    IReadOnlyList<SalesMeetingTranscriptSegmentDraft>? TranscriptSegments = null,
    IReadOnlyList<SalesMeetingObservationDraft>? Observations = null,
    IReadOnlyList<SalesMeetingActionItemDraft>? ActionItems = null);

public sealed record AskSalesMeetingQuestionRequest(Guid ClientQuestionId, long Sequence, Guid AgentId,
    string Question, string AskerType = "host", string? AskerLabel = null, string InputSource = "typed");

public sealed record ApproveSalesMeetingAnswerForStageRequest(long ExpectedVersion);

public sealed record SalesMeetingTranscriptSegmentDto(Guid Id, Guid ClientItemId, long Sequence, string SpeakerType,
    string? SpeakerLabel, string InputSource, string Content, DateTime StartedUtc, DateTime? EndedUtc,
    decimal? Confidence, string ReviewState, DateTime UpdatedUtc, long Version);

public sealed record SalesMeetingObservationDto(Guid Id, Guid ClientItemId, long Sequence, string Category,
    string Content, decimal? Confidence, string? SourceReference, string ReviewState, DateTime UpdatedUtc, long Version);

public sealed record SalesMeetingActionItemDto(Guid Id, Guid ClientItemId, long Sequence, string Title,
    string? Details, string? OwnerLabel, DateTime? DueUtc, string Status, decimal? Confidence,
    string? SourceReference, string ReviewState, DateTime UpdatedUtc, long Version);

public sealed record SalesMeetingQuestionEvidenceDto(int ClaimOrder, string ClaimText, string ClaimType,
    decimal Confidence, string SourceId, string SourceType, string SourceTitle);

public sealed record SalesMeetingQuestionDto(Guid Id, Guid ClientQuestionId, long Sequence, Guid AgentId,
    string Question, string? Answer, string AskerType, string? AskerLabel, string InputSource,
    Guid? VisibleSlideId, long PresentationVersion, string Status, decimal? Confidence,
    bool FollowUpRequired, string ReviewState, string Visibility, Guid? AiRunId,
    string? FailureCode, string? FailureSummary, DateTime AskedUtc, DateTime? AnsweredUtc,
    DateTime? StageApprovedUtc, DateTime UpdatedUtc, long Version,
    IReadOnlyList<SalesMeetingQuestionEvidenceDto> Evidence);

public sealed record SalesMeetingStageAnswerDto(Guid QuestionId, long Sequence, string Question,
    string Answer, DateTime ApprovedUtc);

public sealed record SalesMeetingCaptureSnapshotDto(Guid SessionId, long CaptureVersion, Guid? LastBatchId,
    DateTime UpdatedUtc, IReadOnlyList<SalesMeetingTranscriptSegmentDto> TranscriptSegments,
    IReadOnlyList<SalesMeetingObservationDto> Observations,
    IReadOnlyList<SalesMeetingActionItemDto> ActionItems,
    IReadOnlyList<SalesMeetingQuestionDto> Questions);

public sealed record SalesMeetingCaptureSaveResultDto(string Disposition, SalesMeetingCaptureSnapshotDto Snapshot);

public sealed class SalesMeetingCaptureValidationException(IReadOnlyDictionary<string, string[]> errors) : Exception("The sales meeting capture request is invalid.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class SalesMeetingCaptureConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public interface ISalesMeetingCaptureService
{
    Task<SalesMeetingCaptureSnapshotDto?> GetAsync(Guid companyId, Guid userId, Guid sessionId, CancellationToken cancellationToken);
    Task<SalesMeetingCaptureSaveResultDto?> AutosaveAsync(Guid companyId, Guid userId, Guid sessionId,
        AutosaveSalesMeetingCaptureRequest request, string? correlationId, CancellationToken cancellationToken);
}

public interface ISalesMeetingQuestionAnsweringService
{
    Task<IReadOnlyList<SalesMeetingQuestionDto>> ListQuestionsAsync(Guid companyId, Guid userId, Guid sessionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SalesMeetingStageAnswerDto>> ListStageAnswersAsync(Guid companyId, Guid userId, Guid sessionId, CancellationToken cancellationToken);
    Task<SalesMeetingQuestionDto?> GetQuestionAsync(Guid companyId, Guid userId, Guid sessionId, Guid questionId, CancellationToken cancellationToken);
    Task<SalesMeetingQuestionDto?> AskAsync(Guid companyId, Guid userId, Guid sessionId,
        AskSalesMeetingQuestionRequest request, string? correlationId, CancellationToken cancellationToken);
    Task<SalesMeetingQuestionDto?> ApproveForStageAsync(Guid companyId, Guid userId, Guid sessionId,
        Guid questionId, long expectedVersion, string? correlationId, CancellationToken cancellationToken);
}
