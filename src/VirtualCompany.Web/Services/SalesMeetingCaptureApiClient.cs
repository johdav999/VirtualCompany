using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public sealed class SalesMeetingCaptureApiClient(
    ICompanyApiTransport transport,
    bool useOfflineMode,
    IApiProblemMessageResolver? problemResolver = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<SalesMeetingCaptureSnapshotViewModel?> GetAsync(Guid companyId, Guid sessionId, CancellationToken cancellationToken = default) =>
        SendAsync<SalesMeetingCaptureSnapshotViewModel>(companyId, HttpMethod.Get, BasePath(sessionId), null, true, cancellationToken);

    public Task<SalesMeetingCaptureSaveResultViewModel?> AutosaveAsync(Guid companyId, Guid sessionId,
        AutosaveSalesMeetingCaptureViewModel request, CancellationToken cancellationToken = default) =>
        SendAsync<SalesMeetingCaptureSaveResultViewModel>(companyId, HttpMethod.Post, BasePath(sessionId) + "/autosave", JsonContent.Create(request), true, cancellationToken);

    public async Task<IReadOnlyList<SalesMeetingQuestionViewModel>> ListQuestionsAsync(Guid companyId, Guid sessionId, CancellationToken cancellationToken = default) =>
        await SendAsync<List<SalesMeetingQuestionViewModel>>(companyId, HttpMethod.Get, BasePath(sessionId) + "/questions", null, false, cancellationToken) ?? [];

    public async Task<IReadOnlyList<SalesMeetingStageAnswerViewModel>> ListStageAnswersAsync(Guid companyId, Guid sessionId, CancellationToken cancellationToken = default) =>
        await SendAsync<List<SalesMeetingStageAnswerViewModel>>(companyId, HttpMethod.Get, BasePath(sessionId) + "/stage-answers", null, false, cancellationToken) ?? [];

    public Task<SalesMeetingQuestionViewModel?> AskAsync(Guid companyId, Guid sessionId, AskSalesMeetingQuestionViewModel request, CancellationToken cancellationToken = default) =>
        SendAsync<SalesMeetingQuestionViewModel>(companyId, HttpMethod.Post, BasePath(sessionId) + "/questions", JsonContent.Create(request), true, cancellationToken);

    public Task<SalesMeetingQuestionViewModel?> GetQuestionAsync(Guid companyId, Guid sessionId, Guid questionId, CancellationToken cancellationToken = default) =>
        SendAsync<SalesMeetingQuestionViewModel>(companyId, HttpMethod.Get, BasePath(sessionId) + $"/questions/{questionId:D}", null, true, cancellationToken);

    public Task<SalesMeetingQuestionViewModel?> ApproveForStageAsync(Guid companyId, Guid sessionId, Guid questionId, long expectedVersion, CancellationToken cancellationToken = default) =>
        SendAsync<SalesMeetingQuestionViewModel>(companyId, HttpMethod.Post, BasePath(sessionId) + $"/questions/{questionId:D}/approve-for-stage", JsonContent.Create(new { expectedVersion }), true, cancellationToken);

    private async Task<T?> SendAsync<T>(Guid companyId, HttpMethod method, string uri, HttpContent? content, bool allowNotFound, CancellationToken cancellationToken)
    {
        if (useOfflineMode) throw new SalesMeetingCaptureApiException("Meeting capture needs the backend API. Start the API before opening the meeting cockpit.");
        try
        {
            using var response = await transport.SendAsync(companyId, method, uri, content, cancellationToken);
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound) return default;
            if (!response.IsSuccessStatusCode) throw await CreateExceptionAsync(response, cancellationToken);
            return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
        }
        catch (HttpRequestException) { throw new SalesMeetingCaptureApiException("The meeting capture service could not be reached."); }
    }

    private async Task<SalesMeetingCaptureApiException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentType?.MediaType is not ("application/json" or "application/problem+json"))
            return new($"The meeting capture request failed with status code {(int)response.StatusCode}.", response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblemResponse>(Json, cancellationToken);
        var fallback = problem?.Errors?.SelectMany(x => x.Value).FirstOrDefault() ?? problem?.Detail ?? problem?.Title ?? "The meeting capture request failed.";
        return new(problemResolver?.Resolve(problem, fallback) ?? fallback, response.StatusCode, problem?.Errors);
    }
    private static string BasePath(Guid sessionId) => $"api/sales/meeting-sessions/{sessionId:D}/capture";
}

public sealed class SalesMeetingCaptureApiException(string message, HttpStatusCode? statusCode = null,
    IReadOnlyDictionary<string, string[]>? errors = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;
}

public sealed record SalesMeetingTranscriptSegmentDraftViewModel(Guid ClientItemId, long Sequence, string SpeakerType,
    string? SpeakerLabel, string InputSource, string Content, DateTime StartedUtc, DateTime? EndedUtc,
    decimal? Confidence = null, string ReviewState = "unreviewed", long? ExpectedVersion = null);
public sealed record SalesMeetingObservationDraftViewModel(Guid ClientItemId, long Sequence, string Category, string Content,
    decimal? Confidence = null, string? SourceReference = null, string ReviewState = "unreviewed", long? ExpectedVersion = null);
public sealed record SalesMeetingActionItemDraftViewModel(Guid ClientItemId, long Sequence, string Title, string? Details = null,
    string? OwnerLabel = null, DateTime? DueUtc = null, string Status = "open", decimal? Confidence = null,
    string? SourceReference = null, string ReviewState = "unreviewed", long? ExpectedVersion = null);
public sealed record AutosaveSalesMeetingCaptureViewModel(Guid BatchId, long ExpectedCaptureVersion,
    IReadOnlyList<SalesMeetingTranscriptSegmentDraftViewModel>? TranscriptSegments = null,
    IReadOnlyList<SalesMeetingObservationDraftViewModel>? Observations = null,
    IReadOnlyList<SalesMeetingActionItemDraftViewModel>? ActionItems = null);
public sealed record AskSalesMeetingQuestionViewModel(Guid ClientQuestionId, long Sequence, Guid AgentId,
    string Question, string AskerType = "host", string? AskerLabel = null, string InputSource = "typed");
public sealed record SalesMeetingTranscriptSegmentViewModel(Guid Id, Guid ClientItemId, long Sequence, string SpeakerType,
    string? SpeakerLabel, string InputSource, string Content, DateTime StartedUtc, DateTime? EndedUtc,
    decimal? Confidence, string ReviewState, DateTime UpdatedUtc, long Version);
public sealed record SalesMeetingObservationViewModel(Guid Id, Guid ClientItemId, long Sequence, string Category,
    string Content, decimal? Confidence, string? SourceReference, string ReviewState, DateTime UpdatedUtc, long Version);
public sealed record SalesMeetingActionItemViewModel(Guid Id, Guid ClientItemId, long Sequence, string Title,
    string? Details, string? OwnerLabel, DateTime? DueUtc, string Status, decimal? Confidence,
    string? SourceReference, string ReviewState, DateTime UpdatedUtc, long Version);
public sealed record SalesMeetingQuestionEvidenceViewModel(int ClaimOrder, string ClaimText, string ClaimType,
    decimal Confidence, string SourceId, string SourceType, string SourceTitle);
public sealed record SalesMeetingQuestionViewModel(Guid Id, Guid ClientQuestionId, long Sequence, Guid AgentId,
    string Question, string? Answer, string AskerType, string? AskerLabel, string InputSource, Guid? VisibleSlideId,
    long PresentationVersion, string Status, decimal? Confidence, bool FollowUpRequired, string ReviewState,
    string Visibility, Guid? AiRunId, string? FailureCode, string? FailureSummary, DateTime AskedUtc,
    DateTime? AnsweredUtc, DateTime? StageApprovedUtc, DateTime UpdatedUtc, long Version,
    IReadOnlyList<SalesMeetingQuestionEvidenceViewModel> Evidence);
public sealed record SalesMeetingStageAnswerViewModel(Guid QuestionId, long Sequence, string Question, string Answer, DateTime ApprovedUtc);
public sealed record SalesMeetingCaptureSnapshotViewModel(Guid SessionId, long CaptureVersion, Guid? LastBatchId,
    DateTime UpdatedUtc, IReadOnlyList<SalesMeetingTranscriptSegmentViewModel> TranscriptSegments,
    IReadOnlyList<SalesMeetingObservationViewModel> Observations, IReadOnlyList<SalesMeetingActionItemViewModel> ActionItems,
    IReadOnlyList<SalesMeetingQuestionViewModel> Questions);
public sealed record SalesMeetingCaptureSaveResultViewModel(string Disposition, SalesMeetingCaptureSnapshotViewModel Snapshot);
