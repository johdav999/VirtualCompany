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

    public async Task<DashboardFinanceSnapshotViewModel?> GetFinanceSnapshotAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return OfflineFinanceSnapshot(companyId);
        }

        using var response = await _httpClient.GetAsync($"api/dashboard/finance-snapshot?companyId={companyId:D}", cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<DashboardFinanceSnapshotViewModel>(SerializerOptions, cancellationToken)
            : throw await CreateExceptionAsync(response, cancellationToken);
    }

    public async Task<ExecutiveCockpitFinanceAlertDetailViewModel?> GetFinanceAlertDetailAsync(
        Guid companyId,
        Guid alertId,
        CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return OfflineDashboard(companyId).Finance?.LowCashAlert;
        }

        using var response = await _httpClient.GetAsync(
            $"api/companies/{companyId:D}/executive-cockpit/finance-alerts/{alertId:D}",
            cancellationToken);
        if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized)
        {
            return null;
        }

        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<ExecutiveCockpitFinanceAlertDetailViewModel>(SerializerOptions, cancellationToken)
            : throw await CreateExceptionAsync(response, cancellationToken);
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
            BusinessSignals =
            [
                new BusinessSignalViewModel
                {
                    Type = "operationalLoad",
                    Severity = "info",
                    Title = "Workspace activity is still bootstrapping",
                    Summary = "Signals appear here as tasks and approvals accumulate.",
                    ActionLabel = "Open tasks",
                    ActionUrl = $"/tasks?companyId={companyId:D}",
                    DetectedAtUtc = DateTime.UtcNow
                }
            ],
            SummaryKpis = [],
            DepartmentSections = OfflineDepartmentComposition(companyId).Sections,
            Finance = new ExecutiveCockpitFinanceViewModel
            {
                CashPosition = new ExecutiveCockpitFinanceCashWidgetViewModel
                {
                    Amount = 0m,
                    Currency = "USD",
                    DisplayValue = "USD 0.00",
                    TrendDirection = "flat",
                    TrendAmount = 0m,
                    TrendDisplay = "No cash movement vs previous 7 days",
                    LastRefreshedUtc = DateTime.UtcNow,
                    Route = FinanceRoutes.WithCompanyContext(FinanceRoutes.CashPosition, companyId)
                },
                Runway = new ExecutiveCockpitFinanceRunwayWidgetViewModel
                {
                    EstimatedRunwayDays = null,
                    EstimatedRunwayMonths = null,
                    DisplayValue = "Runway unavailable",
                    Status = "missing",
                    StatusLabel = "Healthy",
                    Route = FinanceRoutes.WithCompanyContext(FinanceRoutes.CashPosition, companyId)
                },
                FinancialHealth = new ExecutiveCockpitFinancialHealthViewModel
                {
                    Status = "healthy",
                    Title = "Financial health is stable",
                    Summary = "No persisted finance insights are available in offline mode.",
                    ActiveInsightCount = 0,
                    CriticalInsightCount = 0,
                    HighInsightCount = 0
                },
                TopActions = [],
                InsightsFeed =
                [
                    new ExecutiveCockpitFinanceInsightFeedItemViewModel
                    {
                        GroupKey = "offline-finance",
                        Severity = "low",
                        Title = "Finance insight feed",
                        Summary = "Persisted finance insights appear here when the live backend is connected.",
                        Recommendation = "Open the finance workspace after the backend is available.",
                        OccurrenceCount = 1,
                        EntitySummary = "Offline workspace",
                        LatestUpdatedUtc = DateTime.UtcNow
                    }
                ],
                AvailableActions =
                [
                    new ExecutiveCockpitFinanceActionViewModel
                    {
                        Key = "open_finance_summary",
                        Label = "Open finance summary",
                        IsEnabled = true,
                        Route = FinanceRoutes.WithCompanyContext(FinanceRoutes.MonthlySummary, companyId),
                        OrchestrationEndpoint = null,
                        HttpMethod = "GET"
                    }
                ],
                DeepLinks =
                [
                    new ExecutiveCockpitDeepLinkViewModel { Key = "finance_workspace", Label = "Finance workspace", Route = FinanceRoutes.WithCompanyContext(FinanceRoutes.Home, companyId) }
                ]
            },
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

    private static DashboardFinanceSnapshotViewModel OfflineFinanceSnapshot(Guid companyId) =>
        new()
        {
            CompanyId = companyId,
            CurrentCashBalance = 0m,
            ExpectedIncomingCash = 0m,
            ExpectedOutgoingCash = 0m,
            OverdueReceivables = 0m,
            UpcomingPayables = 0m,
            Currency = "USD",
            AsOfUtc = DateTime.UtcNow,
            UpcomingWindowDays = 30,
            Cash = 0m,
            BurnRate = 0m,
            RunwayDays = null,
            RiskLevel = "missing",
            HasFinanceData = false
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
    public List<BusinessSignalViewModel> BusinessSignals { get; set; } = [];
    public DateTime? CacheTimestampUtc { get; set; }
    public ExecutiveCockpitDailyBriefingViewModel? DailyBriefing { get; set; }
    public List<ExecutiveCockpitSummaryKpiViewModel> SummaryKpis { get; set; } = [];
    public DashboardFinanceSnapshotViewModel? FinanceSnapshot { get; set; }
    public ExecutiveCockpitFinanceViewModel? Finance { get; set; }
    public ExecutiveCockpitPendingApprovalsViewModel PendingApprovals { get; set; } = new();
    public List<ExecutiveCockpitAlertViewModel> Alerts { get; set; } = [];
    public List<ExecutiveCockpitDepartmentKpiViewModel> DepartmentKpis { get; set; } = [];
    public List<DepartmentDashboardSectionViewModel> DepartmentSections { get; set; } = [];
    public List<ExecutiveCockpitActivityItemViewModel> RecentActivity { get; set; } = [];
    public ExecutiveCockpitSetupStateViewModel SetupState { get; set; } = new();
    public ExecutiveCockpitEmptyStateFlagsViewModel EmptyStateFlags { get; set; } = new();
}

public sealed class BusinessSignalViewModel
{
    public string Type { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public decimal? MetricValue { get; set; }
    public string? MetricLabel { get; set; }
    public string? ActionLabel { get; set; }
    public string? ActionUrl { get; set; }
    public DateTime DetectedAtUtc { get; set; }
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
    public string DeltaText { get; set; } = string.Empty;
    public string ComparisonLabel { get; set; } = string.Empty;
    public string? StatusHint { get; set; }
    public bool IsEmpty { get; set; }
}

public sealed class DashboardFinanceSnapshotViewModel
{
    public Guid CompanyId { get; set; }
    public decimal CurrentCashBalance { get; set; }
    public decimal ExpectedIncomingCash { get; set; }
    public decimal ExpectedOutgoingCash { get; set; }
    public decimal OverdueReceivables { get; set; }
    public decimal UpcomingPayables { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime AsOfUtc { get; set; }
    public int UpcomingWindowDays { get; set; } = 30;
    public decimal Cash { get; set; }
    public decimal BurnRate { get; set; }
    public int? RunwayDays { get; set; }
    public string RiskLevel { get; set; } = string.Empty;
    public bool HasFinanceData { get; set; }
}

public sealed class ExecutiveCockpitFinanceViewModel
{
    public ExecutiveCockpitFinanceCashWidgetViewModel CashPosition { get; set; } = new();
    public ExecutiveCockpitFinanceRunwayWidgetViewModel Runway { get; set; } = new();
    public ExecutiveCockpitFinanceAlertDetailViewModel? LowCashAlert { get; set; }
    public ExecutiveCockpitFinancialHealthViewModel FinancialHealth { get; set; } = new();
    public List<ExecutiveCockpitFinanceInsightFeedItemViewModel> TopActions { get; set; } = [];
    public List<ExecutiveCockpitFinanceInsightFeedItemViewModel> InsightsFeed { get; set; } = [];
    public List<ExecutiveCockpitFinanceActionViewModel> AvailableActions { get; set; } = [];
    public List<ExecutiveCockpitDeepLinkViewModel> DeepLinks { get; set; } = [];
}

public sealed class ExecutiveCockpitFinanceCashWidgetViewModel
{
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string DisplayValue { get; set; } = string.Empty;
    public string TrendDirection { get; set; } = "flat";
    public decimal? TrendAmount { get; set; }
    public string TrendDisplay { get; set; } = string.Empty;
    public DateTime LastRefreshedUtc { get; set; }
    public string Route { get; set; } = string.Empty;
}

public sealed class ExecutiveCockpitFinanceRunwayWidgetViewModel
{
    public int? EstimatedRunwayDays { get; set; }
    public decimal? EstimatedRunwayMonths { get; set; }
    public string DisplayValue { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
}

public sealed class ExecutiveCockpitFinanceAlertDetailViewModel
{
    public Guid AlertId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<string> ContributingFactors { get; set; } = [];
    public List<ExecutiveCockpitFinanceActionViewModel> AvailableActions { get; set; } = [];
    public List<ExecutiveCockpitDeepLinkViewModel> Links { get; set; } = [];
    public string Route { get; set; } = string.Empty;
}

public sealed class ExecutiveCockpitFinancialHealthViewModel
{
    public string Status { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public int ActiveInsightCount { get; set; }
    public int CriticalInsightCount { get; set; }
    public int HighInsightCount { get; set; }
    public DateTime? LastUpdatedUtc { get; set; }
}

public sealed class ExecutiveCockpitFinanceInsightFeedItemViewModel
{
    public string GroupKey { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public int OccurrenceCount { get; set; }
    public string EntitySummary { get; set; } = string.Empty;
    public DateTime LatestUpdatedUtc { get; set; }
    public string? Route { get; set; }
}

public sealed class ExecutiveCockpitFinanceActionViewModel
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string? Route { get; set; }
    public string? OrchestrationEndpoint { get; set; }
    public string HttpMethod { get; set; } = "GET";
    public Guid? TargetId { get; set; }
}

public sealed class ExecutiveCockpitDeepLinkViewModel
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
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
    public IReadOnlyList<DepartmentSignalSummaryViewModel> VisibleSignals =>
        DepartmentSignalSummaryViewModel.SelectTopSignals(DepartmentKey, SummaryCounts);

    public DepartmentDashboardRepresentativeViewModel? Representative { get; set; }
    public DepartmentDashboardNavigationViewModel Navigation { get; set; } = new();
    public DepartmentDashboardEmptyStateViewModel EmptyState { get; set; } = new();
    public List<DepartmentDashboardWidgetViewModel> Widgets { get; set; } = [];
}

public sealed class DepartmentDashboardRepresentativeViewModel
{
    public Guid AgentId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
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

public sealed class DepartmentSignalSummaryViewModel
{
    private static readonly IReadOnlyDictionary<string, int> SignalPriority = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["blocked_workflows"] = 0,
        ["pending_approvals"] = 1,
        ["blocked_tasks"] = 2,
        ["overdue_tasks"] = 3,
        ["open_tasks"] = 4,
        ["workflow_exceptions"] = 5,
        ["active_incidents"] = 6,
        ["unresolved_alerts"] = 7,
        ["recent_alerts"] = 8,
        ["active_agents"] = 9,
        ["backlog_items"] = 10
    };

    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public int Value { get; init; }
    public int Priority { get; init; }

    public static IReadOnlyList<DepartmentSignalSummaryViewModel> SelectTopSignals(
        string? departmentKey,
        IReadOnlyDictionary<string, int>? summaryCounts)
    {
        if (summaryCounts is null || summaryCounts.Count == 0)
        {
            return [];
        }

        // Keep department cards scannable by suppressing zero-value rollups and surfacing the three most actionable signals first.
        return summaryCounts
            .Where(item => item.Value > 0)
            .Select(item => new DepartmentSignalSummaryViewModel
            {
                Key = item.Key,
                Label = ToLabel(item.Key),
                Value = item.Value,
                Priority = SignalPriority.TryGetValue(item.Key, out var priority) ? priority : int.MaxValue
            })
            .OrderBy(item => item.Priority)
            .ThenByDescending(item => item.Value)
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();
    }

    private static string ToLabel(string key) =>
        string.Join(" ",
            key.Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(word => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..]));
}
