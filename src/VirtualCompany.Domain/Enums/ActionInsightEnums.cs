namespace VirtualCompany.Domain.Enums;

public enum ActionInsightType
{
    Approval = 1,
    Risk = 2,
    BlockedWorkflow = 3,
    Opportunity = 4,
    Task = 5
}

public enum ActionInsightPriority
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum ActionInsightSlaState
{
    None = 1,
    OnTrack = 2,
    DueSoon = 3,
    Breached = 4
}

public enum ActionInsightTargetType
{
    Approval = 1,
    Task = 2,
    Workflow = 3,
    Alert = 4
}

public static class ActionInsightTypeValues
{
    public static string ToStorageValue(this ActionInsightType type) =>
        type switch
        {
            ActionInsightType.Approval => "approval",
            ActionInsightType.Risk => "risk",
            ActionInsightType.BlockedWorkflow => "blocked_workflow",
            ActionInsightType.Opportunity => "opportunity",
            ActionInsightType.Task => "task",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported action insight type.")
        };
}

public static class ActionInsightPriorityValues
{
    public static string ToStorageValue(this ActionInsightPriority priority) =>
        priority switch
        {
            ActionInsightPriority.Critical => "critical",
            ActionInsightPriority.High => "high",
            ActionInsightPriority.Medium => "medium",
            ActionInsightPriority.Low => "low",
            _ => throw new ArgumentOutOfRangeException(nameof(priority), priority, "Unsupported action insight priority.")
        };

    public static ActionInsightPriority FromScore(int score) =>
        score switch
        {
            >= 90 => ActionInsightPriority.Critical,
            >= 70 => ActionInsightPriority.High,
            >= 45 => ActionInsightPriority.Medium,
            _ => ActionInsightPriority.Low
        };
}

public static class ActionInsightSlaStateValues
{
    public static string ToStorageValue(this ActionInsightSlaState state) =>
        state switch
        {
            ActionInsightSlaState.None => "none",
            ActionInsightSlaState.OnTrack => "on_track",
            ActionInsightSlaState.DueSoon => "due_soon",
            ActionInsightSlaState.Breached => "breached",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unsupported SLA state.")
        };
}

public static class ActionInsightTargetTypeValues
{
    public static string ToStorageValue(this ActionInsightTargetType type) =>
        type switch
        {
            ActionInsightTargetType.Approval => "approval",
            ActionInsightTargetType.Task => "task",
            ActionInsightTargetType.Workflow => "workflow",
            ActionInsightTargetType.Alert => "alert",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported action insight target type.")
        };
}