using System.Text.Json.Serialization;

namespace VirtualCompany.Web.Services;

public sealed record WeeklyWorkspacePeriodViewModel(DateOnly WeekStart, DateOnly WeekEnd, string Timezone,
    [property: JsonConverter(typeof(JsonStringEnumConverter<DayOfWeek>))] DayOfWeek WeekStartsOn, DateTime StartUtc, DateTime EndUtc, DateTime ActivityEndUtc,
    DateTime ComparisonStartUtc, DateTime ComparisonEndUtc, bool IsWeekToDate, string Label, string ComparisonLabel);
public sealed record WeeklyWorkspaceSourceViewModel(string Id, string Title, DateTime OccurredUtc, string DeepLink);
public sealed record WeeklyWorkspaceMetricViewModel(string Key, string Label, decimal? Value, string DisplayValue,
    decimal? ComparisonValue, string ComparisonDisplayValue, string Kind, string Definition,
    string Coverage, IReadOnlyList<WeeklyWorkspaceSourceViewModel> Sources, bool IsComplete = true,
    DateTime? ObservedAtUtc = null, IReadOnlyList<WeeklyWorkspaceSourceViewModel>? ComparisonSources = null);
public sealed record WeeklyWorkspaceCommitmentViewModel(string Id, string Title, string Summary, DateTime? DueUtc,
    DateTime ObservedAtUtc, string DeepLink, string ResponsiblePerson, string? WorkingAgent);
public sealed record WeeklyWorkspaceContributionViewModel(string Lens, string Title,
    IReadOnlyList<WeeklyWorkspaceMetricViewModel> Metrics, IReadOnlyList<WeeklyWorkspaceCommitmentViewModel> Commitments,
    IReadOnlyList<string> CoverageGaps, string? ResponsiblePerson = null, string? WorkingAgent = null);
public sealed record WeeklyWorkspaceViewModel(Guid CompanyId, string CompanyName, string ActiveLens,
    IReadOnlyList<TodayWorkspaceLensViewModel> AvailableLenses, WeeklyWorkspacePeriodViewModel Period,
    IReadOnlyList<WeeklyWorkspaceContributionViewModel> Contributions, DateTime GeneratedAtUtc, bool IsPartial,
    IReadOnlyList<TodayWorkspaceDiagnosticViewModel> Diagnostics, string CalendarMeaning);
