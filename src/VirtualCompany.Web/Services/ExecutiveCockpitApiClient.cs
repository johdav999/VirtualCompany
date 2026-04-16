using System.Net.Http.Json;
using System.Globalization;
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

    public Task<ExecutiveCockpitKpiDashboardViewModel?> GetKpisAsync(
        Guid companyId,
        string? department,
        DateTime? startUtc,
        DateTime? endUtc,
        CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult<ExecutiveCockpitKpiDashboardViewModel?>(OfflineKpis(companyId, department, startUtc, endUtc));
        }

        return GetKpisCoreAsync(companyId, department, startUtc, endUtc, cancellationToken);
    }

    public async Task<ExecutiveCockpitWidgetPayloadViewModel<TPayload>?> GetWidgetAsync<TPayload>(
        Guid companyId,
        string widgetKey,
        string? department = null,
        DateTime? startUtc = null,
        DateTime? endUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return null;
        }

        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(department))
        {
            query.Add($"department={Uri.EscapeDataString(department)}");
        }

        if (startUtc is not null)
        {
            query.Add($"startUtc={Uri.EscapeDataString(startUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))}");
        }

        if (endUtc is not null)
        {
            query.Add($"endUtc={Uri.EscapeDataString(endUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))}");
        }

        var route = $"api/companies/{companyId}/executive-cockpit/widgets/{Uri.EscapeDataString(widgetKey)}{(query.Count == 0 ? string.Empty : "?" + string.Join("&", query))}";
        using var response = await _httpClient.GetAsync(route, cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<ExecutiveCockpitWidgetPayloadViewModel<TPayload>>(SerializerOptions, cancellationToken)
            : throw await CreateExceptionAsync(response, cancellationToken);
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

    private async Task<DepartmentDashboardCompositionViewModel?> GetDepartmentCompositionCoreAsync(Guid companyId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"api/companies/{companyId}/executive-cockpit/composition", cancellationToken);
            if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized)
            {
                return null;
            }

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<DepartmentDashboardCompositionViewModel>(SerializerOptions, cancellationToken);
            }

            throw await CreateExceptionAsync(response, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw CreateNetworkException(ex);
        }
    }

    private async Task<ExecutiveCockpitKpiDashboardViewModel?> GetKpisCoreAsync(
        Guid companyId,
        string? department,
        DateTime? startUtc,
        DateTime? endUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = new List<string>();
            if (!string.IsNullOrWhiteSpace(department))
            {
                query.Add($"department={Uri.EscapeDataString(department)}");
            }

            if (startUtc is not null)
            {
                query.Add($"startUtc={Uri.EscapeDataString(startUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))}");
            }

            if (endUtc is not null)
            {
                query.Add($"endUtc={Uri.EscapeDataString(endUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))}");
            }

            var route = $"api/companies/{companyId}/executive-cockpit/kpis{(query.Count == 0 ? string.Empty : "?" + string.Join("&", query))}";
            using var response = await _httpClient.GetAsync(route, cancellationToken);
            if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized)
            {
                return null;
            }

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<ExecutiveCockpitKpiDashboardViewModel>(SerializerOptions, cancellationToken);
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
            DepartmentSections = OfflineDepartmentComposition(companyId).Sections,
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

    private static ExecutiveCockpitKpiDashboardViewModel OfflineKpis(Guid companyId, string? department, DateTime? startUtc, DateTime? endUtc)
    {
        var end = endUtc ?? DateTime.UtcNow;
        var start = startUtc ?? end.AddDays(-7);
        var selectedDepartment = string.IsNullOrWhiteSpace(department) ? "Company" : department.Trim();
        return new ExecutiveCockpitKpiDashboardViewModel
        {
            CompanyId = companyId,
            GeneratedAtUtc = DateTime.UtcNow,
            StartUtc = start,
            EndUtc = end,
            Department = string.IsNullOrWhiteSpace(department) ? null : department.Trim(),
            Departments = ["Company"],
            Kpis =
            [
                new ExecutiveCockpitKpiTileViewModel
                {
                    Key = "open_tasks",
                    Label = "Open tasks",
                    Department = selectedDepartment,
                    CurrentValue = 0,
                    BaselineValue = 0,
                    DeltaValue = 0,
                    TrendDirection = "flat",
                    ComparisonLabel = "vs previous 7 days",
                    AsOfUtc = end,
                    Route = $"/tasks?companyId={companyId}"
                },
                new ExecutiveCockpitKpiTileViewModel
                {
                    Key = "pending_approvals",
                    Label = "Pending approvals",
                    Department = selectedDepartment,
                    CurrentValue = 0,
                    BaselineValue = 0,
                    DeltaValue = 0,
                    TrendDirection = "flat",
                    ComparisonLabel = "vs previous 7 days",
                    AsOfUtc = end,
                    Route = $"/approvals?companyId={companyId}"
                },
                new ExecutiveCockpitKpiTileViewModel
                {
                    Key = "workflow_exceptions",
                    Label = "Workflow exceptions",
                    Department = selectedDepartment,
                    CurrentValue = 0,
                    BaselineValue = 0,
                    DeltaValue = 0,
                    TrendDirection = "flat",
                    ComparisonLabel = "vs previous 7 days",
                    AsOfUtc = end,
                    Route = $"/workflows?companyId={companyId}"
                }
            ]
        };
    }

    private static DepartmentDashboardCompositionViewModel OfflineDepartmentComposition(Guid companyId) =>
        new()
        {
            CompanyId = companyId,
            GeneratedAtUtc = DateTime.UtcNow,
            Sections =
            [
                new DepartmentDashboardSectionViewModel
                {
                    Id = Guid.NewGuid(),
                    DepartmentKey = "company",
                    DisplayName = "Company",
                    DisplayOrder = 0,
                    IsVisible = true,
                    HasData = false,
                    IsEmpty = true,
                    Navigation = new DepartmentDashboardNavigationViewModel
                    {
                        Label = "Open agents",
                        Route = $"/agents?companyId={companyId}"
                    },
                    EmptyState = new DepartmentDashboardEmptyStateViewModel
                    {
                        Title = "No department data yet",
                        Message = "Department widgets will appear after agents, workflows, and tasks start producing activity."
                    }
                }
            ]
        };

    private sealed class ApiProblemResponse
    {
        public string? Title { get; set; }
        public string? Detail { get; set; }
        public Dictionary<string, string[]>? Errors { get; set; }
    }
}

public sealed class ExecutiveCockpitKpiDashboardViewModel
{
    public Guid CompanyId { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public string? Department { get; set; }
    public List<string> Departments { get; set; } = [];
    public List<ExecutiveCockpitKpiTileViewModel> Kpis { get; set; } = [];
    public List<ExecutiveCockpitAnomalyViewModel> Anomalies { get; set; } = [];
}

public sealed class ExecutiveCockpitWidgetPayloadViewModel<TPayload>
{
    public Guid CompanyId { get; set; }
    public string WidgetKey { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; }
    public DateTime? CacheTimestampUtc { get; set; }
    public TPayload? Payload { get; set; }
}

public sealed class ExecutiveCockpitKpiTileViewModel
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public decimal CurrentValue { get; set; }
    public decimal? BaselineValue { get; set; }
    public decimal? DeltaValue { get; set; }
    public decimal? DeltaPercentage { get; set; }
    public string TrendDirection { get; set; } = "unknown";
    public string ComparisonLabel { get; set; } = string.Empty;
    public DateTime AsOfUtc { get; set; }
    public string? Unit { get; set; }
    public string? Route { get; set; }
}

public sealed class ExecutiveCockpitAnomalyViewModel
{
    public string KpiKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public DateTime OccurredUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public decimal CurrentValue { get; set; }
    public decimal? BaselineValue { get; set; }
    public decimal? ThresholdValue { get; set; }
    public decimal? DeviationPercentage { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? Route { get; set; }
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
    public List<DepartmentDashboardSectionViewModel> DepartmentSections { get; set; } = [];
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

public sealed class DepartmentDashboardCompositionViewModel
{
    public Guid CompanyId { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public List<DepartmentDashboardSectionViewModel> Sections { get; set; } = [];
}

public sealed class DepartmentDashboardSectionViewModel
{
    public Guid Id { get; set; }
    public string DepartmentKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsVisible { get; set; }
    public string? Icon { get; set; }
    public bool HasData { get; set; }
    public Dictionary<string, int> SummaryCounts { get; set; } = [];
    public bool IsEmpty { get; set; }
    public DepartmentDashboardNavigationViewModel Navigation { get; set; } = new();
    public DepartmentDashboardEmptyStateViewModel EmptyState { get; set; } = new();
    public List<DepartmentDashboardWidgetViewModel> Widgets { get; set; } = [];
}

public sealed class DepartmentDashboardWidgetViewModel
{
    public Guid Id { get; set; }
    public string WidgetKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string WidgetType { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsVisible { get; set; }
    public string SummaryBinding { get; set; } = string.Empty;
    public int SummaryValue { get; set; }
    public bool HasData { get; set; }
    public bool IsEmpty { get; set; }
    public DepartmentDashboardNavigationViewModel Navigation { get; set; } = new();
    public DepartmentDashboardEmptyStateViewModel EmptyState { get; set; } = new();
}

public sealed class DepartmentDashboardNavigationViewModel
{
    public string Label { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
}

public sealed class DepartmentDashboardEmptyStateViewModel
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ActionLabel { get; set; }
    public string? ActionRoute { get; set; }
}
