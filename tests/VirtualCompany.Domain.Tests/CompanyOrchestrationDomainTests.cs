using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using System.Text.Json.Nodes;

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

    [Fact]
    public void ControlledDecision_PreservesBoundedPayloadAndApprovalBoundary()
    {
        var decision = new OperatingDecision(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            OperatingActionClass.ExternalExecute, "operator_notification", "company_member", Guid.NewGuid().ToString("N"),
            null, "Send an approved operating update.", .9m, "low", true, "notify-1",
            new Dictionary<string, JsonNode?> { ["title"] = JsonValue.Create("Plan approved") });

        Assert.True(decision.ApprovalRequired);
        Assert.Equal("Plan approved", decision.Payload["title"]!.GetValue<string>());
        Assert.Equal(OperatingActionClass.ExternalExecute, decision.ActionClass);
    }

    [Fact]
    public void Controlled_operating_decision_is_an_authoritative_approval_target()
    {
        Assert.Equal("operating_decision", ApprovalTargetEntityType.OperatingDecision.ToStorageValue());
        Assert.Equal(ApprovalTargetEntityType.OperatingDecision,
            ApprovalTargetEntityTypeValues.Parse("operating_decision"));
    }

    [Fact]
    public void OperatingReview_RequiresEvidenceVersionAndSupportsReplanningOutcome()
    {
        var review = new OperatingReview(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), OperatingReviewOutcome.Revise,
            "The linked work failed and needs a revised plan.", "A verified outcome", "Failure evidence", "Review the revised plan", "task:1:42", .7m);

        Assert.Equal(OperatingReviewOutcome.Revise, review.Outcome);
        Assert.Equal("task:1:42", review.EvidenceVersion);
    }

    [Fact]
    public void OperatingDispatch_LeasePreventsConcurrentClaimAndRejectsStaleOwner()
    {
        var dispatch = new OperatingDispatch(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            OperatingDispatchKind.SingleAgent, "dispatch-correlation");
        var now = DateTime.UtcNow.AddSeconds(1);

        Assert.True(dispatch.TryClaim("worker-a", now, TimeSpan.FromMinutes(5)));
        Assert.False(dispatch.TryClaim("worker-b", now.AddMinutes(1), TimeSpan.FromMinutes(5)));
        Assert.Throws<InvalidOperationException>(() => dispatch.Start("worker-b", now.AddMinutes(1)));

        Assert.True(dispatch.TryClaim("worker-b", now.AddMinutes(6), TimeSpan.FromMinutes(5)));
        dispatch.Start("worker-b", now.AddMinutes(6));
        dispatch.Complete(Guid.NewGuid(), null, now.AddMinutes(7));
        Assert.Equal(OperatingDispatchStatus.Completed, dispatch.Status);
        Assert.False(dispatch.TryClaim("worker-c", now.AddMinutes(8), TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void OperatingDispatch_DeadLettersAfterConfiguredRetryLimit()
    {
        var dispatch = new OperatingDispatch(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            OperatingDispatchKind.SingleAgent, "dispatch-retry", maxAttempts: 2);
        var now = DateTime.UtcNow.AddSeconds(1);

        Assert.True(dispatch.TryClaim("worker", now, TimeSpan.FromMinutes(5)));
        dispatch.Start("worker", now);
        dispatch.Retry("temporary", "Temporary failure.", now.AddMinutes(2), now.AddMinutes(1));
        Assert.Equal(OperatingDispatchStatus.RetryScheduled, dispatch.Status);

        Assert.True(dispatch.TryClaim("worker", now.AddMinutes(3), TimeSpan.FromMinutes(5)));
        dispatch.Start("worker", now.AddMinutes(3));
        dispatch.Retry("temporary", "Failed again.", now.AddMinutes(5), now.AddMinutes(4));
        Assert.Equal(OperatingDispatchStatus.DeadLettered, dispatch.Status);
        Assert.Null(dispatch.NextAttemptUtc);
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
