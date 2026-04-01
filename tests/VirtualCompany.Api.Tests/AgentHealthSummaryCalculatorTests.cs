using VirtualCompany.Infrastructure.Companies;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class AgentHealthSummaryCalculatorTests
{
    private static readonly DateTime UtcNow = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Calculate_returns_blocked_when_any_blocked_task_exists()
    {
        var summary = AgentHealthSummaryCalculator.Calculate(
            new AgentHealthDerivationInput(1, 0, 1, UtcNow),
            UtcNow);

        Assert.Equal("blocked", summary.Status);
        Assert.Equal("Blocked", summary.Label);
    }

    [Fact]
    public void Calculate_returns_needs_attention_when_failed_task_exists_without_blocked_work()
    {
        var summary = AgentHealthSummaryCalculator.Calculate(
            new AgentHealthDerivationInput(0, 2, 2, UtcNow),
            UtcNow);

        Assert.Equal("needs_attention", summary.Status);
        Assert.Equal("Needs attention", summary.Label);
    }

    [Fact]
    public void Calculate_returns_inactive_when_last_activity_is_missing()
    {
        var summary = AgentHealthSummaryCalculator.Calculate(
            new AgentHealthDerivationInput(0, 0, 0, null),
            UtcNow);

        Assert.Equal("inactive", summary.Status);
        Assert.Equal("Inactive", summary.Label);
    }

    [Fact]
    public void Calculate_returns_inactive_when_last_activity_is_stale()
    {
        var summary = AgentHealthSummaryCalculator.Calculate(
            new AgentHealthDerivationInput(0, 0, 1, UtcNow.AddDays(-4)),
            UtcNow);

        Assert.Equal("inactive", summary.Status);
        Assert.Equal("Inactive", summary.Label);
    }

    [Fact]
    public void Calculate_returns_busy_when_recent_activity_crosses_threshold()
    {
        var summary = AgentHealthSummaryCalculator.Calculate(
            new AgentHealthDerivationInput(0, 0, 3, UtcNow.AddHours(-2)),
            UtcNow);

        Assert.Equal("busy", summary.Status);
        Assert.Equal("Busy", summary.Label);
    }

    [Fact]
    public void Calculate_returns_healthy_when_no_concerning_signals_exist()
    {
        var summary = AgentHealthSummaryCalculator.Calculate(
            new AgentHealthDerivationInput(0, 0, 1, UtcNow.AddHours(-1)),
            UtcNow);

        Assert.Equal("healthy", summary.Status);
        Assert.Equal("Healthy", summary.Label);
    }

    [Fact]
    public void Calculate_prioritizes_blocked_over_inactive()
    {
        var summary = AgentHealthSummaryCalculator.Calculate(
            new AgentHealthDerivationInput(1, 0, 1, UtcNow.AddDays(-7)),
            UtcNow);

        Assert.Equal("blocked", summary.Status);
    }

    [Fact]
    public void Calculate_prioritizes_failed_over_inactive_when_no_blocked_work_exists()
    {
        var summary = AgentHealthSummaryCalculator.Calculate(
            new AgentHealthDerivationInput(0, 1, 1, UtcNow.AddDays(-7)),
            UtcNow);

        Assert.Equal("needs_attention", summary.Status);
    }
}