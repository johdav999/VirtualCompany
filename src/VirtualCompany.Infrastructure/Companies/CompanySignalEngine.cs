using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Signals;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanySignalEngine : ISignalEngine
{
    private const decimal WarningLoadPerAgent = 2m;
    private const decimal CriticalLoadPerAgent = 4m;
    private const decimal CriticalBlockedRatio = 0.50m;
    private const int WarningPendingApprovals = 3;
    private const int CriticalPendingApprovals = 5;
    private static readonly TimeSpan WarningApprovalAge = TimeSpan.FromHours(24);
    private static readonly TimeSpan CriticalApprovalAge = TimeSpan.FromHours(72);

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public CompanySignalEngine(VirtualCompanyDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<BusinessSignal>> GenerateSignals(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(companyId));
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var signals = new List<BusinessSignal>(2);

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

        var blockedTaskCount = await _dbContext.WorkTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(x => x.CompanyId == companyId && x.Status == WorkTaskStatus.Blocked, cancellationToken);

        var activeAgentCount = await _dbContext.Agents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(x => x.CompanyId == companyId && x.Status == AgentStatus.Active, cancellationToken);

        if (openTaskCount > 0)
        {
            var loadPerAgent = openTaskCount / (decimal)Math.Max(1, activeAgentCount);
            var blockedRatio = blockedTaskCount == 0 ? 0m : blockedTaskCount / (decimal)openTaskCount;
            var severity = loadPerAgent >= CriticalLoadPerAgent || blockedRatio >= CriticalBlockedRatio
                ? BusinessSignalSeverity.Critical
                : loadPerAgent >= WarningLoadPerAgent
                    ? BusinessSignalSeverity.Warning
                    : BusinessSignalSeverity.Info;

            signals.Add(new BusinessSignal(
                BusinessSignalType.OperationalLoad,
                severity,
                "Operational load is building",
                activeAgentCount == 0
                    ? $"{openTaskCount} open work item(s) are active with no active agents assigned."
                    : $"{openTaskCount} open work item(s) are spread across {activeAgentCount} active agent(s); {blockedTaskCount} are blocked.",
                decimal.Round(loadPerAgent, 2, MidpointRounding.AwayFromZero),
                "open_tasks_per_agent",
                "Open tasks",
                $"/tasks?companyId={companyId:D}",
                nowUtc));
        }

        var pendingApprovals = await _dbContext.ApprovalRequests
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status == ApprovalRequestStatus.Pending)
            .Select(x => x.CreatedUtc)
            .ToListAsync(cancellationToken);

        if (pendingApprovals.Count > 0)
        {
            var oldestPendingUtc = pendingApprovals.Min();
            var oldestAge = nowUtc - oldestPendingUtc;
            var severity = pendingApprovals.Count >= CriticalPendingApprovals || oldestAge >= CriticalApprovalAge
                ? BusinessSignalSeverity.Critical
                : pendingApprovals.Count >= WarningPendingApprovals || oldestAge >= WarningApprovalAge
                    ? BusinessSignalSeverity.Warning
                    : BusinessSignalSeverity.Info;

            signals.Add(new BusinessSignal(
                BusinessSignalType.ApprovalBottleneck,
                severity,
                "Approvals are waiting on decisions",
                $"{pendingApprovals.Count} approval(s) are pending. Oldest request age is {Math.Max(1, (int)Math.Floor(oldestAge.TotalHours))} hour(s).",
                pendingApprovals.Count,
                "pending_approvals",
                "Review approvals",
                $"/approvals?companyId={companyId:D}&status=pending",
                nowUtc));
        }

        return signals;
    }
}