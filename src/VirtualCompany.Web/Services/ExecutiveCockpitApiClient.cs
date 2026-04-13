using System.Net.Http.Json;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public sealed class ExecutiveCockpitApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly bool _useOfflineMode;

    public ExecutiveCockpitApiClient(HttpClient httpClient, bool useOfflineMode = false)
    {
        _httpClient = httpClient;
        _useOfflineMode = useOfflineMode;
    }

    public Task<ExecutiveCockpitDashboardViewModel?> GetAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult<ExecutiveCockpitDashboardViewModel?>(OfflineDashboard(companyId));
        }

        return GetCoreAsync(companyId, cancellationToken);
    }

    private async Task<ExecutiveCockpitDashboardViewModel?> GetCoreAsync(Guid companyId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"api/companies/{companyId}/executive-cockpit", cancellationToken);
            if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized)
            {
                return null;
            }

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<ExecutiveCockpitDashboardViewModel>(SerializerOptions, cancellationToken);
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

    private static ExecutiveCockpitDashboardViewModel OfflineDashboard(Guid companyId) =>
        new()
        {
            CompanyId = companyId,
            CompanyName = "Offline workspace",
            GeneratedAtUtc = DateTime.UtcNow,
            SummaryKpis =
            [
                new ExecutiveCockpitSummaryKpiViewModel
                {
                    Key = "pending_approvals",
                    Label = "Pending approvals",
                    CurrentValue = 0,
                    TrendDirection = "unknown",
                    DeltaText = "Trend unavailable",
                    ComparisonLabel = "No prior period data",
                    StatusHint = "neutral",
                    IsEmpty = true
                },
                new ExecutiveCockpitSummaryKpiViewModel
                {
                    Key = "open_tasks",
                    Label = "Open tasks",
                    CurrentValue = 0,
                    TrendDirection = "unknown",
                    DeltaText = "Trend unavailable",
                    ComparisonLabel = "No prior period data",
                    StatusHint = "neutral",
                    IsEmpty = true
                },
                new ExecutiveCockpitSummaryKpiViewModel
                {
                    Key = "completed_tasks_7d",
                    Label = "Completed tasks",
                    CurrentValue = 0,
                    PreviousValue = 0,
                    TrendDirection = "flat",
                    DeltaValue = 0,
                    DeltaText = "No change vs previous 7 days",
                    ComparisonLabel = "vs previous 7 days",
                    StatusHint = "neutral",
                    IsEmpty = true
                }
            ],
            PendingApprovals = new ExecutiveCockpitPendingApprovalsViewModel
            {
                TotalCount = 0,
                Route = $"/approvals?companyId={companyId}"
            },
            DepartmentKpis =
            [
                new ExecutiveCockpitDepartmentKpiViewModel
                {
                    Department = "Company",
                    Route = $"/agents?companyId={companyId}"
                }
            ],
            SetupState = new ExecutiveCockpitSetupStateViewModel
            {
                HasAgents = false,
                HasWorkflows = false,
                HasKnowledge = false,
                AgentCount = 0,
                WorkflowCount = 0,
                KnowledgeDocumentCount = 0,
                IsInitialSetupEmpty = true
            },
            EmptyStateFlags = new ExecutiveCockpitEmptyStateFlagsViewModel
            {
                NoAgents = true,
                NoWorkflows = true,
                NoKnowledge = true,
                NoRecentActivity = true,
                NoPendingApprovals = true,
                NoAlerts = true
            }
        };

    private sealed class ApiProblemResponse
    {
        public string? Title { get; set; }
        public string? Detail { get; set; }
        public Dictionary<string, string[]>? Errors { get; set; }
    }
}

public sealed class ExecutiveCockpitDashboardViewModel
{
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; }
    public DateTime? CacheTimestampUtc { get; set; }
    public ExecutiveCockpitDailyBriefingViewModel? DailyBriefing { get; set; }
    public List<ExecutiveCockpitSummaryKpiViewModel> SummaryKpis { get; set; } = [];
    public ExecutiveCockpitPendingApprovalsViewModel PendingApprovals { get; set; } = new();
    public List<ExecutiveCockpitAlertViewModel> Alerts { get; set; } = [];
    public List<ExecutiveCockpitDepartmentKpiViewModel> DepartmentKpis { get; set; } = [];
    public List<ExecutiveCockpitActivityItemViewModel> RecentActivity { get; set; } = [];
    public ExecutiveCockpitSetupStateViewModel SetupState { get; set; } = new();
    public ExecutiveCockpitEmptyStateFlagsViewModel EmptyStateFlags { get; set; } = new();
}

public sealed class ExecutiveCockpitSummaryKpiViewModel
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int CurrentValue { get; set; }
    public int? PreviousValue { get; set; }
    public string TrendDirection { get; set; } = "unknown";
    public int? DeltaValue { get; set; }
    public decimal? DeltaPercentage { get; set; }
    public string DeltaText { get; set; } = "Trend unavailable";
    public string ComparisonLabel { get; set; } = "No prior period data";
    public string? StatusHint { get; set; }
    public bool IsEmpty { get; set; }
}

public sealed class ExecutiveCockpitDailyBriefingViewModel
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public DateTime GeneratedUtc { get; set; }
    public string? Route { get; set; }
}

public sealed class ExecutiveCockpitPendingApprovalsViewModel
{
    public int TotalCount { get; set; }
    public List<ExecutiveCockpitApprovalItemViewModel> Items { get; set; } = [];
    public string Route { get; set; } = "/approvals";
}

public sealed class ExecutiveCockpitApprovalItemViewModel
{
    public Guid Id { get; set; }
    public string ApprovalType { get; set; } = string.Empty;
    public string TargetEntityType { get; set; } = string.Empty;
    public Guid TargetEntityId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public string Route { get; set; } = string.Empty;
}

public sealed class ExecutiveCockpitAlertViewModel
{
    public Guid Id { get; set; }
    public string Severity { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public Guid? SourceId { get; set; }
    public DateTime OccurredUtc { get; set; }
    public string? Route { get; set; }
}

public sealed class ExecutiveCockpitDepartmentKpiViewModel
{
    public string Department { get; set; } = string.Empty;
    public int ActiveAgents { get; set; }
    public int OpenTasks { get; set; }
    public int CompletedTasksLast7Days { get; set; }
    public int PendingApprovals { get; set; }
    public int ActiveWorkflows { get; set; }
    public string Route { get; set; } = string.Empty;
}

public sealed class ExecutiveCockpitActivityItemViewModel
{
    public Guid Id { get; set; }
    public string ActivityType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public DateTime OccurredUtc { get; set; }
    public string? Route { get; set; }
}

public sealed class ExecutiveCockpitSetupStateViewModel
{
    public bool HasAgents { get; set; }
    public bool HasWorkflows { get; set; }
    public bool HasKnowledge { get; set; }
    public int AgentCount { get; set; }
    public int WorkflowCount { get; set; }
    public int KnowledgeDocumentCount { get; set; }
    public bool IsInitialSetupEmpty { get; set; }
}

public sealed class ExecutiveCockpitEmptyStateFlagsViewModel
{
    public bool NoAgents { get; set; }
    public bool NoWorkflows { get; set; }
    public bool NoKnowledge { get; set; }
    public bool NoRecentActivity { get; set; }
    public bool NoPendingApprovals { get; set; }
    public bool NoAlerts { get; set; }
}