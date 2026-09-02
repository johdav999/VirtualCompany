namespace VirtualCompany.Domain.Enums;

public enum FinanceAutonomyRunStatus
{
    Planned = 0,
    Validating = 1,
    Running = 2,
    AwaitingApproval = 3,
    Queued = 4,
    Reconciling = 5,
    Blocked = 6,
    Paused = 7,
    Completed = 8,
    PartiallyCompleted = 9,
    Cancelled = 10,
    Failed = 11,
    DeadLettered = 12,
    Superseded = 13
}

public enum FinanceAutonomyStepStatus
{
    Planned = 0,
    Validating = 1,
    Queued = 2,
    Running = 3,
    AwaitingApproval = 4,
    Reconciling = 5,
    Blocked = 6,
    Paused = 7,
    Completed = 8,
    Cancelled = 9,
    Failed = 10,
    DeadLettered = 11,
    Superseded = 12
}

public static class FinanceAutonomyRunEnumValues
{
    public static string ToStorageValue(this FinanceAutonomyRunStatus value) => value switch
    {
        FinanceAutonomyRunStatus.Planned => "planned",
        FinanceAutonomyRunStatus.Validating => "validating",
        FinanceAutonomyRunStatus.Running => "running",
        FinanceAutonomyRunStatus.AwaitingApproval => "awaiting_approval",
        FinanceAutonomyRunStatus.Queued => "queued",
        FinanceAutonomyRunStatus.Reconciling => "reconciling",
        FinanceAutonomyRunStatus.Blocked => "blocked",
        FinanceAutonomyRunStatus.Paused => "paused",
        FinanceAutonomyRunStatus.Completed => "completed",
        FinanceAutonomyRunStatus.PartiallyCompleted => "partially_completed",
        FinanceAutonomyRunStatus.Cancelled => "cancelled",
        FinanceAutonomyRunStatus.Failed => "failed",
        FinanceAutonomyRunStatus.DeadLettered => "dead_lettered",
        FinanceAutonomyRunStatus.Superseded => "superseded",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static FinanceAutonomyRunStatus ParseRunStatus(string value) => value switch
    {
        "planned" => FinanceAutonomyRunStatus.Planned,
        "validating" => FinanceAutonomyRunStatus.Validating,
        "running" => FinanceAutonomyRunStatus.Running,
        "awaiting_approval" => FinanceAutonomyRunStatus.AwaitingApproval,
        "queued" => FinanceAutonomyRunStatus.Queued,
        "reconciling" => FinanceAutonomyRunStatus.Reconciling,
        "blocked" => FinanceAutonomyRunStatus.Blocked,
        "paused" => FinanceAutonomyRunStatus.Paused,
        "completed" => FinanceAutonomyRunStatus.Completed,
        "partially_completed" => FinanceAutonomyRunStatus.PartiallyCompleted,
        "cancelled" => FinanceAutonomyRunStatus.Cancelled,
        "failed" => FinanceAutonomyRunStatus.Failed,
        "dead_lettered" => FinanceAutonomyRunStatus.DeadLettered,
        "superseded" => FinanceAutonomyRunStatus.Superseded,
        _ => throw new InvalidOperationException($"Unknown Finance autonomy run status '{value}'.")
    };

    public static string ToStorageValue(this FinanceAutonomyStepStatus value) => value switch
    {
        FinanceAutonomyStepStatus.Planned => "planned",
        FinanceAutonomyStepStatus.Validating => "validating",
        FinanceAutonomyStepStatus.Queued => "queued",
        FinanceAutonomyStepStatus.Running => "running",
        FinanceAutonomyStepStatus.AwaitingApproval => "awaiting_approval",
        FinanceAutonomyStepStatus.Reconciling => "reconciling",
        FinanceAutonomyStepStatus.Blocked => "blocked",
        FinanceAutonomyStepStatus.Paused => "paused",
        FinanceAutonomyStepStatus.Completed => "completed",
        FinanceAutonomyStepStatus.Cancelled => "cancelled",
        FinanceAutonomyStepStatus.Failed => "failed",
        FinanceAutonomyStepStatus.DeadLettered => "dead_lettered",
        FinanceAutonomyStepStatus.Superseded => "superseded",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static FinanceAutonomyStepStatus ParseStepStatus(string value) => value switch
    {
        "planned" => FinanceAutonomyStepStatus.Planned,
        "validating" => FinanceAutonomyStepStatus.Validating,
        "queued" => FinanceAutonomyStepStatus.Queued,
        "running" => FinanceAutonomyStepStatus.Running,
        "awaiting_approval" => FinanceAutonomyStepStatus.AwaitingApproval,
        "reconciling" => FinanceAutonomyStepStatus.Reconciling,
        "blocked" => FinanceAutonomyStepStatus.Blocked,
        "paused" => FinanceAutonomyStepStatus.Paused,
        "completed" => FinanceAutonomyStepStatus.Completed,
        "cancelled" => FinanceAutonomyStepStatus.Cancelled,
        "failed" => FinanceAutonomyStepStatus.Failed,
        "dead_lettered" => FinanceAutonomyStepStatus.DeadLettered,
        "superseded" => FinanceAutonomyStepStatus.Superseded,
        _ => throw new InvalidOperationException($"Unknown Finance autonomy step status '{value}'.")
    };
}
