using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Microsoft.AspNetCore.SignalR.Client;

namespace VirtualCompany.Web.Services;

public sealed class ActivityFeedApiClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly bool _useOfflineMode;
    private HubConnection? _connection;
    private Guid? _connectedCompanyId;

    public ActivityFeedApiClient(HttpClient httpClient, bool useOfflineMode = false)
    {
        _httpClient = httpClient;
        _useOfflineMode = useOfflineMode;
    }

    public Task<ActivityFeedPageViewModel> ListAsync(
        Guid companyId,
        string? cursor = null,
        int pageSize = 25,
        ActivityFeedFilterViewModel? filters = null,
        CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult(OfflinePage(companyId));
        }

        var query = HttpUtility.ParseQueryString(string.Empty);
        query["pageSize"] = Math.Clamp(pageSize, 1, 100).ToString();
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            query["cursor"] = cursor;
        }

        if (filters is not null)
        {
            if (!string.IsNullOrWhiteSpace(filters.AgentId))
            {
                query["agentId"] = filters.AgentId.Trim();
            }

            if (!string.IsNullOrWhiteSpace(filters.Department))
            {
                query["department"] = filters.Department.Trim();
            }

            if (!string.IsNullOrWhiteSpace(filters.TaskId))
            {
                query["task"] = filters.TaskId.Trim();
            }

            if (!string.IsNullOrWhiteSpace(filters.EventType))
            {
                query["eventType"] = filters.EventType.Trim();
            }

            if (!string.IsNullOrWhiteSpace(filters.Status))
            {
                query["status"] = filters.Status.Trim();
            }

            if (!ActivityFeedFilterState.IsDefaultTimeframe(filters.Timeframe))
            {
                query["timeframe"] = ActivityFeedFilterState.NormalizeTimeframe(filters.Timeframe);
            }

            AddDateQuery(query, "from", filters.FromUtc);
            AddDateQuery(query, "to", filters.ToUtc);
        }

        return GetAsync<ActivityFeedPageViewModel>(
            $"api/companies/{companyId}/activity-feed?{query}",
            cancellationToken);
    }

    public Task<ActivityCorrelationTimelineViewModel> GetCorrelationForActivityAsync(
        Guid companyId,
        Guid activityEventId,
        CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult(OfflineCorrelation(companyId, activityEventId));
        }

        return GetAsync<ActivityCorrelationTimelineViewModel>(
            $"api/companies/{companyId}/activity-feed/{activityEventId}/correlation",
            cancellationToken);
    }

    public Task<ActivityCorrelationTimelineViewModel> GetCorrelationAsync(
        Guid companyId,
        string correlationId,
        Guid? selectedActivityEventId = null,
        CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult(OfflineCorrelation(companyId, selectedActivityEventId));
        }

        var query = HttpUtility.ParseQueryString(string.Empty);
        if (selectedActivityEventId is Guid selected)
        {
            query["selectedActivityEventId"] = selected.ToString();
        }

        var suffix = query.Count > 0 ? $"?{query}" : string.Empty;
        return GetAsync<ActivityCorrelationTimelineViewModel>(
            $"api/companies/{companyId}/activity-feed/correlations/{Uri.EscapeDataString(correlationId)}{suffix}",
            cancellationToken);
    }

    public async Task ConnectAsync(
        Guid companyId,
        Func<ActivityFeedItemViewModel, Task> onActivityReceived,
        CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return;
        }

        if (_connection is not null && _connectedCompanyId == companyId)
        {
            if (_connection.State == HubConnectionState.Disconnected)
            {
                await _connection.StartAsync(cancellationToken);
            }

            return;
        }

        await DisconnectAsync();
        _connectedCompanyId = companyId;
        _connection = new HubConnectionBuilder()
            .WithUrl(BuildHubUri(companyId), options =>
            {
                foreach (var header in _httpClient.DefaultRequestHeaders)
                {
                    options.Headers[header.Key] = string.Join(",", header.Value);
                }
            })
            .WithAutomaticReconnect()
            .Build();

        _connection.On<ActivityFeedItemViewModel>("activityEventReceived", activityEvent => onActivityReceived(activityEvent));
        await _connection.StartAsync(cancellationToken);
    }

    public async Task DisconnectAsync()
    {
        if (_connection is null)
        {
            return;
        }

        await _connection.DisposeAsync();
        _connection = null;
        _connectedCompanyId = null;
    }

    public async ValueTask DisposeAsync() =>
        await DisconnectAsync();

    private Uri BuildHubUri(Guid companyId)
    {
        var baseAddress = _httpClient.BaseAddress ?? new Uri("http://localhost:5301/");
        return new Uri(baseAddress, $"hubs/activity-feed?tenantId={companyId}");
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
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return new OnboardingApiException("You do not have permission to view activity for this company.");
        }

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

    private static void AddDateQuery(System.Collections.Specialized.NameValueCollection query, string name, DateTime? value)
    {
        if (value is null)
        {
            return;
        }

        query[name] = value.Value.ToUniversalTime().ToString("O");
    }

    private static ActivityFeedPageViewModel OfflinePage(Guid companyId)
    {
        var agentId = Guid.Parse("69a4f0e5-9537-4e8c-9eb1-785c8ab1d4f0");
        return new ActivityFeedPageViewModel
        {
            Items =
            [
                new()
                {
                    EventId = Guid.Parse("f43f0c0a-94ac-4950-8236-69340a202023"),
                    TenantId = companyId,
                    AgentId = agentId,
                    EventType = "task_completed",
                    OccurredAt = DateTime.UtcNow.AddMinutes(-3),
                    Status = "completed",
                    Summary = "Finance Agent completed invoice review.",
                    CorrelationId = "offline-activity-demo",
                    SourceMetadata = new Dictionary<string, JsonElement>
                    {
                        ["sourceType"] = JsonSerializer.SerializeToElement("task"),
                        ["sourceId"] = JsonSerializer.SerializeToElement(Guid.Parse("9bc83a53-7716-48cd-8150-f9b4b4926e39"))
                    }
                },
                new()
                {
                    EventId = Guid.Parse("5e1f7ab8-81c7-458a-8b7f-f1e9af61b3c5"),
                    TenantId = companyId,
                    AgentId = agentId,
                    EventType = "tool_execution_started",
                    OccurredAt = DateTime.UtcNow.AddMinutes(-12),
                    Status = "running",
                    Summary = "Finance Agent started a payment policy check.",
                    CorrelationId = "offline-activity-demo",
                    SourceMetadata = new Dictionary<string, JsonElement>
                    {
                        ["sourceType"] = JsonSerializer.SerializeToElement("tool_execution"),
                        ["toolName"] = JsonSerializer.SerializeToElement("payments")
                    }
                }
            ],
            NextCursor = null
        };
    }

    private static ActivityCorrelationTimelineViewModel OfflineCorrelation(Guid companyId, Guid? selectedActivityEventId)
    {
        var page = OfflinePage(companyId);
        var selected = selectedActivityEventId is Guid id
            ? page.Items.FirstOrDefault(x => x.EventId == id) ?? page.Items[0]
            : page.Items[0];

        return new ActivityCorrelationTimelineViewModel
        {
            TenantId = companyId,
            CorrelationId = selected.CorrelationId ?? "offline-activity-demo",
            Items = page.Items
                .OrderBy(x => x.OccurredAt)
                .Select(item => new ActivityTimelineItemViewModel
                {
                    Activity = item,
                    PrimaryTarget = new ActivityEntityReferenceViewModel
                    {
                        EntityType = item.EventType.Contains("tool", StringComparison.OrdinalIgnoreCase) ? "toolExecution" : "task",
                        EntityId = Guid.Parse("9bc83a53-7716-48cd-8150-f9b4b4926e39")
                    },
                    LinkedEntities =
                    [
                        new()
                        {
                            EntityType = "task",
                            EntityId = Guid.Parse("9bc83a53-7716-48cd-8150-f9b4b4926e39"),
                            Availability = "available",
                            DisplayText = "Invoice review",
                            CurrentStatus = "completed",
                            LastUpdatedAt = DateTime.UtcNow.AddMinutes(-3),
                            IsAvailable = true
                        },
                        new()
                        {
                            EntityType = "toolExecution",
                            EntityId = Guid.Parse("70a2d4e1-29df-4ad2-b4af-3c7b1d5661a7"),
                            Availability = "unavailable_missing",
                            DisplayText = "Unavailable linked entity",
                            IsAvailable = false,
                            UnavailableReason = "missing"
                        }
                    ]
                })
                .ToList(),
            SelectedActivityLinks = new ActivityLinkedEntitiesViewModel
            {
                TenantId = companyId,
                ActivityEventId = selected.EventId,
                LinkedEntities =
                [
                    new()
                    {
                        EntityType = "task",
                        EntityId = Guid.Parse("9bc83a53-7716-48cd-8150-f9b4b4926e39"),
                        Availability = "available",
                        DisplayText = "Invoice review",
                        CurrentStatus = "completed",
                        LastUpdatedAt = DateTime.UtcNow.AddMinutes(-3),
                        IsAvailable = true
                    },
                    new()
                    {
                        EntityType = "toolExecution",
                        EntityId = Guid.Parse("70a2d4e1-29df-4ad2-b4af-3c7b1d5661a7"),
                        Availability = "unavailable_missing",
                        DisplayText = "Unavailable linked entity",
                        IsAvailable = false,
                        UnavailableReason = "missing"
                    }
                ]
            }
        };
    }

    private sealed class ApiProblemResponse
    {
        public string? Title { get; set; }
        public string? Detail { get; set; }
        public Dictionary<string, string[]>? Errors { get; set; }
    }
}

public sealed class ActivityFeedPageViewModel
{
    public List<ActivityFeedItemViewModel> Items { get; set; } = [];
    public string? NextCursor { get; set; }
}

public sealed class ActivityFeedFilterViewModel
{
    public string? AgentId { get; set; }
    public string? Department { get; set; }
    public string? TaskId { get; set; }
    public string? EventType { get; set; }
    public string? Status { get; set; }
    public string? Timeframe { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
}

public sealed class ActivityFeedItemViewModel
{
    public Guid EventId { get; set; }
    public Guid TenantId { get; set; }
    public Guid? AgentId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }
    public string? Department { get; set; }
    public Guid? TaskId { get; set; }
    public ActivityAuditLinkViewModel? AuditLink { get; set; }

    // Current API payloads use source; sourceMetadata is kept for the task contract and forward compatibility.
    public Dictionary<string, JsonElement> Source { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonElement> SourceMetadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonElement> RawPayload { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public ActivitySummaryViewModel? NormalizedSummary { get; set; }
}

public sealed class ActivityAuditLinkViewModel
{
    public Guid AuditEventId { get; set; }
    public string Href { get; set; } = string.Empty;
}

public sealed class ActivitySummaryViewModel
{
    public string SummaryText { get; set; } = string.Empty;
}

public sealed class ActivityCorrelationTimelineViewModel
{
    public Guid TenantId { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public List<ActivityTimelineItemViewModel> Items { get; set; } = [];
    public ActivityLinkedEntitiesViewModel? SelectedActivityLinks { get; set; }
}

public sealed class ActivityTimelineItemViewModel
{
    public ActivityFeedItemViewModel Activity { get; set; } = new();
    public ActivityEntityReferenceViewModel? PrimaryTarget { get; set; }
    public List<ActivityLinkedEntityViewModel> LinkedEntities { get; set; } = [];
}

public sealed class ActivityEntityReferenceViewModel
{
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
}

public sealed class ActivityLinkedEntitiesViewModel
{
    public Guid TenantId { get; set; }
    public Guid ActivityEventId { get; set; }
    public List<ActivityLinkedEntityViewModel> LinkedEntities { get; set; } = [];
}

public sealed class ActivityLinkedEntityViewModel
{
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string Availability { get; set; } = string.Empty;
    public string DisplayText { get; set; } = string.Empty;
    public string? CurrentStatus { get; set; }
    public DateTime? LastUpdatedAt { get; set; }
    public bool IsAvailable { get; set; }
    public string? UnavailableReason { get; set; }
    public Dictionary<string, JsonElement> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
