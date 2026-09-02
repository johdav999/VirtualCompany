using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TimeZoneConverter;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Focus;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyMonthlyWorkspaceQueryService : IMonthlyWorkspaceQueryService
{
    private static readonly ActivitySource ActivitySource = new("VirtualCompany.MonthlyWorkspace");
    private readonly ITodayWorkspaceLensResolver _lensResolver;
    private readonly IReadOnlyDictionary<string, IMonthlyWorkspaceContributor> _contributors;
    private readonly ITodayAgentActivityQueryService _agentActivity;
    private readonly IFocusEngine _focus;
    private readonly IExecutiveCockpitDashboardCache _cache;
    private readonly VirtualCompanyDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CompanyMonthlyWorkspaceQueryService> _logger;

    public CompanyMonthlyWorkspaceQueryService(
        ITodayWorkspaceLensResolver lensResolver,
        IEnumerable<IMonthlyWorkspaceContributor> contributors,
        ITodayAgentActivityQueryService agentActivity,
        IFocusEngine focus,
        IExecutiveCockpitDashboardCache cache,
        VirtualCompanyDbContext db,
        TimeProvider timeProvider,
        ILogger<CompanyMonthlyWorkspaceQueryService> logger)
    {
        _lensResolver = lensResolver;
        _contributors = contributors.ToDictionary(x => x.Lens, StringComparer.OrdinalIgnoreCase);
        _agentActivity = agentActivity;
        _focus = focus;
        _cache = cache;
        _db = db;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<MonthlyWorkspaceDto> GetAsync(GetMonthlyWorkspaceQuery query, CancellationToken cancellationToken)
    {
        if (query.CompanyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(query));
        using var activity = ActivitySource.StartActivity("monthly_workspace.compose");
        var resolution = await _lensResolver.ResolveAsync(query.CompanyId, query.Lens, cancellationToken);
        var timezoneId = await _db.Companies.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Id == query.CompanyId).Select(x => x.Timezone).SingleAsync(cancellationToken);
        var timezone = ResolveTimezone(timezoneId);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var period = MonthlyWorkspacePeriod.Resolve(nowUtc, timezone, query.Year, query.Month);
        activity?.SetTag("vc.monthly.lens", resolution.ActiveLens);
        activity?.SetTag("vc.monthly.period", $"{period.Year:D4}-{period.Month:D2}");

        var scope = ExecutiveCockpitCacheKeyBuilder.MonthlyScope(query.CompanyId, resolution.UserId,
            resolution.MembershipId, resolution.MembershipRole.ToStorageValue(), resolution.ResponsibilityRevision,
            resolution.ActiveLens, resolution.AvailableLenses.Select(x => x.Lens), period.StartUtc, period.EndUtc);
        var cached = await _cache.TryGetMonthlyAsync(scope, cancellationToken);
        if (cached is not null && cached.UserId == resolution.UserId &&
            string.Equals(cached.ActiveLens, resolution.ActiveLens, StringComparison.OrdinalIgnoreCase))
        {
            return cached.Workspace with { CacheTimestampUtc = cached.CachedAtUtc };
        }

        var diagnostics = new List<TodayWorkspaceDiagnosticDto>();
        var contributions = new List<MonthlyWorkspaceFeatureContribution>();
        foreach (var lens in RequiredLenses(resolution))
        {
            var access = resolution.AvailableLenses.First(x => string.Equals(x.Lens, lens, StringComparison.OrdinalIgnoreCase));
            if (!_contributors.TryGetValue(lens, out var contributor))
            {
                diagnostics.Add(new(lens, "monthly_contributor_missing", $"{TodayWorkspaceLenses.Label(lens)} monthly data is unavailable."));
                contributions.Add(UnavailableContribution(lens, access, period));
                continue;
            }

            try
            {
                var contribution = await contributor.ContributeAsync(
                    new(query.CompanyId, nowUtc, period, resolution.ActiveLens, access), cancellationToken);
                if (!string.Equals(contribution.Lens, lens, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Monthly contributor '{lens}' returned '{contribution.Lens}'.");
                contributions.Add(contribution);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (UnauthorizedAccessException) { throw; }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Monthly contributor {Lens} failed for company {CompanyId}.", lens, query.CompanyId);
                diagnostics.Add(new(lens, "monthly_contributor_failed", $"{TodayWorkspaceLenses.Label(lens)} monthly data is temporarily unavailable."));
                contributions.Add(UnavailableContribution(lens, access, period));
            }
        }

        var selectedCandidates = MonthlyWorkspacePriorityOrdering.Select(contributions.SelectMany(x => x.PriorityCandidates));
        var priorities = selectedCandidates.Select((x, index) => new TodayWorkspacePriorityDto(
            x.Key, index + 1, x.Lens, x.WhatChanged, x.WhyItMatters, x.ResponsiblePerson, x.WorkingAgent,
            x.RequiredHumanAction, x.ObservedAtUtc, Freshness(x.ObservedAtUtc, nowUtc), x.EvidenceSourceType,
            x.EvidenceSourceId, x.DeepLink, x.DecisionRequired, x.DueUtc, x.DirectlyOwned, x.Confidence,
            x.VisibilityReason ?? VisibilityReason(resolution.AvailableLenses.First(access =>
                string.Equals(access.Lens, x.Lens, StringComparison.OrdinalIgnoreCase))))).ToList();

        IReadOnlyList<FocusItemDto> focusItems;
        try
        {
            focusItems = await _focus.GetFocusAsync(new(query.CompanyId, resolution.UserId), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Monthly decisions failed for company {CompanyId}.", query.CompanyId);
            diagnostics.Add(new("decisions", "monthly_decisions_unavailable", "Decisions are temporarily unavailable."));
            focusItems = [];
        }
        var decisions = focusItems.Where(x => IsAllowed(x.SourceType, resolution.ActiveLens))
            .Where(x => string.Equals(x.SourceType, "approval", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(x.ActionType, "review", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.PriorityScore).ThenBy(x => x.Id, StringComparer.Ordinal).Take(5)
            .Select(x => new TodayWorkspaceDecisionDto($"focus:{x.Id}", x.Title, x.Description, nowUtc,
                x.NavigationTarget, "Shown because this decision needs a next-period commitment.",
                Guid.TryParse(x.Id, out var approvalId) ? approvalId : null)).ToList();

        IReadOnlyList<TodayWorkspaceAgentUpdateDto> normalizedOutcomes;
        try { normalizedOutcomes = await _agentActivity.GetAsync(resolution, cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException and not UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Monthly agent outcomes failed for company {CompanyId}.", query.CompanyId);
            diagnostics.Add(new("agents", "monthly_agent_outcomes_unavailable", "Agent outcomes are temporarily unavailable."));
            normalizedOutcomes = [];
        }
        var agentOutcomes = normalizedOutcomes.Concat(contributions.SelectMany(x => x.AgentOutcomes))
            .Where(x => (x.ObservedAtUtc >= period.StartUtc && x.ObservedAtUtc < period.EndUtc) ||
                        x.AgentState is TodayAgentStates.Blocked or TodayAgentStates.NeedsUser or TodayAgentStates.Recommended)
            .GroupBy(x => x.RelatedTaskId.HasValue ? $"task:{x.RelatedTaskId:N}" : x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderByDescending(y => y.UpdatedUtc ?? y.ObservedAtUtc).First())
            .OrderByDescending(x => AgentStateRank(x.AgentState)).ThenByDescending(x => x.UpdatedUtc ?? x.ObservedAtUtc)
            .Take(8).ToList();

        var coverage = contributions.SelectMany(x => x.SourceCoverage)
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToList();
        var currentSources = coverage.Count(x => x.State is "current" or "fresh");
        var results = SelectResults(contributions, resolution).Take(4).ToList();
        var isPartial = diagnostics.Count > 0 || coverage.Any(x => x.State is "unavailable" or "partial" or "stale");
        var summary = BuildSummary(resolution.ActiveLens, contributions, currentSources, coverage.Count, isPartial);
        var workspace = new MonthlyWorkspaceDto(query.CompanyId,
            new(resolution.CompanyName,
                resolution.ActiveLens == TodayWorkspaceLenses.Company ? $"Monthly review for {resolution.CompanyName}"
                    : $"{TodayWorkspaceLenses.Label(resolution.ActiveLens)} monthly review",
                "Material change, unresolved risk, decisions, and next-period ownership."),
            resolution.ActiveLens,
            resolution.AvailableLenses.Select(x => new TodayWorkspaceLensDto(x.Lens, x.Label,
                string.Equals(x.Lens, resolution.DefaultLens, StringComparison.OrdinalIgnoreCase), x.AvailabilityReason)).ToList(),
            period, summary, results, priorities, contributions.Select(x => x.Section).ToList(), decisions, agentOutcomes,
            coverage, nowUtc, null, isPartial, diagnostics,
            new(resolution.ResponsibilitiesConfigured, resolution.CanManageResponsibilities,
                resolution.ResponsibilitiesConfigured ? string.Empty : "Assign responsibility owners so monthly reviews reflect accountable work.",
                $"/settings/responsibilities?companyId={query.CompanyId:D}"));
        var cachedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await _cache.SetMonthlyAsync(scope,
            new(query.CompanyId, resolution.UserId, resolution.ActiveLens, cachedAtUtc, workspace), cancellationToken);
        return workspace;
    }

    private static IEnumerable<string> RequiredLenses(TodayWorkspaceLensResolution resolution) =>
        resolution.ActiveLens == TodayWorkspaceLenses.Company
            ? resolution.AvailableLenses.Select(x => x.Lens).OrderBy(x => TodayWorkspaceLenses.Ordered.ToList().IndexOf(x))
            : [resolution.ActiveLens];

    private static MonthlyWorkspaceFeatureContribution UnavailableContribution(
        string lens, TodayWorkspaceLensAccess access, MonthlyWorkspacePeriodDto period)
    {
        var route = lens switch
        {
            TodayWorkspaceLenses.Finance => "/finance",
            TodayWorkspaceLenses.Sales => "/sales",
            TodayWorkspaceLenses.Marketing => "/marketing",
            TodayWorkspaceLenses.Customers => "/support",
            _ => "/company-operation"
        };
        return new(lens,
            new(lens, TodayWorkspaceLenses.Label(lens), "Monthly source data is temporarily unavailable.", "unavailable",
                period.EndUtc, [], [], route, "Source unavailable.", false, route), [], [], [],
            [new(lens, TodayWorkspaceLenses.Label(lens), "unavailable", null, "The monthly source could not be loaded.", route)]);
    }

    private static IEnumerable<MonthlyWorkspaceMetricDto> SelectResults(
        IEnumerable<MonthlyWorkspaceFeatureContribution> contributions, TodayWorkspaceLensResolution resolution) =>
        contributions.SelectMany(x => x.Results.Select(result => new { x.Lens, Result = result }))
            .OrderBy(x => ResultRank(x.Result.Key))
            .ThenByDescending(x => resolution.AvailableLenses.First(a => a.Lens == x.Lens).IsPrimary)
            .ThenBy(x => x.Result.Key, StringComparer.Ordinal).Select(x => x.Result);

    private static int ResultRank(string key) => key switch
    {
        "finance.revenue" => 0,
        "finance.net_result" => 1,
        "sales.stage_movement" => 2,
        "support.sla" => 3,
        "sales.forecast" => 4,
        "support.volume" => 5,
        _ => 10
    };

    private static MonthlyWorkspaceSummaryDto BuildSummary(
        string lens, IReadOnlyList<MonthlyWorkspaceFeatureContribution> contributions,
        int currentSources, int totalSources, bool partial)
    {
        var risks = contributions.Count(x => x.Section.Status == "attention");
        var headline = risks > 0 ? $"{risks} area{(risks == 1 ? " needs" : "s need")} a next-period commitment"
            : "The monthly review has no unresolved high-signal exception";
        var summary = lens == TodayWorkspaceLenses.Company
            ? partial
                ? "Available sources were reviewed without filling gaps from unrelated records. Resolve the unavailable coverage before relying on a complete company view."
                : "Authoritative monthly results, unresolved risks, decisions, and completed work are assembled across assigned responsibilities."
            : $"The {TodayWorkspaceLenses.Label(lens)} review uses only authorized, period-aware source data and existing Work or Approval follow-up.";
        return new(headline, summary, $"{currentSources} of {totalSources} sources current", true);
    }

    private static bool IsAllowed(string? sourceType, string lens)
    {
        if (lens == TodayWorkspaceLenses.Company || string.Equals(sourceType, "approval", StringComparison.OrdinalIgnoreCase)) return true;
        var source = sourceType?.Trim().ToLowerInvariant() ?? string.Empty;
        return lens switch
        {
            TodayWorkspaceLenses.Finance => source.Contains("finance") || source.Contains("invoice") || source.Contains("bill"),
            TodayWorkspaceLenses.Sales => source.Contains("sales") || source.Contains("deal") || source.Contains("lead"),
            TodayWorkspaceLenses.Marketing => source.Contains("marketing") || source.Contains("campaign"),
            TodayWorkspaceLenses.Customers => source.Contains("support") || source.Contains("case") || source.Contains("customer"),
            _ => false
        };
    }

    private static string VisibilityReason(TodayWorkspaceLensAccess access) => access.IsPrimary
        ? $"Shown because you own the {access.Label} responsibility."
        : $"Shown because you have executive oversight of {access.Label}.";
    private static string Freshness(DateTime observed, DateTime now) => now - observed > TimeSpan.FromDays(45) ? "stale" : "current";
    private static int AgentStateRank(string? state) => state switch
    { TodayAgentStates.NeedsUser => 6, TodayAgentStates.Blocked => 5, TodayAgentStates.Recommended => 4,
      TodayAgentStates.Completed => 3, TodayAgentStates.Working => 2, _ => 1 };
    private static TimeZoneInfo ResolveTimezone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Utc;
        try { return TZConvert.GetTimeZoneInfo(id.Trim()); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }
}
