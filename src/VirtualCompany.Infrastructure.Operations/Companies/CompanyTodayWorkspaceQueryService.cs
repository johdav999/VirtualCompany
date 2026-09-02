using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Focus;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyTodayWorkspaceQueryService : ITodayWorkspaceQueryService
{
    private const int PriorityLimit = 5;
    private const int MetricLimit = 4;
    private static readonly ActivitySource ActivitySource = new("VirtualCompany.TodayWorkspace");
    private static readonly Meter Meter = new("VirtualCompany.TodayWorkspace");
    private static readonly Counter<long> PartialResponses = Meter.CreateCounter<long>("today_workspace_partial_responses");
    private static readonly Histogram<double> QueryDuration = Meter.CreateHistogram<double>("today_workspace_query_duration_ms", "ms");

    private readonly ITodayWorkspaceLensResolver _lensResolver;
    private readonly IReadOnlyDictionary<string, ITodayWorkspaceContributor> _contributors;
    private readonly IExecutiveCockpitDashboardService _cockpit;
    private readonly IFocusEngine _focus;
    private readonly IExecutiveCockpitDashboardCache _cache;
    private readonly ITodayAgentActivityQueryService _agentActivity;
    private readonly ICompanyManualReviewService _manualReview;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CompanyTodayWorkspaceQueryService> _logger;

    public CompanyTodayWorkspaceQueryService(
        ITodayWorkspaceLensResolver lensResolver,
        IEnumerable<ITodayWorkspaceContributor> contributors,
        IExecutiveCockpitDashboardService cockpit,
        IFocusEngine focus,
        IExecutiveCockpitDashboardCache cache,
        ITodayAgentActivityQueryService agentActivity,
        ICompanyManualReviewService manualReview,
        TimeProvider timeProvider,
        ILogger<CompanyTodayWorkspaceQueryService> logger)
    {
        _lensResolver = lensResolver;
        _contributors = contributors.ToDictionary(x => x.Lens, StringComparer.OrdinalIgnoreCase);
        _cockpit = cockpit;
        _focus = focus;
        _cache = cache;
        _agentActivity = agentActivity;
        _manualReview = manualReview;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<TodayWorkspaceDto> GetAsync(
        GetTodayWorkspaceQuery query,
        CancellationToken cancellationToken)
    {
        if (query.CompanyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(query));
        using var activity = ActivitySource.StartActivity("today_workspace.compose");
        var stopwatch = Stopwatch.StartNew();
        var resolution = await _lensResolver.ResolveAsync(query.CompanyId, query.Lens, cancellationToken);
        activity?.SetTag("vc.today.lens", resolution.ActiveLens);
        activity?.SetTag("vc.company.id", query.CompanyId);

        var scope = ExecutiveCockpitCacheKeyBuilder.TodayScope(
            query.CompanyId,
            resolution.UserId,
            resolution.MembershipId,
            resolution.MembershipRole.ToStorageValue(),
            resolution.ResponsibilityRevision,
            resolution.ActiveLens,
            resolution.AvailableLenses.Select(x => x.Lens));
        var cached = await _cache.TryGetTodayAsync(scope, cancellationToken);
        if (cached is not null && cached.UserId == resolution.UserId &&
            string.Equals(cached.ActiveLens, resolution.ActiveLens, StringComparison.OrdinalIgnoreCase))
        {
            QueryDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
                [new KeyValuePair<string, object?>("outcome", "cache_hit")]);
            return cached.Workspace with { CacheTimestampUtc = cached.CachedAtUtc };
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var diagnostics = new List<TodayWorkspaceDiagnosticDto>();
        ExecutiveCockpitDashboardDto? executiveCockpit = null;
        var requiredContributorLenses = ResolveContributorLenses(resolution);
        if (resolution.ActiveLens == TodayWorkspaceLenses.Company ||
            requiredContributorLenses.Contains(TodayWorkspaceLenses.Finance, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                executiveCockpit = await _cockpit.GetAsync(
                    new GetExecutiveCockpitDashboardQuery(query.CompanyId),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch (KeyNotFoundException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Executive cockpit contribution failed for Today workspace company {CompanyId}.", query.CompanyId);
                diagnostics.Add(Unavailable("company", "cockpit_unavailable", "Company briefing and activity are temporarily unavailable."));
            }
        }

        IReadOnlyList<FocusItemDto> focusItems;
        try
        {
            focusItems = await _focus.GetFocusAsync(
                new GetDashboardFocusQuery(query.CompanyId, resolution.UserId),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Focus contribution failed for Today workspace company {CompanyId}.", query.CompanyId);
            diagnostics.Add(Unavailable("decisions", "focus_unavailable", "Personal decisions and tasks are temporarily unavailable."));
            focusItems = [];
        }

        var contributions = new List<TodayWorkspaceFeatureContribution>();
        foreach (var lens in requiredContributorLenses)
        {
            var access = resolution.AvailableLenses.First(x => string.Equals(x.Lens, lens, StringComparison.OrdinalIgnoreCase));
            if (!_contributors.TryGetValue(lens, out var contributor))
            {
                diagnostics.Add(Unavailable(lens, "contributor_missing", $"{TodayWorkspaceLenses.Label(lens)} data is unavailable."));
                contributions.Add(UnavailableContribution(lens, nowUtc));
                continue;
            }

            try
            {
                var contribution = await contributor.ContributeAsync(
                    new TodayWorkspaceContributorContext(query.CompanyId, nowUtc, resolution.ActiveLens, access, executiveCockpit),
                    cancellationToken);
                if (!string.Equals(contribution.Lens, lens, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Today contributor '{lens}' returned '{contribution.Lens}'.");
                }
                contributions.Add(contribution);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch (KeyNotFoundException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Today contributor {Lens} failed for company {CompanyId}.", lens, query.CompanyId);
                diagnostics.Add(Unavailable(lens, "contributor_failed", $"{TodayWorkspaceLenses.Label(lens)} data is temporarily unavailable."));
                contributions.Add(UnavailableContribution(lens, nowUtc));
            }
        }

        var activeAccess = resolution.AvailableLenses.First(x =>
            string.Equals(x.Lens, resolution.ActiveLens, StringComparison.OrdinalIgnoreCase));
        var priorityCandidates = contributions.SelectMany(x => x.PriorityCandidates).ToList();
        priorityCandidates.AddRange(MapFocus(focusItems, resolution, activeAccess, nowUtc));
        var selected = TodayWorkspacePriorityOrdering.Select(priorityCandidates, nowUtc, PriorityLimit);
        var priorities = selected.Select((candidate, index) => MapPriority(candidate, index + 1, nowUtc)).ToList();
        var metrics = SelectMetrics(contributions, resolution).Take(MetricLimit).ToList();
        var decisions = focusItems
            .Where(item => string.Equals(item.SourceType, "approval", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(item.ActionType, "review", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.PriorityScore)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .Take(5)
            .Select(item => new TodayWorkspaceDecisionDto(
                $"focus:{item.Id}", item.Title, item.Description, nowUtc, item.NavigationTarget,
                string.Equals(item.SourceType, "approval", StringComparison.OrdinalIgnoreCase)
                    ? "Shown because this decision needs your attention."
                    : VisibilityReason(activeAccess),
                Guid.TryParse(item.Id, out var approvalId) ? approvalId : null))
            .ToList();
        IReadOnlyList<TodayWorkspaceAgentUpdateDto> normalizedAgentUpdates;
        try
        {
            normalizedAgentUpdates = await _agentActivity.GetAsync(resolution, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Normalized agent activity failed for Today workspace company {CompanyId}.", query.CompanyId);
            diagnostics.Add(Unavailable("agents", "agent_activity_unavailable", "Agent activity is temporarily unavailable."));
            normalizedAgentUpdates = [];
        }
        var agentUpdates = BuildAgentUpdates(normalizedAgentUpdates, contributions)
            .Take(5)
            .ToList();
        var manualReview = await _manualReview.GetStatusAsync(query.CompanyId, resolution.CanRequestReview, cancellationToken);
        var situation = BuildSituationSummary(resolution.ActiveLens, priorities, executiveCockpit, diagnostics.Count > 0, nowUtc);

        var workspace = new TodayWorkspaceDto(
            query.CompanyId,
            new TodayWorkspaceHeaderDto(
                resolution.CompanyName,
                resolution.ActiveLens == TodayWorkspaceLenses.Company
                    ? $"Today at {resolution.CompanyName}"
                    : $"{TodayWorkspaceLenses.Label(resolution.ActiveLens)} today",
                "What needs attention, why it matters, and who owns the next step."),
            resolution.ActiveLens,
            resolution.AvailableLenses.Select(x => new TodayWorkspaceLensDto(
                x.Lens,
                x.Label,
                string.Equals(x.Lens, resolution.DefaultLens, StringComparison.OrdinalIgnoreCase),
                x.AvailabilityReason)).ToList(),
            situation,
            priorities,
            metrics,
            contributions.Select(x => x.Finance).FirstOrDefault(x => x is not null),
            contributions.Select(x => x.Sales).FirstOrDefault(x => x is not null),
            contributions.Select(x => x.Support).FirstOrDefault(x => x is not null),
            contributions.Select(x => x.Marketing).FirstOrDefault(x => x is not null),
            decisions,
            agentUpdates,
            nowUtc,
            null,
            diagnostics.Count > 0,
            diagnostics,
            new TodayWorkspaceResponsibilitySetupDto(
                resolution.ResponsibilitiesConfigured,
                resolution.CanManageResponsibilities,
                resolution.ResponsibilitiesConfigured
                    ? string.Empty
                    : resolution.CanManageResponsibilities
                        ? "Responsibility ownership is not configured yet. Set owners so each workspace reflects the right work."
                        : "Responsibility ownership is not configured yet. Ask a company owner or administrator to assign it.",
                $"/settings?companyId={query.CompanyId:D}"),
            manualReview);

        var cachedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await _cache.SetTodayAsync(
            scope,
            new CachedTodayWorkspaceDto(query.CompanyId, resolution.UserId, resolution.ActiveLens, cachedAtUtc, workspace),
            cancellationToken);
        if (workspace.IsPartial)
        {
            PartialResponses.Add(1, [new KeyValuePair<string, object?>("lens", resolution.ActiveLens)]);
        }
        QueryDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
            [new KeyValuePair<string, object?>("outcome", workspace.IsPartial ? "partial" : "success")]);
        return workspace;
    }

    private static IReadOnlyList<string> ResolveContributorLenses(TodayWorkspaceLensResolution resolution) =>
        resolution.ActiveLens == TodayWorkspaceLenses.Company
            ? resolution.AvailableLenses.Select(x => x.Lens)
                .Where(x => x != TodayWorkspaceLenses.Company)
                .OrderBy(x => TodayWorkspaceLenses.Ordered.ToList().IndexOf(x))
                .ToList()
            : [resolution.ActiveLens];

    private static IEnumerable<TodayWorkspacePriorityCandidate> MapFocus(
        IReadOnlyList<FocusItemDto> focusItems,
        TodayWorkspaceLensResolution resolution,
        TodayWorkspaceLensAccess activeAccess,
        DateTime observedUtc)
    {
        foreach (var item in focusItems)
        {
            var source = item.SourceType?.Trim().ToLowerInvariant() ?? "focus";
            if ((source is "finance_alert" or "finance-alert" or "anomaly") &&
                resolution.ActiveLens is not TodayWorkspaceLenses.Company and not TodayWorkspaceLenses.Finance)
            {
                continue;
            }

            yield return new TodayWorkspacePriorityCandidate(
                $"focus:{item.Id}",
                $"{source}:{item.Id}",
                source is "finance_alert" or "finance-alert" or "anomaly" ? TodayWorkspaceLenses.Finance : resolution.ActiveLens,
                item.Title,
                item.Description,
                activeAccess.ResponsiblePerson,
                activeAccess.WorkingAgent,
                string.Equals(item.ActionType, "review", StringComparison.OrdinalIgnoreCase)
                    ? "Review the evidence and record your decision."
                    : "Open the item and confirm the next step.",
                observedUtc,
                source,
                item.Id,
                item.NavigationTarget,
                DecisionRequired: source == "approval" || string.Equals(item.ActionType, "review", StringComparison.OrdinalIgnoreCase),
                Impact: item.PriorityScore,
                DirectlyOwned: true,
                Blocked: item.Description.Contains("blocked", StringComparison.OrdinalIgnoreCase),
                SeverityRank: item.PriorityScore,
                Confidence: 1m,
                VisibilityReason: source == "approval"
                    ? "Shown because this decision needs your attention."
                    : VisibilityReason(activeAccess));
        }
    }

    private static TodayWorkspacePriorityDto MapPriority(
        TodayWorkspacePriorityCandidate candidate,
        int rank,
        DateTime nowUtc) => new(
        candidate.Key,
        rank,
        candidate.Lens,
        candidate.WhatHappened,
        candidate.WhyItMatters,
        candidate.ResponsiblePerson,
        candidate.WorkingAgent,
        candidate.RequiredHumanAction,
        candidate.ObservedAtUtc,
        Freshness(candidate.ObservedAtUtc, nowUtc),
        candidate.EvidenceSourceType,
        candidate.EvidenceSourceId,
        candidate.DeepLink,
        candidate.DecisionRequired,
        candidate.DueUtc,
        candidate.DirectlyOwned,
        candidate.Confidence,
        candidate.VisibilityReason);

    private static string VisibilityReason(TodayWorkspaceLensAccess access) => access.IsPrimary
        ? $"Shown because you own the {access.Label} responsibility."
        : access.IsExecutiveOversight
            ? $"Shown because you have executive oversight of {access.Label}."
            : "Shown because you are directly involved in this work.";

    private static IEnumerable<TodayWorkspaceMetricDto> SelectMetrics(
        IReadOnlyList<TodayWorkspaceFeatureContribution> contributions,
        TodayWorkspaceLensResolution resolution) =>
        contributions.SelectMany(x => x.Metrics.Select(metric => new { x.Lens, Metric = metric }))
            .OrderByDescending(x => MetricStatusRank(x.Metric.Status))
            .ThenByDescending(x => resolution.AvailableLenses.First(access => access.Lens == x.Lens).IsPrimary)
            .ThenBy(x => TodayWorkspaceLenses.Ordered.ToList().IndexOf(x.Lens))
            .ThenBy(x => x.Metric.Key, StringComparer.Ordinal)
            .Select(x => x.Metric);

    private static IEnumerable<TodayWorkspaceAgentUpdateDto> BuildAgentUpdates(
        IReadOnlyList<TodayWorkspaceAgentUpdateDto> normalized,
        IReadOnlyList<TodayWorkspaceFeatureContribution> contributions)
    {
        var updates = normalized.Concat(contributions.SelectMany(x => x.AgentUpdates)).ToList();

        return updates
            .GroupBy(AgentUpdateDeduplicationKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(x => AgentStateRank(x.AgentState))
                .ThenByDescending(x => x.UpdatedUtc ?? x.ObservedAtUtc)
                .First())
            .OrderByDescending(x => AgentStateRank(x.AgentState))
            .ThenByDescending(x => x.UpdatedUtc ?? x.ObservedAtUtc)
            .ThenBy(x => x.Key, StringComparer.Ordinal);
    }

    private static string AgentUpdateDeduplicationKey(TodayWorkspaceAgentUpdateDto update) =>
        update.RelatedTaskId.HasValue ? $"task:{update.RelatedTaskId:N}" :
        update.RelatedWorkflowInstanceId.HasValue ? $"workflow:{update.RelatedWorkflowInstanceId:N}" :
        update.RelatedApprovalId.HasValue ? $"approval:{update.RelatedApprovalId:N}" : update.Key;

    private static int AgentStateRank(string? state) => state switch
    {
        TodayAgentStates.NeedsUser => 600,
        TodayAgentStates.Blocked => 500,
        TodayAgentStates.Recommended => 400,
        TodayAgentStates.Working => 300,
        TodayAgentStates.Monitoring => 200,
        TodayAgentStates.Completed => 100,
        _ => 0
    };

    private static TodayWorkspaceSituationSummaryDto BuildSituationSummary(
        string activeLens,
        IReadOnlyList<TodayWorkspacePriorityDto> priorities,
        ExecutiveCockpitDashboardDto? cockpit,
        bool partial,
        DateTime nowUtc)
    {
        var briefing = activeLens == TodayWorkspaceLenses.Company ? cockpit?.DailyBriefing : null;
        if (briefing is not null && nowUtc - briefing.GeneratedUtc <= TimeSpan.FromHours(24))
        {
            return new TodayWorkspaceSituationSummaryDto(
                briefing.Title,
                briefing.Summary,
                briefing.GeneratedUtc,
                Freshness(briefing.GeneratedUtc, nowUtc),
                false);
        }

        var headline = priorities.Count == 0 ? "No urgent priorities surfaced" : priorities[0].WhatHappened;
        var summary = priorities.Count switch
        {
            0 when partial => "Available sources have no urgent items; some sections could not be refreshed.",
            0 => "Available sources have no urgent items requiring action right now.",
            1 => "One priority currently needs your attention.",
            _ => $"{priorities.Count} ranked priorities currently need your attention."
        };
        return new TodayWorkspaceSituationSummaryDto(headline, summary, nowUtc, "fresh", true);
    }

    private static TodayWorkspaceFeatureContribution UnavailableContribution(string lens, DateTime observedUtc)
    {
        var message = $"{TodayWorkspaceLenses.Label(lens)} data is temporarily unavailable.";
        return lens switch
        {
            TodayWorkspaceLenses.Finance => new(lens, [], [], [], Finance: new(false, message, observedUtc, null, null, null, "unavailable", 0, [], "/finance")),
            TodayWorkspaceLenses.Sales => new(lens, [], [], [], Sales: new(false, message, observedUtc, 0, string.Empty, 0, 0, 0, 0, [], "/app/sales")),
            TodayWorkspaceLenses.Customers => new(lens, [], [], [], Support: new(false, message, observedUtc, 0, 0, 0, 0, 0, [], "/support")),
            TodayWorkspaceLenses.Marketing => new(lens, [], [], [], Marketing: new(false, message, observedUtc, 0, 0, 0, 0, [], "/marketing")),
            _ => new(lens, [], [], [])
        };
    }

    private static TodayWorkspaceDiagnosticDto Unavailable(string section, string code, string message) =>
        new(section, code, message);

    private static int MetricStatusRank(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "critical" or "error" or "breached" => 100,
        "attention" or "warning" or "decision" => 75,
        "opportunity" => 50,
        _ => 20
    };

    private static string Freshness(DateTime observedUtc, DateTime nowUtc)
    {
        var age = nowUtc - observedUtc;
        if (age <= TimeSpan.FromMinutes(15)) return "fresh";
        if (age <= TimeSpan.FromHours(6)) return "current";
        return "stale";
    }
}
