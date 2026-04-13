namespace VirtualCompany.Application.Orchestration;

public sealed record BoundedCollaborationPolicy(
    int MaxFanOut,
    int MaxDepth,
    TimeSpan MaxRuntime,
    int MaxTotalSteps)
{
    public static BoundedCollaborationPolicy FromLimits(CollaborationLimitDto limits) =>
        new(limits.MaxWorkers, limits.MaxDepth, TimeSpan.FromSeconds(limits.MaxRuntimeSeconds), limits.MaxTotalSteps);

    public BoundedCollaborationDecision Evaluate(BoundedCollaborationExecutionState state)
    {
        if (state.RequestedFanOut > MaxFanOut)
        {
            return BoundedCollaborationDecision.Denied(
                MultiAgentCollaborationTerminationReasons.FanOutExceeded,
                "fan_out",
                MaxFanOut,
                state.RequestedFanOut,
                $"Worker fan-out cannot exceed {MaxFanOut}.");
        }

        if (state.CurrentDepth > MaxDepth)
        {
            return BoundedCollaborationDecision.Denied(
                MultiAgentCollaborationTerminationReasons.DepthLimitExceeded,
                "depth",
                MaxDepth,
                state.CurrentDepth,
                $"Delegation depth cannot exceed {MaxDepth}.");
        }

        if (state.ExecutedSteps > MaxTotalSteps)
        {
            return BoundedCollaborationDecision.Denied(
                MultiAgentCollaborationTerminationReasons.StepLimitExceeded,
                "step_count",
                MaxTotalSteps,
                state.ExecutedSteps,
                $"Collaboration step count cannot exceed {MaxTotalSteps}.");
        }

        if (state.Elapsed >= MaxRuntime)
        {
            return BoundedCollaborationDecision.Denied(
                MultiAgentCollaborationTerminationReasons.RuntimeBudgetExceeded,
                "runtime",
                (int)MaxRuntime.TotalSeconds,
                (int)Math.Ceiling(state.Elapsed.TotalSeconds),
                $"Collaboration runtime budget of {(int)MaxRuntime.TotalSeconds} second(s) was exhausted.");
        }

        return BoundedCollaborationDecision.Permitted();
    }
}

public sealed record BoundedCollaborationExecutionState(
    int RequestedFanOut,
    int CurrentDepth,
    int ExecutedSteps,
    DateTime StartedAtUtc)
{
    public TimeSpan Elapsed => DateTime.UtcNow - StartedAtUtc;
}

public sealed record BoundedCollaborationDecision(
    bool Allowed,
    string? TerminationReason,
    string? LimitType,
    int? ConfiguredThreshold,
    int? ObservedValue,
    string? Rationale)
{
    public static BoundedCollaborationDecision Permitted() => new(true, null, null, null, null, null);

    public static BoundedCollaborationDecision Denied(
        string terminationReason,
        string limitType,
        int configuredThreshold,
        int observedValue,
        string rationale) =>
        new(false, terminationReason, limitType, configuredThreshold, observedValue, rationale);
}
