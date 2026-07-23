using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyExecutiveCockpitKpiQueryService : IExecutiveCockpitKpiQueryService
{
    private static readonly WorkTaskStatus[] OpenTaskStatuses =
    [
        WorkTaskStatus.New,
        WorkTaskStatus.InProgress,
        WorkTaskStatus.Blocked,
        WorkTaskStatus.AwaitingApproval
    ];

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyMembershipContextResolver _membershipContextResolver;
    private readonly IExecutiveCockpitDashboardCache _cache;
    private readonly TimeProvider _timeProvider;

    public CompanyExecutiveCockpitKpiQueryService(
        VirtualCompanyDbContext dbContext,
        ICompanyMembershipContextResolver membershipContextResolver,
        IExecutiveCockpitDashboardCache cache,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _membershipContextResolver = membershipContextResolver;
        _cache = cache;
        _timeProvider = timeProvider;
    }

    public async Task<ExecutiveCockpitKpiDashboardDto> GetAsync(
        GetExecutiveCockpitKpiDashboardQuery query,
        CancellationToken cancellationToken)
    {
        if (query.CompanyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(query));
        }

        var membership = await RequireMembershipAsync(query.CompanyId, cancellationToken);

        var (startUtc, endUtc) = NormalizeRange(query.StartUtc, query.EndUtc);
        var period = endUtc - startUtc;
        var baselineStartUtc = startUtc - period;
        var baselineEndUtc = startUtc;
        var selectedDepartment = NormalizeOptionalDepartment(query.Department);

        var scope = ExecutiveCockpitCacheKeyBuilder.KpiScope(
            query.CompanyId,
            membership.MembershipRole.ToStorageValue(),
            selectedDepartment,
            startUtc,
            endUtc);
        var cached = await _cache.TryGetKpiDashboardAsync(scope, cancellationToken);
        if (cached is not null)
        {
            return cached.Dashboard with { GeneratedAtUtc = _timeProvider.GetUtcNow().UtcDateTime };
        }

        var agents = await _dbContext.Agents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId)
            .Select(x => new AgentKpiRow(x.Id, x.Department, x.Status, x.Thresholds))
            .ToListAsync(cancellationToken);

        var agentDepartmentById = agents.ToDictionary(x => x.Id, x => NormalizeDepartment(x.Department));

        var workflowDefinitions = await _dbContext.WorkflowDefinitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.Active)
            .Select(x => new WorkflowDefinitionKpiRow(x.Id, x.Department))
            .ToListAsync(cancellationToken);

        var workflowDepartmentById = workflowDefinitions.ToDictionary(x => x.Id, x => NormalizeDepartment(x.Department));

        var departments = agents
            .Select(x => NormalizeDepartment(x.Department))
            .Concat(workflowDefinitions.Select(x => NormalizeDepartment(x.Department)))
            .DefaultIfEmpty("Company")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var includedDepartments = selectedDepartment is null
            ? departments
            : departments
                .Where(x => string.Equals(x, selectedDepartment, StringComparison.OrdinalIgnoreCase))
                .DefaultIfEmpty(selectedDepartment)
                .ToList();

        var tasks = await _dbContext.WorkTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId)
            .Select(x => new TaskKpiRow(
                x.Id,
                x.AssignedAgentId,
                x.Status,
                x.CreatedUtc,
                x.UpdatedUtc,
                x.CompletedUtc))
            .ToListAsync(cancellationToken);

        var approvals = await _dbContext.ApprovalRequests
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId)
            .Select(x => new ApprovalKpiRow(x.Id, x.AgentId, x.Status, x.CreatedUtc))
            .ToListAsync(cancellationToken);

        var workflowExceptions = await _dbContext.WorkflowExceptions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId)
            .Select(x => new WorkflowExceptionKpiRow(
                x.Id,
                x.WorkflowDefinitionId,
                x.Status,
                x.OccurredUtc))
            .ToListAsync(cancellationToken);

        var definitionsByDepartment = BuildMetricDefinitionsByDepartment(agents);
        var metricValues = new List<ExecutiveCockpitKpiMetricValue>();
        foreach (var department in includedDepartments)
        {
            var departmentAgentIds = agentDepartmentById
                .Where(x => string.Equals(x.Value, department, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Key)
                .ToHashSet();

            var departmentWorkflowDefinitionIds = workflowDepartmentById
                .Where(x => string.Equals(x.Value, department, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Key)
                .ToHashSet();

            metricValues.AddRange(BuildDepartmentMetrics(
                query.CompanyId,
                department,
                startUtc,
                endUtc,
                baselineStartUtc,
                baselineEndUtc,
                departmentAgentIds,
                departmentWorkflowDefinitionIds,
                tasks,
                approvals,
                workflowExceptions,
                agents,
                workflowDefinitions,
                definitionsByDepartment.GetValueOrDefault(department) ?? ExecutiveCockpitKpiMetricCatalog.Defaults));
        }

        var comparisonLabel = $"vs previous {Math.Max(1, (int)Math.Ceiling(period.TotalDays))} days";
        var tiles = metricValues
            .Select(x => ExecutiveCockpitKpiCalculator.CreateTile(x, comparisonLabel))
            .OrderBy(x => x.Department, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var anomalies = metricValues
            .Select(metric => (Metric: metric, Evaluation: ExecutiveCockpitKpiCalculator.DetectAnomaly(metric)))
            .Where(x => x.Evaluation is not null)
            .Select(x => new ExecutiveCockpitAnomalyDto(
                x.Metric.Key,
                x.Metric.Label,
                x.Metric.Department,
                x.Evaluation!.Severity,
                x.Metric.AsOfUtc,
                x.Evaluation.Reason,
                x.Metric.CurrentValue,
                x.Metric.BaselineValue,
                x.Evaluation.ThresholdValue,
                x.Evaluation.DeviationPercentage,
                x.Evaluation.Summary,
                x.Metric.Route))
            .OrderByDescending(x => SeverityRank(x.Severity))
            .ThenByDescending(x => x.OccurredUtc)
            .ThenBy(x => x.Department, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var dashboard = new ExecutiveCockpitKpiDashboardDto(
            query.CompanyId,
            _timeProvider.GetUtcNow().UtcDateTime,
            startUtc,
            endUtc,
            selectedDepartment,
            departments,
            tiles,
            anomalies);

        await _cache.SetKpiDashboardAsync(
            scope,
            new CachedExecutiveCockpitKpiDashboardDto(query.CompanyId, dashboard.GeneratedAtUtc, dashboard),
            cancellationToken);
        return dashboard;
    }

    private IEnumerable<ExecutiveCockpitKpiMetricValue> BuildDepartmentMetrics(
        Guid companyId,
        string department,
        DateTime startUtc,
        DateTime endUtc,
        DateTime baselineStartUtc,
        DateTime baselineEndUtc,
        IReadOnlySet<Guid> departmentAgentIds,
        IReadOnlySet<Guid> departmentWorkflowDefinitionIds,
        IReadOnlyList<TaskKpiRow> tasks,
        IReadOnlyList<ApprovalKpiRow> approvals,
        IReadOnlyList<WorkflowExceptionKpiRow> workflowExceptions,
        IReadOnlyList<AgentKpiRow> agents,
        IReadOnlyList<WorkflowDefinitionKpiRow> workflowDefinitions,
        IReadOnlyList<ExecutiveCockpitKpiMetricDefinition> definitions)
    {
        var routeDepartment = Uri.EscapeDataString(department);
        var departmentTasks = tasks
            .Where(x => x.AssignedAgentId is Guid agentId && departmentAgentIds.Contains(agentId))
            .ToList();
        var departmentApprovals = approvals
            .Where(x => x.AgentId is Guid agentId && departmentAgentIds.Contains(agentId))
            .ToList();
        var departmentExceptions = workflowExceptions
            .Where(x => departmentWorkflowDefinitionIds.Contains(x.WorkflowDefinitionId))
            .ToList();

        yield return CreateMetric(
            "open_tasks",
            department,
            departmentTasks.Count(x => OpenTaskStatuses.Contains(x.Status) && x.CreatedUtc < endUtc),
            departmentTasks.Count(x => OpenTaskStatuses.Contains(x.Status) && x.CreatedUtc < baselineEndUtc),
            endUtc,
            $"/tasks?companyId={companyId}&department={routeDepartment}",
            definitions);

        yield return CreateMetric(
            "blocked_tasks",
            department,
            departmentTasks.Count(x => (x.Status == WorkTaskStatus.Blocked || x.Status == WorkTaskStatus.Failed) && x.UpdatedUtc >= startUtc && x.UpdatedUtc < endUtc),
            departmentTasks.Count(x => (x.Status == WorkTaskStatus.Blocked || x.Status == WorkTaskStatus.Failed) && x.UpdatedUtc >= baselineStartUtc && x.UpdatedUtc < baselineEndUtc),
            endUtc,
            $"/tasks?companyId={companyId}&department={routeDepartment}&status=blocked",
            definitions);

        yield return CreateMetric(
            "pending_approvals",
            department,
            departmentApprovals.Count(x => x.Status == ApprovalRequestStatus.Pending && x.CreatedUtc < endUtc),
            departmentApprovals.Count(x => x.Status == ApprovalRequestStatus.Pending && x.CreatedUtc < baselineEndUtc),
            endUtc,
            $"/approvals?companyId={companyId}&department={routeDepartment}&status=pending",
            definitions);

        yield return CreateMetric(
            "completed_tasks",
            department,
            departmentTasks.Count(x => x.Status == WorkTaskStatus.Completed && x.CompletedUtc >= startUtc && x.CompletedUtc < endUtc),
            departmentTasks.Count(x => x.Status == WorkTaskStatus.Completed && x.CompletedUtc >= baselineStartUtc && x.CompletedUtc < baselineEndUtc),
            endUtc,
            $"/tasks?companyId={companyId}&department={routeDepartment}&status=completed",
            definitions);

        yield return CreateMetric(
            "active_workflows",
            department,
            workflowDefinitions.Count(x => departmentWorkflowDefinitionIds.Contains(x.Id)),
            workflowDefinitions.Count(x => departmentWorkflowDefinitionIds.Contains(x.Id)),
            endUtc,
            $"/workflows?companyId={companyId}&department={routeDepartment}",
            definitions);

        yield return CreateMetric(
            "workflow_exceptions",
            department,
            departmentExceptions.Count(x => x.Status == WorkflowExceptionStatus.Open && x.OccurredUtc >= startUtc && x.OccurredUtc < endUtc),
            departmentExceptions.Count(x => x.Status == WorkflowExceptionStatus.Open && x.OccurredUtc >= baselineStartUtc && x.OccurredUtc < baselineEndUtc),
            endUtc,
            $"/workflows?companyId={companyId}&department={routeDepartment}",
            definitions);
    }

    private static ExecutiveCockpitKpiMetricValue CreateMetric(
        string key,
        string department,
        decimal currentValue,
        decimal? baselineValue,
        DateTime asOfUtc,
        string route,
        IReadOnlyList<ExecutiveCockpitKpiMetricDefinition> definitions)
    {
        var definition = definitions.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? ExecutiveCockpitKpiMetricCatalog.Find(key);

        return new ExecutiveCockpitKpiMetricValue(
            definition.Key,
            definition.Label,
            department,
            currentValue,
            baselineValue,
            asOfUtc,
            route,
            definition);
    }

    private static Dictionary<string, IReadOnlyList<ExecutiveCockpitKpiMetricDefinition>> BuildMetricDefinitionsByDepartment(
        IReadOnlyList<AgentKpiRow> agents)
    {
        var result = new Dictionary<string, IReadOnlyList<ExecutiveCockpitKpiMetricDefinition>>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in agents.GroupBy(x => NormalizeDepartment(x.Department), StringComparer.OrdinalIgnoreCase))
        {
            var definitions = ExecutiveCockpitKpiMetricCatalog.Defaults.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
            foreach (var agent in group)
            {
                foreach (var metric in ExecutiveCockpitKpiMetricCatalog.Defaults)
                {
                    if (TryReadDefinitionOverride(agent.Thresholds, metric, out var overridden))
                    {
                        definitions[metric.Key] = overridden;
                    }
                }
            }

            result[group.Key] = definitions.Values.ToList();
        }

        return result;
    }

    private static bool TryReadDefinitionOverride(
        IReadOnlyDictionary<string, JsonNode?> thresholds,
        ExecutiveCockpitKpiMetricDefinition fallback,
        out ExecutiveCockpitKpiMetricDefinition definition)
    {
        definition = fallback;
        if (!thresholds.TryGetValue(fallback.Key, out var node) || node is not JsonObject obj)
        {
            return false;
        }

        definition = fallback with
        {
            WarningThreshold = ReadDecimal(obj, "warningThreshold") ?? fallback.WarningThreshold,
            CriticalThreshold = ReadDecimal(obj, "criticalThreshold") ?? fallback.CriticalThreshold,
            WarningDeviationPercentage = ReadDecimal(obj, "warningDeviationPercentage") ?? fallback.WarningDeviationPercentage,
            CriticalDeviationPercentage = ReadDecimal(obj, "criticalDeviationPercentage") ?? fallback.CriticalDeviationPercentage
        };
        return true;
    }

    private static decimal? ReadDecimal(JsonObject obj, string key)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is null)
        {
            return null;
        }

        return node is JsonValue value && value.TryGetValue<decimal>(out var number)
            ? number
            : null;
    }

    private (DateTime StartUtc, DateTime EndUtc) NormalizeRange(DateTime? startUtc, DateTime? endUtc)
    {
        var end = NormalizeUtc(endUtc) ?? _timeProvider.GetUtcNow().UtcDateTime;
        var start = NormalizeUtc(startUtc) ?? end.AddDays(-7);
        if (start >= end)
        {
            start = end.AddDays(-7);
        }

        return (start, end);
    }

    private static DateTime? NormalizeUtc(DateTime? value) =>
        value is null
            ? null
            : value.Value.Kind == DateTimeKind.Utc
                ? value.Value
                : value.Value.ToUniversalTime();

    private static string NormalizeDepartment(string? department) =>
        string.IsNullOrWhiteSpace(department) ? "Unassigned" : department.Trim();

    private static string? NormalizeOptionalDepartment(string? department) =>
        string.IsNullOrWhiteSpace(department) ? null : NormalizeDepartment(department);

    private static int SeverityRank(string severity) =>
        severity.Trim().ToLowerInvariant() switch
        {
            ExecutiveCockpitKpiSeverity.Critical => 3,
            ExecutiveCockpitKpiSeverity.Warning => 2,
            ExecutiveCockpitKpiSeverity.Info => 1,
            _ => 0
        };

    private async Task<ResolvedCompanyMembershipContext> RequireMembershipAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var membership = await _membershipContextResolver.ResolveAsync(companyId, cancellationToken);
        if (membership is null)
        {
            throw new UnauthorizedAccessException("The current user does not have an active membership in the requested company.");
        }

        return membership;
    }

    private sealed record AgentKpiRow(
        Guid Id,
        string Department,
        AgentStatus Status,
        Dictionary<string, JsonNode?> Thresholds);

    private sealed record WorkflowDefinitionKpiRow(Guid Id, string? Department);

    private sealed record TaskKpiRow(
        Guid Id,
        Guid? AssignedAgentId,
        WorkTaskStatus Status,
        DateTime CreatedUtc,
        DateTime UpdatedUtc,
        DateTime? CompletedUtc);

    private sealed record ApprovalKpiRow(
        Guid Id,
        Guid? AgentId,
        ApprovalRequestStatus Status,
        DateTime CreatedUtc);

    private sealed record WorkflowExceptionKpiRow(
        Guid Id,
        Guid WorkflowDefinitionId,
        WorkflowExceptionStatus Status,
        DateTime OccurredUtc);
}