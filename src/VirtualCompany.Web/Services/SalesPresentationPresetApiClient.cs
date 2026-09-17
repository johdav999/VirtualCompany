using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public sealed class SalesPresentationPresetApiClient(ICompanyApiTransport transport, bool offline, IApiProblemMessageResolver problemResolver)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Root = "api/sales/presentation-presets";
    public static string CoverUrl(Guid company, Guid preset, Guid slide) => $"/sales/presentation-preset-cover/{company:D}/{preset:D}/{slide:D}";
    public static string SlideImageUrl(Guid company, Guid preset, Guid version, Guid slide) => $"/sales/presentation-preset-slide/{company:D}/{preset:D}/{version:D}/{slide:D}";

    public async Task<IReadOnlyList<SalesPresentationPresetListItemViewModel>> ListAsync(Guid companyId, string? search = null, bool includeArchived = false, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(search)) query.Add($"search={Uri.EscapeDataString(search.Trim())}");
        if (includeArchived) query.Add("includeArchived=true");
        return await SendAsync<List<SalesPresentationPresetListItemViewModel>>(companyId, HttpMethod.Get, query.Count == 0 ? Root : $"{Root}?{string.Join('&', query)}", null, false, ct) ?? [];
    }

    public Task<SalesPresentationPresetViewModel?> GetAsync(Guid companyId, Guid presetId, CancellationToken ct = default) => SendAsync<SalesPresentationPresetViewModel>(companyId, HttpMethod.Get, $"{Root}/{presetId:D}", null, true, ct);
    public Task<SalesPresentationPresetViewModel> CreateAsync(Guid companyId, SalesPresentationPresetCreateRequest request, CancellationToken ct = default) => RequiredAsync<SalesPresentationPresetViewModel>(companyId, HttpMethod.Post, Root, JsonContent.Create(request, options: Json), ct);
    public Task<SalesPresentationPresetViewModel> UpdateDraftAsync(Guid companyId, Guid presetId, SalesPresentationPresetUpdateRequest request, CancellationToken ct = default) => RequiredAsync<SalesPresentationPresetViewModel>(companyId, HttpMethod.Put, $"{Root}/{presetId:D}/draft", JsonContent.Create(request, options: Json), ct);
    public Task<SalesPresentationPresetViewModel> UpdateSpeakerNotesAsync(Guid companyId, Guid presetId, Guid versionId, Guid slideId, long expectedVersion, string? speakerNotes, CancellationToken ct = default) =>
        RequiredAsync<SalesPresentationPresetViewModel>(companyId, HttpMethod.Put, $"{Root}/{presetId:D}/versions/{versionId:D}/slides/{slideId:D}/speaker-notes", JsonContent.Create(new { expectedVersion, speakerNotes }, options: Json), ct);

    public async Task<SalesPresentationPresetAssetViewModel> ImportAssetAsync(Guid companyId, Guid presetId, Guid versionId, string fileName, string contentType, Stream content, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var file = new StreamContent(content);
        file.Headers.ContentType = new(contentType);
        form.Add(file, "file", fileName);
        return await RequiredAsync<SalesPresentationPresetAssetViewModel>(companyId, HttpMethod.Post, $"{Root}/{presetId:D}/versions/{versionId:D}/asset", form, ct);
    }

    public Task<SalesPresentationPresetAssetViewModel> RetryAssetAsync(Guid companyId, Guid presetId, Guid versionId, CancellationToken ct = default) => RequiredAsync<SalesPresentationPresetAssetViewModel>(companyId, HttpMethod.Post, $"{Root}/{presetId:D}/versions/{versionId:D}/asset/retry", null, ct);
    public Task<SalesPresentationPresetReadinessViewModel?> GetReadinessAsync(Guid companyId, Guid presetId, Guid versionId, CancellationToken ct = default) => SendAsync<SalesPresentationPresetReadinessViewModel>(companyId, HttpMethod.Get, $"{Root}/{presetId:D}/versions/{versionId:D}/readiness", null, true, ct);
    public async Task<IReadOnlyList<SalesPresentationPresetSlideViewModel>> GetSlidesAsync(Guid companyId, Guid presetId, Guid versionId, CancellationToken ct = default) => await SendAsync<List<SalesPresentationPresetSlideViewModel>>(companyId, HttpMethod.Get, $"{Root}/{presetId:D}/versions/{versionId:D}/slides", null, true, ct) ?? [];
    public Task<SalesPresentationPresetViewModel> PublishAsync(Guid companyId, Guid presetId, Guid versionId, long presetVersion, long version, CancellationToken ct = default) => RequiredAsync<SalesPresentationPresetViewModel>(companyId, HttpMethod.Post, $"{Root}/{presetId:D}/versions/{versionId:D}/publish", JsonContent.Create(new { expectedPresetVersion = presetVersion, expectedVersion = version }, options: Json), ct);
    public Task<SalesPresentationPresetViewModel> CreateNextDraftAsync(Guid companyId, Guid presetId, long version, CancellationToken ct = default) => RequiredAsync<SalesPresentationPresetViewModel>(companyId, HttpMethod.Post, $"{Root}/{presetId:D}/versions", JsonContent.Create(new { expectedPresetVersion = version }, options: Json), ct);
    public Task<SalesPresentationPresetViewModel> ArchiveAsync(Guid companyId, Guid presetId, long version, string? rationale, CancellationToken ct = default) => RequiredAsync<SalesPresentationPresetViewModel>(companyId, HttpMethod.Post, $"{Root}/{presetId:D}/archive", JsonContent.Create(new { expectedPresetVersion = version, rationale }, options: Json), ct);

    private async Task<T> RequiredAsync<T>(Guid companyId, HttpMethod method, string uri, HttpContent? content, CancellationToken ct) => await SendAsync<T>(companyId, method, uri, content, false, ct) ?? throw new SalesPresentationPresetApiException("The presentation preset API returned an empty response.");
    private async Task<T?> SendAsync<T>(Guid companyId, HttpMethod method, string uri, HttpContent? content, bool allowNotFound, CancellationToken ct)
    {
        if (offline) throw new SalesPresentationPresetApiException("Presentation presets need the backend API. Start the API project and try again.");
        try
        {
            using var response = await transport.SendAsync(companyId, method, uri, content, ct);
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound) return default;
            if (!response.IsSuccessStatusCode) throw await ExceptionAsync(response, ct);
            return await response.Content.ReadFromJsonAsync<T>(Json, ct);
        }
        catch (HttpRequestException) { throw new SalesPresentationPresetApiException("The presentation preset library could not reach the backend API."); }
    }

    private async Task<SalesPresentationPresetApiException> ExceptionAsync(HttpResponseMessage response, CancellationToken ct)
    {
        ApiProblemResponse? problem = null;
        if (response.Content.Headers.ContentType?.MediaType is "application/json" or "application/problem+json") problem = await response.Content.ReadFromJsonAsync<ApiProblemResponse>(Json, ct);
        var fallback = problem?.Errors?.SelectMany(x => x.Value).FirstOrDefault() ?? problem?.Detail ?? problem?.Title ?? $"The request failed with status code {(int)response.StatusCode}.";
        var message = problemResolver.Resolve(problem, fallback);
        return response.StatusCode == HttpStatusCode.Conflict ? new SalesPresentationPresetConflictApiException(message, problem?.Code) : new SalesPresentationPresetApiException(message, response.StatusCode, problem?.Errors, problem?.Code);
    }
}

public class SalesPresentationPresetApiException(string message, HttpStatusCode? statusCode = null, IReadOnlyDictionary<string, string[]>? errors = null, string? code = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;
    public string? Code { get; } = code;
}
public sealed class SalesPresentationPresetConflictApiException(string message, string? code) : SalesPresentationPresetApiException(message, HttpStatusCode.Conflict, code: code);

public sealed record SalesPresentationPresetCreateRequest(string Name, string? Description, Guid OwnerUserId, Guid? DefaultPresenterAgentId, string Goal, string Audience, int DurationMinutes, string? DemoScenario, string ControlMode, string Language, IReadOnlyCollection<string> AllowedContextTypes, string? RequiredKnowledgeScope, string? BehaviorSettingsJson);
public sealed record SalesPresentationPresetUpdateRequest(long ExpectedPresetVersion, long ExpectedDraftVersion, string Name, string? Description, Guid OwnerUserId, Guid? DefaultPresenterAgentId, string Goal, string Audience, int DurationMinutes, string? DemoScenario, string ControlMode, string Language, IReadOnlyCollection<string> AllowedContextTypes, string? RequiredKnowledgeScope, string? BehaviorSettingsJson);
public sealed record SalesPresentationPresetListItemViewModel(Guid Id, string Name, string? Description, Guid OwnerUserId, string Lifecycle, int? CurrentPublishedVersion, int? CurrentDraftVersion, string? AssetStatus, int SlideCount, int WhereUsedCount, DateTime UpdatedUtc, long ConcurrencyVersion, Guid? CoverSlideId = null);
public sealed record SalesPresentationPresetAssetViewModel(Guid Id, string OriginalFileName, string? ContentType, long FileSizeBytes, string ContentHash, string Status, int ProcessingVersion, int ProcessingAttemptCount, int SlideCount, string? RendererName, string? RendererVersion, string? AnimationHandling, string? FailureCode, string? FailureSummary, bool CanRetry, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? ProcessedUtc, long ConcurrencyVersion);
public sealed record SalesPresentationPresetSlideViewModel(Guid Id, int SlideNumber, string? Title, string ExtractedText, string? SpeakerNotes, string ImageStorageKey, string? ImageStorageUrl, int ImageWidthPixels, int ImageHeightPixels, long SourceWidthEmus, long SourceHeightEmus, string ContentHash, string Objective, string BaselineTalkingPoints, int ExpectedDurationSeconds, string TransitionText, bool AudioNeedsUpdate = false);
public sealed record SalesPresentationPresetVersionViewModel(Guid Id, int VersionNumber, string Lifecycle, Guid? DefaultPresenterAgentId, string? BehaviorSettingsJson, string Goal, string Audience, int DurationMinutes, string? DemoScenario, string ControlMode, string Language, IReadOnlyList<string> AllowedContextTypes, string? RequiredKnowledgeScope, Guid? PublishedByUserId, DateTime? PublishedUtc, DateTime CreatedUtc, DateTime UpdatedUtc, long ConcurrencyVersion, SalesPresentationPresetAssetViewModel? Asset);
public sealed record SalesPresentationPresetViewModel(Guid Id, Guid CompanyId, string Name, string? Description, Guid OwnerUserId, string Lifecycle, Guid? CurrentPublishedVersionId, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? ArchivedUtc, long ConcurrencyVersion, int WhereUsedCount, SalesPresentationPresetVersionViewModel? CurrentDraft, SalesPresentationPresetVersionViewModel? CurrentPublished, IReadOnlyList<SalesPresentationPresetVersionViewModel> Versions, IReadOnlyList<string> AllowedActions);
public sealed record SalesPresentationPresetReadinessBlockerViewModel(string Code, string Explanation, IReadOnlyList<string> CorrectiveActions, bool RequiresReview);
public sealed record SalesPresentationPresetReadinessViewModel(string State, bool IsReady, IReadOnlyList<SalesPresentationPresetReadinessBlockerViewModel> Blockers, IReadOnlyList<string> AllowedActions, bool RequiresReview);
