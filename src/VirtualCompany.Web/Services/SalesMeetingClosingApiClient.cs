using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public sealed class SalesMeetingClosingApiClient(ICompanyApiTransport transport, bool useOfflineMode,
    IApiProblemMessageResolver? problemResolver = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public Task<SalesMeetingClosingSnapshotViewModel?> PrepareAsync(Guid companyId, Guid sessionId, PrepareSalesMeetingClosingViewModel request, CancellationToken ct = default) => SendAsync<SalesMeetingClosingSnapshotViewModel>(companyId, HttpMethod.Post, Base(sessionId) + "/prepare", JsonContent.Create(request), true, ct);
    public Task<SalesMeetingClosingSnapshotViewModel?> RegenerateAsync(Guid companyId, Guid sessionId, PrepareSalesMeetingClosingViewModel request, CancellationToken ct = default) => SendAsync<SalesMeetingClosingSnapshotViewModel>(companyId, HttpMethod.Post, Base(sessionId) + "/regenerate", JsonContent.Create(request), true, ct);
    public async Task<IReadOnlyList<SalesMeetingMinutesViewModel>> ListMinutesAsync(Guid companyId, Guid sessionId, CancellationToken ct = default) => await SendAsync<List<SalesMeetingMinutesViewModel>>(companyId, HttpMethod.Get, Base(sessionId) + "/customer-minutes", null, false, ct) ?? [];
    public Task<SalesMeetingCustomerMinutesPreviewViewModel?> GetCustomerPreviewAsync(Guid companyId, Guid sessionId, Guid minutesId, CancellationToken ct = default) => SendAsync<SalesMeetingCustomerMinutesPreviewViewModel>(companyId, HttpMethod.Get, Base(sessionId) + $"/customer-preview/{minutesId:D}", null, true, ct);
    public Task<SalesMeetingMinutesViewModel?> EditMinutesAsync(Guid companyId, Guid sessionId, Guid minutesId, EditSalesMeetingMinutesViewModel request, CancellationToken ct = default) => SendAsync<SalesMeetingMinutesViewModel>(companyId, HttpMethod.Put, Base(sessionId) + $"/customer-minutes/{minutesId:D}", JsonContent.Create(request), true, ct);
    public Task<SalesMeetingMinutesViewModel?> SubmitMinutesAsync(Guid companyId, Guid sessionId, Guid minutesId, long version, CancellationToken ct = default) => ReviewMinutesAsync(companyId, sessionId, minutesId, version, "submit-review", ct);
    public Task<SalesMeetingMinutesViewModel?> ApproveMinutesAsync(Guid companyId, Guid sessionId, Guid minutesId, long version, CancellationToken ct = default) => ReviewMinutesAsync(companyId, sessionId, minutesId, version, "approve", ct);
    public Task<SalesMeetingInternalIntelligenceViewModel?> GetInternalAsync(Guid companyId, Guid sessionId, Guid id, CancellationToken ct = default) => SendAsync<SalesMeetingInternalIntelligenceViewModel>(companyId, HttpMethod.Get, Base(sessionId) + $"/internal-intelligence/{id:D}", null, true, ct);
    public Task<SalesMeetingInternalIntelligenceViewModel?> EditInternalAsync(Guid companyId, Guid sessionId, Guid id, EditSalesMeetingInternalIntelligenceViewModel request, CancellationToken ct = default) => SendAsync<SalesMeetingInternalIntelligenceViewModel>(companyId, HttpMethod.Put, Base(sessionId) + $"/internal-intelligence/{id:D}", JsonContent.Create(request), true, ct);
    public Task<SalesMeetingInternalIntelligenceViewModel?> SubmitInternalAsync(Guid companyId, Guid sessionId, Guid id, long version, CancellationToken ct = default) => ReviewInternalAsync(companyId, sessionId, id, version, "submit-review", ct);
    public Task<SalesMeetingInternalIntelligenceViewModel?> ApproveInternalAsync(Guid companyId, Guid sessionId, Guid id, long version, CancellationToken ct = default) => ReviewInternalAsync(companyId, sessionId, id, version, "approve", ct);
    public Task<SalesMeetingSessionViewModel?> CompleteAsync(Guid companyId, Guid sessionId, CompleteSalesMeetingClosingViewModel request, CancellationToken ct = default) => SendAsync<SalesMeetingSessionViewModel>(companyId, HttpMethod.Post, Base(sessionId) + "/complete", JsonContent.Create(request), true, ct);

    private Task<SalesMeetingMinutesViewModel?> ReviewMinutesAsync(Guid companyId, Guid sessionId, Guid id, long version, string action, CancellationToken ct) => SendAsync<SalesMeetingMinutesViewModel>(companyId, HttpMethod.Post, Base(sessionId) + $"/customer-minutes/{id:D}/{action}", JsonContent.Create(new { expectedVersion = version }), true, ct);
    private Task<SalesMeetingInternalIntelligenceViewModel?> ReviewInternalAsync(Guid companyId, Guid sessionId, Guid id, long version, string action, CancellationToken ct) => SendAsync<SalesMeetingInternalIntelligenceViewModel>(companyId, HttpMethod.Post, Base(sessionId) + $"/internal-intelligence/{id:D}/{action}", JsonContent.Create(new { expectedVersion = version }), true, ct);
    private async Task<T?> SendAsync<T>(Guid companyId, HttpMethod method, string uri, HttpContent? content, bool allowNotFound, CancellationToken ct)
    {
        if (useOfflineMode) throw new SalesMeetingClosingApiException("Meeting closing needs the backend API. Start the API before preparing closing artifacts.");
        try
        {
            using var response = await transport.SendAsync(companyId, method, uri, content, ct);
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound) return default;
            if (!response.IsSuccessStatusCode) throw await CreateExceptionAsync(response, ct);
            return await response.Content.ReadFromJsonAsync<T>(Json, ct);
        }
        catch (HttpRequestException) { throw new SalesMeetingClosingApiException("The meeting closing service could not be reached."); }
    }
    private async Task<SalesMeetingClosingApiException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentType?.MediaType is not ("application/json" or "application/problem+json")) return new($"The meeting closing request failed with status code {(int)response.StatusCode}.", response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblemResponse>(Json, ct); var fallback = problem?.Errors?.SelectMany(x => x.Value).FirstOrDefault() ?? problem?.Detail ?? problem?.Title ?? "The meeting closing request failed.";
        return new(problemResolver?.Resolve(problem, fallback) ?? fallback, response.StatusCode, problem?.Errors);
    }
    private static string Base(Guid sessionId) => $"api/sales/meeting-sessions/{sessionId:D}/closing";
}

public sealed class SalesMeetingClosingApiException(string message, HttpStatusCode? statusCode = null, IReadOnlyDictionary<string, string[]>? errors = null) : Exception(message)
{ public HttpStatusCode? StatusCode { get; } = statusCode; public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors; }
public sealed record PrepareSalesMeetingClosingViewModel(Guid GenerationRequestId, Guid AgentId, long ExpectedSessionVersion, long ExpectedCaptureVersion, Guid? CaptureCheckpointId, DateTime? ProposedNextMeetingUtc = null, IReadOnlyList<string>? ProposedDealChanges = null);
public sealed record EditSalesMeetingMinutesItemViewModel(int Order, string Type, string Content, string? OwnerLabel, DateTime? DueUtc, string SourceId, Guid? SourceArtifactId, bool RequiresReview);
public sealed record EditSalesMeetingMinutesViewModel(long ExpectedVersion, IReadOnlyList<EditSalesMeetingMinutesItemViewModel> Items);
public sealed record EditSalesMeetingInternalItemViewModel(int Order, string Type, string Content, decimal? Confidence, string SourceId, Guid? SourceArtifactId, bool RequiresReview);
public sealed record EditSalesMeetingInternalIntelligenceViewModel(long ExpectedVersion, IReadOnlyList<EditSalesMeetingInternalItemViewModel> Items);
public sealed record CompleteSalesMeetingClosingViewModel(long ExpectedSessionVersion, long ExpectedCaptureVersion, Guid? CaptureCheckpointId, Guid MinutesId, int MinutesArtifactVersion, Guid InternalIntelligenceId, int InternalArtifactVersion);
public sealed record SalesMeetingMinutesItemViewModel(Guid Id, int Order, string Type, string Content, string? OwnerLabel, DateTime? DueUtc, string SourceId, Guid? SourceArtifactId, bool RequiresReview);
public sealed record SalesMeetingMinutesViewModel(Guid Id, Guid SessionId, Guid? PreviousVersionId, int ArtifactVersion, string Status, long EvidenceCaptureVersion, DateTime EvidenceCutoffUtc, Guid GeneratorAgentId, Guid? AiRunId, string GeneratorVersion, string PromptVersion, DateTime RetentionUntilUtc, DateTime? ReviewedUtc, DateTime? ApprovedUtc, DateTime UpdatedUtc, long Version, bool IsEvidenceStale, DateTime? EvidenceStaleUtc, string? EvidenceStaleReason, IReadOnlyList<SalesMeetingMinutesItemViewModel> Items);
public sealed record SalesMeetingCustomerMinutesPreviewItemViewModel(int Order, string Type, string Content, string? OwnerLabel, DateTime? DueUtc);
public sealed record SalesMeetingCustomerMinutesPreviewViewModel(Guid Id, Guid SessionId, int ArtifactVersion, string Status, DateTime? ApprovedUtc, IReadOnlyList<SalesMeetingCustomerMinutesPreviewItemViewModel> Items);
public sealed record SalesMeetingInternalIntelligenceItemViewModel(Guid Id, int Order, string Type, string Content, decimal? Confidence, string SourceId, Guid? SourceArtifactId, bool RequiresReview);
public sealed record SalesMeetingInternalIntelligenceViewModel(Guid Id, Guid SessionId, Guid MinutesId, int ArtifactVersion, string Status, long EvidenceCaptureVersion, DateTime EvidenceCutoffUtc, Guid GeneratorAgentId, Guid? AiRunId, string GeneratorVersion, string PromptVersion, DateTime RetentionUntilUtc, DateTime? ReviewedUtc, DateTime? ApprovedUtc, DateTime UpdatedUtc, long Version, bool IsEvidenceStale, DateTime? EvidenceStaleUtc, string? EvidenceStaleReason, IReadOnlyList<SalesMeetingInternalIntelligenceItemViewModel> Items);
public sealed record SalesMeetingClosingSnapshotViewModel(SalesMeetingMinutesViewModel CustomerMinutes, SalesMeetingInternalIntelligenceViewModel InternalIntelligence);
