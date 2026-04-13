using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VirtualCompany.Web.Services;

public sealed class TaskApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly bool _useOfflineMode;

    public TaskApiClient(HttpClient httpClient, bool useOfflineMode = false)
    {
        _httpClient = httpClient;
        _useOfflineMode = useOfflineMode;
    }

    public Task<TaskListResultViewModel> ListAsync(
        Guid companyId,
        string? status,
        Guid? assignedAgentId,
        CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            var items = OfflineTasks(companyId)
                .Where(x => string.IsNullOrWhiteSpace(status) || string.Equals(x.Status, status, StringComparison.OrdinalIgnoreCase))
                .Where(x => assignedAgentId is null || x.AssignedAgentId == assignedAgentId)
                .ToList();
            return Task.FromResult(new TaskListResultViewModel { Items = items, TotalCount = items.Count, Take = items.Count });
        }

        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(status)) query.Add($"status={Uri.EscapeDataString(status)}");
        if (assignedAgentId is Guid agentId) query.Add($"assignedAgentId={agentId}");
        var uri = query.Count == 0
            ? $"api/companies/{companyId}/tasks"
            : $"api/companies/{companyId}/tasks?{string.Join("&", query)}";
        return GetAsync<TaskListResultViewModel>(uri, cancellationToken);
    }

    public Task<TaskDetailViewModel> GetAsync(Guid companyId, Guid taskId, CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            var item = OfflineTasks(companyId).FirstOrDefault(x => x.Id == taskId)
                ?? throw new OnboardingApiException("Task was not found.");
            return Task.FromResult(new TaskDetailViewModel
            {
                Id = item.Id,
                CompanyId = item.CompanyId,
                Type = item.Type,
                Title = item.Title,
                Priority = item.Priority,
                Status = item.Status,
                AssignedAgentId = item.AssignedAgentId,
                CreatedAt = item.CreatedAt,
                UpdatedAt = item.UpdatedAt,
                AssignedAgent = item.AssignedAgent
            });
        }

        return GetAsync<TaskDetailViewModel>($"api/companies/{companyId}/tasks/{taskId}", cancellationToken);
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

    private async Task<OnboardingApiException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var problem = await response.Content.ReadFromJsonAsync<ApiProblemResponse>(SerializerOptions, cancellationToken);
        return problem?.Errors is { Count: > 0 }
            ? new OnboardingApiException(problem.Detail ?? problem.Title ?? "The request failed.", problem.Errors)
            : new OnboardingApiException(problem?.Detail ?? problem?.Title ?? $"The request failed with status code {(int)response.StatusCode}.");
    }

    private OnboardingApiException CreateNetworkException(HttpRequestException ex)
    {
        var baseAddress = _httpClient.BaseAddress?.ToString().TrimEnd('/') ?? "the configured API";
        return new OnboardingApiException($"The web app could not reach the backend API at {baseAddress}. Start the API project or update the web app API base URL.");
    }

    private static IReadOnlyList<TaskListItemViewModel> OfflineTasks(Guid companyId) =>
    [
        new()
        {
            Id = Guid.Parse("9bc83a53-7716-48cd-8150-f9b4b4926e39"),
            CompanyId = companyId,
            Type = "review",
            Title = "Vendor payment run for April",
            Priority = "high",
            Status = "awaiting_approval",
            CreatedAt = DateTime.UtcNow.AddHours(-6),
            UpdatedAt = DateTime.UtcNow.AddHours(-1)
        }
    ];

    private sealed class ApiProblemResponse
    {
        public string? Title { get; set; }
        public string? Detail { get; set; }
        public Dictionary<string, string[]>? Errors { get; set; }
    }
}

public sealed class TaskListResultViewModel
{
    public List<TaskListItemViewModel> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}

public sealed class TaskListItemViewModel
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? DueAt { get; set; }
    public Guid? AssignedAgentId { get; set; }
    public Guid? ParentTaskId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public TaskAgentSummaryViewModel? AssignedAgent { get; set; }
}

public sealed class TaskDetailViewModel
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? DueAt { get; set; }
    public Guid? AssignedAgentId { get; set; }
    public Guid? ParentTaskId { get; set; }
    public Guid? WorkflowInstanceId { get; set; }
    public string CreatedByActorType { get; set; } = string.Empty;
    public Guid? CreatedByActorId { get; set; }
    public Dictionary<string, JsonNode?> InputPayload { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> OutputPayload { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? RationaleSummary { get; set; }
    public decimal? ConfidenceScore { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TaskAgentSummaryViewModel? AssignedAgent { get; set; }
    public TaskParentSummaryViewModel? ParentTask { get; set; }
    public string? CorrelationId { get; set; }
    public List<TaskSubtaskSummaryViewModel> Subtasks { get; set; } = [];
}

public sealed class TaskAgentSummaryViewModel
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public sealed class TaskParentSummaryViewModel
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public sealed class TaskSubtaskSummaryViewModel
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? DueAt { get; set; }
    public Guid? AssignedAgentId { get; set; }
    public Guid? ParentTaskId { get; set; }
    public Guid? WorkflowInstanceId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TaskAgentSummaryViewModel? AssignedAgent { get; set; }
}