using Microsoft.EntityFrameworkCore;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

internal static class CollaborationContentVisibility
{
    public static async Task<bool> AllowsAsync(VirtualCompanyDbContext db, CompanyWorkScope scope, Guid task, CancellationToken ct)
    {
        var company = scope.Resolution.CompanyId;
        var authorized = scope.Tasks(db.WorkTasks.Where(x => x.CompanyId == company));
        // Legacy consolidated payloads also embed child contributions. Apply this boundary even
        // before typed receipts exist and when a parent was linked below an operating source task.
        if (await db.WorkTasks.AnyAsync(x => x.CompanyId == company && (x.ParentTaskId == task ||
            db.WorkTasks.Any(p => p.CompanyId == company && p.Id == x.ParentTaskId && p.ParentTaskId == task)) &&
            !authorized.Any(t => t.Id == x.Id), ct)) return false;
        var rows = await db.CollaborationContributions.AsNoTracking().Where(x => x.CompanyId == company && x.SourceTaskId == task)
            .OrderByDescending(x => x.Version).Take(501).ToListAsync(ct);
        if (rows.Count > 500) return false;
        var pending = rows.Select(x => x.Id).ToHashSet(); var seen = new HashSet<Guid>();
        while (pending.Count > 0)
        {
            if (seen.Count > 2000) return false;
            var ids = pending.ToArray(); seen.UnionWith(ids);
            var inputs = await db.CollaborationArtifactHandoffs.AsNoTracking().Where(x => x.CompanyId == company && ids.Contains(x.ReceivingContributionId))
                .Select(x => x.InputContributionId).Distinct().Take(2001).ToListAsync(ct);
            if (inputs.Count > 2000) return false;
            var references = await db.CollaborationContributions.AsNoTracking().Where(x => x.CompanyId == company && inputs.Contains(x.Id))
                .Select(x => new { x.Id, x.SourceTaskId, x.AgentId }).ToListAsync(ct);
            var agents = scope.Agents(db.Agents);
            if (references.Count != inputs.Count || await db.CollaborationContributions.AnyAsync(x => x.CompanyId == company && inputs.Contains(x.Id) &&
                (!authorized.Any(t => t.Id == x.SourceTaskId) || !agents.Any(a => a.Id == x.AgentId)), ct)) return false;
            pending = inputs.Where(x => !seen.Contains(x)).ToHashSet();
        }
        return true;
    }
}
