using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public sealed class SalesMeetingTranscriptReconciliationApiClient(
    ICompanyApiTransport transport,
    bool useOfflineMode,
    IApiProblemMessageResolver? problemResolver = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<SalesMeetingTranscriptReconciliationStatusViewModel?> GetAsync(Guid companyId, Guid sessionId,
        CancellationToken cancellationToken = default) => SendAsync<SalesMeetingTranscriptReconciliationStatusViewModel>(
            companyId, HttpMethod.Get, Base(sessionId), null, cancellationToken);

    public Task<SalesMeetingTranscriptSubscriptionViewModel?> EnableAsync(Guid companyId, Guid sessionId,
        Guid? calendarConnectionId = null, CancellationToken cancellationToken = default) =>
        SendAsync<SalesMeetingTranscriptSubscriptionViewModel>(companyId, HttpMethod.Post,
            Base(sessionId) + "/subscription", JsonContent.Create(new { calendarConnectionId }), cancellationToken);

    public Task<SalesMeetingTranscriptSubscriptionViewModel?> RenewAsync(Guid companyId, Guid sessionId,
        Guid subscriptionId, CancellationToken cancellationToken = default) =>
        SendAsync<SalesMeetingTranscriptSubscriptionViewModel>(companyId, HttpMethod.Post,
            Base(sessionId) + $"/subscription/{subscriptionId:D}/renew", JsonContent.Create(new { }), cancellationToken);

    private async Task<T?> SendAsync<T>(Guid companyId, HttpMethod method, string uri, HttpContent? content,
        CancellationToken cancellationToken)
    {
        if (useOfflineMode) throw new SalesMeetingTranscriptApiException("Transcript reconciliation needs the backend API. Start the API to review Microsoft Graph transcript status.");
        try
        {
            using var response = await transport.SendAsync(companyId, method, uri, content, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound) return default;
            if (!response.IsSuccessStatusCode) throw await CreateExceptionAsync(response, cancellationToken);
            return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new SalesMeetingTranscriptApiException("The meeting transcript reconciliation service could not be reached.");
        }
    }

    private async Task<SalesMeetingTranscriptApiException> CreateExceptionAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentType?.MediaType is not ("application/json" or "application/problem+json"))
            return new($"The transcript reconciliation request failed with status code {(int)response.StatusCode}.", response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblemResponse>(Json, cancellationToken);
        var fallback = problem?.Errors?.SelectMany(x => x.Value).FirstOrDefault() ?? problem?.Detail ?? problem?.Title ?? "The transcript reconciliation request failed.";
        return new(problemResolver?.Resolve(problem, fallback) ?? fallback, response.StatusCode);
    }

    private static string Base(Guid sessionId) => $"api/sales/meeting-sessions/{sessionId:D}/transcript-reconciliation";
}

public sealed class SalesMeetingTranscriptApiException(string message, HttpStatusCode? statusCode = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}

public sealed record SalesMeetingTranscriptSubscriptionViewModel(Guid Id, Guid SessionId, Guid CalendarConnectionId,
    string Provider, string Status, DateTime ExpiresUtc, DateTime RetentionUntilUtc, DateTime? LastNotificationUtc,
    DateTime? LastRenewedUtc, int RenewalAttemptCount, int AuthenticityFailureCount,
    string? LastErrorCode, string? LastErrorSummary, long Version);
public sealed record SalesMeetingTranscriptIngestionViewModel(Guid Id, string Status, string ProviderTranscriptId,
    string ProviderVersion, DateTime ReceivedUtc, int AttemptCount, int EquivalentCount, int AddedCount,
    int SpeakerCorrectionCount, int ConflictCount, bool MateriallyChanged, string? FailureCode,
    string? FailureSummary, DateTime? CompletedUtc);
public sealed record SalesMeetingTranscriptConflictViewModel(Guid Id, Guid TranscriptSegmentId,
    string ProviderSegmentId, string? ExistingContent, string ProviderContent,
    string? ExistingSpeakerLabel, string? ProviderSpeakerLabel,
    string Summary, DateTime CreatedUtc);
public sealed record SalesMeetingTranscriptReconciliationStatusViewModel(Guid SessionId,
    long ReconciliationVersion, bool HasStaleClosingArtifacts, int PendingCount, int ConflictCount,
    IReadOnlyList<SalesMeetingTranscriptSubscriptionViewModel> Subscriptions,
    IReadOnlyList<SalesMeetingTranscriptIngestionViewModel> Ingestions,
    IReadOnlyList<SalesMeetingTranscriptConflictViewModel> Conflicts);
