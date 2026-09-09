using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public sealed class SalesMeetingSessionApiClient(
    ICompanyApiTransport transport,
    bool useOfflineMode,
    IApiProblemMessageResolver? problemResolver = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<SalesMeetingSessionViewModel?> GetByInvitationAsync(
        Guid companyId,
        Guid invitationId,
        CancellationToken cancellationToken = default) =>
        SendAsync<SalesMeetingSessionViewModel>(
            companyId,
            HttpMethod.Get,
            $"api/sales/meeting-invitations/{invitationId:D}/session",
            null,
            allowNotFound: true,
            cancellationToken);

    public Task<SalesMeetingSessionViewModel?> GetAsync(
        Guid companyId,
        Guid sessionId,
        CancellationToken cancellationToken = default) =>
        SendAsync<SalesMeetingSessionViewModel>(
            companyId,
            HttpMethod.Get,
            $"api/sales/meeting-sessions/{sessionId:D}",
            null,
            allowNotFound: true,
            cancellationToken);

    public async Task<SalesMeetingSessionViewModel> CreateOrUpdateAsync(
        Guid companyId,
        Guid invitationId,
        CreateOrUpdateSalesMeetingSessionViewModel request,
        CancellationToken cancellationToken = default) =>
        await SendAsync<SalesMeetingSessionViewModel>(
            companyId,
            HttpMethod.Put,
            $"api/sales/meeting-invitations/{invitationId:D}/session",
            JsonContent.Create(request, options: Json),
            allowNotFound: false,
            cancellationToken)
        ?? throw new SalesMeetingSessionApiException("The sales meeting API returned an empty response.");

    public async Task<SalesMeetingSessionViewModel> TransitionAsync(
        Guid companyId,
        Guid sessionId,
        TransitionSalesMeetingSessionViewModel request,
        CancellationToken cancellationToken = default) =>
        await SendAsync<SalesMeetingSessionViewModel>(
            companyId,
            HttpMethod.Post,
            $"api/sales/meeting-sessions/{sessionId:D}/transitions",
            JsonContent.Create(request, options: Json),
            allowNotFound: false,
            cancellationToken)
        ?? throw new SalesMeetingSessionApiException("The sales meeting API returned an empty response.");

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
            throw new SalesMeetingSessionApiException(
                "Sales meeting sessions need the backend API. Start the API project before changing live tenant data.");
        }

        try
        {
            using var response = await transport.SendAsync(
                companyId, method, uri, content, cancellationToken);
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound) return default;
            if (!response.IsSuccessStatusCode) throw await CreateExceptionAsync(response, cancellationToken);
            return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken)
                ?? throw new SalesMeetingSessionApiException("The sales meeting API returned an empty response.");
        }
        catch (HttpRequestException)
        {
            throw new SalesMeetingSessionApiException("The sales meeting workspace could not reach the backend API.");
        }
    }

    private async Task<SalesMeetingSessionApiException> CreateExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentType?.MediaType is not ("application/json" or "application/problem+json"))
        {
            return new SalesMeetingSessionApiException(
                $"The sales meeting request failed with status code {(int)response.StatusCode}.",
                response.StatusCode);
        }

        var problem = await response.Content.ReadFromJsonAsync<ApiProblemResponse>(Json, cancellationToken);
        var fallback = problem?.Errors?.SelectMany(pair => pair.Value).FirstOrDefault()
            ?? problem?.Detail
            ?? problem?.Title
            ?? "The sales meeting request failed.";
        return new SalesMeetingSessionApiException(
            problemResolver?.Resolve(problem, fallback) ?? fallback,
            response.StatusCode,
            problem?.Errors,
            problem?.Code);
    }
}

public sealed class SalesMeetingSessionApiException(
    string message,
    HttpStatusCode? statusCode = null,
    IReadOnlyDictionary<string, string[]>? errors = null,
    string? problemCode = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;
    public string? ProblemCode { get; } = problemCode;
}

public sealed record CreateOrUpdateSalesMeetingSessionViewModel(
    string MeetingGoal,
    string IntendedAudience,
    int PlannedDurationMinutes,
    string? DemoScenario,
    string ConsentStatus,
    string RetentionPolicy,
    int RetentionDays,
    long? ExpectedVersion = null);

public sealed record TransitionSalesMeetingSessionViewModel(
    string TargetStatus,
    long ExpectedVersion,
    int? CurrentSlideIndex = null,
    int? CurrentTalkingPointIndex = null,
    string? ResumeMarker = null,
    string? Reason = null);

public sealed record SalesMeetingSessionViewModel(
    Guid Id,
    Guid CompanyId,
    Guid InvitationId,
    Guid LeadId,
    Guid? DealId,
    Guid? ContactId,
    Guid CustomerCompanyId,
    string MeetingGoal,
    string IntendedAudience,
    int PlannedDurationMinutes,
    string? DemoScenario,
    string ProviderMeetingId,
    string Status,
    int CurrentSlideIndex,
    int CurrentTalkingPointIndex,
    string? ResumeMarker,
    string ConsentStatus,
    DateTime? ConsentRecordedUtc,
    Guid? ConsentRecordedByUserId,
    string RetentionPolicy,
    int RetentionDays,
    DateTime RetentionStartsUtc,
    DateTime RetentionUntilUtc,
    string? StatusReason,
    DateTime? EndedUtc,
    Guid CreatedByUserId,
    Guid UpdatedByUserId,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    long ConcurrencyVersion);
