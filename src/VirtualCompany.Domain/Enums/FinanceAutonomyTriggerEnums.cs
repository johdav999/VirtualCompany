namespace VirtualCompany.Domain.Enums;

public enum FinanceAutonomyTriggerCursorStatus
{
    Idle,
    Claimed,
    Processed,
    Coalesced,
    Suppressed,
    RetryScheduled,
    DeadLettered
}

public enum FinanceAutonomyTriggerEventStatus
{
    Received,
    Processed,
    Coalesced,
    Suppressed,
    DeadLettered
}

public static class FinanceAutonomyTriggerEnumValues
{
    public static string ToStorageValue(this FinanceAutonomyTriggerCursorStatus value) => value switch
    {
        FinanceAutonomyTriggerCursorStatus.Idle => "idle",
        FinanceAutonomyTriggerCursorStatus.Claimed => "claimed",
        FinanceAutonomyTriggerCursorStatus.Processed => "processed",
        FinanceAutonomyTriggerCursorStatus.Coalesced => "coalesced",
        FinanceAutonomyTriggerCursorStatus.Suppressed => "suppressed",
        FinanceAutonomyTriggerCursorStatus.RetryScheduled => "retry_scheduled",
        FinanceAutonomyTriggerCursorStatus.DeadLettered => "dead_lettered",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static FinanceAutonomyTriggerCursorStatus ParseCursorStatus(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "idle" => FinanceAutonomyTriggerCursorStatus.Idle,
            "claimed" => FinanceAutonomyTriggerCursorStatus.Claimed,
            "processed" => FinanceAutonomyTriggerCursorStatus.Processed,
            "coalesced" => FinanceAutonomyTriggerCursorStatus.Coalesced,
            "suppressed" => FinanceAutonomyTriggerCursorStatus.Suppressed,
            "retry_scheduled" => FinanceAutonomyTriggerCursorStatus.RetryScheduled,
            "dead_lettered" => FinanceAutonomyTriggerCursorStatus.DeadLettered,
            _ => throw new InvalidOperationException($"Unknown Finance autonomy trigger cursor status '{value}'.")
        };

    public static string ToStorageValue(this FinanceAutonomyTriggerEventStatus value) => value switch
    {
        FinanceAutonomyTriggerEventStatus.Received => "received",
        FinanceAutonomyTriggerEventStatus.Processed => "processed",
        FinanceAutonomyTriggerEventStatus.Coalesced => "coalesced",
        FinanceAutonomyTriggerEventStatus.Suppressed => "suppressed",
        FinanceAutonomyTriggerEventStatus.DeadLettered => "dead_lettered",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static FinanceAutonomyTriggerEventStatus ParseEventStatus(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "received" => FinanceAutonomyTriggerEventStatus.Received,
            "processed" => FinanceAutonomyTriggerEventStatus.Processed,
            "coalesced" => FinanceAutonomyTriggerEventStatus.Coalesced,
            "suppressed" => FinanceAutonomyTriggerEventStatus.Suppressed,
            "dead_lettered" => FinanceAutonomyTriggerEventStatus.DeadLettered,
            _ => throw new InvalidOperationException($"Unknown Finance autonomy trigger event status '{value}'.")
        };
}
