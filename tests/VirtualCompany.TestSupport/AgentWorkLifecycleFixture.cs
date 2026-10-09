using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

// Additive durable fixtures for integration tests and the disposable browser host.
public sealed record AgentWorkLifecycleFixture(Guid CompanyId, Guid HumanId, Guid AgentId, Guid ReviewerId,
    Guid SharedId, Guid SharedTaskId, Guid PausedId, Guid BlockedId, Guid CompletedTaskId)
{
    public async Task SeedPagingHistoryAsync(TestWebApplicationFactory factory)
    {
        await factory.SeedAsync(db =>
        {
            var observed = DateTime.UtcNow;
            foreach (var (status, count) in new[] { (WorkTaskStatus.InProgress, 8), (WorkTaskStatus.Completed, 55) })
            {
                for (var i = 1; i <= count; i++)
                {
                    var label = status == WorkTaskStatus.InProgress ? "active" : "completed";
                    var task = new WorkTask(Guid.NewGuid(), CompanyId, "finance_review", $"Kanban {label} review {i:00}",
                        "Kanban paging regression", WorkTaskPriority.Normal, AgentId, null, "user", HumanId,
                        rationaleSummary: "Retained internal paging fixture");
                    task.UpdateStatus(status, rationaleSummary: task.RationaleSummary);
                    db.WorkTasks.Add(task);
                    db.Entry(task).Property(x => x.UpdatedUtc).CurrentValue = observed.AddDays(label == "active" ? -2 : 0).AddSeconds(i);
                }
            }
            return Task.CompletedTask;
        });
    }

    public static async Task<AgentWorkLifecycleFixture> SeedAsync(TestWebApplicationFactory factory, Guid company, Guid human)
    {
        var agent = Guid.NewGuid(); var reviewer = Guid.NewGuid(); var sharedId = Guid.NewGuid(); var sharedTaskId = Guid.NewGuid();
        var pausedId = Guid.NewGuid(); var blockedId = Guid.NewGuid(); var completedId = Guid.NewGuid();
        await factory.SeedAsync(db =>
        {
            db.Agents.AddRange(new Agent(agent, company, "finance", "P10 Laura", "Finance reviewer", "Finance", null, AgentSeniority.Senior, AgentStatus.Paused),
                new Agent(reviewer, company, "support", "P10 Ben", "Customer reviewer", "Support", null, AgentSeniority.Senior, AgentStatus.Active));
            var cycle = new OperatingCycle(Guid.NewGuid(), company, "manual", null, agent, "p10-fixture", "p10-" + company, 1);
            var plan = new OperatingPlan(Guid.NewGuid(), company, cycle.Id, 1, "P10 retain accountable outcomes", "Internal test evidence only");
            db.AddRange(cycle, plan);
            var goal = new CompanyGoal(Guid.NewGuid(), company, "P10 outcome", "Customer renewal is reviewed", CompanyGoalPriority.High,
                DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(7), ownerUserId: human, ownerAgentId: agent);
            goal.Activate(); db.CompanyGoals.Add(goal);
            foreach (var status in new[] { WorkTaskStatus.New, WorkTaskStatus.InProgress, WorkTaskStatus.AwaitingApproval, WorkTaskStatus.Completed, WorkTaskStatus.Blocked, WorkTaskStatus.Failed })
            {
                var task = new WorkTask(status == WorkTaskStatus.Completed ? completedId : Guid.NewGuid(), company, "finance_review",
                    "P10 " + status.ToStorageValue() + " review", "Review renewal terms", WorkTaskPriority.Normal, agent, null, "user", human,
                    rationaleSummary: status == WorkTaskStatus.Blocked ? "Obtain the signed renewal terms from the customer before retrying." : "Retained internal review rationale",
                    outputPayload: status == WorkTaskStatus.InProgress ? new Dictionary<string, JsonNode?> { ["draft"] = JsonValue.Create("Retained draft is not completion") } : null);
                if (status != WorkTaskStatus.New) task.UpdateStatus(status,task.OutputPayload,task.RationaleSummary);
                db.WorkTasks.Add(task);
            }
            var worker = new WorkTask(sharedTaskId, company, "finance_review", "P10 renewal evidence worker", "Collect retained renewal evidence",
                WorkTaskPriority.High, agent, null, "user", human, rationaleSummary: "Worker execution is separate from the company outcome");
            worker.UpdateStatus(WorkTaskStatus.Completed,rationaleSummary:worker.RationaleSummary); db.WorkTasks.Add(worker);
            var shared = new OperatingInitiative(sharedId, company, plan.Id, goal.Id, "P10 shared renewal review", "One accountable company renewal outcome",
                CompanyGoalPriority.High, "Signed customer terms and independent review", agent, DateTime.UtcNow.AddDays(2), null);
            shared.LinkWork(worker.Id, null); db.OperatingInitiatives.Add(shared);
            db.OperatingInitiativeCollaborators.Add(new OperatingInitiativeCollaborator(Guid.NewGuid(), company, sharedId, reviewer,
                OperatingCollaborationRole.Reviewer, OperatingCollaborationPattern.SequentialHandoff, 1, "Review customer context", "Retained customer evidence"));
            var dispatch = new OperatingDispatch(Guid.NewGuid(), company, shared.Id, worker.Id, OperatingDispatchKind.MultiAgent, "p10-fixture");
            var now = DateTime.UtcNow.AddSeconds(1); dispatch.TryClaim("fixture", now, TimeSpan.FromMinutes(1)); dispatch.Start("fixture", now); dispatch.Complete(null, null, now);
            db.OperatingDispatches.Add(dispatch);
            db.OperatingReviews.Add(new OperatingReview(Guid.NewGuid(), company, plan.Id, 1, shared.Id, OperatingReviewOutcome.Continue,
                "Worker finished; the company outcome still needs customer evidence", "Signed terms", "Draft only; signed customer terms are absent",
                "Obtain signed customer terms and record the outcome decision", "p10-v1", 0.8m));
            var pausedGoal = new CompanyGoal(Guid.NewGuid(), company, "P10 paused goal", "Reconcile before continuing", CompanyGoalPriority.Normal,
                DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(7), ownerUserId: human);
            pausedGoal.Activate(); pausedGoal.Pause(); db.CompanyGoals.Add(pausedGoal);
            var paused = new OperatingInitiative(pausedId, company, plan.Id, pausedGoal.Id, "P10 paused outcome", "Review continuation with the human owner",
                CompanyGoalPriority.Normal, "Human continuation decision", agent, null, null);
            db.OperatingInitiatives.Add(paused);
            var blocked = new OperatingInitiative(blockedId, company, plan.Id, goal.Id, "P10 dependent review", "Finish the dependent customer review",
                CompanyGoalPriority.Normal, "Reviewed renewal evidence", agent, null, null);
            blocked.Approve(); blocked.Block(); db.OperatingInitiatives.Add(blocked);
            db.OperatingPlanDependencies.Add(new OperatingPlanDependency(Guid.NewGuid(), company, plan.Id, blocked.Id, shared.Id));
            return Task.CompletedTask;
        });
        return new(company, human, agent, reviewer, sharedId, sharedTaskId, pausedId, blockedId, completedId);
    }
}
