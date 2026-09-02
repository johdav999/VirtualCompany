namespace VirtualCompany.Domain.Enums;

public enum FinanceAutonomyLevel
{
    ReadMonitor = 1,
    RecommendDraft = 2,
    SupervisedInternalExecute = 3,
    ScheduledBoundedExecute = 4
}

public enum FinanceAutonomyGrantVersionStatus
{
    Prospective = 1,
    PendingReview = 2,
    Active = 3,
    Superseded = 4,
    Revoked = 5
}

public enum FinanceAutonomyControlScope
{
    Company = 1,
    Agent = 2,
    Capability = 3
}

public enum FinanceAutonomyControlState
{
    Active = 1,
    Paused = 2,
    EmergencyStopped = 3
}

public static class FinanceAutonomyEnumValues
{
    public static string ToStorageValue(this FinanceAutonomyLevel value) => value switch
    {
        FinanceAutonomyLevel.ReadMonitor => "read_monitor",
        FinanceAutonomyLevel.RecommendDraft => "recommend_draft",
        FinanceAutonomyLevel.SupervisedInternalExecute => "supervised_internal_execute",
        FinanceAutonomyLevel.ScheduledBoundedExecute => "scheduled_bounded_execute",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static FinanceAutonomyLevel ParseFinanceAutonomyLevel(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "read_monitor" => FinanceAutonomyLevel.ReadMonitor,
        "recommend_draft" => FinanceAutonomyLevel.RecommendDraft,
        "supervised_internal_execute" => FinanceAutonomyLevel.SupervisedInternalExecute,
        "scheduled_bounded_execute" => FinanceAutonomyLevel.ScheduledBoundedExecute,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Unknown Finance autonomy level.")
    };

    public static string ToStorageValue(this FinanceAutonomyGrantVersionStatus value) => value switch
    {
        FinanceAutonomyGrantVersionStatus.Prospective => "prospective",
        FinanceAutonomyGrantVersionStatus.PendingReview => "pending_review",
        FinanceAutonomyGrantVersionStatus.Active => "active",
        FinanceAutonomyGrantVersionStatus.Superseded => "superseded",
        FinanceAutonomyGrantVersionStatus.Revoked => "revoked",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static FinanceAutonomyGrantVersionStatus ParseFinanceAutonomyGrantStatus(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "prospective" => FinanceAutonomyGrantVersionStatus.Prospective,
        "pending_review" => FinanceAutonomyGrantVersionStatus.PendingReview,
        "active" => FinanceAutonomyGrantVersionStatus.Active,
        "superseded" => FinanceAutonomyGrantVersionStatus.Superseded,
        "revoked" => FinanceAutonomyGrantVersionStatus.Revoked,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Unknown Finance autonomy grant status.")
    };

    public static string ToStorageValue(this FinanceAutonomyControlScope value) => value switch
    {
        FinanceAutonomyControlScope.Company => "company",
        FinanceAutonomyControlScope.Agent => "agent",
        FinanceAutonomyControlScope.Capability => "capability",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static FinanceAutonomyControlScope ParseFinanceAutonomyControlScope(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "company" => FinanceAutonomyControlScope.Company,
        "agent" => FinanceAutonomyControlScope.Agent,
        "capability" => FinanceAutonomyControlScope.Capability,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Unknown Finance autonomy control scope.")
    };

    public static string ToStorageValue(this FinanceAutonomyControlState value) => value switch
    {
        FinanceAutonomyControlState.Active => "active",
        FinanceAutonomyControlState.Paused => "paused",
        FinanceAutonomyControlState.EmergencyStopped => "emergency_stopped",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static FinanceAutonomyControlState ParseFinanceAutonomyControlState(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "active" => FinanceAutonomyControlState.Active,
        "paused" => FinanceAutonomyControlState.Paused,
        "emergency_stopped" => FinanceAutonomyControlState.EmergencyStopped,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Unknown Finance autonomy control state.")
    };
}
