using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class MarketingCompanyOrchestrationTests
{
    [Fact]
    public void Authority_policy_uses_the_most_restrictive_authority_ceiling()
    {
        var decision = MarketingAuthorityPolicy.Evaluate(new MarketingAuthorityContext(
            CompanyAutonomyLevel.ControlledExecution, AgentAutonomyLevel.Level3,
            CompanyAutonomyLevel.OperateInternally));

        Assert.True(decision.Allowed);
        Assert.Equal(CompanyAutonomyLevel.OperateInternally, decision.EffectiveAuthority);
    }

    [Fact]
    public void Authority_policy_returns_a_stable_reason_for_every_restricting_input()
    {
        var baseline = new MarketingAuthorityContext(CompanyAutonomyLevel.ControlledExecution,
            AgentAutonomyLevel.Level3, CompanyAutonomyLevel.ControlledExecution);
        var restricted = new[]
        {
            (baseline with { CompanyPaused = true }, "company_paused"),
            (baseline with { GoalActive = false }, "goal_inactive"),
            (baseline with { InitiativeAllowed = false }, "initiative_restricted"),
            (baseline with { AgentAvailable = false }, "marketing_agent_unavailable"),
            (baseline with { CapabilityAllowed = false }, "capability_not_allowed"),
            (baseline with { ToolAllowed = false }, "tool_not_allowed"),
            (baseline with { MarketingActionAllowed = false }, "marketing_policy_restricted"),
            (baseline with { ConsentAllowed = false }, "consent_required"),
            (baseline with { ProviderHealthy = false }, "provider_unavailable"),
            (baseline with { WorkloadAvailable = false }, "capacity_exhausted"),
            (baseline with { BudgetAvailable = false }, "budget_exhausted")
        };

        foreach (var (context, reasonCode) in restricted)
        {
            var decision = MarketingAuthorityPolicy.Evaluate(context);
            Assert.False(decision.Allowed);
            Assert.Equal(reasonCode, decision.ReasonCode);
            Assert.Equal(CompanyAutonomyLevel.Recommend, decision.EffectiveAuthority);
        }

        var approval = MarketingAuthorityPolicy.Evaluate(baseline with { ApprovalSatisfied = false });
        Assert.True(approval.Allowed);
        Assert.True(approval.RequiresApproval);
        Assert.Equal("approval_required", approval.ReasonCode);
        Assert.Equal(CompanyAutonomyLevel.Recommend, approval.EffectiveAuthority);
    }

    [Fact]
    public async Task Assignment_resolution_returns_exact_versions_budget_dependencies_and_correlation()
    {
        await using var db = CreateDb();
        var fixture = await SeedAssignmentAsync(db);
        var service = new MarketingCompanyOrchestrationService(db);

        var result = await service.ResolveAssignmentAsync(fixture.CompanyId, fixture.AgentId,
            Request(fixture), CancellationToken.None);

        Assert.True(result.IsAccepted);
        Assert.Equal(fixture.GoalId, result.CompanyGoalId);
        Assert.Equal(fixture.PlanId, result.OperatingPlanId);
        Assert.Equal(fixture.InitiativeId, result.OperatingInitiativeId);
        Assert.Equal(2, result.GoalVersion);
        Assert.Equal(1, result.PlanVersion);
        Assert.Equal(2, result.InitiativeVersion);
        Assert.Equal("corr-assignment", result.CorrelationId);
        Assert.Equal(500m, result.BudgetLimit);
        Assert.Equal("Approved report with source links", result.CompletionEvidence);
        Assert.Empty(result.Dependencies);
    }

    [Fact]
    public async Task Assignment_resolution_rejects_stale_and_paused_work_before_creating_a_run()
    {
        await using var db = CreateDb();
        var fixture = await SeedAssignmentAsync(db);
        var service = new MarketingCompanyOrchestrationService(db);

        var stale = await Assert.ThrowsAsync<MarketingAssignmentException>(() => service.ResolveAssignmentAsync(
            fixture.CompanyId, fixture.AgentId, Request(fixture) with { ExpectedInitiativeVersion = 1 },
            CancellationToken.None));
        Assert.Equal(MarketingAssignmentReasonCodes.StaleInitiativeVersion, stale.ReasonCode);
        Assert.Empty(db.MarketingOperatingRuns);

        fixture.Configuration.Pause("Executive review");
        await db.SaveChangesAsync();
        var paused = await Assert.ThrowsAsync<MarketingAssignmentException>(() => service.ResolveAssignmentAsync(
            fixture.CompanyId, fixture.AgentId, Request(fixture), CancellationToken.None));
        Assert.Equal(MarketingAssignmentReasonCodes.CompanyPaused, paused.ReasonCode);
        Assert.Empty(db.MarketingOperatingRuns);
    }

    [Fact]
    public async Task Outcome_and_signal_are_idempotent_and_feed_company_review()
    {
        await using var db = CreateDb();
        var fixture = await SeedAssignmentAsync(db);
        var run = new MarketingOperatingRun(Guid.NewGuid(), fixture.CompanyId, fixture.AgentId,
            "initiative", fixture.InitiativeId.ToString("N"), "run-idempotency", "corr-assignment",
            fixture.GoalId, fixture.InitiativeId, null, "operate_internally", 2, "evidence-v1", 500m);
        db.MarketingOperatingRuns.Add(run);
        await db.SaveChangesAsync();
        var service = new MarketingCompanyOrchestrationService(db);
        var command = new ReportMarketingWorkCommand(run.Id, fixture.InitiativeId, null,
            "outcome-idempotency", "evidence-v2", "[{\"type\":\"report\",\"id\":\"report-1\"}]",
            "{\"qualifiedLeads\":10}", "{\"qualifiedLeads\":12}", .8m,
            "[]", "[]", "[]", "{\"qualifiedLeads\":14}",
            "The selected segment converted above the expected range.", "Review a measured budget increase.",
            "corr-assignment");

        var first = await service.ReportOutcomeAsync(fixture.CompanyId, command, CancellationToken.None);
        var second = await service.ReportOutcomeAsync(fixture.CompanyId, command, CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(await db.MarketingWorkEvidence.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await db.OperatingReviews.IgnoreQueryFilters().ToListAsync());
        var signalCommand = new RaiseMarketingCompanySignalCommand(run.Id, "opportunity", "high",
            "Qualified demand is above plan.", "{\"marketingWorkEvidenceId\":\"" + first.Id + "\"}",
            "signal-idempotency", "corr-assignment");
        var signal1 = await service.RaiseSignalAsync(fixture.CompanyId, signalCommand, CancellationToken.None);
        var signal2 = await service.RaiseSignalAsync(fixture.CompanyId, signalCommand, CancellationToken.None);
        Assert.Equal(signal1.Id, signal2.Id);
        Assert.Single(await db.MarketingCompanySignals.IgnoreQueryFilters().ToListAsync());
        Assert.True(signal1.CycleEvaluationRequested);
        Assert.Equal("pending", signal1.Status);
    }

    private static RequestMarketingOperatingRun Request(Fixture fixture) => new("initiative",
        fixture.InitiativeId.ToString("N"), "assignment-idempotency", "corr-assignment",
        fixture.GoalId, fixture.InitiativeId, null, "on_demand", 2, 1, 2);

    private static async Task<Fixture> SeedAssignmentAsync(VirtualCompanyDbContext db)
    {
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var goal = new CompanyGoal(Guid.NewGuid(), companyId, "Grow qualified demand",
            "Increase qualified demand from the approved customer segment.", CompanyGoalPriority.High,
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddMonths(2));
        goal.Activate();
        var configuration = new CompanyOperatingConfiguration(Guid.NewGuid(), companyId);
        configuration.Update(null, CompanyAutonomyLevel.OperateInternally, "UTC", 6, 60, 4,
            5, 12, 3, 120, 4, 20, 1000m);
        var agent = new Agent(agentId, companyId, "marketing", "Maya", "Marketing Manager", "Marketing",
            null, AgentSeniority.Lead, AgentStatus.Active, AgentAutonomyLevel.Level2);
        var cycle = new OperatingCycle(Guid.NewGuid(), companyId, "manual", null, agentId,
            "corr-assignment", "cycle-idempotency", configuration.Version);
        var plan = new OperatingPlan(Guid.NewGuid(), companyId, cycle.Id, 1,
            "Grow qualified demand", "The approved segment has enough evidence to test demand.");
        plan.SubmitForReview(); plan.Approve(); plan.BeginCommit(); plan.MarkCommitted();
        var initiative = new OperatingInitiative(Guid.NewGuid(), companyId, plan.Id, goal.Id,
            "Run a segment campaign", "Deliver a measured campaign for the approved segment.",
            CompanyGoalPriority.High, "Approved report with source links", agentId,
            DateTime.UtcNow.AddDays(30), 500m);
        initiative.Approve();
        db.AddRange(goal, configuration, agent, cycle, plan, initiative);
        await db.SaveChangesAsync();
        return new Fixture(companyId, agentId, goal.Id, plan.Id, initiative.Id, configuration);
    }

    private static VirtualCompanyDbContext CreateDb() => new(
        new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private sealed record Fixture(Guid CompanyId, Guid AgentId, Guid GoalId, Guid PlanId,
        Guid InitiativeId, CompanyOperatingConfiguration Configuration);
}
