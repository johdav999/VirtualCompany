namespace VirtualCompany.Domain.Enums;

public enum TriggerExecutionAttemptStatus
{
    Pending = 1,
    Dispatched = 2,
    Blocked = 3,
    DuplicateSkipped = 4,
    Failed = 5,
    Retried = 6,
    RetryScheduled = 7,
    DeadLettered = 8
}

public static class TriggerExecutionAttemptStatusValues
{
    private static readonly IReadOnlyDictionary<TriggerExecutionAttemptStatus, string> Values = new Dictionary<TriggerExecutionAttemptStatus, string>
    {
        [TriggerExecutionAttemptStatus.Pending] = "pending",
        [TriggerExecutionAttemptStatus.Dispatched] = "dispatched",
        [TriggerExecutionAttemptStatus.Blocked] = "blocked",
        [TriggerExecutionAttemptStatus.DuplicateSkipped] = "duplicate_skipped",
        [TriggerExecutionAttemptStatus.Failed] = "failed",
        [TriggerExecutionAttemptStatus.Retried] = "retried",
        [TriggerExecutionAttemptStatus.RetryScheduled] = "retry_scheduled",
        [TriggerExecutionAttemptStatus.DeadLettered] = "dead_lettered"
    };

    private static readonly IReadOnlyDictionary<string, TriggerExecutionAttemptStatus> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static TriggerExecutionAttemptStatus DefaultStatus => TriggerExecutionAttemptStatus.Pending;

    public static string ToStorageValue(this TriggerExecutionAttemptStatus status) =>
        Values.TryGetValue(status, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported trigger execution attempt status.");

    public static TriggerExecutionAttemptStatus Parse(string value) =>
        TryParse(value, out var status)
            ? status
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported trigger execution attempt status value.");

    public static bool TryParse(string? value, out TriggerExecutionAttemptStatus status)
    {
        status = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return ReverseValues.TryGetValue(trimmed, out status) ||
            Enum.TryParse(trimmed, ignoreCase: true, out status) && Values.ContainsKey(status);
    }
}
