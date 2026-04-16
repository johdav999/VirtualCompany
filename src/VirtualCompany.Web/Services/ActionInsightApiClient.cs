using System.Net.Http.Json;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public sealed class ActionInsightApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly bool _useOfflineMode;
    private readonly List<ActionQueueItemViewModel> _offlineItems = [];

    public ActionInsightApiClient(HttpClient httpClient, bool useOfflineMode = false)
    {
        _httpClient = httpClient;
        _useOfflineMode = useOfflineMode;
    }

    public Task<IReadOnlyList<ActionQueueItemViewModel>> GetQueueAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            if (_offlineItems.Count == 0)
            {
                _offlineItems.AddRange(OfflineQueue(companyId));
            }

            return Task.FromResult<IReadOnlyList<ActionQueueItemViewModel>>(_offlineItems.ToList());
        }

        return GetAsync<IReadOnlyList<ActionQueueItemViewModel>>($"api/companies/{companyId}/action-insights/queue", cancellationToken);
    }

    public async Task<ActionQueueItemViewModel?> AcknowledgeAsync(Guid companyId, string insightKey, CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            var item = _offlineItems.FirstOrDefault(x => string.Equals(x.InsightKey, insightKey, StringComparison.Ordinal));
            if (item is not null)
            {
                item.IsAcknowledged = true;
                item.AcknowledgedAt = DateTime.UtcNow;
            }

            return item;
        }

        return await SendAsync<ActionQueueItemViewModel?>(
            HttpMethod.Post,
            $"api/companies/{companyId}/action-insights/{Uri.EscapeDataString(insightKey)}/acknowledgment",
            cancellationToken);
    }

    private async Task<T> GetAsync<T>(string uri, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(uri, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken)
                    ?? throw new OnboardingApiException("The server returned an empty response.");
            }

            throw await CreateExceptionAsync(response, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw CreateNetworkException(ex);
        }
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string uri, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, uri);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken)
                    ?? throw new OnboardingApiException("The server returned an empty response.");
            }

            throw await CreateExceptionAsync(response, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw CreateNetworkException(ex);
        }
    }

    private async Task<OnboardingApiException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var problem = await response.Content.ReadFromJsonAsync<ApiProblemResponse>(SerializerOptions, cancellationToken);
        return new OnboardingApiException(problem?.Detail ?? problem?.Title ?? $"The request failed with status code {(int)response.StatusCode}.");
    }

    private OnboardingApiException CreateNetworkException(HttpRequestException ex)
    {
        var baseAddress = _httpClient.BaseAddress?.ToString().TrimEnd('/') ?? "the configured API";
        return new OnboardingApiException($"The web app could not reach the backend API at {baseAddress}. Start the API project or update the web app API base URL.");
    }

    private static IReadOnlyList<ActionQueueItemViewModel> OfflineQueue(Guid companyId) =>
    [
        new()
        {
            InsightKey = $"{companyId:N}:approval:d0131d5718954b7596249b8b04d8a116:v0",
            CompanyId = companyId,
            Type = "approval",
            SourceEntityType = "approval",
            SourceEntityId = Guid.Parse("d0131d57-1895-4b75-9624-9b8b04d8a116"),
            TargetType = "approval",
            TargetId = Guid.Parse("d0131d57-1895-4b75-9624-9b8b04d8a116"),
            Title = "Approval required",
            Reason = "Pending threshold approval for task.",
            Owner = "owner",
            DueUtc = DateTime.UtcNow.AddHours(2),
            SlaState = "due_soon",
            PriorityScore = 90,
            Priority = "critical",
            DeepLink = $"/approvals?companyId={companyId}&approvalId=d0131d57-1895-4b75-9624-9b8b04d8a116",
            StableSortKey = $"{companyId:N}:approval:d0131d5718954b7596249b8b04d8a116:v0"
        },
        new()
        {
            InsightKey = $"{companyId:N}:task:21fa1767fd7742d7a4d5c6d50ec8bf5b:v0",
            CompanyId = companyId,
            Type = "task",
            SourceEntityType = "task",
            SourceEntityId = Guid.Parse("21fa1767-fd77-42d7-a4d5-c6d50ec8bf5b"),
            TargetType = "task",
            TargetId = Guid.Parse("21fa1767-fd77-42d7-a4d5-c6d50ec8bf5b"),
            Title = "Resolve blocked fulfillment task",
            Reason = "Task is blocked and needs intervention.",
            Owner = "Operations",
            DueUtc = DateTime.UtcNow.AddHours(4),
            SlaState = "due_soon",
            PriorityScore = 75,
            Priority = "high",
            DeepLink = $"/tasks?companyId={companyId}&taskId=21fa1767-fd77-42d7-a4d5-c6d50ec8bf5b",
            StableSortKey = $"{companyId:N}:task:21fa1767fd7742d7a4d5c6d50ec8bf5b:v0"
        },
        new()
        {
            InsightKey = $"{companyId:N}:blocked_workflow:2cf1da3993ad4543bdd23862eedec738:v0",
            CompanyId = companyId,
            Type = "blocked_workflow",
            SourceEntityType = "workflow",
            SourceEntityId = Guid.Parse("2cf1da39-93ad-4543-bdd2-3862eedec738"),
            TargetType = "workflow",
            TargetId = Guid.Parse("2cf1da39-93ad-4543-bdd2-3862eedec738"),
            Title = "Fulfillment workflow",
            Reason = "Workflow is blocked at step review.",
            Owner = "Operations",
            DueUtc = DateTime.UtcNow.AddHours(1),
            SlaState = "due_soon",
            PriorityScore = 85,
            Priority = "high",
            DeepLink = $"/workflows?companyId={companyId}&workflowInstanceId=2cf1da39-93ad-4543-bdd2-3862eedec738",
            StableSortKey = $"{companyId:N}:blocked_workflow:2cf1da3993ad4543bdd23862eedec738:v0"
        }
    ];

    private sealed class ApiProblemResponse
    {
        public string? Title { get; set; }
        public string? Detail { get; set; }
    }
}

public sealed class ActionQueueItemViewModel
{
    public string InsightKey { get; set; } = string.Empty;
    public Guid CompanyId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string SourceEntityType { get; set; } = string.Empty;
    public Guid SourceEntityId { get; set; }
    public string TargetType { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public DateTime? DueUtc { get; set; }
    public string SlaState { get; set; } = string.Empty;
    public int PriorityScore { get; set; }
    public string Priority { get; set; } = string.Empty;
    public string DeepLink { get; set; } = string.Empty;
    public bool IsAcknowledged { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public string StableSortKey { get; set; } = string.Empty;
}