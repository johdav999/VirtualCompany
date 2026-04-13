namespace VirtualCompany.Application.Cockpit;

public sealed record GetExecutiveCockpitDashboardQuery(Guid CompanyId);

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

public interface IExecutiveCockpitDashboardService
{
    Task<ExecutiveCockpitDashboardDto> GetAsync(
        GetExecutiveCockpitDashboardQuery query,
        CancellationToken cancellationToken);
}

public interface IExecutiveCockpitDashboardCache
{
    Task<CachedExecutiveCockpitDashboardDto?> TryGetAsync(
        Guid companyId,
        CancellationToken cancellationToken);

    Task SetAsync(
        CachedExecutiveCockpitDashboardDto snapshot,
        CancellationToken cancellationToken);

    Task InvalidateAsync(
        Guid companyId,
        CancellationToken cancellationToken);
}

public sealed record CachedExecutiveCockpitDashboardDto(
    Guid CompanyId,
    DateTime CachedAtUtc,
    ExecutiveCockpitDashboardDto Dashboard);