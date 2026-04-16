using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyDepartmentDashboardConfigurationService : IDepartmentDashboardConfigurationService
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
    private readonly TimeProvider _timeProvider;

    public CompanyDepartmentDashboardConfigurationService(
        VirtualCompanyDbContext dbContext,
        ICompanyMembershipContextResolver membershipContextResolver,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _membershipContextResolver = membershipContextResolver;
        _timeProvider = timeProvider;
    }

    public async Task<DepartmentDashboardConfigurationDto> GetAsync(
        GetDepartmentDashboardConfigurationQuery query,
        CancellationToken cancellationToken)
    {
        if (query.CompanyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(query));
        }

        var membership = await _membershipContextResolver.ResolveAsync(query.CompanyId, cancellationToken);
        if (membership is null)
        {
            throw new UnauthorizedAccessException("The current user does not have an active membership in the requested company.");
        }

        if (!await _dbContext.Companies.IgnoreQueryFilters().AnyAsync(x => x.Id == query.CompanyId, cancellationToken))
        {
            throw new KeyNotFoundException("Company not found.");
        }

        await EnsureDefaultConfigurationAsync(query.CompanyId, cancellationToken);

        var configs = await _dbContext.DashboardDepartmentConfigs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Widgets)
            .Where(x => x.CompanyId == query.CompanyId && x.IsEnabled)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Department)
            .ToListAsync(cancellationToken);

        configs = configs
            .OrderBy(x => RequiredDepartmentDashboardSections.GetDisplayOrder(x.Department, x.DisplayOrder))
            .ThenBy(x => x.DisplayOrder)
            .ThenBy(x => x.Department, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var visibleConfigs = configs
            .Where(x => DepartmentDashboardVisibility.IsVisibleToMembership(x.Visibility, membership, query.CompanyId))
            .ToList();

        // Visible empty sections are returned with fallback metadata so the client can render a stable composition.
        if (visibleConfigs.Count == 0)
        {
            return new DepartmentDashboardConfigurationDto(
                query.CompanyId,
                _timeProvider.GetUtcNow().UtcDateTime,
                []);
        }

        var metrics = await LoadMetricsAsync(
            query.CompanyId,
            visibleConfigs.Select(x => x.Department),
            cancellationToken);

        var sections = visibleConfigs
            .Select(config =>
            {
                var departmentMetrics = metrics.GetValueOrDefault(config.Department, DepartmentDashboardMetrics.Empty);
                var widgets = config.Widgets
                    .Where(x => x.IsEnabled && DepartmentDashboardVisibility.IsVisibleToMembership(x.Visibility, membership, query.CompanyId))
                    .OrderBy(x => x.DisplayOrder)
                    .ThenBy(x => x.WidgetKey)
                    .Select(widget =>
                    {
                        var summaryValue = ResolveSummaryValue(departmentMetrics, widget.SummaryBinding);
                        return new DepartmentDashboardWidgetDto(
                            widget.Id,
                            widget.WidgetKey,
                            widget.Title,
                            widget.WidgetType,
                            widget.DisplayOrder,
                            true,
                            widget.SummaryBinding,
                            summaryValue,
                            summaryValue > 0,
                            summaryValue == 0,
                            ReadNavigation(widget.Navigation, widget.Title, $"/dashboard?companyId={query.CompanyId}&department={Uri.EscapeDataString(config.Department)}"),
                            ReadEmptyState(widget.EmptyState, widget.Title, config.DisplayName, query.CompanyId, config.Department));
                    })
                    .ToList();

                return new DepartmentDashboardSectionDto(
                    config.Id,
                    config.Department,
                    config.DisplayName,
                    RequiredDepartmentDashboardSections.GetDisplayOrder(config.Department, config.DisplayOrder),
                    true,
                    config.Icon,
                    departmentMetrics.HasData,
                    departmentMetrics.ToSummaryCounts(),
                    !departmentMetrics.HasData,
                    ReadNavigation(config.Navigation, config.DisplayName, $"/dashboard?companyId={query.CompanyId}&department={Uri.EscapeDataString(config.Department)}"),
                    ReadEmptyState(config.EmptyState, config.DisplayName, config.DisplayName, query.CompanyId, config.Department),
                    widgets);
            })
            .ToList();

        return new DepartmentDashboardConfigurationDto(
            query.CompanyId,
            _timeProvider.GetUtcNow().UtcDateTime,
            sections);
    }

    private async Task EnsureDefaultConfigurationAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var existingDepartments = await _dbContext.DashboardDepartmentConfigs
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .Select(x => x.Department)
            .ToListAsync(cancellationToken);

        var existingDepartmentSet = existingDepartments.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingSeeds = DefaultDepartmentDashboardConfigSeeds.Create(companyId)
            .Where(x => !existingDepartmentSet.Contains(x.Department))
            .ToList();

        if (missingSeeds.Count == 0)
        {
            return;
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (var seed in missingSeeds)
        {
            SetTimestamp(seed, nowUtc);
            foreach (var widget in seed.Widgets) SetTimestamp(widget, nowUtc);

            _dbContext.DashboardDepartmentConfigs.Add(seed);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, DepartmentDashboardMetrics>> LoadMetricsAsync(
        Guid companyId,
        IEnumerable<string> departments,
        CancellationToken cancellationToken)
    {
        var departmentSet = departments
            .Select(NormalizeDepartmentKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var agentRows = await _dbContext.Agents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new { x.Id, x.Department, x.Status })
            .ToListAsync(cancellationToken);

        var agentDepartmentById = agentRows
            .Select(x => new { x.Id, Department = NormalizeDepartmentKey(x.Department) })
            .Where(x => departmentSet.Contains(x.Department))
            .ToDictionary(x => x.Id, x => x.Department);

        var taskRows = await _dbContext.WorkTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.AssignedAgentId.HasValue)
            .Select(x => new { x.AssignedAgentId, x.Status, x.CompletedUtc })
            .ToListAsync(cancellationToken);

        var pendingApprovalRows = await _dbContext.ApprovalRequests
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status == ApprovalRequestStatus.Pending)
            .Select(x => new { x.AgentId })
            .ToListAsync(cancellationToken);

        var workflowRows = await _dbContext.WorkflowDefinitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Active)
            .Select(x => x.Department)
            .ToListAsync(cancellationToken);

        var workflowExceptionRows = await (
            from exception in _dbContext.WorkflowExceptions.IgnoreQueryFilters().AsNoTracking()
            join definition in _dbContext.WorkflowDefinitions.IgnoreQueryFilters().AsNoTracking()
                on exception.WorkflowDefinitionId equals definition.Id
            where exception.CompanyId == companyId &&
                  definition.CompanyId == companyId &&
                  exception.Status == WorkflowExceptionStatus.Open
            select definition.Department)
            .ToListAsync(cancellationToken);

        var completedSinceUtc = _timeProvider.GetUtcNow().UtcDateTime.AddDays(-7);
        var metrics = departmentSet.ToDictionary(
            x => x,
            _ => new MutableDepartmentDashboardMetrics(),
            StringComparer.OrdinalIgnoreCase);

        foreach (var agent in agentRows)
        {
            var department = NormalizeDepartmentKey(agent.Department);
            if (metrics.TryGetValue(department, out var metric) && agent.Status == AgentStatus.Active)
            {
                metric.ActiveAgents++;
            }
        }

        foreach (var task in taskRows)
        {
            if (task.AssignedAgentId is not Guid agentId ||
                !agentDepartmentById.TryGetValue(agentId, out var department) ||
                !metrics.TryGetValue(department, out var metric))
            {
                continue;
            }

            if (OpenTaskStatuses.Contains(task.Status))
            {
                metric.OpenTasks++;
            }

            if (task.Status == WorkTaskStatus.Completed && task.CompletedUtc >= completedSinceUtc)
            {
                metric.CompletedTasksLast7Days++;
            }
        }

        foreach (var approval in pendingApprovalRows)
        {
            if (agentDepartmentById.TryGetValue(approval.AgentId, out var department) &&
                metrics.TryGetValue(department, out var metric))
            {
                metric.PendingApprovals++;
            }
        }

        foreach (var department in workflowRows.Select(NormalizeDepartmentKey))
        {
            if (metrics.TryGetValue(department, out var metric))
            {
                metric.ActiveWorkflows++;
            }
        }

        foreach (var department in workflowExceptionRows.Select(NormalizeDepartmentKey))
        {
            if (metrics.TryGetValue(department, out var metric))
            {
                metric.WorkflowExceptions++;
            }
        }

        return metrics.ToDictionary(
            x => x.Key,
            x => x.Value.ToImmutable(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static int ResolveSummaryValue(DepartmentDashboardMetrics metrics, string summaryBinding) =>
        summaryBinding switch
        {
            DepartmentDashboardSummaryBindings.ActiveAgents => metrics.ActiveAgents,
            DepartmentDashboardSummaryBindings.OpenTasks => metrics.OpenTasks,
            DepartmentDashboardSummaryBindings.CompletedTasksLast7Days => metrics.CompletedTasksLast7Days,
            DepartmentDashboardSummaryBindings.PendingApprovals => metrics.PendingApprovals,
            DepartmentDashboardSummaryBindings.ActiveWorkflows => metrics.ActiveWorkflows,
            DepartmentDashboardSummaryBindings.WorkflowExceptions => metrics.WorkflowExceptions,
            _ => 0
        };

    private static DepartmentDashboardNavigationDto ReadNavigation(
        IReadOnlyDictionary<string, JsonNode?> metadata,
        string fallbackLabel,
        string fallbackRoute)
    {
        var label = ReadString(metadata, "label") ?? fallbackLabel;
        var route = ReadString(metadata, "route") ?? fallbackRoute;
        return new DepartmentDashboardNavigationDto(label, route);
    }

    private static DepartmentDashboardEmptyStateDto ReadEmptyState(
        IReadOnlyDictionary<string, JsonNode?> metadata,
        string fallbackTitle,
        string departmentName,
        Guid companyId,
        string departmentKey)
    {
        var defaultRoute = $"/agents?companyId={companyId}&department={Uri.EscapeDataString(departmentKey)}";
        return new DepartmentDashboardEmptyStateDto(
            ReadString(metadata, "title") ?? fallbackTitle,
            ReadString(metadata, "message") ?? $"{departmentName} has no activity yet.",
            ReadString(metadata, "actionLabel"),
            ReadString(metadata, "actionRoute") ?? defaultRoute);
    }

    private static string? ReadString(IReadOnlyDictionary<string, JsonNode?> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var node) || node is not JsonValue value)
        {
            return null;
        }

        return value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;
    }

    private static string NormalizeDepartmentKey(string? department) =>
        string.IsNullOrWhiteSpace(department) ? "unassigned" : department.Trim().ToLowerInvariant();

    private static void SetTimestamp(object entity, DateTime nowUtc)
    {
        entity.GetType().GetProperty("CreatedUtc")?.SetValue(entity, nowUtc);
        entity.GetType().GetProperty("UpdatedUtc")?.SetValue(entity, nowUtc);
    }

    private sealed record DepartmentDashboardMetrics(
        int ActiveAgents,
        int OpenTasks,
        int CompletedTasksLast7Days,
        int PendingApprovals,
        int ActiveWorkflows,
        int WorkflowExceptions)
    {
        public static DepartmentDashboardMetrics Empty { get; } = new(0, 0, 0, 0, 0, 0);

        public bool HasData =>
            ActiveAgents > 0 ||
            OpenTasks > 0 ||
            CompletedTasksLast7Days > 0 ||
            PendingApprovals > 0 ||
            ActiveWorkflows > 0 ||
            WorkflowExceptions > 0;

        public IReadOnlyDictionary<string, int> ToSummaryCounts() =>
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [DepartmentDashboardSummaryBindings.ActiveAgents] = ActiveAgents,
                [DepartmentDashboardSummaryBindings.OpenTasks] = OpenTasks,
                [DepartmentDashboardSummaryBindings.CompletedTasksLast7Days] = CompletedTasksLast7Days,
                [DepartmentDashboardSummaryBindings.PendingApprovals] = PendingApprovals,
                [DepartmentDashboardSummaryBindings.ActiveWorkflows] = ActiveWorkflows,
                [DepartmentDashboardSummaryBindings.WorkflowExceptions] = WorkflowExceptions
            };
    }

    private sealed class MutableDepartmentDashboardMetrics
    {
        public int ActiveAgents { get; set; }
        public int OpenTasks { get; set; }
        public int CompletedTasksLast7Days { get; set; }
        public int PendingApprovals { get; set; }
        public int ActiveWorkflows { get; set; }
        public int WorkflowExceptions { get; set; }

        public DepartmentDashboardMetrics ToImmutable() =>
            new(ActiveAgents, OpenTasks, CompletedTasksLast7Days, PendingApprovals, ActiveWorkflows, WorkflowExceptions);
    }
}

internal static class DefaultDepartmentDashboardConfigSeeds
{
    private static readonly string[] ExecutiveRoles = ["owner", "admin", "manager"];

    public static IReadOnlyList<DashboardDepartmentConfig> Create(Guid companyId) =>
    [
        Department(companyId, "finance", "Finance", RequiredDepartmentDashboardSections.GetDisplayOrder("finance", 10), "cash-stack", Roles("owner", "admin", "finance_approver"), "Approvals", "Review finance approvals", DepartmentDashboardSummaryBindings.PendingApprovals, "Spend anomalies", "Review exceptions", DepartmentDashboardSummaryBindings.WorkflowExceptions, "Invoice queue", "Open finance tasks", DepartmentDashboardSummaryBindings.OpenTasks),
        Department(companyId, "sales", "Sales", RequiredDepartmentDashboardSections.GetDisplayOrder("sales", 20), "graph-up", ExecutiveRoles, "Pipeline", "Active sales workflows", DepartmentDashboardSummaryBindings.ActiveWorkflows, "Open deals", "Open sales tasks", DepartmentDashboardSummaryBindings.OpenTasks, "Follow-ups", "Completed sales tasks", DepartmentDashboardSummaryBindings.CompletedTasksLast7Days),
        Department(companyId, "support", "Support", RequiredDepartmentDashboardSections.GetDisplayOrder("support", 30), "headset", Roles("owner", "admin", "manager", "support_supervisor"), "Open tickets", "Open support tasks", DepartmentDashboardSummaryBindings.OpenTasks, "SLA risk", "Support exceptions", DepartmentDashboardSummaryBindings.WorkflowExceptions, "Escalations", "Pending support approvals", DepartmentDashboardSummaryBindings.PendingApprovals),
        Department(companyId, "operations", "Operations", RequiredDepartmentDashboardSections.GetDisplayOrder("operations", 40), "gear", ExecutiveRoles, "Workflow exceptions", "Open workflow exceptions", DepartmentDashboardSummaryBindings.WorkflowExceptions, "Task backlog", "Open operations tasks", DepartmentDashboardSummaryBindings.OpenTasks, "Throughput", "Completed operations tasks", DepartmentDashboardSummaryBindings.CompletedTasksLast7Days)
    ];

    private static DashboardDepartmentConfig Department(
        Guid companyId,
        string department,
        string displayName,
        int order,
        string icon,
        IReadOnlyCollection<string> roles,
        string widgetOneTitle,
        string widgetOneEmpty,
        string widgetOneBinding,
        string widgetTwoTitle,
        string widgetTwoEmpty,
        string widgetTwoBinding,
        string widgetThreeTitle,
        string widgetThreeEmpty,
        string widgetThreeBinding)
    {
        var id = Guid.NewGuid();
        var section = new DashboardDepartmentConfig(
            id,
            companyId,
            department,
            displayName,
            order,
            true,
            icon,
            Navigation($"Open {displayName}", $"/dashboard?companyId={companyId}&department={department}"),
            Visibility(roles),
            EmptyState($"{displayName} is ready", $"Add agents, workflows, or tasks to start filling the {displayName.ToLowerInvariant()} dashboard.", "Open agents", $"/agents?companyId={companyId}&department={department}"));

        section.AddWidget(Widget(companyId, id, $"{department}_{widgetOneBinding}", widgetOneTitle, 10, widgetOneBinding, roles, widgetOneEmpty, department));
        section.AddWidget(Widget(companyId, id, $"{department}_{widgetTwoBinding}", widgetTwoTitle, 20, widgetTwoBinding, roles, widgetTwoEmpty, department));
        section.AddWidget(Widget(companyId, id, $"{department}_{widgetThreeBinding}", widgetThreeTitle, 30, widgetThreeBinding, roles, widgetThreeEmpty, department));
        return section;
    }

    private static DashboardWidgetConfig Widget(Guid companyId, Guid departmentId, string key, string title, int order, string binding, IReadOnlyCollection<string> roles, string emptyMessage, string department) =>
        new(
            Guid.NewGuid(),
            companyId,
            departmentId,
            key,
            title,
            "summary_count",
            order,
            true,
            binding,
            Navigation($"Open {title}", $"/dashboard?companyId={companyId}&department={department}&widget={key}"),
            Visibility(roles),
            EmptyState(title, emptyMessage, "Open tasks", $"/tasks?companyId={companyId}"));

    private static IReadOnlyCollection<string> Roles(params string[] roles) => roles;

    private static Dictionary<string, JsonNode?> Visibility(IEnumerable<string> roles) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["roles"] = new JsonArray(roles.Select(role => JsonValue.Create(role)).ToArray<JsonNode?>())
        };

    private static Dictionary<string, JsonNode?> Navigation(string label, string route) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["label"] = JsonValue.Create(label),
            ["route"] = JsonValue.Create(route)
        };

    private static Dictionary<string, JsonNode?> EmptyState(string title, string message, string actionLabel, string actionRoute) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = JsonValue.Create(title),
            ["message"] = JsonValue.Create(message),
            ["actionLabel"] = JsonValue.Create(actionLabel),
            ["actionRoute"] = JsonValue.Create(actionRoute)
        };
}