using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public sealed class SalesPresentationPreparationApiClient(
    ICompanyApiTransport transport,
    bool useOfflineMode,
    IApiProblemMessageResolver? problemResolver = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SalesPresentationPreparationViewModel?> GetAsync(
        Guid companyId,
        Guid invitationId,
        CancellationToken cancellationToken = default)
    {
        if (useOfflineMode)
        {
            throw new SalesPresentationPreparationApiException(
                "Presentation preparation needs the backend API. Start the API project before opening live tenant data.");
        }

        try
        {
            using var response = await transport.SendAsync(
                companyId, HttpMethod.Get,
                $"api/sales/meeting-invitations/{invitationId:D}/presentation-preparation",
                null, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode)
                throw await CreateExceptionAsync(response, cancellationToken);
            return await response.Content.ReadFromJsonAsync<SalesPresentationPreparationViewModel>(
                Json, cancellationToken)
                ?? throw new SalesPresentationPreparationApiException(
                    "The presentation preparation API returned an empty response.");
        }
        catch (HttpRequestException)
        {
            throw new SalesPresentationPreparationApiException(
                "The presentation preparation workspace could not reach the backend API.");
        }
    }

    private async Task<SalesPresentationPreparationApiException> CreateExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentType?.MediaType is not ("application/json" or "application/problem+json"))
        {
            return new SalesPresentationPreparationApiException(
                $"The presentation preparation request failed with status code {(int)response.StatusCode}.",
                response.StatusCode);
        }

        var problem = await response.Content.ReadFromJsonAsync<ApiProblemResponse>(Json, cancellationToken);
        var fallback = problem?.Errors?.SelectMany(pair => pair.Value).FirstOrDefault()
            ?? problem?.Detail
            ?? problem?.Title
            ?? "The presentation preparation request failed.";
        return new SalesPresentationPreparationApiException(
            problemResolver?.Resolve(problem, fallback) ?? fallback,
            response.StatusCode);
    }
}

public sealed class SalesPresentationPreparationApiException(
    string message,
    HttpStatusCode? statusCode = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}

public sealed record SalesPresentationPreparationViewModel(
    Guid CompanyId, Guid InvitationId, Guid LeadId, string LeadTitle,
    string? CustomerCompanyName, Guid? MeetingSessionId, long MaximumUploadBytes,
    SalesPresentationPreparationInvitationViewModel Invitation,
    SalesMeetingSessionViewModel? Session,
    IReadOnlyList<SalesPresentationPreparationAgentViewModel> EligibleAgents,
    IReadOnlyList<SalesPresentationPreparationDeckViewModel> Decks,
    Guid? ActiveDeckId, int ActiveDeckSlideCount, string ReadinessState,
    bool CanOpenPresenter,
    IReadOnlyList<SalesPresentationPreparationBlockerViewModel> BlockingReasons,
    IReadOnlyList<string> AllowedActions);

public sealed record SalesPresentationPreparationInvitationViewModel(
    Guid Id, Guid LeadId, Guid? DealId, Guid? ContactId, string Title,
    DateTime StartsUtc, DateTime EndsUtc, string TimeZoneId, string? Location,
    bool CreateOnlineMeeting, string Provider, string Status,
    bool HasProviderEvent, bool IsEligibleForSessionCreation);

public sealed record SalesPresentationPreparationAgentViewModel(
    Guid Id, string DisplayName, string RoleName, string TemplateId,
    string Department, string Status, string? AvatarUrl);

public sealed record SalesPresentationPreparationDeckViewModel(
    Guid Id, Guid AgentId, int Version, string Title, string OriginalFileName,
    string Status, int ProcessingVersion, int ProcessingAttemptCount, int SlideCount,
    string? FailureCode, string? FailureSummary, bool CanRetry, bool IsActive,
    DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? ProcessedUtc,
    DateTime? FailedUtc, DateTime? ActivatedUtc, long ConcurrencyVersion);

public sealed record SalesPresentationPreparationBlockerViewModel(
    string Code, string Explanation);
