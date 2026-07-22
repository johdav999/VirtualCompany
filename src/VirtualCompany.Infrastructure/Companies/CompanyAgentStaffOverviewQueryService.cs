using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Sales;
using VirtualCompany.Application.Support;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Shared;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyAgentStaffOverviewQueryService : IAgentStaffOverviewQueryService
{
    private const int PreviewItemsPerStage = 2;
    private const int CandidateLimitPerStage = 2000;

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyMembershipContextResolver _membershipContextResolver;
    private readonly IAuthorizationService _authorizationService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IFinanceReadService _financeReadService;
    private readonly ISalesOperationsService _salesOperationsService;
    private readonly ISupportAnalyticsService _supportAnalyticsService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CompanyAgentStaffOverviewQueryService> _logger;

    public CompanyAgentStaffOverviewQueryService(
        VirtualCompanyDbContext dbContext,
        ICompanyMembershipContextResolver membershipContextResolver,
        IAuthorizationService authorizationService,
        IHttpContextAccessor httpContextAccessor,
        IFinanceReadService financeReadService,
        ISalesOperationsService salesOperationsService,
        ISupportAnalyticsService supportAnalyticsService,
        TimeProvider timeProvider,
        ILogger<CompanyAgentStaffOverviewQueryService> logger)
    {
        _dbContext = dbContext;
        _membershipContextResolver = membershipContextResolver;
        _authorizationService = authorizationService;
        _httpContextAccessor = httpContextAccessor;
        _financeReadService = financeReadService;
        _salesOperationsService = salesOperationsService;
        _supportAnalyticsService = supportAnalyticsService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<AgentStaffOverviewDto> GetAsync(
        GetAgentStaffOverviewQuery query,
        CancellationToken cancellationToken)
    {
        Validate(query);
        var membership = await _membershipContextResolver.ResolveAsync(query.CompanyId, cancellationToken)
            ?? throw new UnauthorizedAccessException("The current user does not have an active membership in the requested company.");

        var companyName = await _dbContext.Companies
            .AsNoTracking()
            .Where(x => x.Id == query.CompanyId)
            .Select(x => x.Name)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Company not found.");

        var periodStartUtc = await ResolvePeriodStartUtcAsync(query, cancellationToken);
        var periodEndUtc = periodStartUtc.AddMonths(1);
        var period = new AgentStaffOverviewPeriodDto(
            periodStartUtc.Year,
            periodStartUtc.Month,
            periodStartUtc,
            periodEndUtc,
            periodStartUtc.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture));

        var finance = await BuildFinanceSummaryAsync(query.CompanyId, membership, period, cancellationToken);
        var sales = await _salesOperationsService.GetDashboardAsync(query.CompanyId, cancellationToken);
        var support = await _supportAnalyticsService.GetDashboardAsync(query.CompanyId, cancellationToken);

        var agents = await _dbContext.Agents
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId)
            .Select(x => new AgentProjection(
                x.Id,
                x.DisplayName,
                x.RoleName,
                x.Department,
                x.Status,
                x.AvatarUrl))
            .ToListAsync(cancellationToken);
        agents = agents
            .OrderBy(x => DepartmentOrder(x.Department))
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var agentIds = agents.Select(x => x.Id).ToArray();
        var defaultAgentByDepartment = agents
            .Where(x => x.Status == AgentStatus.Active)
            .GroupBy(x => NormalizeDepartment(x.Department), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Id, StringComparer.OrdinalIgnoreCase);

        var plannedCandidates = await LoadTaskCandidatesAsync(
            query.CompanyId,
            agentIds,
            defaultAgentByDepartment,
            x => x.Status != WorkTaskStatus.InProgress && x.Status != WorkTaskStatus.AwaitingApproval && x.Status != WorkTaskStatus.Completed,
            cancellationToken);
        var inProgressCandidates = await LoadTaskCandidatesAsync(
            query.CompanyId,
            agentIds,
            defaultAgentByDepartment,
            x => x.Status == WorkTaskStatus.InProgress,
            cancellationToken);
        var approvalCandidates = await LoadTaskCandidatesAsync(
            query.CompanyId,
            agentIds,
            defaultAgentByDepartment,
            x => x.Status == WorkTaskStatus.AwaitingApproval,
            cancellationToken);
        var completedCandidates = await LoadTaskCandidatesAsync(
            query.CompanyId,
            agentIds,
            defaultAgentByDepartment,
            x => x.Status == WorkTaskStatus.Completed && x.CompletedUtc >= periodStartUtc && x.CompletedUtc < periodEndUtc,
            cancellationToken);

        await AddDepartmentWorkAsync(
            query.CompanyId,
            agentIds,
            defaultAgentByDepartment,
            periodStartUtc,
            periodEndUtc,
            plannedCandidates,
            inProgressCandidates,
            approvalCandidates,
            completedCandidates,
            cancellationToken);

        SortCandidates(plannedCandidates);
        SortCandidates(inProgressCandidates);
        SortCandidates(approvalCandidates);
        SortCandidates(completedCandidates);

        var counts = agents
            .Select(agent => new TaskCountProjection(
                agent.Id,
                plannedCandidates.Count(x => x.AgentId == agent.Id),
                inProgressCandidates.Count(x => x.AgentId == agent.Id),
                approvalCandidates.Count(x => x.AgentId == agent.Id),
                completedCandidates.Count(x => x.AgentId == agent.Id)))
            .ToList();

        var awaitingTaskIds = approvalCandidates.Select(x => x.Id).ToArray();
        List<TaskApprovalProjection> taskApprovalRows = awaitingTaskIds.Length == 0
            ? []
            : await _dbContext.ApprovalRequests
                .AsNoTracking()
                .Where(x =>
                    x.CompanyId == query.CompanyId &&
                    x.Status == ApprovalRequestStatus.Pending &&
                    x.TargetEntityType == ApprovalTargetEntityType.Task.ToStorageValue() &&
                    awaitingTaskIds.Contains(x.TargetEntityId))
                .OrderBy(x => x.CreatedUtc)
                .Select(x => new TaskApprovalProjection(x.TargetEntityId, x.Id))
                .ToListAsync(cancellationToken);
        var approvalByTaskId = taskApprovalRows
            .GroupBy(x => x.TaskId)
            .ToDictionary(x => x.Key, x => x.First().ApprovalId);

        var countsByAgent = counts.ToDictionary(x => x.AgentId);
        var rows = agents.Select(agent =>
        {
            var agentCounts = countsByAgent.GetValueOrDefault(agent.Id) ?? new TaskCountProjection(agent.Id, 0, 0, 0, 0);
            return new AgentStaffRowDto(
                agent.Id,
                agent.DisplayName,
                agent.RoleName,
                agent.Department,
                agent.Status.ToStorageValue(),
                agent.AvatarUrl,
                $"/agents/{agent.Id:D}?companyId={query.CompanyId:D}",
                MapTasks(plannedCandidates, agent.Id, query.CompanyId, approvalByTaskId),
                MapTasks(inProgressCandidates, agent.Id, query.CompanyId, approvalByTaskId),
                MapTasks(approvalCandidates, agent.Id, query.CompanyId, approvalByTaskId),
                MapTasks(completedCandidates, agent.Id, query.CompanyId, approvalByTaskId),
                new AgentStaffStageCountsDto(agentCounts.Planned, agentCounts.InProgress, agentCounts.AwaitingApproval, agentCounts.Completed));
        }).ToList();

        var stageCounts = new AgentStaffStageCountsDto(
            counts.Sum(x => x.Planned),
            counts.Sum(x => x.InProgress),
            counts.Sum(x => x.AwaitingApproval),
            counts.Sum(x => x.Completed));

        var salesSummary = new AgentStaffSalesSummaryDto(
            sales.PipelineValue > 0,
            sales.PipelineValue,
            sales.ForecastRevenue,
            sales.Currency,
            sales.DealsNeedingAttention,
            $"/app/sales?companyId={query.CompanyId:D}");
        var supportSummary = new AgentStaffSupportSummaryDto(
            support.Summary.SlaRisk,
            support.Summary.SlaBreached,
            support.Summary.Open,
            $"/support?companyId={query.CompanyId:D}&slaRisk=true");

        var attention = BuildAttentionItems(query.CompanyId, stageCounts, salesSummary, supportSummary);
        return new AgentStaffOverviewDto(
            query.CompanyId,
            companyName,
            _timeProvider.GetUtcNow().UtcDateTime,
            period,
            finance,
            salesSummary,
            supportSummary,
            stageCounts,
            rows,
            attention);
    }

    private async Task<AgentStaffFinancialSummaryDto> BuildFinanceSummaryAsync(
        Guid companyId,
        ResolvedCompanyMembershipContext membership,
        AgentStaffOverviewPeriodDto period,
        CancellationToken cancellationToken)
    {
        if (!FinanceAccess.CanView(membership.MembershipRole.ToStorageValue()))
        {
            return FinanceUnavailable(companyId, "Your company role does not include access to finance data.");
        }

        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true ||
            !(await _authorizationService.AuthorizeAsync(principal, companyId, CompanyPolicies.FinanceView)).Succeeded)
        {
            return FinanceUnavailable(companyId, "Finance access is required to view revenue, costs, and result.");
        }

        try
        {
            var current = await _financeReadService.GetMonthlyProfitAndLossAsync(
                new GetFinanceMonthlyProfitAndLossQuery(companyId, period.Year, period.Month),
                cancellationToken);
            var previousDate = period.StartUtc.AddMonths(-1);
            var previous = await _financeReadService.GetMonthlyProfitAndLossAsync(
                new GetFinanceMonthlyProfitAndLossQuery(companyId, previousDate.Year, previousDate.Month),
                cancellationToken);
            var hasActivity = current.Revenue != 0 || current.Expenses != 0;

            return new AgentStaffFinancialSummaryDto(
                true,
                true,
                hasActivity,
                hasActivity ? current.Revenue : null,
                hasActivity ? current.Expenses : null,
                hasActivity ? current.NetResult : null,
                current.Currency,
                hasActivity ? PercentageChange(current.Revenue, previous.Revenue) : null,
                hasActivity ? PercentageChange(current.Expenses, previous.Expenses) : null,
                hasActivity ? PercentageChange(current.NetResult, previous.NetResult) : null,
                hasActivity
                    ? "Financial values use posted records for the selected month."
                    : "No posted finance activity exists for the selected month.",
                $"/finance/monthly-summary?companyId={companyId:D}");
        }
        catch (FinanceNotInitializedException)
        {
            _logger.LogInformation("Agent staff finance summary is unavailable because finance is not initialized for company {CompanyId}.", companyId);
            return new AgentStaffFinancialSummaryDto(
                true,
                false,
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                "Connect or initialize finance to show revenue, costs, and result.",
                $"/finance/settings?companyId={companyId:D}");
        }
    }

    private async Task<List<TaskProjection>> LoadTaskCandidatesAsync(
        Guid companyId,
        Guid[] agentIds,
        IReadOnlyDictionary<string, Guid> defaultAgentByDepartment,
        System.Linq.Expressions.Expression<Func<VirtualCompany.Domain.Entities.WorkTask, bool>> stagePredicate,
        CancellationToken cancellationToken)
    {
        if (agentIds.Length == 0)
        {
            return [];
        }

        var rows = await _dbContext.WorkTasks
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                (!x.AssignedAgentId.HasValue || agentIds.Contains(x.AssignedAgentId.Value)))
            .Where(stagePredicate)
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.DueUtc == null)
            .ThenBy(x => x.DueUtc)
            .ThenByDescending(x => x.UpdatedUtc)
            .Take(CandidateLimitPerStage)
            .Select(x => new TaskProjection(
                x.Id,
                x.AssignedAgentId,
                x.Title,
                x.Description,
                x.Type,
                x.Priority,
                x.Status,
                x.DueUtc,
                x.UpdatedUtc,
                x.CompletedUtc,
                null))
            .ToListAsync(cancellationToken);

        return rows
            .Select(item => item.AgentId.HasValue
                ? item
                : item with { AgentId = ResolveDepartmentAgent(item.Type, defaultAgentByDepartment) })
            .Where(item => item.AgentId.HasValue)
            .ToList();
    }

    private async Task AddDepartmentWorkAsync(
        Guid companyId,
        Guid[] agentIds,
        IReadOnlyDictionary<string, Guid> defaultAgentByDepartment,
        DateTime periodStartUtc,
        DateTime periodEndUtc,
        List<TaskProjection> planned,
        List<TaskProjection> inProgress,
        List<TaskProjection> awaitingApproval,
        List<TaskProjection> completed,
        CancellationToken cancellationToken)
    {
        if (defaultAgentByDepartment.TryGetValue("support", out var supportAgentId))
        {
            var cases = await _dbContext.SupportCases
                .AsNoTracking()
                .Where(x =>
                    x.CompanyId == companyId &&
                    (x.Status != SupportCaseStatuses.Resolved && x.Status != SupportCaseStatuses.Closed ||
                     x.ResolvedUtc >= periodStartUtc && x.ResolvedUtc < periodEndUtc ||
                     x.ClosedUtc >= periodStartUtc && x.ClosedUtc < periodEndUtc))
                .OrderByDescending(x => x.Priority)
                .ThenBy(x => x.ResolutionDueUtc)
                .Take(CandidateLimitPerStage)
                .Select(x => new SupportCaseProjection(
                    x.Id,
                    x.AssignedAgentId,
                    x.Subject,
                    x.Summary,
                    x.Status,
                    x.Priority,
                    x.FirstResponseDueUtc,
                    x.ResolutionDueUtc,
                    x.UpdatedUtc,
                    x.ResolvedUtc,
                    x.ClosedUtc))
                .ToListAsync(cancellationToken);

            foreach (var supportCase in cases)
            {
                var agentId = supportCase.AssignedAgentId is Guid assignedAgentId && agentIds.Contains(assignedAgentId)
                    ? assignedAgentId
                    : supportAgentId;
                var status = MapSupportStatus(supportCase.Status);
                var candidate = new TaskProjection(
                    supportCase.Id,
                    agentId,
                    supportCase.Subject,
                    supportCase.Summary,
                    "support.case",
                    MapSupportPriority(supportCase.Priority),
                    status,
                    Earliest(supportCase.FirstResponseDueUtc, supportCase.ResolutionDueUtc),
                    supportCase.UpdatedUtc,
                    supportCase.ResolvedUtc ?? supportCase.ClosedUtc,
                    $"/support/cases/{supportCase.Id:D}?companyId={companyId:D}");
                AddByStage(candidate, planned, inProgress, awaitingApproval, completed);
            }
        }

        if (defaultAgentByDepartment.TryGetValue("sales", out var salesAgentId))
        {
            var deals = await _dbContext.Deals
                .AsNoTracking()
                .Where(x =>
                    x.CompanyId == companyId &&
                    !x.IsDeleted &&
                    (x.Status != SalesStatuses.Won && x.Status != SalesStatuses.Lost ||
                     x.UpdatedUtc >= periodStartUtc && x.UpdatedUtc < periodEndUtc))
                .OrderBy(x => x.ExpectedCloseUtc == null)
                .ThenBy(x => x.ExpectedCloseUtc)
                .ThenByDescending(x => x.UpdatedUtc)
                .Take(CandidateLimitPerStage)
                .Select(x => new DealProjection(
                    x.Id,
                    x.Title,
                    x.Amount,
                    x.Currency,
                    x.Status,
                    x.ExpectedCloseUtc,
                    x.UpdatedUtc))
                .ToListAsync(cancellationToken);

            foreach (var deal in deals)
            {
                var status = MapSalesStatus(deal.Status);
                var candidate = new TaskProjection(
                    deal.Id,
                    salesAgentId,
                    deal.Title,
                    $"Pipeline value {deal.Amount:0.##} {deal.Currency}",
                    "sales.deal",
                    WorkTaskPriority.Normal,
                    status,
                    deal.ExpectedCloseUtc,
                    deal.UpdatedUtc,
                    status == WorkTaskStatus.Completed ? deal.UpdatedUtc : null,
                    $"/app/sales/deals/{deal.Id:D}?companyId={companyId:D}");
                AddByStage(candidate, planned, inProgress, awaitingApproval, completed);
            }
        }
    }

    private async Task<DateTime> ResolvePeriodStartUtcAsync(
        GetAgentStaffOverviewQuery query,
        CancellationToken cancellationToken)
    {
        if (query.Year.HasValue && query.Month.HasValue)
        {
            return new DateTime(query.Year.Value, query.Month.Value, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        var latestInvoiceUtc = await _dbContext.FinanceInvoices
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId)
            .Select(x => (DateTime?)x.IssuedUtc)
            .MaxAsync(cancellationToken);
        var latestTransactionUtc = await _dbContext.FinanceTransactions
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId)
            .Select(x => (DateTime?)x.TransactionUtc)
            .MaxAsync(cancellationToken);
        var latestUtc = latestInvoiceUtc is null || latestTransactionUtc > latestInvoiceUtc
            ? latestTransactionUtc
            : latestInvoiceUtc;
        var selected = latestUtc ?? _timeProvider.GetUtcNow().UtcDateTime;
        return new DateTime(selected.Year, selected.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    private static void AddByStage(
        TaskProjection candidate,
        ICollection<TaskProjection> planned,
        ICollection<TaskProjection> inProgress,
        ICollection<TaskProjection> awaitingApproval,
        ICollection<TaskProjection> completed)
    {
        switch (candidate.Status)
        {
            case WorkTaskStatus.InProgress:
                inProgress.Add(candidate);
                break;
            case WorkTaskStatus.AwaitingApproval:
                awaitingApproval.Add(candidate);
                break;
            case WorkTaskStatus.Completed:
                completed.Add(candidate);
                break;
            default:
                planned.Add(candidate);
                break;
        }
    }

    private static void SortCandidates(List<TaskProjection> candidates)
    {
        var ordered = candidates
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.DueUtc == null)
            .ThenBy(x => x.DueUtc)
            .ThenByDescending(x => x.UpdatedUtc)
            .ToList();
        candidates.Clear();
        candidates.AddRange(ordered);
    }

    private static Guid? ResolveDepartmentAgent(
        string taskType,
        IReadOnlyDictionary<string, Guid> defaultAgentByDepartment)
    {
        var normalized = taskType.Trim().ToLowerInvariant();
        var department = normalized.StartsWith("finance", StringComparison.Ordinal) ||
                         normalized.StartsWith("accounting", StringComparison.Ordinal) ||
                         normalized.StartsWith("supplier", StringComparison.Ordinal)
            ? "finance"
            : normalized.StartsWith("support", StringComparison.Ordinal)
                ? "support"
                : normalized.StartsWith("sales", StringComparison.Ordinal) ||
                  normalized.StartsWith("lead", StringComparison.Ordinal) ||
                  normalized.StartsWith("deal", StringComparison.Ordinal) ||
                  normalized.StartsWith("campaign", StringComparison.Ordinal)
                    ? "sales"
                    : null;
        return department is not null && defaultAgentByDepartment.TryGetValue(department, out var agentId)
            ? agentId
            : null;
    }

    private static string NormalizeDepartment(string department) => department.Trim().ToLowerInvariant();

    private static WorkTaskStatus MapSupportStatus(string status) => status switch
    {
        SupportCaseStatuses.New => WorkTaskStatus.New,
        SupportCaseStatuses.AwaitingApproval => WorkTaskStatus.AwaitingApproval,
        SupportCaseStatuses.Resolved or SupportCaseStatuses.Closed => WorkTaskStatus.Completed,
        _ => WorkTaskStatus.InProgress
    };

    private static WorkTaskPriority MapSupportPriority(string priority) => priority switch
    {
        SupportPriorities.Urgent => WorkTaskPriority.Critical,
        SupportPriorities.High => WorkTaskPriority.High,
        SupportPriorities.Low => WorkTaskPriority.Low,
        _ => WorkTaskPriority.Normal
    };

    private static WorkTaskStatus MapSalesStatus(string status) => status switch
    {
        SalesStatuses.Draft or SalesStatuses.Pending => WorkTaskStatus.New,
        SalesStatuses.WaitingForApproval => WorkTaskStatus.AwaitingApproval,
        SalesStatuses.Won or SalesStatuses.Lost or SalesStatuses.Completed => WorkTaskStatus.Completed,
        _ => WorkTaskStatus.InProgress
    };

    private static DateTime? Earliest(DateTime? first, DateTime? second) => first is null
        ? second
        : second is null || first <= second
            ? first
            : second;

    private static IReadOnlyList<AgentStaffTaskDto> MapTasks(
        IEnumerable<TaskProjection> candidates,
        Guid agentId,
        Guid companyId,
        IReadOnlyDictionary<Guid, Guid> approvalByTaskId) =>
        candidates
            .Where(x => x.AgentId == agentId)
            .Take(PreviewItemsPerStage)
            .Select(task =>
            {
                var approvalId = approvalByTaskId.GetValueOrDefault(task.Id);
                return new AgentStaffTaskDto(
                    task.Id,
                    task.Title,
                    BuildTaskContext(task),
                    task.Priority.ToStorageValue(),
                    task.Status.ToStorageValue(),
                    task.DueUtc,
                    task.UpdatedUtc,
                    task.CompletedUtc,
                    task.Route ?? $"/tasks?companyId={companyId:D}&taskId={task.Id:D}",
                    approvalId == Guid.Empty ? null : approvalId,
                    approvalId == Guid.Empty
                        ? null
                        : $"/approvals?companyId={companyId:D}&status=pending&approvalId={approvalId:D}");
            })
            .ToList();

    private static string BuildTaskContext(TaskProjection task)
    {
        var value = string.IsNullOrWhiteSpace(task.Description)
            ? task.Status switch
            {
                WorkTaskStatus.New => "Ready to start",
                WorkTaskStatus.InProgress => "Work is in progress",
                WorkTaskStatus.Blocked => "Blocked and needs attention",
                WorkTaskStatus.AwaitingApproval => "Waiting for human approval",
                WorkTaskStatus.Completed => "Completed work",
                WorkTaskStatus.Failed => "Execution failed and needs attention",
                _ => "Agent work item"
            }
            : task.Description.Trim();
        return value.Length <= 110 ? value : $"{value[..107]}...";
    }

    private static IReadOnlyList<AgentStaffAttentionItemDto> BuildAttentionItems(
        Guid companyId,
        AgentStaffStageCountsDto stages,
        AgentStaffSalesSummaryDto sales,
        AgentStaffSupportSummaryDto support)
    {
        var items = new List<AgentStaffAttentionItemDto>();
        if (stages.AwaitingHumanApproval > 0)
        {
            items.Add(new AgentStaffAttentionItemDto(
                "approvals",
                "warning",
                $"{stages.AwaitingHumanApproval} approval{(stages.AwaitingHumanApproval == 1 ? string.Empty : "s")} need review",
                "Human review is required before these tasks can continue.",
                "Open approvals",
                $"/approvals?companyId={companyId:D}&status=pending"));
        }

        items.Add(sales.DealsNeedingAttention > 0
            ? new AgentStaffAttentionItemDto(
                "sales",
                "warning",
                $"{sales.DealsNeedingAttention} sales deal{(sales.DealsNeedingAttention == 1 ? string.Empty : "s")} need attention",
                "These open deals have not been updated for at least seven days.",
                "Review sales",
                sales.Route)
            : new AgentStaffAttentionItemDto(
                "sales",
                "positive",
                "Sales pipeline is up to date",
                sales.HasData ? "No open deals are currently stale." : "No active pipeline value is available yet.",
                sales.HasData ? "Open sales" : null,
                sales.HasData ? sales.Route : null));

        if (support.CasesAtSlaRisk > 0 || support.BreachedCases > 0)
        {
            var supportTitle = support.BreachedCases switch
            {
                1 => "1 support case has missed its SLA target",
                > 1 => $"{support.BreachedCases} support cases have missed their SLA targets",
                _ => $"{support.CasesAtSlaRisk} support case{(support.CasesAtSlaRisk == 1 ? string.Empty : "s")} at SLA risk"
            };
            items.Add(new AgentStaffAttentionItemDto(
                "support",
                support.BreachedCases > 0 ? "critical" : "warning",
                supportTitle,
                support.BreachedCases > 0
                    ? support.BreachedCases == 1
                        ? "1 open case has already breached its target."
                        : $"{support.BreachedCases} open cases have already breached their targets."
                    : "Review these cases before their response or resolution target is missed.",
                "Review cases",
                support.Route));
        }
        else
        {
            items.Add(new AgentStaffAttentionItemDto(
                "support",
                "positive",
                "Support SLA is on track",
                "No open support cases are currently at risk.",
                support.OpenCases > 0 ? "Open support" : null,
                support.OpenCases > 0 ? support.Route : null));
        }

        return items;
    }

    private static AgentStaffFinancialSummaryDto FinanceUnavailable(Guid companyId, string explanation) =>
        new(false, false, false, null, null, null, null, null, null, null, explanation, $"/finance?companyId={companyId:D}");

    private static decimal? PercentageChange(decimal current, decimal previous) =>
        previous == 0
            ? null
            : Math.Round((current - previous) / Math.Abs(previous) * 100m, 1, MidpointRounding.AwayFromZero);

    private static int DepartmentOrder(string department) => department.Trim().ToLowerInvariant() switch
    {
        "finance" => 10,
        "sales" => 20,
        "support" => 30,
        _ => 100
    };

    private static void Validate(GetAgentStaffOverviewQuery query)
    {
        if (query.CompanyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(query));
        }

        if (query.Year.HasValue != query.Month.HasValue)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Year and month must be supplied together.");
        }

        if (query.Year is < 2000 or > 2100 || query.Month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "The reporting month must be between January 2000 and December 2100.");
        }
    }

    private sealed record AgentProjection(
        Guid Id,
        string DisplayName,
        string RoleName,
        string Department,
        AgentStatus Status,
        string? AvatarUrl);

    private sealed record TaskProjection(
        Guid Id,
        Guid? AgentId,
        string Title,
        string? Description,
        string Type,
        WorkTaskPriority Priority,
        WorkTaskStatus Status,
        DateTime? DueUtc,
        DateTime UpdatedUtc,
        DateTime? CompletedUtc,
        string? Route);

    private sealed record SupportCaseProjection(
        Guid Id,
        Guid? AssignedAgentId,
        string Subject,
        string Summary,
        string Status,
        string Priority,
        DateTime? FirstResponseDueUtc,
        DateTime? ResolutionDueUtc,
        DateTime UpdatedUtc,
        DateTime? ResolvedUtc,
        DateTime? ClosedUtc);

    private sealed record DealProjection(
        Guid Id,
        string Title,
        decimal Amount,
        string Currency,
        string Status,
        DateTime? ExpectedCloseUtc,
        DateTime UpdatedUtc);

    private sealed record TaskCountProjection(Guid AgentId, int Planned, int InProgress, int AwaitingApproval, int Completed);

    private sealed record TaskApprovalProjection(Guid TaskId, Guid ApprovalId);
}
