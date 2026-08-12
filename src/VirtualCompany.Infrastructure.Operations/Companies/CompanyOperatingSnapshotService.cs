using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyOperatingSnapshotService : ICompanyOperatingSnapshotService, ICompanyOperatingSnapshotQueryService
{
    public const string SchemaVersion = "company-operating-snapshot.v1";
    private const int MaximumItemsPerSection = 50;
    private readonly VirtualCompanyDbContext _db;
    private readonly ISignalEngine _signals;
    private readonly IReadOnlyList<ICompanyOperatingSnapshotContributor> _contributors;
    private readonly ILogger<CompanyOperatingSnapshotService> _logger;
    private readonly ICompanyMembershipContextResolver _memberships;

    public CompanyOperatingSnapshotService(
        VirtualCompanyDbContext db,
        ISignalEngine signals,
        IEnumerable<ICompanyOperatingSnapshotContributor> contributors,
        ICompanyMembershipContextResolver memberships,
        ILogger<CompanyOperatingSnapshotService> logger)
    {
        _db = db;
        _signals = signals;
        _contributors = contributors.ToList();
        _memberships = memberships;
        _logger = logger;
    }

    public async Task<OperatingSnapshotDto> CaptureAsync(Guid companyId, Guid cycleId, CancellationToken ct)
    {
        var cycle = await _db.OperatingCycles.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == cycleId, ct)
            ?? throw new KeyNotFoundException("Operating cycle not found.");

        var goals = await _db.CompanyGoals.AsNoTracking().Where(x => x.CompanyId == companyId && x.Status == CompanyGoalStatus.Active)
            .OrderByDescending(x => x.Priority).ThenBy(x => x.TargetUtc).Take(MaximumItemsPerSection + 1)
            .Select(x => new { x.Id, x.Name, x.Outcome, Status = x.Status.ToStorageValue(), Priority = x.Priority.ToStorageValue(), x.MetricKey, x.BaselineValue, x.TargetValue, x.TargetUtc, x.OwnerAgentId, x.Version }).ToListAsync(ct);
        var tasks = await _db.WorkTasks.AsNoTracking().Where(x => x.CompanyId == companyId && x.Status != WorkTaskStatus.Completed && x.Status != WorkTaskStatus.Failed)
            .OrderByDescending(x => x.Priority).ThenBy(x => x.DueUtc).Take(MaximumItemsPerSection + 1)
            .Select(x => new { x.Id, x.Title, Status = x.Status.ToStorageValue(), Priority = x.Priority.ToStorageValue(), x.AssignedAgentId, x.DueUtc, x.WorkflowInstanceId, x.UpdatedUtc }).ToListAsync(ct);
        var agents = await _db.Agents.AsNoTracking().Where(x => x.CompanyId == companyId && x.Status == AgentStatus.Active)
            .OrderBy(x => x.Department).ThenBy(x => x.DisplayName).Take(MaximumItemsPerSection + 1)
            .Select(x => new { x.Id, x.DisplayName, x.RoleName, x.Department, Status = x.Status.ToStorageValue(), Autonomy = x.AutonomyLevel.ToStorageValue(), x.UpdatedUtc }).ToListAsync(ct);
        var approvals = await _db.ApprovalRequests.AsNoTracking().Where(x => x.CompanyId == companyId && x.Status == ApprovalRequestStatus.Pending)
            .OrderBy(x => x.CreatedUtc).Take(MaximumItemsPerSection + 1)
            .Select(x => new { x.Id, x.ApprovalType, x.TargetEntityType, x.TargetEntityId, Status = x.Status.ToStorageValue(), x.RequiredRole, x.CreatedUtc }).ToListAsync(ct);
        var workflows = await _db.WorkflowInstances.AsNoTracking().Where(x => x.CompanyId == companyId && x.State != WorkflowInstanceStatus.Completed && x.State != WorkflowInstanceStatus.Cancelled && x.State != WorkflowInstanceStatus.Failed)
            .OrderByDescending(x => x.UpdatedUtc).Take(MaximumItemsPerSection + 1)
            .Select(x => new { x.Id, x.DefinitionId, Status = x.State.ToStorageValue(), x.CurrentStep, x.StartedUtc, x.UpdatedUtc }).ToListAsync(ct);
        var initiatives = await _db.OperatingInitiatives.AsNoTracking().Where(x => x.CompanyId == companyId &&
                x.Status != OperatingInitiativeStatus.Completed && x.Status != OperatingInitiativeStatus.Cancelled)
            .OrderByDescending(x => x.Priority).ThenBy(x => x.TargetUtc).Take(MaximumItemsPerSection + 1)
            .Select(x => new { x.Id, x.PlanId, x.GoalId, x.Title, x.DesiredOutcome, Status = x.Status.ToStorageValue(), x.OwnerAgentId, x.TargetUtc, x.TaskId, x.WorkflowInstanceId, x.UpdatedUtc }).ToListAsync(ct);
        var decisions = await _db.OperatingDecisions.AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedUtc).Take(MaximumItemsPerSection + 1)
            .Select(x => new { x.Id, x.PlanId, x.InitiativeId, ActionClass = x.ActionClass.ToStorageValue(), x.ActionType, x.TargetType, x.TargetId, x.RiskLevel, x.ApprovalRequired, x.Confidence, x.CreatedUtc }).ToListAsync(ct);
        var reviews = await _db.OperatingReviews.AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedUtc).Take(MaximumItemsPerSection + 1)
            .Select(x => new { x.Id, x.PlanId, x.PlanVersion, x.InitiativeId, Outcome = x.Outcome.ToStorageValue(), x.ExpectedEvidence, x.ActualEvidence, x.NextAction, x.CreatedUtc }).ToListAsync(ct);
        var backgroundFailures = await _db.BackgroundExecutions.AsNoTracking().Where(x => x.CompanyId == companyId &&
                (x.Status == BackgroundExecutionStatus.Failed || x.Status == BackgroundExecutionStatus.Blocked ||
                 x.Status == BackgroundExecutionStatus.Escalated || x.Status == BackgroundExecutionStatus.RetryScheduled))
            .OrderByDescending(x => x.UpdatedUtc).Take(MaximumItemsPerSection + 1)
            .Select(x => new { x.Id, Type = x.ExecutionType.ToStorageValue(), Status = x.Status.ToStorageValue(), x.RelatedEntityType, x.RelatedEntityId, x.FailureCode, x.FailureMessage, x.NextRetryUtc, x.UpdatedUtc }).ToListAsync(ct);
        var signals = await _signals.GenerateSignals(companyId, ct);

        var contributorResults = new List<CompanyOperatingSnapshotContribution>();
        var contributorGaps = new List<string>();
        foreach (var contributor in _contributors.OrderBy(x => x.SectionName, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var contribution = await contributor.CaptureAsync(companyId, ct);
                if (!string.Equals(contribution.SectionName, contributor.SectionName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Snapshot contributor '{contributor.SectionName}' returned section '{contribution.SectionName}'.");
                contributorResults.Add(contribution);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Operating snapshot contributor {SectionName} failed for company {CompanyId}.", contributor.SectionName, companyId);
                contributorGaps.Add($"{contributor.SectionName}: source data was temporarily unavailable.");
            }
        }

        var truncated = goals.Count > MaximumItemsPerSection || tasks.Count > MaximumItemsPerSection || agents.Count > MaximumItemsPerSection || approvals.Count > MaximumItemsPerSection || workflows.Count > MaximumItemsPerSection || initiatives.Count > MaximumItemsPerSection || decisions.Count > MaximumItemsPerSection || reviews.Count > MaximumItemsPerSection || backgroundFailures.Count > MaximumItemsPerSection || contributorResults.Any(x => x.IsTruncated);
        var payload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["observedAtUtc"] = JsonValue.Create(DateTime.UtcNow),
            ["goals"] = JsonSerializer.SerializeToNode(goals.Take(MaximumItemsPerSection)),
            ["tasks"] = JsonSerializer.SerializeToNode(tasks.Take(MaximumItemsPerSection)),
            ["agents"] = JsonSerializer.SerializeToNode(agents.Take(MaximumItemsPerSection)),
            ["approvals"] = JsonSerializer.SerializeToNode(approvals.Take(MaximumItemsPerSection)),
            ["workflows"] = JsonSerializer.SerializeToNode(workflows.Take(MaximumItemsPerSection)),
            ["initiatives"] = JsonSerializer.SerializeToNode(initiatives.Take(MaximumItemsPerSection)),
            ["recentDecisions"] = JsonSerializer.SerializeToNode(decisions.Take(MaximumItemsPerSection)),
            ["recentReviews"] = JsonSerializer.SerializeToNode(reviews.Take(MaximumItemsPerSection)),
            ["backgroundExceptions"] = JsonSerializer.SerializeToNode(backgroundFailures.Take(MaximumItemsPerSection)),
            ["signals"] = JsonSerializer.SerializeToNode(signals.OrderByDescending(x => x.Severity).ThenBy(x => x.Type).Select(x => new { SignalType = x.Type.ToString(), SourceType = "company_signal_engine", SourceId = x.Type.ToString(), x.Title, x.Summary, ObservedUtc = x.DetectedAtUtc, Severity = x.Severity.ToString(), FreshUntilUtc = x.DetectedAtUtc.AddHours(24), x.MetricValue, x.MetricLabel, x.ActionLabel, x.ActionUrl })),
            ["dataGaps"] = JsonSerializer.SerializeToNode(BuildDataGaps(goals.Count, agents.Count))
        };
        foreach (var contribution in contributorResults)
        {
            if (payload.ContainsKey(contribution.SectionName))
                throw new InvalidOperationException($"Duplicate operating snapshot section '{contribution.SectionName}'.");
            payload[contribution.SectionName] = contribution.Payload;
        }
        var sourceCount = goals.Count + tasks.Count + agents.Count + approvals.Count + workflows.Count + initiatives.Count + decisions.Count + reviews.Count + backgroundFailures.Count + signals.Count + contributorResults.Sum(x => x.SourceCount);
        var gaps = BuildDataGaps(goals.Count, agents.Count);
        gaps.AddRange(contributorResults.SelectMany(x => x.DataGaps));
        gaps.AddRange(contributorGaps);
        payload["dataGaps"] = JsonSerializer.SerializeToNode(gaps);
        var snapshot = new OperatingSnapshot(Guid.NewGuid(), companyId, cycle.Id, SchemaVersion, payload, sourceCount, gaps.Count, truncated);
        _db.OperatingSnapshots.Add(snapshot);
        await _db.SaveChangesAsync(ct);
        return Map(snapshot);
    }

    private static List<string> BuildDataGaps(int goalCount, int agentCount)
    {
        var gaps = new List<string>();
        if (goalCount == 0) gaps.Add("No active company goals are available.");
        if (agentCount == 0) gaps.Add("No active agents are available.");
        return gaps;
    }

    internal static OperatingSnapshotDto Map(OperatingSnapshot x) => new(x.Id, x.CompanyId, x.CycleId, x.SchemaVersion, x.Payload, x.SourceCount, x.DataGapCount, x.IsTruncated, x.CreatedUtc);

    public async Task<OperatingSnapshotDto> GetAsync(Guid companyId, Guid snapshotId, CancellationToken ct)
    {
        await RequireMemberAsync(companyId, ct);
        var row = await _db.OperatingSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == snapshotId, ct)
            ?? throw new KeyNotFoundException("Operating snapshot not found.");
        return Map(row);
    }

    public async Task<IReadOnlyList<OperatingSnapshotDto>> ListAsync(Guid companyId, int take, CancellationToken ct)
    {
        await RequireMemberAsync(companyId, ct);
        take = Math.Clamp(take, 1, 100);
        return (await _db.OperatingSnapshots.AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedUtc).Take(take).ToListAsync(ct)).Select(Map).ToArray();
    }

    private async Task RequireMemberAsync(Guid companyId, CancellationToken ct) =>
        _ = await _memberships.ResolveAsync(companyId, ct)
            ?? throw new UnauthorizedAccessException("Active company membership is required.");
}
