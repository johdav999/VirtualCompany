using PreviewQuarterReview = VirtualCompany.Application.Orchestration.PreviewQuarterReview;
using QuarterObjectiveInput = VirtualCompany.Application.Orchestration.QuarterObjectiveInput;
using QuarterMeasureLink = VirtualCompany.Application.Orchestration.QuarterMeasureLink;
using QuarterMilestone = VirtualCompany.Application.Orchestration.QuarterMilestone;
using QuarterReviewDocument = VirtualCompany.Application.Orchestration.QuarterReviewDocument;
using QuarterReviewPreview = VirtualCompany.Application.Orchestration.QuarterReviewPreview;
using SaveQuarterReview = VirtualCompany.Application.Orchestration.SaveQuarterReview;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
namespace VirtualCompany.Api.Tests;

public sealed record QuarterlyPlanningFixture(WeeklyWorkspaceFixture Company, Guid Goal, Guid Initiative,
    IReadOnlyList<Guid> Snapshots, decimal Actual)
{
    public string Root => $"/api/companies/{Company.Company:D}/planning/quarters";
    public PreviewQuarterReview Proposal => new(2026, 3,
        [new(Goal, 1, Company.Owner, 0, 100, "cases", "at_least", Snapshots.Select(x => new QuarterMeasureLink(x, "support.volume")).ToArray(),
            [new("Review service capacity", new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc), "planned")], [Initiative])],
        [new(Goal, Company.Owner, "Support", 20, 30, Snapshots[2], "Recorded quarter handling assumption")], "Review the cross-plan dependency and capacity gap.");
    public HttpClient Client(TestWebApplicationFactory f, string subject = "p19-owner")
    { var h = WeeklyWorkspaceFixture.Client(f, subject); h.DefaultRequestHeaders.Add("X-Company-Id", Company.Company.ToString()); return h; }
    public static async Task<QuarterlyPlanningFixture> Seed(TestWebApplicationFactory f, WeeklyWorkspaceFixture? existing = null)
    {
        var company = existing ?? await WeeklyWorkspaceFixture.Seed(f); var goal = Guid.NewGuid(); var initiative = Guid.NewGuid();
        await f.SeedAsync(db =>
        {
            if (!db.AccountingConfigurations.IgnoreQueryFilters().Any(x => x.CompanyId == company.Company))
                db.Add(new AccountingConfiguration(Guid.NewGuid(), company.Company, "SEK", 1, 1, "se-bas", "1", new DateOnly(2026, 1, 1), 2, "midpoint_to_even", company.Owner, WeeklyWorkspaceFixture.Now));
            db.Add(new CompanyGoal(goal, company.Company, "Quarter service objective", "Manage recorded service demand", CompanyGoalPriority.High,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), "support.volume", "cases", 0, 100, company.Owner));
            foreach (var month in new[] { 7, 8 }) for (var i = 0; i < month - 5; i++)
                db.Add(new SupportCase(Guid.NewGuid(), company.Company, $"P25-{month}-{i}", "Quarter recorded case", null, "manual", createdUtc: new DateTime(2026, month, 5, 8, 0, 0, DateTimeKind.Utc)));
            var agent = new Agent(Guid.NewGuid(), company.Company, "p25-review", "Quarter coordinator", "Coordinator", "support", null, AgentSeniority.Lead); db.Add(agent);
            var c1 = new OperatingCycle(Guid.NewGuid(), company.Company, "manual", null, agent.Id, "p25-1", "p25-1", 1);
            var c2 = new OperatingCycle(Guid.NewGuid(), company.Company, "manual", null, agent.Id, "p25-2", "p25-2", 1); db.AddRange(c1, c2);
            var p1 = new OperatingPlan(Guid.NewGuid(), company.Company, c1.Id, 1, "Service review", "Recorded bounded plan");
            var p2 = new OperatingPlan(Guid.NewGuid(), company.Company, c2.Id, 1, "Dependency review", "Other native plan"); db.AddRange(p1, p2);
            var dep = new OperatingInitiative(Guid.NewGuid(), company.Company, p2.Id, goal, "Upstream knowledge review", "Review knowledge", CompanyGoalPriority.Normal, "Reviewed source", agent.Id, null, null);
            db.AddRange(dep, new OperatingInitiative(initiative, company.Company, p1.Id, goal, "Quarter handling review", "Review demand", CompanyGoalPriority.High, "Reviewed demand", agent.Id, null, null));
            db.Add(new OperatingPlanDependency(Guid.NewGuid(), company.Company, p1.Id, initiative, dep.Id));
            return Task.CompletedTask;
        });
        using var h = WeeklyWorkspaceFixture.Client(f); var snapshots = new List<Guid>(); decimal actual = 0;
        foreach (var month in new[] { 7, 8, 9 })
        {
            var s = await QuarterlyPlanningIntegrationTests.Read<MonthlyReviewSnapshotDto>(await h.PostAsJsonAsync($"/api/companies/{company.Company}/workspace/monthly/reviews", new SaveMonthlyReviewCommand("customers", 2026, month, Guid.NewGuid(), "Quarter source review")));
            snapshots.Add(s.Summary.Id); actual += s.Workspace.Review!.Measures.Single(x => x.Key == "support.volume").Actual!.Value;
        }
        return new(company, goal, initiative, snapshots, actual);
    }
}
