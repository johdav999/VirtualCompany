namespace VirtualCompany.Domain.Enums;

public enum FinanceSeedBackfillRunStatus
{
    Running = 1,
    Completed = 2,
    CompletedWithErrors = 3,
    Failed = 4
}

public enum FinanceSeedBackfillAttemptStatus
{
    Queued = 1,
    Skipped = 2,
    InProgress = 3,
    Succeeded = 4,
    Failed = 5
}

public static class FinanceSeedBackfillRunStatusValues
{
    private static readonly IReadOnlyDictionary<FinanceSeedBackfillRunStatus, string> Values = new Dictionary<FinanceSeedBackfillRunStatus, string>
    {
        [FinanceSeedBackfillRunStatus.Running] = "running",
        [FinanceSeedBackfillRunStatus.Completed] = "completed",
        [FinanceSeedBackfillRunStatus.CompletedWithErrors] = "completed_with_errors",
        [FinanceSeedBackfillRunStatus.Failed] = "failed"
    };

    private static readonly IReadOnlyDictionary<string, FinanceSeedBackfillRunStatus> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToStorageValue(this FinanceSeedBackfillRunStatus status) =>
        Values.TryGetValue(status, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported finance seed backfill run status.");

    public static FinanceSeedBackfillRunStatus Parse(string value) =>
        ReverseValues.TryGetValue(value.Trim(), out var status)
            ? status
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported finance seed backfill run status value.");
}

public static class FinanceSeedBackfillAttemptStatusValues
{
    private static readonly IReadOnlyDictionary<FinanceSeedBackfillAttemptStatus, string> Values = new Dictionary<FinanceSeedBackfillAttemptStatus, string>
    {
        [FinanceSeedBackfillAttemptStatus.Queued] = "queued",
        [FinanceSeedBackfillAttemptStatus.Skipped] = "skipped",
        [FinanceSeedBackfillAttemptStatus.InProgress] = "in_progress",
        [FinanceSeedBackfillAttemptStatus.Succeeded] = "succeeded",
        [FinanceSeedBackfillAttemptStatus.Failed] = "failed"
    };

    private static readonly IReadOnlyDictionary<string, FinanceSeedBackfillAttemptStatus> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToStorageValue(this FinanceSeedBackfillAttemptStatus status) =>
        Values.TryGetValue(status, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported finance seed backfill attempt status.");

    public static FinanceSeedBackfillAttemptStatus Parse(string value) =>
        ReverseValues.TryGetValue(value.Trim(), out var status)
            ? status
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported finance seed backfill attempt status value.");
}

public static class FinanceSeedBackfillSkipReasons
{
    public const string AlreadySeeded = "already_seeded";
    public const string ActiveExecution = "active_execution";
    public const string IneligibleState = "ineligible_state";
    public const string MaxQueuedPerRunReached = "max_queued_per_run_reached";
    public const string MissingCompany = "missing_company";
}

public static class FinanceSeedBackfillEligibilityReasons
{
    public const string NotSeeded = "not_seeded";
    public const string PartialSeedResume = "partial_seed_resume";
    public const string OrphanedPartialSeed = "orphaned_partial_seed";
}

public static class FinanceSeedBackfillRunErrors
{
    public const string UnhandledFailure = "finance_seed_backfill_unhandled_failure";
}

public static class FinanceSeedBackfillExecutionStates
{
    public static bool IsActive(BackgroundExecutionStatus status) =>
        status is BackgroundExecutionStatus.Pending
            or BackgroundExecutionStatus.InProgress
            or BackgroundExecutionStatus.RetryScheduled;
}