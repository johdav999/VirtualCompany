using System.Globalization;
using System.Text.Json.Serialization;

namespace VirtualCompany.Application.Cockpit;

public sealed record GetWeeklyWorkspaceQuery(Guid CompanyId, string? Lens = null, DateOnly? Week = null);
public sealed record WeeklyWorkspacePeriodDto(DateOnly WeekStart, DateOnly WeekEnd, string Timezone,
    [property: JsonConverter(typeof(JsonStringEnumConverter<DayOfWeek>))] DayOfWeek WeekStartsOn, DateTime StartUtc, DateTime EndUtc, DateTime ActivityEndUtc,
    DateTime ComparisonStartUtc, DateTime ComparisonEndUtc, bool IsWeekToDate, string Label, string ComparisonLabel);

public static class WeeklyWorkspacePeriod
{
    public static WeeklyWorkspacePeriodDto Resolve(DateTime nowUtc, TimeZoneInfo timezone,
        DateOnly? requestedWeek = null, DayOfWeek weekStartsOn = DayOfWeek.Monday)
    {
        if (!Enum.IsDefined(weekStartsOn)) throw new ArgumentOutOfRangeException(nameof(weekStartsOn));
        nowUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, timezone);
        var selected = requestedWeek ?? DateOnly.FromDateTime(localNow);
        if (selected.Year is < 2000 or > 2100) throw new ArgumentOutOfRangeException(nameof(requestedWeek));
        var start = selected.AddDays(-(((int)selected.DayOfWeek - (int)weekStartsOn + 7) % 7));
        if (start.Year is < 2000 or > 2100) throw new ArgumentOutOfRangeException(nameof(requestedWeek), "Choose a week starting between 2000 and 2100.");
        var end = start.AddDays(7);
        var localStart = start.ToDateTime(TimeOnly.MinValue);
        if (localStart > localNow) throw new ArgumentException("Choose this week or an earlier week.", nameof(requestedWeek));
        var localEnd = end.ToDateTime(TimeOnly.MinValue);
        var partial = localNow < localEnd;
        var activityEnd = partial ? nowUtc : ToUtc(localEnd, timezone);
        var comparisonEnd = ToUtc((partial ? localNow : localEnd).AddDays(-7), timezone);
        return new(start, end.AddDays(-1), timezone.Id, weekStartsOn, ToUtc(localStart, timezone),
            ToUtc(localEnd, timezone), activityEnd, ToUtc(localStart.AddDays(-7), timezone), comparisonEnd, partial,
            $"{start:MMM d}–{end.AddDays(-1):MMM d, yyyy}",
            $"{start.AddDays(-7):MMM d}–{(partial ? DateOnly.FromDateTime(localNow).AddDays(-7) : start.AddDays(-1)):MMM d, yyyy}" +
                (partial ? " (same elapsed local interval)" : " (completed week)"));
    }

    // Some zones advance at midnight. Move a missing local time to its first valid instant.
    // Ambiguous local times use the zone's standard offset, consistently for both cutoffs.
    private static DateTime ToUtc(DateTime local, TimeZoneInfo timezone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (timezone.IsInvalidTime(local)) local = local.AddMinutes(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, timezone);
    }
}

public sealed record WeeklyWorkspaceSourceDto(string Id, string Title, DateTime OccurredUtc, string DeepLink);
public sealed record WeeklyWorkspaceMetricDto(string Key, string Label, decimal? Value, string DisplayValue,
    decimal? ComparisonValue, string ComparisonDisplayValue, string Kind, string Definition,
    string Coverage, IReadOnlyList<WeeklyWorkspaceSourceDto> Sources, bool IsComplete = true,
    DateTime? ObservedAtUtc = null, IReadOnlyList<WeeklyWorkspaceSourceDto>? ComparisonSources = null);
public sealed record WeeklyWorkspaceCommitmentDto(string Id, string Title, string Summary, DateTime? DueUtc,
    DateTime ObservedAtUtc, string DeepLink, string ResponsiblePerson, string? WorkingAgent);
public sealed record WeeklyWorkspaceContribution(string Lens, string Title,
    IReadOnlyList<WeeklyWorkspaceMetricDto> Metrics, IReadOnlyList<WeeklyWorkspaceCommitmentDto> Commitments,
    IReadOnlyList<string> CoverageGaps, string? ResponsiblePerson = null, string? WorkingAgent = null);
public sealed record WeeklyWorkspaceContributorContext(Guid CompanyId, Guid UserId, DateTime NowUtc,
    WeeklyWorkspacePeriodDto Period, TodayWorkspaceLensAccess Access);
public sealed record WeeklyWorkspaceDto(Guid CompanyId, string CompanyName, string ActiveLens,
    IReadOnlyList<TodayWorkspaceLensDto> AvailableLenses, WeeklyWorkspacePeriodDto Period,
    IReadOnlyList<WeeklyWorkspaceContribution> Contributions, DateTime GeneratedAtUtc, bool IsPartial,
    IReadOnlyList<TodayWorkspaceDiagnosticDto> Diagnostics, string CalendarMeaning);
public interface IWeeklyWorkspaceContributor
{
    string Lens { get; }
    Task<WeeklyWorkspaceContribution> ContributeAsync(WeeklyWorkspaceContributorContext context, CancellationToken token);
}
public interface IWeeklyWorkspaceQueryService
{
    Task<WeeklyWorkspaceDto> GetAsync(GetWeeklyWorkspaceQuery query, CancellationToken token);
}

public static class WeeklyWorkspaceMeasures
{
    public const int SourceLimit = 2000;
    public static WeeklyWorkspaceMetricDto Activity(string key, string label,
        IReadOnlyList<WeeklyWorkspaceSourceDto> sources, IReadOnlyList<WeeklyWorkspaceSourceDto> previous,
        string definition, string coverage, bool complete = true) =>
        new(key, label, sources.Count, sources.Count.ToString(CultureInfo.InvariantCulture), previous.Count,
            previous.Count.ToString(CultureInfo.InvariantCulture), "activity", definition, coverage,
            sources, complete, ComparisonSources: previous);
    public static WeeklyWorkspaceMetricDto Unavailable(string key, string label, string kind, string reason) =>
        new(key, label, null, "Unavailable", null, "Unavailable", kind, reason, reason, [], false);
}
