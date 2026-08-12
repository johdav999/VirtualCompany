using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Tests;

public sealed class CompanyOperatingAutonomyPolicyTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Organize_allows_only_current_fully_allowed_plan_with_level_one_owner()
    {
        var seed = await SeedAsync(CompanyAutonomyLevel.Organize, AgentAutonomyLevel.Level1,
            OperatingValidationOutcome.Allowed, approvalRequired: false);

        using var scope = _factory.Services.CreateScope();
        using var tenant = scope.ServiceProvider.GetRequiredService<ICompanyExecutionScopeFactory>().BeginScope(seed.CompanyId);
        var policy = scope.ServiceProvider.GetRequiredService<ICompanyOperatingAutonomyPolicy>();

        var result = await policy.EvaluateAsync(seed.CompanyId, seed.PlanId,
            CompanyOperatingAutonomyPhase.AutomaticCommit, CancellationToken.None);

        Assert.True(result.Allowed);
        Assert.False(result.ReviewRequired);
        Assert.Equal("within_autonomy", result.ReasonCode);
    }

    [Fact]
    public async Task Review_required_validation_prevents_automatic_commit()
    {
        var seed = await SeedAsync(CompanyAutonomyLevel.Organize, AgentAutonomyLevel.Level1,
            OperatingValidationOutcome.ReviewRequired, approvalRequired: true);

        using var scope = _factory.Services.CreateScope();
        using var tenant = scope.ServiceProvider.GetRequiredService<ICompanyExecutionScopeFactory>().BeginScope(seed.CompanyId);
        var policy = scope.ServiceProvider.GetRequiredService<ICompanyOperatingAutonomyPolicy>();

        var result = await policy.EvaluateAsync(seed.CompanyId, seed.PlanId,
            CompanyOperatingAutonomyPhase.AutomaticCommit, CancellationToken.None);

        Assert.False(result.Allowed);
        Assert.True(result.ReviewRequired);
        Assert.Equal("approval_required", result.ReasonCode);
    }

    [Fact]
    public async Task Dispatch_requires_level_two_agent_authority()
    {
        var seed = await SeedAsync(CompanyAutonomyLevel.OperateInternally, AgentAutonomyLevel.Level1,
            OperatingValidationOutcome.Allowed, approvalRequired: false);

        using var scope = _factory.Services.CreateScope();
        using var tenant = scope.ServiceProvider.GetRequiredService<ICompanyExecutionScopeFactory>().BeginScope(seed.CompanyId);
        var policy = scope.ServiceProvider.GetRequiredService<ICompanyOperatingAutonomyPolicy>();

        var result = await policy.EvaluateAsync(seed.CompanyId, seed.PlanId,
            CompanyOperatingAutonomyPhase.Dispatch, CancellationToken.None);

        Assert.False(result.Allowed);
        Assert.True(result.ReviewRequired);
        Assert.Equal("agent_autonomy_insufficient", result.ReasonCode);
    }

    [Fact]
    public async Task A_plan_from_another_company_is_not_disclosed_or_authorized()
    {
        var seed = await SeedAsync(CompanyAutonomyLevel.Organize, AgentAutonomyLevel.Level1,
            OperatingValidationOutcome.Allowed, approvalRequired: false);

        using var scope = _factory.Services.CreateScope();
        var policy = scope.ServiceProvider.GetRequiredService<ICompanyOperatingAutonomyPolicy>();
        var otherCompanyId = Guid.NewGuid();
        using var tenant = scope.ServiceProvider.GetRequiredService<ICompanyExecutionScopeFactory>().BeginScope(otherCompanyId);

        var result = await policy.EvaluateAsync(otherCompanyId, seed.PlanId,
            CompanyOperatingAutonomyPhase.AutomaticCommit, CancellationToken.None);

        Assert.False(result.Allowed);
        Assert.Equal("operating_state_missing", result.ReasonCode);
    }

    private async Task<(Guid CompanyId, Guid PlanId)> SeedAsync(CompanyAutonomyLevel companyLevel,
        AgentAutonomyLevel agentLevel, OperatingValidationOutcome validationOutcome, bool approvalRequired)
    {
        var companyId = Guid.NewGuid(); var agentId = Guid.NewGuid(); var goalId = Guid.NewGuid();
        var cycleId = Guid.NewGuid(); var planId = Guid.NewGuid(); var initiativeId = Guid.NewGuid();
        await _factory.SeedAsync(db =>
        {
            db.Companies.Add(new Company(companyId, "Autonomy policy company"));
            db.Agents.Add(new Agent(agentId, companyId, "operations", "Nina", "Operations Manager", "Operations",
                null, AgentSeniority.Lead, AgentStatus.Active, agentLevel));
            var goal = new CompanyGoal(goalId, companyId, "Improve delivery", "Deliver approved internal work reliably.",
                CompanyGoalPriority.High, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddMonths(2), ownerAgentId: agentId);
            goal.Activate(); db.CompanyGoals.Add(goal);
            var config = new CompanyOperatingConfiguration(Guid.NewGuid(), companyId);
            config.Update(agentId, companyLevel, "UTC", 6, 60, 4, 5, 12, 3, 120, 4, 20, null);
            db.CompanyOperatingConfigurations.Add(config);
            var cycle = new OperatingCycle(cycleId, companyId, "scheduled", null, agentId, "corr-policy", "policy-cycle", config.Version);
            db.OperatingCycles.Add(cycle);
            var plan = new OperatingPlan(planId, companyId, cycleId, 1, "Improve delivery", "Validated internal work.");
            plan.SubmitForReview(); db.OperatingPlans.Add(plan);
            db.OperatingInitiatives.Add(new OperatingInitiative(initiativeId, companyId, planId, goalId,
                "Review delivery risks", "Produce a bounded delivery-risk review.", CompanyGoalPriority.High,
                "A source-linked review is attached.", agentId, DateTime.UtcNow.AddDays(7), null));
            var decisionId = Guid.NewGuid();
            db.OperatingDecisions.Add(new OperatingDecision(decisionId, companyId, planId, initiativeId,
                OperatingActionClass.Recommend, "initiative", "company_goal", goalId.ToString("N"), agentId,
                "Review current delivery risks.", .9m, "low", approvalRequired, "policy-decision"));
            db.OperatingValidationResults.Add(new OperatingValidationResult(Guid.NewGuid(), companyId, planId,
                decisionId, "test-policy", "1.0", validationOutcome,
                validationOutcome == OperatingValidationOutcome.Allowed ? "allowed" : "approval_required",
                validationOutcome == OperatingValidationOutcome.Allowed ? "Allowed." : "Approval is required.",
                approvalRequired, config.Version));
            return Task.CompletedTask;
        });
        return (companyId, planId);
    }
}
