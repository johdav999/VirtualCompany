using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyOperationMonthlyWorkspaceContributor(VirtualCompanyDbContext db, CompanyWorkVisibility visibility) : IMonthlyWorkspaceContributor
{
    public string Lens => TodayWorkspaceLenses.Company;

    public async Task<MonthlyWorkspaceFeatureContribution> ContributeAsync(
        MonthlyWorkspaceContributorContext context,
        CancellationToken cancellationToken)
    {
        var period = context.Period;
        var scope = await visibility.ResolveAsync(context.CompanyId, cancellationToken);
        var allowedTasks = scope.Tasks(db.WorkTasks.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == context.CompanyId));
        var completedTasks = await allowedTasks
            .Where(x => x.CompanyId == context.CompanyId && x.CompletedUtc >= period.StartUtc && x.CompletedUtc < period.EndUtc)
            .OrderByDescending(x => x.CompletedUtc).Take(20).ToListAsync(cancellationToken);
        var blockers = await allowedTasks
            .Where(x => x.CompanyId == context.CompanyId && (x.Status == WorkTaskStatus.Blocked || x.Status == WorkTaskStatus.Failed))
            .OrderByDescending(x => x.Priority).ThenBy(x => x.DueUtc).Take(10).ToListAsync(cancellationToken);
        var nextMonth = await allowedTasks
            .Where(x => x.CompanyId == context.CompanyId && x.Status != WorkTaskStatus.Completed && x.Status != WorkTaskStatus.Failed &&
                        x.DueUtc >= period.EndUtc && x.DueUtc < period.EndUtc.AddMonths(1))
            .OrderBy(x => x.DueUtc).Take(10).ToListAsync(cancellationToken);
        var completedInitiatives = await db.OperatingInitiatives.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == context.CompanyId && x.Status == OperatingInitiativeStatus.Completed &&
                        x.UpdatedUtc >= period.StartUtc && x.UpdatedUtc < period.EndUtc)
            .Where(x => scope.Executive || x.TaskId.HasValue && allowedTasks.Any(t => t.Id == x.TaskId))
            .OrderByDescending(x => x.UpdatedUtc).Take(10).ToListAsync(cancellationToken);
        foreach (var list in new[] { completedTasks, blockers, nextMonth })
            foreach (var task in list.ToArray())
                if (!await CollaborationContentVisibility.AllowsAsync(db, scope, task.Id, cancellationToken)) list.Remove(task);
        foreach (var initiative in completedInitiatives.ToArray())
            if (initiative.TaskId.HasValue && !await CollaborationContentVisibility.AllowsAsync(db, scope, initiative.TaskId.Value, cancellationToken)) completedInitiatives.Remove(initiative);
        var constraints = blockers.Count(x => x.Type.Contains("budget") || x.Type.Contains("autonomy"));
        var route = $"/company-operation?companyId={context.CompanyId:D}&source=dashboard";

        var priorities = blockers.Concat(nextMonth).DistinctBy(x => x.Id).Take(5).Select(x =>
            new MonthlyWorkspacePriorityCandidate(
                $"work:{x.Id:N}", $"work-task:{x.Id:N}", Lens, x.Title,
                x.Description ?? x.RationaleSummary ?? "This work still needs a committed next step.",
                context.Access.ResponsiblePerson, context.Access.WorkingAgent,
                x.Status is WorkTaskStatus.Blocked or WorkTaskStatus.Failed
                    ? "Resolve the blocker or reassign the task before next month."
                    : "Confirm ownership and the next delivery step.",
                x.UpdatedUtc, "work_task", x.Id.ToString("D"),
                $"/tasks?companyId={context.CompanyId:D}&taskId={x.Id:D}&view=detail&source=dashboard",
                DecisionRequired: x.Status == WorkTaskStatus.AwaitingApproval, DueUtc: x.DueUtc,
                MaterialChange: Priority(x.Priority), UnresolvedRisk: x.Status is WorkTaskStatus.Blocked or WorkTaskStatus.Failed,
                SustainedTrend: x.CreatedUtc < period.StartUtc, DirectlyOwned: true,
                Confidence: x.ConfidenceScore ?? 1m)).ToList();
        var items = completedInitiatives.Select(x => new TodayWorkspaceFeatureItemDto(
            $"initiative:{x.Id:N}", x.Title, x.CompletionEvidence, "completed", x.UpdatedUtc, route)).ToList();
        var section = new MonthlyWorkspaceSectionDto(Lens, "Company operation",
            blockers.Count > 0 ? "Delivery continued during the month, with unresolved blockers requiring next-period ownership."
                : "Delivery continued during the month with no blocked Work item remaining.",
            blockers.Count > 0 ? "attention" : "healthy", period.EndUtc,
            [new("Work completed", completedTasks.Count.ToString(), "positive"),
             new("Initiatives completed", completedInitiatives.Count.ToString(), "positive"),
             new("Unresolved blockers", blockers.Count.ToString(), blockers.Count > 0 ? "attention" : "current"),
             new("Budget/autonomy constraints", constraints.ToString(), constraints > 0 ? "attention" : "current"),
             new("Next-month tasks", nextMonth.Count.ToString(), "current")],
            items, route, "Authorized Work cohort: at most 20 completed tasks and 10 initiatives, blockers or upcoming tasks. Counts describe this bounded cohort, not all company history.");
        var outcomes = completedTasks.Take(5).Select(x => new TodayWorkspaceAgentUpdateDto(
            $"work-completed:{x.Id:N}", "Work completed", x.Title, x.AssignedAgent?.DisplayName ?? context.Access.WorkingAgent,
            x.CompletedUtc ?? x.UpdatedUtc, "work_task",
            $"/tasks?companyId={context.CompanyId:D}&taskId={x.Id:D}&view=detail&source=dashboard",
            "Company operation", TodayAgentStates.Completed,
            RationaleSummary: x.RationaleSummary ?? "Backed by a completed Work task.", RelatedTaskId: x.Id,
            UpdatedUtc: x.UpdatedUtc)).ToList();
        return new(Lens, section, priorities,
            [new("company.completed_work", "Included completed Work", completedTasks.Count, completedTasks.Count.ToString(), null,
                "Historical cohort comparison unavailable", "tasks", "current", context.NowUtc, "work_task", route,
                true, "Bounded current authorized cohort; no full historical completion balance is reconstructed.")], outcomes,
            [new("company_operation", "Company operation", "current", period.EndUtc,
                "Bounded authorized initiatives and Work cohort; current blockers are observed at review time, not historical month-end balances.")],
            completedTasks.Concat(blockers).Concat(nextMonth).Select(x => x.Id)
                .Concat(completedInitiatives.Where(x => x.TaskId.HasValue).Select(x => x.TaskId!.Value)).Distinct().ToArray());
    }

    private static int Priority(WorkTaskPriority value) => value switch
    { WorkTaskPriority.Critical => 100, WorkTaskPriority.High => 75, WorkTaskPriority.Normal => 50, _ => 20 };
}
