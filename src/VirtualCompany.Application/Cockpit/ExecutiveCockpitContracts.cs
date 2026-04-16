namespace VirtualCompany.Application.Cockpit;

using VirtualCompany.Domain.Enums;

public sealed record GetExecutiveCockpitDashboardQuery(Guid CompanyId);

public sealed record GetExecutiveCockpitWidgetPayloadQuery(
    Guid CompanyId,
    string WidgetKey,
    string? Department,
    DateTime? StartUtc,
    DateTime? EndUtc);

public sealed record ExecutiveCockpitDashboardDto(
    Guid CompanyId,
    string CompanyName,
    DateTime GeneratedAtUtc,
    DateTime? CacheTimestampUtc,
    IReadOnlyList<ExecutiveCockpitSummaryKpiDto> SummaryKpis,
    ExecutiveCockpitDailyBriefingDto? DailyBriefing,
    ExecutiveCockpitPendingApprovalsDto PendingApprovals,
    IReadOnlyList<ExecutiveCockpitAlertDto> Alerts,
    IReadOnlyList<ExecutiveCockpitDepartmentKpiDto> DepartmentKpis,
    IReadOnlyList<DepartmentDashboardSectionDto> DepartmentSections,
    IReadOnlyList<ExecutiveCockpitActivityItemDto> RecentActivity,
    ExecutiveCockpitSetupStateDto SetupState,
    ExecutiveCockpitEmptyStateFlagsDto EmptyStateFlags);

public sealed record ExecutiveCockpitSummaryKpiDto(
    string Key,
    string Label,
    int CurrentValue,
    int? PreviousValue,
    string TrendDirection,
    int? DeltaValue,
    decimal? DeltaPercentage,
    string DeltaText,
    string ComparisonLabel,
    string? StatusHint,
    bool IsEmpty);

public static class ExecutiveCockpitWidgetKeys
{
    public const string SummaryKpis = "summary-kpis";
    public const string DailyBriefing = "daily-briefing";
    public const string PendingApprovals = "pending-approvals";
    public const string Alerts = "alerts";
    public const string DepartmentKpis = "department-kpis";
    public const string DepartmentSections = "department-sections";
    public const string RecentActivity = "recent-activity";
    public const string Kpis = "kpis";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        SummaryKpis, DailyBriefing, PendingApprovals, Alerts, DepartmentKpis, DepartmentSections, RecentActivity, Kpis
    };
}

public static class ExecutiveCockpitTrendDirections
{
    public const string Up = "up";
    public const string Down = "down";
    public const string Flat = "flat";
    public const string Unknown = "unknown";
}

public sealed record ExecutiveCockpitKpiTrend(
    string Direction,
    int? DeltaValue,
    decimal? DeltaPercentage,
    string DeltaText,
    string ComparisonLabel);

public static class ExecutiveCockpitKpiTrendCalculator
{
    public const string DefaultComparisonLabel = "vs previous 7 days";

    public static ExecutiveCockpitKpiTrend Calculate(
        int currentValue,
        int? previousValue,
        string comparisonLabel = DefaultComparisonLabel)
    {
        if (previousValue is null)
        {
            return new ExecutiveCockpitKpiTrend(
                ExecutiveCockpitTrendDirections.Unknown,
                null,
                null,
                "Trend unavailable",
                "No prior period data");
        }

        var delta = currentValue - previousValue.Value;
        var direction = delta switch
        {
            > 0 => ExecutiveCockpitTrendDirections.Up,
            < 0 => ExecutiveCockpitTrendDirections.Down,
            _ => ExecutiveCockpitTrendDirections.Flat
        };

        decimal? deltaPercentage = previousValue.Value == 0
            ? null
            : Math.Round(delta / (decimal)previousValue.Value * 100m, 1, MidpointRounding.AwayFromZero);

        var deltaText = direction == ExecutiveCockpitTrendDirections.Flat
            ? $"No change {comparisonLabel}"
            : $"{FormatDelta(delta)} {comparisonLabel}";

        if (deltaPercentage is not null && direction != ExecutiveCockpitTrendDirections.Flat)
        {
            deltaText = $"{deltaText} ({FormatDelta(deltaPercentage.Value)}%)";
        }

        return new ExecutiveCockpitKpiTrend(direction, delta, deltaPercentage, deltaText, comparisonLabel);
    }

    private static string FormatDelta(int delta) =>
        delta > 0 ? $"+{delta}" : delta.ToString();

    private static string FormatDelta(decimal delta) =>
        delta > 0 ? $"+{delta:0.#}" : delta.ToString("0.#");
}

public sealed record ExecutiveCockpitDailyBriefingDto(
    Guid Id,
    string Title,
    string Summary,
    DateTime GeneratedUtc,
    string? Route);

public sealed record ExecutiveCockpitPendingApprovalsDto(
    int TotalCount,
    IReadOnlyList<ExecutiveCockpitApprovalItemDto> Items,
    string Route);

public sealed record ExecutiveCockpitApprovalItemDto(
    Guid Id,
    string ApprovalType,
    string TargetEntityType,
    Guid TargetEntityId,
    string Status,
    string Summary,
    DateTime CreatedUtc,
    string Route);

public sealed record ExecutiveCockpitAlertDto(
    Guid Id,
    string Severity,
    string Title,
    string Summary,
    string SourceType,
    Guid? SourceId,
    DateTime OccurredUtc,
    string? Route);

public sealed record ExecutiveCockpitDepartmentKpiDto(
    string Department,
    int ActiveAgents,
    int OpenTasks,
    int CompletedTasksLast7Days,
    int PendingApprovals,
    int ActiveWorkflows,
    string Route);

public sealed record ExecutiveCockpitActivityItemDto(
    Guid Id,
    string ActivityType,
    string Title,
    string Summary,
    DateTime OccurredUtc,
    string? Route);

public sealed record ExecutiveCockpitSetupStateDto(
    bool HasAgents,
    bool HasWorkflows,
    bool HasKnowledge,
    int AgentCount,
    int WorkflowCount,
    int KnowledgeDocumentCount,
    bool IsInitialSetupEmpty);

public sealed record ExecutiveCockpitEmptyStateFlagsDto(
    bool NoAgents,
    bool NoWorkflows,
    bool NoKnowledge,
    bool NoRecentActivity,
    bool NoPendingApprovals,
    bool NoAlerts);

public sealed record GetDepartmentDashboardConfigurationQuery(Guid CompanyId);

public sealed record DepartmentDashboardConfigurationDto(
    Guid CompanyId,
    DateTime GeneratedAtUtc,
    IReadOnlyList<DepartmentDashboardSectionDto> Sections);

public sealed record DepartmentDashboardSectionDto(
    Guid Id,
    string DepartmentKey,
    string DisplayName,
    int DisplayOrder,
    bool IsVisible,
    string? Icon,
    bool HasData,
    IReadOnlyDictionary<string, int> SummaryCounts,
    bool IsEmpty,
    DepartmentDashboardNavigationDto Navigation,
    DepartmentDashboardEmptyStateDto EmptyState,
    IReadOnlyList<DepartmentDashboardWidgetDto> Widgets);

public sealed record DepartmentDashboardWidgetDto(
    Guid Id,
    string WidgetKey,
    string Title,
    string WidgetType,
    int DisplayOrder,
    bool IsVisible,
    string SummaryBinding,
    int SummaryValue,
    bool HasData,
    bool IsEmpty,
    DepartmentDashboardNavigationDto Navigation,
    DepartmentDashboardEmptyStateDto EmptyState);

public sealed record DepartmentDashboardNavigationDto(
    string Label,
    string Route);

public sealed record DepartmentDashboardEmptyStateDto(
    string Title,
    string Message,
    string? ActionLabel,
    string? ActionRoute);

public static class DepartmentDashboardSummaryBindings
{
    public const string ActiveAgents = "active_agents";
    public const string OpenTasks = "open_tasks";
    public const string CompletedTasksLast7Days = "completed_tasks_7d";
    public const string PendingApprovals = "pending_approvals";
    public const string ActiveWorkflows = "active_workflows";
    public const string WorkflowExceptions = "workflow_exceptions";
}

public interface IDepartmentDashboardConfigurationService
{
    Task<DepartmentDashboardConfigurationDto> GetAsync(
        GetDepartmentDashboardConfigurationQuery query,
        CancellationToken cancellationToken);
}

public interface IExecutiveCockpitDashboardService
{
    Task<ExecutiveCockpitDashboardDto> GetAsync(
        GetExecutiveCockpitDashboardQuery query,
        CancellationToken cancellationToken);

    Task<ExecutiveCockpitWidgetPayloadDto> GetWidgetAsync(
        GetExecutiveCockpitWidgetPayloadQuery query,
        CancellationToken cancellationToken);
}

public sealed record ExecutiveCockpitWidgetPayloadDto(
    Guid CompanyId,
    string WidgetKey,
    DateTime GeneratedAtUtc,
    DateTime? CacheTimestampUtc,
    object? Payload);

public interface IExecutiveCockpitDashboardCache
{
    Task<CachedExecutiveCockpitDashboardDto?> TryGetAsync(
        Guid companyId,
        CancellationToken cancellationToken);

    Task<CachedExecutiveCockpitDashboardDto?> TryGetDashboardAsync(
        ExecutiveCockpitCacheScope scope,
        CancellationToken cancellationToken);

    Task SetAsync(
        CachedExecutiveCockpitDashboardDto snapshot,
        CancellationToken cancellationToken);

    Task SetDashboardAsync(
        ExecutiveCockpitCacheScope scope,
        CachedExecutiveCockpitDashboardDto snapshot,
        CancellationToken cancellationToken);

    Task<CachedExecutiveCockpitKpiDashboardDto?> TryGetKpiDashboardAsync(
        ExecutiveCockpitCacheScope scope,
        CancellationToken cancellationToken);

    Task SetKpiDashboardAsync(
        ExecutiveCockpitCacheScope scope,
        CachedExecutiveCockpitKpiDashboardDto snapshot,
        CancellationToken cancellationToken);

    Task<CachedExecutiveCockpitWidgetDto<TPayload>?> TryGetWidgetAsync<TPayload>(
        ExecutiveCockpitCacheScope scope,
        CancellationToken cancellationToken);

    Task SetWidgetAsync<TPayload>(
        ExecutiveCockpitCacheScope scope,
        CachedExecutiveCockpitWidgetDto<TPayload> snapshot,
        CancellationToken cancellationToken);

    Task InvalidateAsync(
        Guid companyId,
        CancellationToken cancellationToken);

    Task InvalidateAsync(
        ExecutiveCockpitCacheInvalidationEvent invalidationEvent,
        CancellationToken cancellationToken);
}

public interface IExecutiveCockpitDashboardCacheInvalidator
{
    Task InvalidateAsync(
        Guid companyId,
        CancellationToken cancellationToken);

    Task InvalidateAsync(
        ExecutiveCockpitCacheInvalidationEvent invalidationEvent,
        CancellationToken cancellationToken);
}

public sealed record ExecutiveCockpitCacheInvalidationEvent(
    Guid CompanyId,
    string TriggerType,
    string? EntityType,
    Guid? EntityId,
    DateTime OccurredAtUtc);

public sealed record ExecutiveCockpitCacheScope(
    Guid CompanyId,
    string EffectiveRole,
    IReadOnlyList<string> DepartmentFilters,
    DateTime? StartUtc,
    DateTime? EndUtc,
    string Identity);

public sealed record CachedExecutiveCockpitDashboardDto(
    Guid CompanyId,
    DateTime CachedAtUtc,
    ExecutiveCockpitDashboardDto Dashboard);

public sealed record CachedExecutiveCockpitKpiDashboardDto(
    Guid CompanyId,
    DateTime CachedAtUtc,
    ExecutiveCockpitKpiDashboardDto Dashboard);

public sealed record CachedExecutiveCockpitWidgetDto<TPayload>(
    Guid CompanyId,
    string WidgetKey,
    DateTime CachedAtUtc,
    TPayload Payload);