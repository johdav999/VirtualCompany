using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VirtualCompany.Web.Services;

public sealed class DirectChatApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly bool _useOfflineMode;

    public DirectChatApiClient(HttpClient httpClient, bool useOfflineMode = false)
    {
        _httpClient = httpClient;
        _useOfflineMode = useOfflineMode;
    }

    public Task<DirectConversationViewModel> OpenDirectConversationAsync(Guid companyId, Guid agentId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? throw new OnboardingApiException("Direct chat requires the backend API.")
            : SendAsync<DirectConversationViewModel>(HttpMethod.Post, $"api/companies/{companyId}/agents/{agentId}/conversations/direct", new { agentId }, cancellationToken);

    public Task<DirectConversationPageViewModel> GetDirectConversationsAsync(Guid companyId, int skip = 0, int take = 50, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? throw new OnboardingApiException("Direct chat requires the backend API.")
            : GetAsync<DirectConversationPageViewModel>($"api/companies/{companyId}/conversations/direct?skip={skip}&take={take}", cancellationToken);

    public Task<ChatMessagePageViewModel> GetMessagesAsync(Guid companyId, Guid conversationId, int skip = 0, int take = 50, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? throw new OnboardingApiException("Direct chat requires the backend API.")
            : GetAsync<ChatMessagePageViewModel>($"api/companies/{companyId}/conversations/{conversationId}/messages?skip={skip}&take={take}", cancellationToken);

    public Task<SendDirectAgentMessageResultViewModel> SendMessageAsync(Guid companyId, Guid conversationId, string body, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? throw new OnboardingApiException("Direct chat requires the backend API.")
            : SendAsync<SendDirectAgentMessageResultViewModel>(HttpMethod.Post, $"api/companies/{companyId}/conversations/{conversationId}/messages", new SendDirectAgentMessageRequest { Body = body }, cancellationToken);

    public Task<CreateTaskFromChatResultViewModel> CreateTaskFromChatAsync(Guid companyId, Guid conversationId, Guid? sourceMessageId, string? title, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? throw new OnboardingApiException("Direct chat requires the backend API.")
            : SendAsync<CreateTaskFromChatResultViewModel>(
                HttpMethod.Post,
                $"api/companies/{companyId}/conversations/{conversationId}/tasks",
                new CreateTaskFromChatRequest { SourceMessageId = sourceMessageId, Title = title },
                cancellationToken);

    public Task<LinkConversationToTaskResultViewModel> LinkConversationToTaskAsync(Guid companyId, Guid conversationId, Guid taskId, Guid? sourceMessageId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? throw new OnboardingApiException("Direct chat requires the backend API.")
            : SendAsync<LinkConversationToTaskResultViewModel>(
                HttpMethod.Post,
                $"api/companies/{companyId}/conversations/{conversationId}/task-links",
                new LinkConversationToTaskRequest { TaskId = taskId, SourceMessageId = sourceMessageId },
                cancellationToken);

    public Task<ConversationRelatedTaskListViewModel> GetRelatedTasksAsync(Guid companyId, Guid conversationId, int skip = 0, int take = 25, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? throw new OnboardingApiException("Direct chat requires the backend API.")
            : GetAsync<ConversationRelatedTaskListViewModel>($"api/companies/{companyId}/conversations/{conversationId}/tasks?skip={skip}&take={take}", cancellationToken);

    private async Task<T> GetAsync<T>(string uri, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(uri, cancellationToken);
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
        try
        {
            using var request = new HttpRequestMessage(method, uri)
            {
                Content = JsonContent.Create(payload)
            };

            using var response = await _httpClient.SendAsync(request, cancellationToken);
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
        return result ?? throw new OnboardingApiException("The server returned an empty response.");
    }

    private async Task<OnboardingApiException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (!string.Equals(contentType, "application/json", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(contentType, "application/problem+json", StringComparison.OrdinalIgnoreCase))
        {
            await response.Content.ReadAsStringAsync(cancellationToken);
            return new OnboardingApiException($"The request failed with status code {(int)response.StatusCode}.");
        }

        var problem = await response.Content.ReadFromJsonAsync<ApiProblemResponse>(SerializerOptions, cancellationToken);
        return problem?.Errors is { Count: > 0 }
            ? new OnboardingApiException(problem.Detail ?? problem.Title ?? "The request failed.", problem.Errors)
            : new OnboardingApiException(problem?.Detail ?? problem?.Title ?? "The request failed.");
    }

    private OnboardingApiException CreateNetworkException(HttpRequestException ex)
    {
        var baseAddress = _httpClient.BaseAddress?.ToString().TrimEnd('/') ?? "the configured API";
        return new OnboardingApiException($"The web app could not reach the backend API at {baseAddress}. Start the API project or update the web app API base URL.");
    }

    private sealed class ApiProblemResponse
    {
        public string? Title { get; set; }
        public string? Detail { get; set; }
        public Dictionary<string, string[]>? Errors { get; set; }
    }
}

public sealed class DirectConversationViewModel
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string ChannelType { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public Guid AgentId { get; set; }
    public string AgentDisplayName { get; set; } = string.Empty;
    public string AgentRoleName { get; set; } = string.Empty;
    public string AgentStatus { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class DirectConversationPageViewModel
{
    public List<DirectConversationViewModel> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}

public sealed class ChatMessageViewModel
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ConversationId { get; set; }
    public string SenderType { get; set; } = string.Empty;
    public Guid? SenderId { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? RationaleSummary { get; set; }
    public Dictionary<string, JsonNode?> StructuredPayload { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedAt { get; set; }
}

public sealed class ChatMessagePageViewModel
{
    public List<ChatMessageViewModel> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}

public sealed class SendDirectAgentMessageRequest
{
    public string Body { get; set; } = string.Empty;
}

public sealed class SendDirectAgentMessageResultViewModel
{
    public DirectConversationViewModel Conversation { get; set; } = new();
    public ChatMessageViewModel HumanMessage { get; set; } = new();
    public ChatMessageViewModel AgentMessage { get; set; } = new();
}

public sealed class CreateTaskFromChatRequest
{
    public Guid? SourceMessageId { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Priority { get; set; }
}

public sealed class CreateTaskFromChatResultViewModel
{
    public Guid TaskId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid? SourceMessageId { get; set; }
    public Guid LinkId { get; set; }
}

public sealed class LinkConversationToTaskRequest
{
    public Guid TaskId { get; set; }
    public Guid? SourceMessageId { get; set; }
}

public sealed class LinkConversationToTaskResultViewModel
{
    public Guid LinkId { get; set; }
    public Guid TaskId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid? SourceMessageId { get; set; }
    public bool Created { get; set; }
}

public sealed class ConversationRelatedTaskViewModel
{
    public Guid TaskId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public Guid? AssignedAgentId { get; set; }
    public string? AssignedAgentDisplayName { get; set; }
    public string LinkType { get; set; } = string.Empty;
    public Guid? SourceMessageId { get; set; }
    public DateTime LinkedAt { get; set; }
}

public sealed class ConversationRelatedTaskListViewModel
{
    public List<ConversationRelatedTaskViewModel> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}