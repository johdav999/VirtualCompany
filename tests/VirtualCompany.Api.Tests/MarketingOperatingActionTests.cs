using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Api.Tests;

public sealed class MarketingOperatingActionTests
{
    [Fact]
    public void Action_supports_lease_retry_completion_and_preserves_evidence()
    {
        var action = Create(maximumAttempts: 3);

        action.Claim("worker-a", TimeSpan.FromMinutes(1));
        action.RenewLease("worker-a", TimeSpan.FromMinutes(2));
        action.Block("worker-a", "provider_unavailable", "Retry after provider recovery.", true, TimeSpan.Zero);
        action.Claim("worker-b", TimeSpan.FromMinutes(1));
        var artifactId = Guid.NewGuid();
        action.Complete("worker-b", "marketing_plan", artifactId, "{\"version\":1}", 0.01m);

        Assert.Equal("completed", action.Status);
        Assert.Equal(2, action.AttemptCount);
        Assert.Equal("marketing_plan", action.ArtifactType);
        Assert.Equal(artifactId, action.ArtifactId);
        Assert.Equal(0.01m, action.ActualCost);
        Assert.Null(action.LeaseExpiresUtc);
    }

    [Fact]
    public void Action_dead_letters_at_attempt_limit_and_requires_operator_retry()
    {
        var action = Create(maximumAttempts: 1);
        action.Claim("worker", TimeSpan.FromMinutes(1));
        action.Block("worker", "command_failed", "Inspect the grounded command failure.", true, TimeSpan.Zero);

        Assert.Equal("dead_letter", action.Status);
        Assert.Throws<InvalidOperationException>(() => action.Claim("worker", TimeSpan.FromMinutes(1)));

        action.Retry("The operator confirmed the dependency recovered.");
        action.Claim("worker", TimeSpan.FromMinutes(1));
        Assert.Equal("running", action.Status);
    }

    [Fact]
    public void Run_rejects_budget_overrun()
    {
        var run = new MarketingOperatingRun(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "scheduled", "daily",
            "key", "correlation", null, null, null, "operate_internally", 1, "snapshot:v1", 1m);
        run.AddBudgetUsage(0.75m);

        Assert.Throws<InvalidOperationException>(() => run.AddBudgetUsage(0.26m));
        Assert.Equal(0.75m, run.BudgetUsed);
    }

    private static MarketingOperatingAction Create(int maximumAttempts) => new(Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), 1, "prepare", "Prepare a governed draft", "planning", "marketing.prepare_plan",
        "{}", "snapshot:v1", "Supports the assigned company outcome", "[]", "A linked draft",
        "allowed", false, Guid.NewGuid().ToString("N"), 0.01m, maximumAttempts);
}
