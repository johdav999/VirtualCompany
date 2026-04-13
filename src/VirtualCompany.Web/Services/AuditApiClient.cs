using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;

namespace VirtualCompany.Web.Services;

public sealed class AuditApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly bool _useOfflineMode;

    public AuditApiClient(HttpClient httpClient, bool useOfflineMode = false)
    {
        _httpClient = httpClient;
        _useOfflineMode = useOfflineMode;
    }

    public Task<AuditHistoryResultViewModel> ListAsync(
        Guid companyId,
        AuditHistoryFilterViewModel filter,
        CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult(OfflineHistory(companyId));
        }

        return GetAsync<AuditHistoryResultViewModel>(
            $"api/companies/{companyId}/audit{BuildQueryString(filter)}",
            cancellationToken);
    }

    public Task<AuditDetailViewModel> GetAsync(
        Guid companyId,
        Guid auditEventId,
        CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult(OfflineDetail(companyId, auditEventId));
        }

        return GetAsync<AuditDetailViewModel>($"api/companies/{companyId}/audit/{auditEventId}", cancellationToken);
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

    private static string BuildQueryString(AuditHistoryFilterViewModel filter)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        if (filter.AgentId is Guid agentId)
        {
            query["agentId"] = agentId.ToString();
        }

        if (filter.TaskId is Guid taskId)
        {
            query["taskId"] = taskId.ToString();
        }

        if (filter.WorkflowInstanceId is Guid workflowInstanceId)
        {
            query["workflowInstanceId"] = workflowInstanceId.ToString();
        }

        if (filter.FromUtc is DateTime fromUtc)
        {
            query["fromUtc"] = fromUtc.ToString("O");
        }

        if (filter.ToUtc is DateTime toUtc)
        {
            query["toUtc"] = toUtc.ToString("O");
        }

        query["skip"] = Math.Max(filter.Skip, 0).ToString();
        query["take"] = Math.Clamp(filter.Take, 1, 200).ToString();

        var queryString = query.ToString();
        return string.IsNullOrWhiteSpace(queryString) ? string.Empty : $"?{queryString}";
    }

    private async Task<OnboardingApiException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return new OnboardingApiException("You do not have permission to view audit history for this company.");
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

    private static AuditHistoryResultViewModel OfflineHistory(Guid companyId)
    {
        var eventId = Guid.Parse("8d570986-8f94-43f9-9991-2cb8c29990f0");
        return new AuditHistoryResultViewModel
        {
            Items =
            [
                new()
                {
                    Id = eventId,
                    CompanyId = companyId,
                    ActorType = "agent",
                    ActorId = Guid.Parse("69a4f0e5-9537-4e8c-9eb1-785c8ab1d4f0"),
                    ActorLabel = "Finance Agent",
                    Action = "agent.tool_execution.approval_requested",
                    TargetType = "agent_tool_execution",
                    TargetId = "bb521f6f-7b56-4bea-9f32-4f7fc863cc82",
                    TargetLabel = "Payment tool execution",
                    Outcome = "pending",
                    RationaleSummary = "The requested payment exceeded the configured approval threshold.",
                    Explanation = new()
                    {
                        Summary = "The requested payment exceeded the configured approval threshold.",
                        WhyThisAction = "The system recorded 'agent.tool_execution.approval_requested' with outcome 'pending' under configured company policy.",
                        Outcome = "pending",
                        DataSources = ["approval thresholds", "policy guardrails"]
                    },
                    OccurredAt = DateTime.UtcNow.AddMinutes(-20),
                    CorrelationId = "offline-audit-demo",
                    AffectedEntities =
                    [
                        new() { EntityType = "work_task", EntityId = "9bc83a53-7716-48cd-8150-f9b4b4926e39", Label = "Vendor payment run for April" }
                    ]
                }
            ],
            TotalCount = 1,
            Skip = 0,
            Take = 50
        };
    }

    private static AuditDetailViewModel OfflineDetail(Guid companyId, Guid auditEventId) =>
        new()
        {
            Id = auditEventId,
            CompanyId = companyId,
            ActorType = "agent",
            ActorId = Guid.Parse("69a4f0e5-9537-4e8c-9eb1-785c8ab1d4f0"),
            ActorLabel = "Finance Agent",
            Action = "agent.tool_execution.approval_requested",
            TargetType = "agent_tool_execution",
            TargetId = "bb521f6f-7b56-4bea-9f32-4f7fc863cc82",
            TargetLabel = "Payment tool execution",
            Outcome = "pending",
            RationaleSummary = "The requested payment exceeded the configured approval threshold.",
            DataSources = ["policy guardrails", "approval thresholds"],
            Explanation = new()
            {
                Summary = "The requested payment exceeded the configured approval threshold.",
                WhyThisAction = "The system recorded 'agent.tool_execution.approval_requested' with outcome 'pending' under configured company policy.",
                Outcome = "pending",
                DataSources = ["approval thresholds", "policy guardrails"]
            },
            SourceReferences = [new() { Label = "Policy Version", Reference = "task_8_3_7", Type = "metadata", SourceType = "metadata", DisplayName = "Policy Version" }],
            OccurredAt = DateTime.UtcNow.AddMinutes(-20),
            CorrelationId = "offline-audit-demo",
            Metadata = new Dictionary<string, string?> { ["taskId"] = "9bc83a53-7716-48cd-8150-f9b4b4926e39" },
            LinkedApprovals =
            [
                new()
                {
                    Id = Guid.Parse("d0131d57-1895-4b75-9624-9b8b04d8a116"),
                    ApprovalType = "threshold",
                    Status = "pending",
                    TargetEntityType = "task",
                    TargetEntityId = Guid.Parse("9bc83a53-7716-48cd-8150-f9b4b4926e39"),
                    CreatedAt = DateTime.UtcNow.AddMinutes(-19)
                }
            ],
            LinkedToolExecutions =
            [
                new()
                {
                    Id = Guid.Parse("bb521f6f-7b56-4bea-9f32-4f7fc863cc82"),
                    AgentId = Guid.Parse("69a4f0e5-9537-4e8c-9eb1-785c8ab1d4f0"),
                    AgentLabel = "Finance Agent",
                    ToolName = "payments",
                    ActionType = "execute",
                    Status = "awaiting_approval",
                    StartedAt = DateTime.UtcNow.AddMinutes(-20)
                }
            ],
            AffectedEntities =
            [
                new() { EntityType = "work_task", EntityId = "9bc83a53-7716-48cd-8150-f9b4b4926e39", Label = "Vendor payment run for April" }
            ]
        };

    private sealed class ApiProblemResponse
    {
        public string? Title { get; set; }
        public string? Detail { get; set; }
        public Dictionary<string, string[]>? Errors { get; set; }
    }
}

public sealed class AuditHistoryFilterViewModel
{
    public Guid? AgentId { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? WorkflowInstanceId { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; } = 50;
}

public sealed class AuditHistoryResultViewModel
{
    public List<AuditHistoryListItemViewModel> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}

public class AuditHistoryListItemViewModel
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string ActorType { get; set; } = string.Empty;
    public Guid? ActorId { get; set; }
    public string? ActorLabel { get; set; }
    public string Action { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string? TargetLabel { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string? RationaleSummary { get; set; }
    public DateTime OccurredAt { get; set; }
    public AuditSafeExplanationViewModel Explanation { get; set; } = new();
    public string? CorrelationId { get; set; }
    public List<AuditEntityReferenceViewModel> AffectedEntities { get; set; } = [];
}

public sealed class AuditDetailViewModel : AuditHistoryListItemViewModel
{
    public List<string> DataSources { get; set; } = [];
    public List<AuditSourceReferenceViewModel> SourceReferences { get; set; } = [];
    public Dictionary<string, string?> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<AuditApprovalReferenceViewModel> LinkedApprovals { get; set; } = [];
    public List<AuditToolExecutionReferenceViewModel> LinkedToolExecutions { get; set; } = [];
}

public sealed class AuditSafeExplanationViewModel
{
    public string Summary { get; set; } = "Action completed using configured policy and available company data.";
    public string WhyThisAction { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public List<string> DataSources { get; set; } = [];
}

public sealed class AuditSourceReferenceViewModel
{
    public string Label { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string? Type { get; set; }
    public string? SourceType { get; set; }
    public string? DisplayName { get; set; }
    public string? SecondaryText { get; set; }
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? Snippet { get; set; }
}

public sealed class AuditApprovalReferenceViewModel
{
    public Guid Id { get; set; }
    public string ApprovalType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string TargetEntityType { get; set; } = string.Empty;
    public Guid TargetEntityId { get; set; }
    public string? DecisionSummary { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
}

public sealed class AuditToolExecutionReferenceViewModel
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }
    public string? AgentLabel { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? TaskId { get; set; }
    public Guid? WorkflowInstanceId { get; set; }
    public Guid? ApprovalRequestId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public sealed class AuditEntityReferenceViewModel
{
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? Label { get; set; }
}
