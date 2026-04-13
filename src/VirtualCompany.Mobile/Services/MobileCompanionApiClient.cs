using System.Net.Http.Json;
using System.Text.Json;
using VirtualCompany.Shared.Mobile;

namespace VirtualCompany.Mobile.Services;

public sealed class MobileCompanionApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;

    public MobileCompanionApiClient(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public Uri? BaseAddress
    {
        get => httpClient.BaseAddress;
        set => httpClient.BaseAddress = value;
    }

    public void SetDevelopmentIdentity(string? subject, string? email, string? displayName)
    {
        RemoveDevelopmentHeaders();

        if (string.IsNullOrWhiteSpace(subject))
        {
            return;
        }

        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Dev-Auth-Subject", subject);
        if (!string.IsNullOrWhiteSpace(email))
        {
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Dev-Auth-Email", email);
        }

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Dev-Auth-DisplayName", displayName);
        }

        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Dev-Auth-Provider", "dev-header");
    }

    public void SetCompanyContext(Guid? companyId)
    {
        httpClient.DefaultRequestHeaders.Remove("X-Company-Id");
        if (companyId is Guid value)
        {
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Company-Id", value.ToString());
        }
    }

    public Task<CurrentUserContextDto> GetCurrentUserAsync(CancellationToken cancellationToken = default) =>
        GetAsync<CurrentUserContextDto>(MobileBackendApiRoutes.AuthMe, cancellationToken);

    public Task<CompanySelectionDto> SelectCompanyAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        SendAsync<CompanySelectionDto>(
            HttpMethod.Post,
            MobileBackendApiRoutes.SelectCompany,
            new SelectCompanyRequestDto { CompanyId = companyId },
            cancellationToken);

    public Task<MobileInboxDto> GetInboxAsync(Guid companyId, DateTime? sinceUtc = null, CancellationToken cancellationToken = default) =>
        GetAsync<MobileInboxDto>(MobileBackendApiRoutes.CompactInbox(companyId, take: 30, sinceUtc), cancellationToken);

    public Task<MobileHomeSummaryResponse> GetMobileSummaryAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        GetAsync<MobileHomeSummaryResponse>(MobileBackendApiRoutes.MobileSummary(companyId), cancellationToken);

    public Task<MobileBriefingDto> GetLatestBriefingsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        GetAsync<MobileBriefingDto>(MobileBackendApiRoutes.CompactLatestBriefing(companyId), cancellationToken);

    public Task<List<CompanyAgentSummaryDto>> GetAgentsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        GetAsync<List<CompanyAgentSummaryDto>>(MobileBackendApiRoutes.Agents(companyId), cancellationToken);

    public Task<ApprovalDecisionResultDto> DecideApprovalAsync(
        Guid companyId,
        Guid approvalId,
        ApprovalDecisionCommandDto request,
        CancellationToken cancellationToken = default) =>
        SendAsync<ApprovalDecisionResultDto>(
            HttpMethod.Post,
            MobileBackendApiRoutes.ApprovalDecision(companyId, approvalId),
            request,
            cancellationToken);

    public Task<NotificationListItemDto> SetNotificationStatusAsync(
        Guid companyId,
        Guid notificationId,
        string status,
        CancellationToken cancellationToken = default) =>
        SendAsync<NotificationListItemDto>(
            HttpMethod.Patch,
            MobileBackendApiRoutes.NotificationStatus(companyId, notificationId),
            new SetNotificationStatusCommandDto { Status = status },
            cancellationToken);

    public Task<DirectConversationDto> OpenConversationAsync(
        Guid companyId,
        Guid agentId,
        CancellationToken cancellationToken = default) =>
        SendAsync<DirectConversationDto>(
            HttpMethod.Post,
            MobileBackendApiRoutes.OpenDirectConversation(companyId, agentId),
            new OpenDirectAgentConversationCommandDto { AgentId = agentId },
            cancellationToken);

    public Task<MobileConversationPageDto> GetDirectConversationsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default) =>
        GetAsync<MobileConversationPageDto>(MobileBackendApiRoutes.CompactDirectConversations(companyId, skip: 0, take: 25), cancellationToken);

    public Task<MobileMessagePageDto> GetMessagesAsync(
        Guid companyId,
        Guid conversationId,
        CancellationToken cancellationToken = default) =>
        GetAsync<MobileMessagePageDto>(MobileBackendApiRoutes.CompactConversationMessages(companyId, conversationId, skip: 0, take: 50), cancellationToken);

    public Task<SendDirectAgentMessageResultDto> SendMessageAsync(
        Guid companyId,
        Guid conversationId,
        string body,
        Guid clientRequestId,
        CancellationToken cancellationToken = default) =>
        SendAsync<SendDirectAgentMessageResultDto>(
            HttpMethod.Post,
            MobileBackendApiRoutes.SendConversationMessage(companyId, conversationId),
            new SendDirectAgentMessageCommandDto { Body = body, ClientRequestId = clientRequestId },
            cancellationToken);

    private async Task<T> GetAsync<T>(string uri, CancellationToken cancellationToken)
    {
        EnsureBaseAddress();

        try
        {
            using var response = await httpClient.GetAsync(uri, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await ReadAsync<T>(response, cancellationToken);
            }

            throw await CreateExceptionAsync(response, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw CreateNetworkException(ex);
        }
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string uri, object payload, CancellationToken cancellationToken)
    {
        EnsureBaseAddress();

        try
        {
            using var request = new HttpRequestMessage(method, uri)
            {
                Content = JsonContent.Create(payload, options: SerializerOptions)
            };
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await ReadAsync<T>(response, cancellationToken);
            }

            throw await CreateExceptionAsync(response, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw CreateNetworkException(ex);
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var result = await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
        return result ?? throw new MobileCompanionApiException("The server returned an empty response.");
    }

    private async Task<MobileCompanionApiException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (!string.Equals(contentType, "application/json", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(contentType, "application/problem+json", StringComparison.OrdinalIgnoreCase))
        {
            return new MobileCompanionApiException($"The request failed with status code {(int)response.StatusCode}.");
        }

        var problem = await response.Content.ReadFromJsonAsync<ApiProblemResponse>(SerializerOptions, cancellationToken);
        if (problem?.Errors is { Count: > 0 } errors)
        {
            var errorText = string.Join(" ", errors.Values.SelectMany(values => values).Where(value => !string.IsNullOrWhiteSpace(value)));
            var summary = problem.Detail ?? problem.Title;
            return new MobileCompanionApiException(
                string.IsNullOrWhiteSpace(summary)
                    ? errorText
                    : string.IsNullOrWhiteSpace(errorText) ? summary : $"{summary} {errorText}");
        }

        return new MobileCompanionApiException(problem?.Detail ?? problem?.Title ?? $"The request failed with status code {(int)response.StatusCode}.");
    }

    private MobileCompanionApiException CreateNetworkException(HttpRequestException ex)
    {
        var baseAddress = httpClient.BaseAddress?.ToString().TrimEnd('/') ?? "the configured API";
        return new MobileCompanionApiException($"The mobile app could not reach the backend API at {baseAddress}. Check the API URL and network access.");
    }

    private void EnsureBaseAddress()
    {
        if (httpClient.BaseAddress is null)
        {
            throw new MobileCompanionApiException("Enter an API base URL before calling the backend.");
        }
    }

    private void RemoveDevelopmentHeaders()
    {
        httpClient.DefaultRequestHeaders.Remove("X-Dev-Auth-Subject");
        httpClient.DefaultRequestHeaders.Remove("X-Dev-Auth-Email");
        httpClient.DefaultRequestHeaders.Remove("X-Dev-Auth-DisplayName");
        httpClient.DefaultRequestHeaders.Remove("X-Dev-Auth-Provider");
    }

    private sealed class ApiProblemResponse
    {
        public string? Title { get; set; }
        public string? Detail { get; set; }
        public Dictionary<string, string[]>? Errors { get; set; }
    }
}

public sealed class MobileCompanionApiException : Exception
{
    public MobileCompanionApiException(string message)
        : base(message)
    {
    }
}
