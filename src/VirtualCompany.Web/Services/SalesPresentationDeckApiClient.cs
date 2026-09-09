using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public sealed class SalesPresentationDeckApiClient(
    ICompanyApiTransport transport,
    bool useOfflineMode,
    IApiProblemMessageResolver? problemResolver = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SalesPresentationDeckViewModel> ImportAsync(
        Guid companyId,
        Guid sessionId,
        Guid agentId,
        string? title,
        string fileName,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(agentId.ToString("D", CultureInfo.InvariantCulture)), "agentId");
        if (!string.IsNullOrWhiteSpace(title)) form.Add(new StringContent(title.Trim()), "title");
        var file = new StreamContent(content);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);

        return await SendAsync<SalesPresentationDeckViewModel>(
            companyId,
            HttpMethod.Post,
            $"api/sales/meeting-sessions/{sessionId:D}/decks",
            form,
            false,
            cancellationToken)
            ?? throw EmptyResponse();
    }

    public async Task<IReadOnlyList<SalesPresentationDeckViewModel>> ListAsync(
        Guid companyId, Guid sessionId, CancellationToken cancellationToken = default) =>
        await SendAsync<List<SalesPresentationDeckViewModel>>(
            companyId, HttpMethod.Get, BasePath(sessionId) + "/decks", null, false, cancellationToken)
        ?? [];

    public Task<SalesPresentationDeckViewModel?> GetAsync(
        Guid companyId, Guid sessionId, Guid deckId, CancellationToken cancellationToken = default) =>
        SendAsync<SalesPresentationDeckViewModel>(
            companyId, HttpMethod.Get, $"{BasePath(sessionId)}/decks/{deckId:D}", null, true, cancellationToken);

    public Task<SalesPresentationSlideViewModel?> GetSlideAsync(
        Guid companyId, Guid sessionId, Guid deckId, int slideNumber,
        CancellationToken cancellationToken = default) =>
        SendAsync<SalesPresentationSlideViewModel>(
            companyId, HttpMethod.Get,
            $"{BasePath(sessionId)}/decks/{deckId:D}/slides/{slideNumber}",
            null, true, cancellationToken);

    public Task<SalesPresentationDeckViewModel?> ActivateAsync(
        Guid companyId, Guid sessionId, Guid deckId, CancellationToken cancellationToken = default) =>
        SendAsync<SalesPresentationDeckViewModel>(
            companyId, HttpMethod.Post,
            $"{BasePath(sessionId)}/decks/{deckId:D}/activate",
            null, true, cancellationToken);

    public Task<SalesPresentationDeckViewModel?> RetryAsync(
        Guid companyId, Guid sessionId, Guid deckId, CancellationToken cancellationToken = default) =>
        SendAsync<SalesPresentationDeckViewModel>(
            companyId, HttpMethod.Post,
            $"{BasePath(sessionId)}/decks/{deckId:D}/retry",
            null, true, cancellationToken);

    public Task<SalesMeetingBriefViewModel?> GetBriefAsync(
        Guid companyId, Guid sessionId, CancellationToken cancellationToken = default) =>
        SendAsync<SalesMeetingBriefViewModel>(
            companyId, HttpMethod.Get, BasePath(sessionId) + "/brief", null, true, cancellationToken);

    public Task<SalesPresentationDeckViewModel?> RegenerateBriefAsync(
        Guid companyId, Guid sessionId, CancellationToken cancellationToken = default) =>
        SendAsync<SalesPresentationDeckViewModel>(
            companyId, HttpMethod.Post, BasePath(sessionId) + "/brief/regenerate", null, true, cancellationToken);

    private async Task<T?> SendAsync<T>(
        Guid companyId,
        HttpMethod method,
        string uri,
        HttpContent? content,
        bool allowNotFound,
        CancellationToken cancellationToken)
    {
        if (useOfflineMode)
        {
            throw new SalesPresentationDeckApiException(
                "Presentation processing needs the backend API. Start the API project before working with meeting decks.");
        }

        try
        {
            using var response = await transport.SendAsync(companyId, method, uri, content, cancellationToken);
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound) return default;
            if (!response.IsSuccessStatusCode) throw await CreateExceptionAsync(response, cancellationToken);
            return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new SalesPresentationDeckApiException("The sales presentation workspace could not reach the backend API.");
        }
    }

    private async Task<SalesPresentationDeckApiException> CreateExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentType?.MediaType is not ("application/json" or "application/problem+json"))
        {
            return new SalesPresentationDeckApiException(
                $"The sales presentation request failed with status code {(int)response.StatusCode}.",
                response.StatusCode);
        }

        var problem = await response.Content.ReadFromJsonAsync<ApiProblemResponse>(Json, cancellationToken);
        var fallback = problem?.Errors?.SelectMany(pair => pair.Value).FirstOrDefault()
            ?? problem?.Detail
            ?? problem?.Title
            ?? "The sales presentation request failed.";
        return new SalesPresentationDeckApiException(
            problemResolver?.Resolve(problem, fallback) ?? fallback,
            response.StatusCode,
            problem?.Errors);
    }

    private static string BasePath(Guid sessionId) => $"api/sales/meeting-sessions/{sessionId:D}";
    private static SalesPresentationDeckApiException EmptyResponse() =>
        new("The sales presentation API returned an empty response.");
}

public sealed class SalesPresentationDeckApiException(
    string message,
    HttpStatusCode? statusCode = null,
    IReadOnlyDictionary<string, string[]>? errors = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;
}

public sealed record SalesPresentationArtifactViewModel(
    Guid Id,
    Guid? SlideId,
    int ArtifactVersion,
    string ArtifactType,
    string Section,
    int Order,
    string Content,
    string Classification,
    string? SourceId,
    Guid? AiRunId,
    DateTime CreatedUtc);

public sealed record SalesPresentationSlideViewModel(
    Guid Id,
    int ProcessingVersion,
    int SlideNumber,
    string? Title,
    string ExtractedText,
    string? SpeakerNotes,
    string ImageStorageKey,
    string? ImageStorageUrl,
    int ImageWidthPixels,
    int ImageHeightPixels,
    long SourceWidthEmus,
    long SourceHeightEmus,
    string ContentHash,
    string Objective,
    int ExpectedDurationSeconds,
    string TransitionText,
    string Status,
    IReadOnlyList<SalesPresentationArtifactViewModel> PlanArtifacts);

public sealed record SalesPresentationDeckViewModel(
    Guid Id,
    Guid CompanyId,
    Guid SessionId,
    Guid AgentId,
    int Version,
    string Title,
    string OriginalFileName,
    string? ContentType,
    long FileSizeBytes,
    string ContentHash,
    string StorageKey,
    string? StorageUrl,
    string Status,
    int ProcessingVersion,
    int ProcessingAttemptCount,
    int SlideCount,
    string? RendererName,
    string? RendererVersion,
    string? AnimationHandling,
    string? FailureCode,
    string? FailureSummary,
    bool CanRetry,
    bool IsActive,
    int BriefVersion,
    bool BriefRegenerationPending,
    Guid UploadedByUserId,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    DateTime? ProcessingStartedUtc,
    DateTime? ProcessedUtc,
    DateTime? FailedUtc,
    DateTime? ActivatedUtc,
    long ConcurrencyVersion,
    IReadOnlyList<SalesPresentationSlideViewModel> Slides);

public sealed record SalesMeetingBriefViewModel(
    Guid SessionId,
    Guid DeckId,
    int Version,
    bool RequiresReview,
    IReadOnlyList<SalesPresentationArtifactViewModel> Items);
