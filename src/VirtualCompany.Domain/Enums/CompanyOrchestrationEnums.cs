namespace VirtualCompany.Domain.Enums;

public enum CompanyGoalStatus { Draft = 1, Active = 2, Paused = 3, Completed = 4, Cancelled = 5 }
public enum CompanyGoalPriority { Low = 1, Normal = 2, High = 3, Critical = 4 }
public enum CompanyAutonomyLevel { Recommend = 1, Organize = 2, OperateInternally = 3, ControlledExecution = 4 }
public enum OperatingCycleStatus { Requested = 1, Observing = 2, Planning = 3, Validating = 4, AwaitingReview = 5, Completed = 6, Failed = 7, Cancelled = 8 }
public enum OperatingPlanStatus { Draft = 1, AwaitingReview = 2, Approved = 3, Rejected = 4, Committing = 5, Committed = 6, Superseded = 7, Cancelled = 8 }
public enum OperatingInitiativeStatus { Proposed = 1, Approved = 2, Active = 3, Blocked = 4, Completed = 5, Failed = 6, Cancelled = 7 }
public enum OperatingActionClass { Read = 1, Recommend = 2, InternalMutation = 3, ExternalExecute = 4 }
public enum OperatingValidationOutcome { Allowed = 1, ReviewRequired = 2, Denied = 3 }
public enum OperatingReviewOutcome { CloseSuccessful = 1, Continue = 2, Revise = 3, Reassign = 4, RequestEvidence = 5, Escalate = 6, Pause = 7, Stop = 8 }

public static class CompanyGoalStatusValues
{
    public static string ToStorageValue(this CompanyGoalStatus value) => value switch
    {
        CompanyGoalStatus.Draft => "draft", CompanyGoalStatus.Active => "active", CompanyGoalStatus.Paused => "paused",
        CompanyGoalStatus.Completed => "completed", CompanyGoalStatus.Cancelled => "cancelled", _ => throw Unsupported(value)
    };
    public static CompanyGoalStatus Parse(string value) => ParseEnum(value, new Dictionary<string, CompanyGoalStatus>(StringComparer.OrdinalIgnoreCase)
    { ["draft"] = CompanyGoalStatus.Draft, ["active"] = CompanyGoalStatus.Active, ["paused"] = CompanyGoalStatus.Paused, ["completed"] = CompanyGoalStatus.Completed, ["cancelled"] = CompanyGoalStatus.Cancelled });
    private static Exception Unsupported(CompanyGoalStatus value) => new ArgumentOutOfRangeException(nameof(value), value, "Unsupported company goal status.");
    private static T ParseEnum<T>(string value, IReadOnlyDictionary<string, T> values) => values.TryGetValue(value?.Trim() ?? string.Empty, out var parsed) ? parsed : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported storage value.");
}

public static class CompanyGoalPriorityValues
{
    public static string ToStorageValue(this CompanyGoalPriority value) => value switch
    { CompanyGoalPriority.Low => "low", CompanyGoalPriority.Normal => "normal", CompanyGoalPriority.High => "high", CompanyGoalPriority.Critical => "critical", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    public static CompanyGoalPriority Parse(string value) => value?.Trim().ToLowerInvariant() switch
    { "low" => CompanyGoalPriority.Low, "normal" => CompanyGoalPriority.Normal, "high" => CompanyGoalPriority.High, "critical" => CompanyGoalPriority.Critical, _ => throw new ArgumentOutOfRangeException(nameof(value)) };
}

public static class CompanyAutonomyLevelValues
{
    public static string ToStorageValue(this CompanyAutonomyLevel value) => value switch
    { CompanyAutonomyLevel.Recommend => "recommend", CompanyAutonomyLevel.Organize => "organize", CompanyAutonomyLevel.OperateInternally => "operate_internally", CompanyAutonomyLevel.ControlledExecution => "controlled_execution", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    public static CompanyAutonomyLevel Parse(string value) => value?.Trim().ToLowerInvariant() switch
    { "recommend" => CompanyAutonomyLevel.Recommend, "organize" => CompanyAutonomyLevel.Organize, "operate_internally" => CompanyAutonomyLevel.OperateInternally, "controlled_execution" => CompanyAutonomyLevel.ControlledExecution, _ => throw new ArgumentOutOfRangeException(nameof(value)) };
}

public static class OperatingCycleStatusValues
{
    public static string ToStorageValue(this OperatingCycleStatus value) => value switch
    { OperatingCycleStatus.Requested => "requested", OperatingCycleStatus.Observing => "observing", OperatingCycleStatus.Planning => "planning", OperatingCycleStatus.Validating => "validating", OperatingCycleStatus.AwaitingReview => "awaiting_review", OperatingCycleStatus.Completed => "completed", OperatingCycleStatus.Failed => "failed", OperatingCycleStatus.Cancelled => "cancelled", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    public static OperatingCycleStatus Parse(string value) => EnumStorage.Parse(value, new Dictionary<string, OperatingCycleStatus>(StringComparer.OrdinalIgnoreCase)
    { ["requested"] = OperatingCycleStatus.Requested, ["observing"] = OperatingCycleStatus.Observing, ["planning"] = OperatingCycleStatus.Planning, ["validating"] = OperatingCycleStatus.Validating, ["awaiting_review"] = OperatingCycleStatus.AwaitingReview, ["completed"] = OperatingCycleStatus.Completed, ["failed"] = OperatingCycleStatus.Failed, ["cancelled"] = OperatingCycleStatus.Cancelled });
}

public static class OperatingPlanStatusValues
{
    public static string ToStorageValue(this OperatingPlanStatus value) => value switch
    { OperatingPlanStatus.Draft => "draft", OperatingPlanStatus.AwaitingReview => "awaiting_review", OperatingPlanStatus.Approved => "approved", OperatingPlanStatus.Rejected => "rejected", OperatingPlanStatus.Committing => "committing", OperatingPlanStatus.Committed => "committed", OperatingPlanStatus.Superseded => "superseded", OperatingPlanStatus.Cancelled => "cancelled", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    public static OperatingPlanStatus Parse(string value) => EnumStorage.Parse(value, new Dictionary<string, OperatingPlanStatus>(StringComparer.OrdinalIgnoreCase)
    { ["draft"] = OperatingPlanStatus.Draft, ["awaiting_review"] = OperatingPlanStatus.AwaitingReview, ["approved"] = OperatingPlanStatus.Approved, ["rejected"] = OperatingPlanStatus.Rejected, ["committing"] = OperatingPlanStatus.Committing, ["committed"] = OperatingPlanStatus.Committed, ["superseded"] = OperatingPlanStatus.Superseded, ["cancelled"] = OperatingPlanStatus.Cancelled });
}

public static class OperatingInitiativeStatusValues
{
    public static string ToStorageValue(this OperatingInitiativeStatus value) => value switch
    { OperatingInitiativeStatus.Proposed => "proposed", OperatingInitiativeStatus.Approved => "approved", OperatingInitiativeStatus.Active => "active", OperatingInitiativeStatus.Blocked => "blocked", OperatingInitiativeStatus.Completed => "completed", OperatingInitiativeStatus.Failed => "failed", OperatingInitiativeStatus.Cancelled => "cancelled", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    public static OperatingInitiativeStatus Parse(string value) => EnumStorage.Parse(value, new Dictionary<string, OperatingInitiativeStatus>(StringComparer.OrdinalIgnoreCase)
    { ["proposed"] = OperatingInitiativeStatus.Proposed, ["approved"] = OperatingInitiativeStatus.Approved, ["active"] = OperatingInitiativeStatus.Active, ["blocked"] = OperatingInitiativeStatus.Blocked, ["completed"] = OperatingInitiativeStatus.Completed, ["failed"] = OperatingInitiativeStatus.Failed, ["cancelled"] = OperatingInitiativeStatus.Cancelled });
}

public static class OperatingActionClassValues
{
    public static string ToStorageValue(this OperatingActionClass value) => value switch
    { OperatingActionClass.Read => "read", OperatingActionClass.Recommend => "recommend", OperatingActionClass.InternalMutation => "internal_mutation", OperatingActionClass.ExternalExecute => "external_execute", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    public static OperatingActionClass Parse(string value) => value?.Trim().ToLowerInvariant() switch
    { "read" => OperatingActionClass.Read, "recommend" => OperatingActionClass.Recommend, "internal_mutation" => OperatingActionClass.InternalMutation, "external_execute" => OperatingActionClass.ExternalExecute, _ => throw new ArgumentOutOfRangeException(nameof(value)) };
}

public static class OperatingValidationOutcomeValues
{
    public static string ToStorageValue(this OperatingValidationOutcome value) => value switch
    { OperatingValidationOutcome.Allowed => "allowed", OperatingValidationOutcome.ReviewRequired => "review_required", OperatingValidationOutcome.Denied => "denied", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    public static OperatingValidationOutcome Parse(string value) => value?.Trim().ToLowerInvariant() switch
    { "allowed" => OperatingValidationOutcome.Allowed, "review_required" => OperatingValidationOutcome.ReviewRequired, "denied" => OperatingValidationOutcome.Denied, _ => throw new ArgumentOutOfRangeException(nameof(value)) };
}

public static class OperatingReviewOutcomeValues
{
    public static string ToStorageValue(this OperatingReviewOutcome value) => value switch
    { OperatingReviewOutcome.CloseSuccessful => "close_successful", OperatingReviewOutcome.Continue => "continue", OperatingReviewOutcome.Revise => "revise", OperatingReviewOutcome.Reassign => "reassign", OperatingReviewOutcome.RequestEvidence => "request_evidence", OperatingReviewOutcome.Escalate => "escalate", OperatingReviewOutcome.Pause => "pause", OperatingReviewOutcome.Stop => "stop", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    public static OperatingReviewOutcome Parse(string value) => EnumStorage.Parse(value, new Dictionary<string, OperatingReviewOutcome>(StringComparer.OrdinalIgnoreCase)
    { ["close_successful"] = OperatingReviewOutcome.CloseSuccessful, ["continue"] = OperatingReviewOutcome.Continue, ["revise"] = OperatingReviewOutcome.Revise, ["reassign"] = OperatingReviewOutcome.Reassign, ["request_evidence"] = OperatingReviewOutcome.RequestEvidence, ["escalate"] = OperatingReviewOutcome.Escalate, ["pause"] = OperatingReviewOutcome.Pause, ["stop"] = OperatingReviewOutcome.Stop });
}

internal static class EnumStorage
{
    public static T Parse<T>(string value, IReadOnlyDictionary<string, T> values) =>
        values.TryGetValue(value?.Trim() ?? string.Empty, out var parsed) ? parsed : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported storage value.");
}
