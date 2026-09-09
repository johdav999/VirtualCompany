using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public sealed class SalesMeetingRealtimeApiClient(
    ICompanyApiTransport transport,
    bool useOfflineMode,
    IApiProblemMessageResolver? problemResolver = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<SalesMeetingRealtimeStatusViewModel?> GetStatusAsync(Guid companyId, Guid sessionId, CancellationToken cancellationToken = default) =>
        SendAsync<SalesMeetingRealtimeStatusViewModel>(companyId, HttpMethod.Get, BasePath(sessionId) + "/status", null, true, cancellationToken);

    public Task<SalesMeetingRealtimeStartResultViewModel?> StartAsync(Guid companyId, Guid sessionId,
        StartSalesMeetingRealtimeViewModel request, CancellationToken cancellationToken = default) =>
        SendAsync<SalesMeetingRealtimeStartResultViewModel>(companyId, HttpMethod.Post, BasePath(sessionId) + "/sessions",
            JsonContent.Create(request), true, cancellationToken);

    public Task<SalesMeetingRealtimeEventResultViewModel?> SubmitEventAsync(Guid companyId, Guid sessionId,
        SubmitSalesMeetingRealtimeEventViewModel request, CancellationToken cancellationToken = default) =>
        SendAsync<SalesMeetingRealtimeEventResultViewModel>(companyId, HttpMethod.Post, BasePath(sessionId) + "/events",
            JsonContent.Create(request), true, cancellationToken);

    public Task<SalesMeetingRealtimeCancelResultViewModel?> CancelResponseAsync(Guid companyId, Guid sessionId,
        CancelSalesMeetingRealtimeResponseViewModel request, CancellationToken cancellationToken = default) =>
        SendAsync<SalesMeetingRealtimeCancelResultViewModel>(companyId, HttpMethod.Post, BasePath(sessionId) + "/responses/cancel",
            JsonContent.Create(request), true, cancellationToken);

    public Task<SalesMeetingRealtimeStatusViewModel?> StopAsync(Guid companyId, Guid sessionId, Guid voiceSessionId,
        long expectedVersion, string reason = "ended", CancellationToken cancellationToken = default) =>
        SendAsync<SalesMeetingRealtimeStatusViewModel>(companyId, HttpMethod.Post,
            BasePath(sessionId) + $"/sessions/{voiceSessionId:D}/stop", JsonContent.Create(new { expectedVersion, reason }), true, cancellationToken);

    public Task<SalesMeetingRealtimeStatusViewModel?> RevokeConsentAsync(Guid companyId, Guid sessionId, Guid voiceSessionId,
        long expectedVersion, CancellationToken cancellationToken = default) =>
        SendAsync<SalesMeetingRealtimeStatusViewModel>(companyId, HttpMethod.Post,
            BasePath(sessionId) + $"/sessions/{voiceSessionId:D}/revoke-consent", JsonContent.Create(new { expectedVersion, reason = "consent_revoked" }), true, cancellationToken);

    private async Task<T?> SendAsync<T>(Guid companyId, HttpMethod method, string uri, HttpContent? content,
        bool allowNotFound, CancellationToken cancellationToken)
    {
        if (useOfflineMode) throw new SalesMeetingRealtimeApiException("The voice pilot needs the backend API. Typed meeting controls remain available.");
        try
        {
            using var response = await transport.SendAsync(companyId, method, uri, content, cancellationToken);
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound) return default;
            if (!response.IsSuccessStatusCode) throw await CreateExceptionAsync(response, cancellationToken);
            return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new SalesMeetingRealtimeApiException("The voice pilot could not be reached. Typed meeting controls remain available.");
        }
    }

    private async Task<SalesMeetingRealtimeApiException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentType?.MediaType is not ("application/json" or "application/problem+json"))
            return new($"The voice request failed with status code {(int)response.StatusCode}. Typed meeting controls remain available.", response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblemResponse>(Json, cancellationToken);
        var fallback = problem?.Errors?.SelectMany(x => x.Value).FirstOrDefault() ?? problem?.Detail ?? problem?.Title ??
            "The voice request failed. Typed meeting controls remain available.";
        return new(problemResolver?.Resolve(problem, fallback) ?? fallback, response.StatusCode, problem?.Errors);
    }

    private static string BasePath(Guid sessionId) => $"api/sales/meeting-sessions/{sessionId:D}/voice";
}

public sealed class SalesMeetingRealtimeApiException(string message, HttpStatusCode? statusCode = null,
    IReadOnlyDictionary<string, string[]>? errors = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;
}

public sealed record StartSalesMeetingRealtimeViewModel(Guid AgentId, string OfferSdp);
public sealed record SubmitSalesMeetingRealtimeEventViewModel(Guid VoiceSessionId, string EventId, long Sequence, string PayloadJson);
public sealed record CancelSalesMeetingRealtimeResponseViewModel(Guid VoiceSessionId, string? ResponseId);
public sealed record SalesMeetingRealtimeStatusViewModel(Guid? VoiceSessionId, Guid MeetingSessionId,
    bool FeatureEnabled, bool ConsentGranted, bool ProviderConfigured, bool MediaRouteApproved, bool VoiceAvailable,
    string State, string DegradedStatus, string? Provider, string? Model, string? MediaRoute, DateTime? ExpiresUtc,
    int ReconnectCount, int AudioDurationSeconds, int InputTokens, int OutputTokens, string? LastErrorCode,
    string? LastErrorSummary, long Version);
public sealed record SalesMeetingRealtimeStartResultViewModel(SalesMeetingRealtimeStatusViewModel Status, string AnswerSdp);
public sealed record SalesMeetingRealtimeEventResultViewModel(SalesMeetingRealtimeStatusViewModel Status,
    bool Duplicate, bool IgnoredAsReordered, string Outcome, Guid? QuestionId, string? ToolResultJson);
public sealed record SalesMeetingRealtimeCancelResultViewModel(SalesMeetingRealtimeStatusViewModel Status, string? ClientEventJson);
