using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyWeeklyWorkspaceContributor(IAgentWorkQueryService work, VirtualCompanyDbContext db) : IWeeklyWorkspaceContributor
{
    public string Lens => TodayWorkspaceLenses.Company;
    public async Task<WeeklyWorkspaceContribution> ContributeAsync(WeeklyWorkspaceContributorContext c, CancellationToken token)
    {
        // Reuse P10/P11's current, transitive source authorization; never project raw protected tasks.
        var board = await work.ListAsync(new(c.CompanyId, Take: 100, PerState: true), token);
        var roots = board.Items;
        var p = c.Period;
        IReadOnlyList<WeeklyWorkspaceSourceDto> Completed(DateTime start, DateTime end) => roots
            .Where(x => x.State == AgentWorkStates.Completed && x.CompletedUtc >= start && x.CompletedUtc < end)
            .Select(x => new WeeklyWorkspaceSourceDto($"{x.Kind}:{x.Id:D}", x.Title, x.CompletedUtc!.Value, x.DetailRoute)).ToList();
        var complete = !board.HasNext && !board.IsPartial;
        var completed = WeeklyWorkspaceMeasures.Activity("company.completed", "Completed commitments",
            Completed(p.StartUtc, p.ActivityEndUtc), Completed(p.ComparisonStartUtc, p.ComparisonEndUtc),
            "Current completed root work with a recorded completion timestamp in the selected interval. Worker outputs and approvals do not complete a company outcome.",
            "Current authorized root-work cohort; up to 100 per lifecycle lane within the owning source window. Reopened or deleted historical outcomes are not reconstructed.", complete);
        var ids = roots.Where(x => x.Kind == "task").Select(x => x.Id).ToArray();
        var due = await db.WorkTasks.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == c.CompanyId && ids.Contains(x.Id))
            .Select(x => new { x.Id, x.DueUtc }).ToDictionaryAsync(x => x.Id, x => x.DueUtc, token);
        var commitments = roots.Where(x => x.State != AgentWorkStates.Completed &&
                (x.State is AgentWorkStates.Blocked or AgentWorkStates.Failed or AgentWorkStates.AwaitingApproval ||
                 due.ContainsKey(x.Id) && due[x.Id] < p.EndUtc.AddDays(7)))
            .OrderByDescending(x => x.State is AgentWorkStates.Blocked or AgentWorkStates.AwaitingApproval)
            .ThenBy(x => due.GetValueOrDefault(x.Id)).ThenBy(x => x.Id).Take(12)
            .Select(x => new WeeklyWorkspaceCommitmentDto($"{x.Kind}:{x.Id:D}", x.Title,
                $"Current {x.CurrentStep}. {x.Dependency}", due.GetValueOrDefault(x.Id), board.ObservedUtc,
                x.DetailRoute, x.AccountableHuman, x.Agents.FirstOrDefault()?.Name)).ToList();
        var gaps = new List<string> { "No complete historical blocker or commitment-balance snapshots exist. Current risks are shown as of refresh, including when reviewing an earlier week." };
        if (!complete) gaps.Add("Work source window is incomplete; completed counts describe only the included authorized cohort.");
        return new(Lens, "CEO commitments and risks", [completed,
            WeeklyWorkspaceMeasures.Unavailable("company.blocker_movement", "Historical blocker balance movement", "balance", gaps[0])], commitments, gaps);
    }
}
