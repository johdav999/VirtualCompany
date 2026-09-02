namespace VirtualCompany.Web.Services;

public sealed record MonthlyWorkspaceViewModel(
    Guid CompanyId,
    TodayWorkspaceHeaderViewModel Header,
    string ActiveLens,
    IReadOnlyList<TodayWorkspaceLensViewModel> AvailableLenses,
    MonthlyWorkspacePeriodViewModel Period,
    MonthlyWorkspaceSummaryViewModel ManagementSummary,
    IReadOnlyList<MonthlyWorkspaceMetricViewModel> Results,
    IReadOnlyList<TodayWorkspacePriorityViewModel> Priorities,
    IReadOnlyList<MonthlyWorkspaceSectionViewModel> Sections,
    IReadOnlyList<TodayWorkspaceDecisionViewModel> Decisions,
    IReadOnlyList<TodayWorkspaceAgentUpdateViewModel> AgentOutcomes,
    IReadOnlyList<MonthlyWorkspaceSourceCoverageViewModel> SourceCoverage,
    DateTime GeneratedAtUtc,
    DateTime? CacheTimestampUtc,
    bool IsPartial,
    IReadOnlyList<TodayWorkspaceDiagnosticViewModel> Diagnostics,
    TodayWorkspaceResponsibilitySetupViewModel? ResponsibilitySetup = null);

public sealed record MonthlyWorkspacePeriodViewModel(
    int Year, int Month, string Timezone, DateTime StartUtc, DateTime EndUtc,
    DateTime ComparisonStartUtc, DateTime ComparisonEndUtc, string Label, string ComparisonLabel);
public sealed record MonthlyWorkspaceSummaryViewModel(
    string Headline, string Summary, string CoverageSummary, bool IsDeterministicFallback);
public sealed record MonthlyWorkspaceMetricViewModel(
    string Key, string Label, decimal? Value, string DisplayValue, decimal? ComparisonValue,
    string ComparisonDisplayValue, string? Unit, string Status, DateTime ObservedAtUtc,
    string EvidenceSourceType, string DeepLink, bool IsAvailable = true, string? UnavailableReason = null);
public sealed record MonthlyWorkspaceFactViewModel(string Label, string Value, string Status = "current");
public sealed record MonthlyWorkspaceSectionViewModel(
    string Lens, string Title, string Summary, string Status, DateTime ObservedAtUtc,
    IReadOnlyList<MonthlyWorkspaceFactViewModel> Facts, IReadOnlyList<TodayWorkspaceFeatureItemViewModel> Items,
    string DeepLink, string CoverageSummary, bool IsAvailable = true, string? SetupDeepLink = null);
public sealed record MonthlyWorkspaceSourceCoverageViewModel(
    string Key, string Label, string State, DateTime? ObservedAtUtc, string Message, string? SetupDeepLink = null);
