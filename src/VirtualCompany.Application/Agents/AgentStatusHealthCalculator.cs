namespace VirtualCompany.Application.Agents;

public static class AgentStatusHealthCalculator
{
    public const string Healthy = "Healthy";
    public const string Warning = "Warning";
    public const string Critical = "Critical";

    public static AgentStatusHealthResult Calculate(
        AgentStatusHealthMetrics metrics,
        AgentStatusHealthThresholds? thresholds = null)
    {
        var resolvedThresholds = thresholds ?? new AgentStatusHealthThresholds();
        var reasons = new List<string>();

        if (metrics.PolicyViolationCount >= resolvedThresholds.CriticalPolicyViolations)
        {
            reasons.Add(metrics.PolicyViolationCount == 1
                ? "1 policy violation requires review."
                : $"{metrics.PolicyViolationCount} policy violations require review.");
        }

        if (metrics.FailedRunCount >= resolvedThresholds.CriticalFailedRuns)
        {
            reasons.Add($"{metrics.FailedRunCount} failed runs reached the critical threshold.");
        }

        if (metrics.StalledWorkCount >= resolvedThresholds.CriticalStalledWork)
        {
            reasons.Add($"{metrics.StalledWorkCount} stalled work items reached the critical threshold.");
        }

        if (reasons.Count > 0)
        {
            return new AgentStatusHealthResult(Critical, reasons);
        }

        if (metrics.FailedRunCount >= resolvedThresholds.WarningFailedRuns)
        {
            reasons.Add(metrics.FailedRunCount == 1 ? "1 failed run needs attention." : $"{metrics.FailedRunCount} failed runs need attention.");
        }

        if (metrics.StalledWorkCount >= resolvedThresholds.WarningStalledWork)
        {
            reasons.Add(metrics.StalledWorkCount == 1 ? "1 stalled work item needs attention." : $"{metrics.StalledWorkCount} stalled work items need attention.");
        }

        if (metrics.PolicyViolationCount >= resolvedThresholds.WarningPolicyViolations)
        {
            reasons.Add(metrics.PolicyViolationCount == 1
                ? "1 policy violation needs review."
                : $"{metrics.PolicyViolationCount} policy violations need review.");
        }

        return reasons.Count > 0
            ? new AgentStatusHealthResult(Warning, reasons)
            : new AgentStatusHealthResult(Healthy, ["No failed runs, stalled work, or policy violations are active."]);
    }
}
