namespace VirtualCompany.Application.Cockpit;

public sealed record GetMonthlyWorkspaceQuery(
    Guid CompanyId,
    string? Lens = null,
    int? Year = null,
    int? Month = null);

public sealed record MonthlyWorkspacePeriodDto(
    int Year,
    int Month,
    string Timezone,
    DateTime StartUtc,
    DateTime EndUtc,
    DateTime ComparisonStartUtc,
    DateTime ComparisonEndUtc,
    string Label,
    string ComparisonLabel);

public static class MonthlyWorkspacePeriod
{
    public static MonthlyWorkspacePeriodDto Resolve(
        DateTime nowUtc,
        TimeZoneInfo timezone,
        int? requestedYear = null,
        int? requestedMonth = null)
    {
        nowUtc = nowUtc.Kind == DateTimeKind.Utc ? nowUtc : nowUtc.ToUniversalTime();
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, timezone);
        var year = requestedYear ?? localNow.Year;
        var month = requestedMonth ?? localNow.Month;
        if (year is < 2000 or > 2100) throw new ArgumentOutOfRangeException(nameof(requestedYear));
        if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(requestedMonth));

        var localStart = DateTime.SpecifyKind(new DateTime(year, month, 1), DateTimeKind.Unspecified);
        var localEnd = localStart.AddMonths(1);
        var localComparisonStart = localStart.AddMonths(-1);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, timezone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(localEnd, timezone);
        var comparisonStartUtc = TimeZoneInfo.ConvertTimeToUtc(localComparisonStart, timezone);

        return new MonthlyWorkspacePeriodDto(
            year,
            month,
            timezone.Id,
            startUtc,
            endUtc,
            comparisonStartUtc,
            startUtc,
            $"{localStart:MMMM d}–{localEnd.AddDays(-1):d, yyyy}",
            $"{localComparisonStart:MMMM yyyy}");
    }
}

public sealed record MonthlyWorkspaceDto(
    Guid CompanyId,
    TodayWorkspaceHeaderDto Header,
    string ActiveLens,
    IReadOnlyList<TodayWorkspaceLensDto> AvailableLenses,
    MonthlyWorkspacePeriodDto Period,
    MonthlyWorkspaceSummaryDto ManagementSummary,
    IReadOnlyList<MonthlyWorkspaceMetricDto> Results,
    IReadOnlyList<TodayWorkspacePriorityDto> Priorities,
    IReadOnlyList<MonthlyWorkspaceSectionDto> Sections,
    IReadOnlyList<TodayWorkspaceDecisionDto> Decisions,
    IReadOnlyList<TodayWorkspaceAgentUpdateDto> AgentOutcomes,
    IReadOnlyList<MonthlyWorkspaceSourceCoverageDto> SourceCoverage,
    DateTime GeneratedAtUtc,
    DateTime? CacheTimestampUtc,
    bool IsPartial,
    IReadOnlyList<TodayWorkspaceDiagnosticDto> Diagnostics,
    TodayWorkspaceResponsibilitySetupDto? ResponsibilitySetup = null);

public sealed record MonthlyWorkspaceSummaryDto(
    string Headline,
    string Summary,
    string CoverageSummary,
    bool IsDeterministicFallback);

public sealed record MonthlyWorkspaceMetricDto(
    string Key,
    string Label,
    decimal? Value,
    string DisplayValue,
    decimal? ComparisonValue,
    string ComparisonDisplayValue,
    string? Unit,
    string Status,
    DateTime ObservedAtUtc,
    string EvidenceSourceType,
    string DeepLink,
    bool IsAvailable = true,
    string? UnavailableReason = null);

public sealed record MonthlyWorkspaceFactDto(string Label, string Value, string Status = "current");

public sealed record MonthlyWorkspaceSectionDto(
    string Lens,
    string Title,
    string Summary,
    string Status,
    DateTime ObservedAtUtc,
    IReadOnlyList<MonthlyWorkspaceFactDto> Facts,
    IReadOnlyList<TodayWorkspaceFeatureItemDto> Items,
    string DeepLink,
    string CoverageSummary,
    bool IsAvailable = true,
    string? SetupDeepLink = null);

public sealed record MonthlyWorkspaceSourceCoverageDto(
    string Key,
    string Label,
    string State,
    DateTime? ObservedAtUtc,
    string Message,
    string? SetupDeepLink = null);

public sealed record MonthlyWorkspaceContributorContext(
    Guid CompanyId,
    DateTime NowUtc,
    MonthlyWorkspacePeriodDto Period,
    string ActiveLens,
    TodayWorkspaceLensAccess Access);

public sealed record MonthlyWorkspacePriorityCandidate(
    string Key,
    string DeduplicationKey,
    string Lens,
    string WhatChanged,
    string WhyItMatters,
    string ResponsiblePerson,
    string? WorkingAgent,
    string RequiredHumanAction,
    DateTime ObservedAtUtc,
    string EvidenceSourceType,
    string? EvidenceSourceId,
    string DeepLink,
    bool DecisionRequired = false,
    DateTime? DueUtc = null,
    decimal MaterialChange = 0,
    bool UnresolvedRisk = false,
    bool ComplianceOrCloseDeadline = false,
    bool SustainedTrend = false,
    bool DirectlyOwned = false,
    decimal Confidence = 1m,
    string? VisibilityReason = null);

public sealed record MonthlyWorkspaceFeatureContribution(
    string Lens,
    MonthlyWorkspaceSectionDto Section,
    IReadOnlyList<MonthlyWorkspacePriorityCandidate> PriorityCandidates,
    IReadOnlyList<MonthlyWorkspaceMetricDto> Results,
    IReadOnlyList<TodayWorkspaceAgentUpdateDto> AgentOutcomes,
    IReadOnlyList<MonthlyWorkspaceSourceCoverageDto> SourceCoverage);

public interface IMonthlyWorkspaceContributor
{
    string Lens { get; }
    Task<MonthlyWorkspaceFeatureContribution> ContributeAsync(
        MonthlyWorkspaceContributorContext context,
        CancellationToken cancellationToken);
}

public interface IMonthlyWorkspaceQueryService
{
    Task<MonthlyWorkspaceDto> GetAsync(GetMonthlyWorkspaceQuery query, CancellationToken cancellationToken);
}

public static class MonthlyWorkspacePriorityOrdering
{
    public static IReadOnlyList<MonthlyWorkspacePriorityCandidate> Select(
        IEnumerable<MonthlyWorkspacePriorityCandidate> candidates,
        int limit = 5)
    {
        var selected = new List<MonthlyWorkspacePriorityCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates
                     .Where(IsValid)
                     .OrderByDescending(x => x.DecisionRequired)
                     .ThenByDescending(x => x.ComplianceOrCloseDeadline)
                     .ThenByDescending(x => x.UnresolvedRisk)
                     .ThenByDescending(x => x.MaterialChange)
                     .ThenByDescending(x => x.SustainedTrend)
                     .ThenByDescending(x => x.DirectlyOwned)
                     .ThenBy(x => x.DueUtc ?? DateTime.MaxValue)
                     .ThenByDescending(x => x.ObservedAtUtc)
                     .ThenBy(x => x.Key, StringComparer.Ordinal))
        {
            if (!seen.Add(candidate.DeduplicationKey)) continue;
            selected.Add(candidate);
            if (selected.Count >= Math.Clamp(limit, 3, 5)) break;
        }
        return selected;
    }

    private static bool IsValid(MonthlyWorkspacePriorityCandidate candidate) =>
        !string.IsNullOrWhiteSpace(candidate.Key) &&
        !string.IsNullOrWhiteSpace(candidate.DeduplicationKey) &&
        !string.IsNullOrWhiteSpace(candidate.WhatChanged) &&
        !string.IsNullOrWhiteSpace(candidate.WhyItMatters) &&
        !string.IsNullOrWhiteSpace(candidate.DeepLink);
}
