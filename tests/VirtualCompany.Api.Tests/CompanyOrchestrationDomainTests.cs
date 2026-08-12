using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed class CompanyOrchestrationDomainTests
{
    [Fact]
    public void CompanyGoal_UsesDraftLifecycleAndOptimisticVersioning()
    {
        var goal = CreateGoal();

        Assert.Equal(CompanyGoalStatus.Draft, goal.Status);
        Assert.Equal(1, goal.Version);

        goal.Activate();
        goal.Pause();
        goal.Activate();
        goal.Complete();

        Assert.Equal(CompanyGoalStatus.Completed, goal.Status);
        Assert.NotNull(goal.CompletedUtc);
        Assert.Equal(5, goal.Version);
        Assert.Throws<InvalidOperationException>(() => goal.Activate());
    }

    [Fact]
    public void CompanyGoal_RequiresMetricKeyForNumericTarget()
    {
        Assert.Throws<ArgumentException>(() => new CompanyGoal(
            Guid.NewGuid(), Guid.NewGuid(), "Grow revenue", "Increase recurring revenue", CompanyGoalPriority.High,
            DateTime.UtcNow, DateTime.UtcNow.AddMonths(3), targetValue: 100_000m));
    }

    [Fact]
    public void CompanyOperatingConfiguration_DefaultsToRecommendationOnly()
    {
        var configuration = new CompanyOperatingConfiguration(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(CompanyAutonomyLevel.Recommend, configuration.AutonomyLevel);
        Assert.False(configuration.IsPaused);

        configuration.Pause("Quarterly governance review");
        Assert.True(configuration.IsPaused);
        configuration.Resume();
        Assert.False(configuration.IsPaused);
    }

    [Fact]
    public void OperatingCycle_RequiresOrderedTransitions()
    {
        var cycle = new OperatingCycle(Guid.NewGuid(), Guid.NewGuid(), "manual", null, Guid.NewGuid(), "corr-1", "cycle-1", 1);

        Assert.Throws<InvalidOperationException>(() => cycle.MarkPlanning(Guid.NewGuid()));

        cycle.MarkObserving();
        cycle.MarkPlanning(Guid.NewGuid());
        cycle.MarkValidating();
        cycle.MarkAwaitingReview();
        cycle.Complete();

        Assert.Equal(OperatingCycleStatus.Completed, cycle.Status);
        Assert.NotNull(cycle.CompletedUtc);
    }

    [Fact]
    public void OperatingPlan_IsImmutableThroughExplicitVersionedLifecycle()
    {
        var plan = new OperatingPlan(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "Protect cash runway", "Finance signals require action.");

        plan.SubmitForReview();
        plan.Approve();
        plan.BeginCommit();
        plan.MarkCommitted();

        Assert.Equal(OperatingPlanStatus.Committed, plan.Status);
        Assert.NotNull(plan.CommittedUtc);
        Assert.Throws<InvalidOperationException>(() => plan.MarkSuperseded());
    }

    [Fact]
    public void OperatingPlanDependency_RejectsSelfDependency()
    {
        var initiativeId = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => new OperatingPlanDependency(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), initiativeId, initiativeId));
    }

    [Fact]
    public void OperatingDecision_RejectsConfidenceOutsideSupportedRange()
    {
        Assert.Throws<ArgumentException>(() => new OperatingDecision(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, OperatingActionClass.Recommend,
            "finance.review", "cash_forecast", "forecast-1", Guid.NewGuid(), "Review cash exposure.",
            1.1m, "medium", false, "decision-1"));
    }

    private static CompanyGoal CreateGoal() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Protect cash runway",
        "Maintain at least six months of operating runway.",
        CompanyGoalPriority.Critical,
        DateTime.UtcNow,
        DateTime.UtcNow.AddMonths(6),
        "cash_runway_months",
        "months",
        5m,
        6m);
}
