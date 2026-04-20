using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Finance;
using VirtualCompany.Shared;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyExecutiveCockpitDashboardService : IExecutiveCockpitDashboardService
{
    private const int PendingApprovalPreviewLimit = 5;
    private const int AlertLimit = 5;
    private const int PerSourceActivityLimit = 8;
    private const int RecentActivityLimit = 10;

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyMembershipContextResolver _membershipContextResolver;
    private readonly IDepartmentDashboardConfigurationService _departmentDashboardConfigurationService;
    private readonly IExecutiveCockpitDashboardCache _cache;
    private readonly IExecutiveCockpitFinanceAdapter _financeAdapter;
    private readonly ISignalEngine _signalEngine;
    private readonly IAuthorizationService _authorizationService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IFinanceCashPositionWorkflowService _financeCashPositionWorkflowService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CompanyExecutiveCockpitDashboardService> _logger;

    public CompanyExecutiveCockpitDashboardService(
        VirtualCompanyDbContext dbContext,
        ICompanyMembershipContextResolver membershipContextResolver,
        IDepartmentDashboardConfigurationService departmentDashboardConfigurationService,
        IExecutiveCockpitDashboardCache cache,
        IExecutiveCockpitFinanceAdapter financeAdapter,
        ISignalEngine signalEngine,
        IAuthorizationService authorizationService,
        IHttpContextAccessor httpContextAccessor,
        IFinanceCashPositionWorkflowService financeCashPositionWorkflowService,
        TimeProvider timeProvider,
        ILogger<CompanyExecutiveCockpitDashboardService> logger)
    {
        _dbContext = dbContext;
        _membershipContextResolver = membershipContextResolver;
        _departmentDashboardConfigurationService = departmentDashboardConfigurationService;
        _cache = cache;
        _financeAdapter = financeAdapter;
        _signalEngine = signalEngine;
        _authorizationService = authorizationService;
        _httpContextAccessor = httpContextAccessor;
        _financeCashPositionWorkflowService = financeCashPositionWorkflowService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ExecutiveCockpitDashboardDto> GetAsync(
        GetExecutiveCockpitDashboardQuery query,
        CancellationToken cancellationToken)
    {
        if (query.CompanyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(query));
        }

        var membership = await RequireMembershipAsync(query.CompanyId, cancellationToken);
        var scope = ExecutiveCockpitCacheKeyBuilder.DashboardScope(query.CompanyId, membership.MembershipRole.ToStorageValue());

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var cached = await _cache.TryGetDashboardAsync(scope, cancellationToken);
        if (cached is not null)
        {
            _logger.LogDebug("Executive cockpit dashboard cache hit for company {CompanyId}.", query.CompanyId);
            var cachedVisibleSections = await GetVisibleDepartmentSectionsAsync(query.CompanyId, cancellationToken);
            var canViewFinance = await CanViewFinanceAsync(query.CompanyId, membership, cancellationToken);
            var currentCashPosition = canViewFinance
                ? await _financeCashPositionWorkflowService.EvaluateAsync(
                    new EvaluateFinanceCashPositionWorkflowCommand(query.CompanyId),
                    cancellationToken)
                : null;
            var finance = canViewFinance
                ? await _financeAdapter.GetAsync(query.CompanyId, cancellationToken)
                : null;
            return cached.Dashboard with
            {
                CacheTimestampUtc = cached.CachedAtUtc,
                CashPosition = currentCashPosition,
                Finance = finance,
                DepartmentSections = cachedVisibleSections
            };
        }

        _logger.LogDebug(
            "Executive cockpit dashboard cache miss for company {CompanyId}; building aggregate from database.",
            query.CompanyId);
        var dashboard = await BuildAsync(query.CompanyId, nowUtc, cancellationToken);
        var cachedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var visibleSections = await GetVisibleDepartmentSectionsAsync(query.CompanyId, cancellationToken);
        var cacheableDashboard = dashboard with { CacheTimestampUtc = null, DepartmentSections = [] };
        await _cache.SetDashboardAsync(scope, new CachedExecutiveCockpitDashboardDto(query.CompanyId, cachedAtUtc, cacheableDashboard), cancellationToken);
        return cacheableDashboard with { DepartmentSections = visibleSections };
    }

    public async Task<ExecutiveCockpitFinanceAlertDetailDto?> GetFinanceAlertDetailAsync(
        GetExecutiveCockpitFinanceAlertDetailQuery query,
        CancellationToken cancellationToken)
    {
        if (query.CompanyId == Guid.Empty || query.AlertId == Guid.Empty)
        {
            throw new ArgumentException("Company id and alert id are required.", nameof(query));
        }

        var membership = await RequireMembershipAsync(query.CompanyId, cancellationToken);
        if (!await CanViewFinanceAsync(query.CompanyId, membership, cancellationToken))
        {
            throw new UnauthorizedAccessException("Finance detail access requires finance.view permission.");
        }

        return await _financeAdapter.GetAlertDetailAsync(query.CompanyId, query.AlertId, cancellationToken);
    }

    public async Task<ExecutiveCockpitWidgetPayloadDto> GetWidgetAsync(
        GetExecutiveCockpitWidgetPayloadQuery query,
        CancellationToken cancellationToken)
    {
        if (query.CompanyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(query));
        }

        if (!ExecutiveCockpitWidgetKeys.All.Contains(query.WidgetKey))
        {
            throw new KeyNotFoundException("Dashboard widget not found.");
        }

        var membership = await RequireMembershipAsync(query.CompanyId, cancellationToken);
        var scope = ExecutiveCockpitCacheKeyBuilder.WidgetScope(
            query.CompanyId,
            membership.MembershipRole.ToStorageValue(),
            query.WidgetKey,
            string.IsNullOrWhiteSpace(query.Department) ? [] : [query.Department],
            query.StartUtc,
            query.EndUtc);

        _logger.LogDebug(
            "Refreshing executive cockpit widget {WidgetKey} for company {CompanyId}.",
            query.WidgetKey,
            query.CompanyId);
        var cached = await _cache.TryGetWidgetAsync<object>(scope, cancellationToken);
        if (cached is not null)
        {
            return new ExecutiveCockpitWidgetPayloadDto(
                query.CompanyId,
                query.WidgetKey,
                _timeProvider.GetUtcNow().UtcDateTime,
                cached.CachedAtUtc,
                cached.Payload);
        }

        var dashboard = await GetAsync(new GetExecutiveCockpitDashboardQuery(query.CompanyId), cancellationToken);
        object payload = query.WidgetKey.Trim().ToLowerInvariant() switch
        {
            ExecutiveCockpitWidgetKeys.BusinessSignals => dashboard.BusinessSignals,
            ExecutiveCockpitWidgetKeys.SummaryKpis => dashboard.SummaryKpis,
            ExecutiveCockpitWidgetKeys.DailyBriefing => dashboard.DailyBriefing,
            ExecutiveCockpitWidgetKeys.PendingApprovals => dashboard.PendingApprovals,
            ExecutiveCockpitWidgetKeys.Alerts => dashboard.Alerts,
            ExecutiveCockpitWidgetKeys.DepartmentKpis => dashboard.DepartmentKpis,
            ExecutiveCockpitWidgetKeys.DepartmentSections => dashboard.DepartmentSections,
            ExecutiveCockpitWidgetKeys.CashPosition => dashboard.CashPosition,
            ExecutiveCockpitWidgetKeys.RecentActivity => dashboard.RecentActivity,
            ExecutiveCockpitWidgetKeys.Finance => dashboard.Finance,
            ExecutiveCockpitWidgetKeys.Kpis => dashboard.DepartmentKpis,
            _ => throw new KeyNotFoundException("Dashboard widget not found.")
        };

        var cachedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await _cache.SetWidgetAsync(
            scope,
            new CachedExecutiveCockpitWidgetDto<object>(query.CompanyId, query.WidgetKey, cachedAtUtc, payload),
            cancellationToken);

        return new ExecutiveCockpitWidgetPayloadDto(query.CompanyId, query.WidgetKey, cachedAtUtc, null, payload);
    }

    private async Task<ExecutiveCockpitDashboardDto> BuildAsync(
        Guid companyId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var company = await _dbContext.Companies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == companyId, cancellationToken)
            ?? throw new KeyNotFoundException("Company not found.");

        var dailyBriefing = await _dbContext.CompanyBriefings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.BriefingType == CompanyBriefingType.Daily)
            .OrderByDescending(x => x.GeneratedUtc)
            .Select(x => new ExecutiveCockpitDailyBriefingDto(
                x.Id,
                x.Title,
                FirstParagraph(x.SummaryBody, 420),
                x.GeneratedUtc,
                $"/briefing-preferences?companyId={companyId}"))
            .FirstOrDefaultAsync(cancellationToken);

        var pendingApprovalRows = await _dbContext.ApprovalRequests
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status == ApprovalRequestStatus.Pending)
            .OrderBy(x => x.CreatedUtc)
            .Select(x => new
            {
                x.Id,
                x.ApprovalType,
                x.TargetEntityType,
                x.TargetEntityId,
                x.Status,
                x.CreatedUtc
            })
            .Take(PendingApprovalPreviewLimit)
            .ToListAsync(cancellationToken);

        var pendingApprovalCount = await _dbContext.ApprovalRequests
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(x => x.CompanyId == companyId && x.Status == ApprovalRequestStatus.Pending, cancellationToken);

        var businessSignals = await _signalEngine.GenerateSignals(companyId, cancellationToken);
        var summaryKpis = await BuildSummaryKpisAsync(companyId, nowUtc, pendingApprovalCount, cancellationToken);
        var canViewFinance = await CanViewFinanceAsync(companyId, await RequireMembershipAsync(companyId, cancellationToken), cancellationToken);
        FinanceCashPositionDto? cashPosition = null;
        ExecutiveCockpitFinanceDto? finance = null;
        if (canViewFinance)
        {
            try
            {
                cashPosition = await _financeCashPositionWorkflowService.EvaluateAsync(
                    new EvaluateFinanceCashPositionWorkflowCommand(companyId),
                    cancellationToken);
                finance = await _financeAdapter.GetAsync(companyId, cancellationToken);
            }
            catch (FinanceNotInitializedException)
            {
                cashPosition = null;
                finance = null;
            }
        }

        var pendingApprovals = new ExecutiveCockpitPendingApprovalsDto(
            pendingApprovalCount,
            pendingApprovalRows
                .Select(x => new ExecutiveCockpitApprovalItemDto(
                    x.Id,
                    x.ApprovalType,
                    x.TargetEntityType,
                    x.TargetEntityId,
                    x.Status.ToStorageValue(),
                    $"Review {x.ApprovalType} approval for {x.TargetEntityType}.",
                    x.CreatedUtc,
                    $"/approvals?companyId={companyId}&approvalId={x.Id}"))
                .ToList(),
            $"/approvals?companyId={companyId}&status=pending");

        var domainAlerts = await _dbContext.Alerts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.Status != AlertStatus.Resolved &&
                        x.Status != AlertStatus.Closed)
            .OrderByDescending(x => x.LastDetectedUtc ?? x.UpdatedUtc)
            .Take(AlertLimit)
            .Select(x => new ExecutiveCockpitAlertDto(
                x.Id,
                x.Severity.ToStorageValue(),
                x.Title,
                x.Summary,
                "alert",
                TryResolveFinanceAlertSourceId(x),
                x.LastDetectedUtc ?? x.UpdatedUtc,
                BuildAlertRoute(companyId, x)))
            .ToListAsync(cancellationToken);

        var workflowAlerts = await _dbContext.WorkflowExceptions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status == WorkflowExceptionStatus.Open)
            .OrderByDescending(x => x.OccurredUtc)
            .Take(Math.Max(0, AlertLimit - domainAlerts.Count))
            .Select(x => new ExecutiveCockpitAlertDto(
                x.Id,
                "high",
                x.Title,
                x.Details,
                "workflow_exception",
                x.Id,
                x.OccurredUtc,
                $"/workflows?companyId={companyId}&workflowInstanceId={x.WorkflowInstanceId}&exceptionId={x.Id}"))
            .ToListAsync(cancellationToken);

        var taskAlerts = await _dbContext.WorkTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                (x.Status == WorkTaskStatus.Blocked || x.Status == WorkTaskStatus.Failed))
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(Math.Max(0, AlertLimit - domainAlerts.Count - workflowAlerts.Count))
            .Select(x => new ExecutiveCockpitAlertDto(
                x.Id,
                x.Status == WorkTaskStatus.Failed ? "high" : "medium",
                x.Title,
                $"Task is {x.Status.ToStorageValue()}.",
                "task",
                x.Id,
                x.UpdatedUtc,
                $"/tasks?companyId={companyId}&taskId={x.Id}"))
            .ToListAsync(cancellationToken);

        var alerts = domainAlerts.Concat(workflowAlerts).Concat(taskAlerts)
            .OrderByDescending(x => x.OccurredUtc)
            .Take(AlertLimit)
            .ToList();

        var agentRows = await _dbContext.Agents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new AgentDashboardRow(x.Id, x.Department, x.Status))
            .ToListAsync(cancellationToken);

        var agentDepartmentById = agentRows.ToDictionary(
            x => x.Id,
            x => string.IsNullOrWhiteSpace(x.Department) ? "Unassigned" : x.Department.Trim());

        var taskAggregatesByAgentId = await _dbContext.WorkTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.AssignedAgentId.HasValue)
            .GroupBy(x => x.AssignedAgentId!.Value)
            .Select(group => new AgentTaskDashboardAggregate(
                group.Key,
                group.Count(x => x.Status != WorkTaskStatus.Completed && x.Status != WorkTaskStatus.Failed),
                group.Count(x => x.Status == WorkTaskStatus.Completed && x.CompletedUtc >= nowUtc.AddDays(-7))))
            .ToListAsync(cancellationToken);

        var pendingApprovalAggregatesByAgentId = await _dbContext.ApprovalRequests
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status == ApprovalRequestStatus.Pending)
            .GroupBy(x => x.AgentId)
            .Select(group => new AgentApprovalDashboardAggregate(group.Key, group.Count()))
            .ToListAsync(cancellationToken);

        var workflowDepartments = await _dbContext.WorkflowDefinitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Active)
            .Select(x => x.Department ?? "Unassigned")
            .ToListAsync(cancellationToken);

        var workflowCountByDepartment = workflowDepartments
            .GroupBy(NormalizeDepartment, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);

        var departments = agentRows.Select(x => NormalizeDepartment(x.Department))
            .Concat(workflowDepartments)
            .DefaultIfEmpty("Company")
            .Select(NormalizeDepartment)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var taskAggregatesByAgent = taskAggregatesByAgentId.ToDictionary(x => x.AgentId);
        var approvalAggregatesByAgent = pendingApprovalAggregatesByAgentId.ToDictionary(x => x.AgentId);

        var departmentKpis = departments
            .Select(department =>
            {
                var departmentAgentIds = agentDepartmentById
                    .Where(x => string.Equals(x.Value, department, StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Key)
                    .ToList();

                var openTasks = 0;
                var completedTasksLast7Days = 0;
                var pendingApprovalsForDepartment = 0;
                foreach (var agentId in departmentAgentIds)
                {
                    if (taskAggregatesByAgent.TryGetValue(agentId, out var taskAggregate))
                    {
                        openTasks += taskAggregate.OpenTasks;
                        completedTasksLast7Days += taskAggregate.CompletedTasksLast7Days;
                    }

                    if (approvalAggregatesByAgent.TryGetValue(agentId, out var approvalAggregate))
                    {
                        pendingApprovalsForDepartment += approvalAggregate.PendingApprovals;
                    }
                }

                return new ExecutiveCockpitDepartmentKpiDto(
                    department,
                    agentRows.Count(x => string.Equals(NormalizeDepartment(x.Department), department, StringComparison.OrdinalIgnoreCase) && x.Status == AgentStatus.Active),
                    openTasks,
                    completedTasksLast7Days,
                    pendingApprovalsForDepartment,
                    workflowCountByDepartment.GetValueOrDefault(department),
                    $"/agents?companyId={companyId}&department={Uri.EscapeDataString(department)}");
            })
            .ToList();

        var recentActivity = await BuildRecentActivityAsync(companyId, cancellationToken);

        var hasWorkflowInstances = await _dbContext.WorkflowInstances
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId, cancellationToken);

        var knowledgeDocumentCount = await _dbContext.CompanyKnowledgeDocuments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(x => x.CompanyId == companyId, cancellationToken);

        var setupState = new ExecutiveCockpitSetupStateDto(
            agentRows.Count > 0,
            workflowDepartments.Count > 0 || hasWorkflowInstances,
            knowledgeDocumentCount > 0,
            agentRows.Count,
            workflowDepartments.Count,
            knowledgeDocumentCount,
            agentRows.Count == 0 && workflowDepartments.Count == 0 && !hasWorkflowInstances && knowledgeDocumentCount == 0);

        var emptyStates = new ExecutiveCockpitEmptyStateFlagsDto(
            !setupState.HasAgents,
            !setupState.HasWorkflows,
            !setupState.HasKnowledge,
            recentActivity.Count == 0,
            pendingApprovals.TotalCount == 0,
            alerts.Count == 0);

        return new ExecutiveCockpitDashboardDto(
            companyId,
            company.Name,
            nowUtc,
            businessSignals,
            null,
            summaryKpis,
            dailyBriefing,
            cashPosition,
            finance,
            pendingApprovals,
            alerts,
            departmentKpis,
            [],
            recentActivity,
            setupState,
            emptyStates);
    }

    private async Task<IReadOnlyList<DepartmentDashboardSectionDto>> GetVisibleDepartmentSectionsAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        (await _departmentDashboardConfigurationService.GetAsync(
            new GetDepartmentDashboardConfigurationQuery(companyId),
            cancellationToken)).Sections;

    private async Task<IReadOnlyList<ExecutiveCockpitSummaryKpiDto>> BuildSummaryKpisAsync(
        Guid companyId,
        DateTime nowUtc,
        int pendingApprovalCount,
        CancellationToken cancellationToken)
    {
        var currentPeriodStartUtc = nowUtc.AddDays(-7);
        var previousPeriodStartUtc = nowUtc.AddDays(-14);

        var openTaskStatuses = new[]
        {
            WorkTaskStatus.New,
            WorkTaskStatus.InProgress,
            WorkTaskStatus.Blocked,
            WorkTaskStatus.AwaitingApproval
        };

        var openTaskCount = await _dbContext.WorkTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(x => x.CompanyId == companyId && openTaskStatuses.Contains(x.Status), cancellationToken);

        var completedTasksCurrentPeriod = await _dbContext.WorkTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(
                x => x.CompanyId == companyId &&
                     x.Status == WorkTaskStatus.Completed &&
                     x.CompletedUtc >= currentPeriodStartUtc &&
                     x.CompletedUtc < nowUtc,
                cancellationToken);

        var completedTasksPreviousPeriod = await _dbContext.WorkTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(
                x => x.CompanyId == companyId &&
                     x.Status == WorkTaskStatus.Completed &&
                     x.CompletedUtc >= previousPeriodStartUtc &&
                     x.CompletedUtc < currentPeriodStartUtc,
                cancellationToken);

        var activeAgentCount = await _dbContext.Agents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(x => x.CompanyId == companyId && x.Status == AgentStatus.Active, cancellationToken);

        var blockedTaskCount = await _dbContext.WorkTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(
                x => x.CompanyId == companyId &&
                     (x.Status == WorkTaskStatus.Blocked || x.Status == WorkTaskStatus.Failed),
                cancellationToken);

        return
        [
            CreateSummaryKpi(
                "pending_approvals",
                "Pending approvals",
                pendingApprovalCount,
                null,
                "attention"),
            CreateSummaryKpi(
                "open_tasks",
                "Open tasks",
                openTaskCount,
                null,
                openTaskCount > 0 ? "active" : "neutral"),
            CreateSummaryKpi(
                "completed_tasks_7d",
                "Completed tasks",
                completedTasksCurrentPeriod,
                completedTasksPreviousPeriod,
                completedTasksCurrentPeriod > 0 ? "positive" : "neutral"),
            CreateSummaryKpi(
                "active_agents",
                "Active agents",
                activeAgentCount,
                null,
                activeAgentCount > 0 ? "positive" : "neutral"),
            CreateSummaryKpi(
                "blocked_tasks",
                "Blocked tasks",
                blockedTaskCount,
                null,
                blockedTaskCount > 0 ? "attention" : "neutral")
        ];
    }

    private static ExecutiveCockpitSummaryKpiDto CreateSummaryKpi(
        string key,
        string label,
        int currentValue,
        int? previousValue,
        string? statusHint)
    {
        var trend = ExecutiveCockpitKpiTrendCalculator.Calculate(currentValue, previousValue);
        return new ExecutiveCockpitSummaryKpiDto(
            key,
            label,
            currentValue,
            previousValue,
            trend.Direction,
            trend.DeltaValue,
            trend.DeltaPercentage,
            trend.DeltaText,
            trend.ComparisonLabel,
            statusHint,
            currentValue == 0 && previousValue.GetValueOrDefault() == 0);
    }

    private async Task<IReadOnlyList<ExecutiveCockpitActivityItemDto>> BuildRecentActivityAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var auditItems = await _dbContext.AuditEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.OccurredUtc)
            .Take(PerSourceActivityLimit)
            .Select(x => new ExecutiveCockpitActivityItemDto(
                x.Id,
                "audit",
                x.Action,
                string.IsNullOrWhiteSpace(x.RationaleSummary) ? $"{x.TargetType} {x.Outcome}" : x.RationaleSummary!,
                x.OccurredUtc,
                BuildAuditRoute(companyId, x.TargetType, x.TargetId)))
            .ToListAsync(cancellationToken);

        var taskItems = await _dbContext.WorkTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(PerSourceActivityLimit)
            .Select(x => new ExecutiveCockpitActivityItemDto(
                x.Id,
                "task",
                x.Title,
                $"Task {x.Status.ToStorageValue()}.",
                x.UpdatedUtc,
                $"/tasks?companyId={companyId}&taskId={x.Id}"))
            .ToListAsync(cancellationToken);

        var workflowItems = await (
            from instance in _dbContext.WorkflowInstances.IgnoreQueryFilters().AsNoTracking()
            join definition in _dbContext.WorkflowDefinitions.IgnoreQueryFilters().AsNoTracking()
                on instance.DefinitionId equals definition.Id
            where instance.CompanyId == companyId && definition.CompanyId == companyId
            orderby instance.UpdatedUtc descending
            select new ExecutiveCockpitActivityItemDto(
                instance.Id,
                "workflow",
                definition.Name,
                $"Workflow {instance.State.ToStorageValue()}.",
                instance.UpdatedUtc,
                $"/workflows?companyId={companyId}&workflowInstanceId={instance.Id}&state={Uri.EscapeDataString(instance.State.ToStorageValue())}"))
            .Take(PerSourceActivityLimit)
            .ToListAsync(cancellationToken);

        return auditItems
            .Concat(taskItems)
            .Concat(workflowItems)
            .OrderByDescending(x => x.OccurredUtc)
            .Take(RecentActivityLimit)
            .ToList();
    }

    private static string NormalizeDepartment(string? department) =>
        string.IsNullOrWhiteSpace(department) ? "Unassigned" : department.Trim();

    private static string FirstParagraph(string value, int maxLength)
    {
        var paragraph = value.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? value;
        return paragraph.Length <= maxLength ? paragraph : paragraph[..maxLength].TrimEnd() + "...";
    }

    private static string? BuildAuditRoute(Guid companyId, string targetType, string targetId)
    {
        if (!Guid.TryParse(targetId, out var id))
        {
            return null;
        }

        return targetType.Trim().ToLowerInvariant() switch
        {
            "agent" => $"/agents/{id}?companyId={companyId}",
            "approval_request" => $"/approvals?companyId={companyId}&approvalId={id}",
            "work_task" => $"/tasks?companyId={companyId}&taskId={id}",
            "workflow_instance" => $"/workflows?companyId={companyId}&workflowInstanceId={id}",
            "workflow_exception" => $"/workflows?companyId={companyId}&exceptionId={id}",
            _ => null
        };
    }

    private static string BuildAlertRoute(Guid companyId, Domain.Entities.Alert alert) =>
        IsFinanceCashAlert(alert)
            ? $"/finance/alerts/{alert.Id:D}?companyId={companyId:D}"
            : $"/dashboard?companyId={companyId:D}";

    private static Guid? TryResolveFinanceAlertSourceId(Domain.Entities.Alert alert) =>
        IsFinanceCashAlert(alert) ? alert.Id : alert.Id;

    private static bool IsFinanceCashAlert(Domain.Entities.Alert alert) =>
        !string.IsNullOrWhiteSpace(alert.Fingerprint) &&
        alert.Fingerprint.StartsWith("finance-cash-position:", StringComparison.OrdinalIgnoreCase);

    private async Task<ResolvedCompanyMembershipContext> RequireMembershipAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var membership = await _membershipContextResolver.ResolveAsync(companyId, cancellationToken);
        if (membership is null)
        {
            throw new UnauthorizedAccessException("The current user does not have an active membership in the requested company.");
        }

        return membership;
    }

    private async Task<bool> CanViewFinanceAsync(
        Guid companyId,
        ResolvedCompanyMembershipContext membership,
        CancellationToken cancellationToken)
    {
        if (!FinanceAccess.CanView(membership.MembershipRole.ToStorageValue()))
        {
            return false;
        }

        var principal = _httpContextAccessor.HttpContext?.User;
        return principal?.Identity?.IsAuthenticated == true &&
               (await _authorizationService.AuthorizeAsync(principal, companyId, CompanyPolicies.FinanceView)).Succeeded;
    }
}

internal sealed record AgentDashboardRow(Guid Id, string Department, AgentStatus Status);

internal sealed record AgentTaskDashboardAggregate(Guid AgentId, int OpenTasks, int CompletedTasksLast7Days);

internal sealed record AgentApprovalDashboardAggregate(Guid? AgentId, int PendingApprovals);
