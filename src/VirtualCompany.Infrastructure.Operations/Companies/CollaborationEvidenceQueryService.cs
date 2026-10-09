using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CollaborationEvidenceQueryService(VirtualCompanyDbContext db, CompanyWorkVisibility visibility,
    IAgentWorkQueryService work, IAuditEventWriter audit, TimeProvider clock,
    ILogger<CollaborationEvidenceQueryService> logger) : ICollaborationEvidenceQueryService
{
    private static readonly Meter Meter = new("VirtualCompany.CollaborationEvidence", "1.0");
    private static readonly Counter<long> Failures = Meter.CreateCounter<long>("collaboration.projection_failed");
    public async Task<CollaborationEvidenceDto> GetAsync(Guid company, string kind, Guid id, CancellationToken ct)
    {
        try
        {
            var item = await work.GetAsync(company, kind, id, ct);
            var scope = await visibility.ResolveAsync(company, ct);
            if (kind == "task")
            {
                var contributorParent = await db.CollaborationContributions.Where(x => x.CompanyId == company && x.SourceTaskId == id)
                    .Select(x => (Guid?)x.ParentTaskId).FirstOrDefaultAsync(ct);
                if (contributorParent.HasValue)
                {
                    var outcomeTask = await db.WorkTasks.Where(x => x.CompanyId == company && x.Id == contributorParent)
                        .Select(x => x.ParentTaskId).SingleOrDefaultAsync(ct) ?? contributorParent.Value;
                    try { var outcome = await work.GetAsync(company, "task", outcomeTask, ct);
                        item = outcome with { RelatedRecords = outcome.RelatedRecords.Concat([new AgentWorkLinkDto("Owning business outcome", outcome.DetailRoute)]).ToArray() }; }
                    catch (KeyNotFoundException) { /* Keep only the independently permitted worker identity. */ }
                }
            }
            var root = kind == "task" ? id : kind == "initiative"
                ? await db.OperatingInitiatives.Where(x => x.CompanyId == company && x.Id == id).Select(x => x.TaskId).SingleAsync(ct)
                : null;
            var rows = new List<CollaborationContribution>();
            if (root.HasValue)
            {
                var parentIds = db.WorkTasks.Where(x => x.CompanyId == company && (x.Id == root || x.ParentTaskId == root)).Select(x => x.Id);
                rows = await db.CollaborationContributions.AsNoTracking().Where(x => x.CompanyId == company && parentIds.Contains(x.ParentTaskId))
                    .OrderBy(x => x.CreatedUtc).ThenBy(x => x.Id).Take(501).ToListAsync(ct);
                // A worker's direct identity resolves to the same collaboration, never a duplicate outcome.
                if (rows.Count == 0 && kind == "task")
                {
                    var parent = await db.WorkTasks.Where(x => x.CompanyId == company && x.Id == root).Select(x => x.ParentTaskId).SingleOrDefaultAsync(ct);
                    if (parent.HasValue && await scope.Tasks(db.WorkTasks.Where(x => x.CompanyId == company && x.Id == parent)).AnyAsync(ct))
                        rows = await db.CollaborationContributions.AsNoTracking().Where(x => x.CompanyId == company && x.ParentTaskId == parent)
                            .OrderBy(x => x.CreatedUtc).ThenBy(x => x.Id).Take(501).ToListAsync(ct);
                }
            }
            var partial = rows.Count > 500; rows = rows.Take(500).ToList();
            var taskIds = rows.Select(x => x.SourceTaskId).Concat(rows.Select(x => x.ParentTaskId)).Distinct().ToArray();
            var permittedTasks = await scope.Tasks(db.WorkTasks.AsNoTracking().Where(x => x.CompanyId == company && taskIds.Contains(x.Id)))
                .Select(x => x.Id).ToListAsync(ct);
            var agents = await scope.Agents(db.Agents.AsNoTracking()).Where(x => rows.Select(r => r.AgentId).Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct);
            var allIds = rows.Select(x => x.Id).ToArray();
            var handoffs = await db.CollaborationArtifactHandoffs.AsNoTracking().Where(x => x.CompanyId == company &&
                allIds.Contains(x.ReceivingContributionId)).OrderBy(x => x.CreatedUtc).Take(2001).ToListAsync(ct);
            var handoffWindowExceeded = handoffs.Count > 2000;
            partial |= handoffWindowExceeded; handoffs = handoffs.Take(2000).ToList();
            var visible = rows.Where(x => permittedTasks.Contains(x.SourceTaskId) && permittedTasks.Contains(x.ParentTaskId) && agents.ContainsKey(x.AgentId))
                .Select(x => x.Id).ToHashSet();
            if (handoffWindowExceeded) visible.Clear(); // Missing dependencies cannot authorize derived output.
            // Derived artifacts carry the scope of every input. Do not disclose outputs, rationale or input identities
            // from a restricted ancestor (including chains and inputs beyond the source window).
            bool changed;
            do { changed = false; foreach (var edge in handoffs)
                if (!visible.Contains(edge.InputContributionId) && visible.Remove(edge.ReceivingContributionId)) changed = true;
            } while (changed);
            var restricted = rows.Any(x => !visible.Contains(x.Id));
            var artifacts = rows.Where(x => visible.Contains(x.Id)).Select(x => new CollaborationArtifactDto(x.Id,
                x.ParentTaskId, x.SourceTaskId, x.Sequence, x.Version,
                new(agents[x.AgentId].Id, agents[x.AgentId].DisplayName, agents[x.AgentId].RoleName),
                x.Role.ToStorageValue(), x.Pattern.ToStorageValue(), x.Objective, x.Status,
                x.Output, x.Rationale, x.ReviewOutcome, x.CreatedUtc,
                handoffs.Where(h => h.ReceivingContributionId == x.Id).Select(h => h.InputContributionId).ToArray(),
                [new("Source worker record", $"/agents/work/task/{x.SourceTaskId:D}?companyId={company:D}", x.CreatedUtc)])).ToArray();
            await audit.WriteAsync(new(company, "user", scope.Resolution.UserId, "collaboration.evidence_read", "work",
                id.ToString("N"), "succeeded", DataSources: ["collaboration_contributions", "collaboration_artifact_handoffs"]), ct);
            return new(company, kind, id, item.Objective, item.State, item.AccountableHuman,
                restricted ? "Some contributions are unavailable in your access scope. Ask the accountable human to review the next dependency." : item.Dependency,
                clock.GetUtcNow().UtcDateTime, artifacts,
                handoffs.Where(x => visible.Contains(x.InputContributionId) && visible.Contains(x.ReceivingContributionId))
                    .Select(x => new CollaborationHandoffDto(x.Id, x.InputContributionId, x.ReceivingContributionId, x.Passed, x.Reason, x.CreatedUtc)).ToArray(),
                item.RelatedRecords, partial, restricted,
                (partial ? new[] { "Only the first 500 artifact versions and 2,000 handoff events are shown. Open owning work for older history." } : [])
                .Concat(artifacts.Length == 0 ? ["No permitted typed collaboration artifacts are recorded. Existing work evidence remains available through owning records."] : []).ToArray());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { Failures.Add(1); logger.LogWarning("Collaboration evidence projection failed ({FailureType})", ex.GetType().Name); throw; }
    }
}
