namespace VirtualCompany.Domain.Enums;

public enum SalesMeetingSessionStatus
{
    Ready = 1,
    Presenting = 2,
    Interrupted = 3,
    Answering = 4,
    Resuming = 5,
    Discussion = 6,
    Closing = 7,
    Completed = 8,
    Cancelled = 9,
    Failed = 10
}

public enum SalesPresentationCommandType
{
    Next = 1,
    Previous = 2,
    Goto = 3,
    Pause = 4,
    Resume = 5
}

public static class SalesMeetingSessionStatusValues
{
    private static readonly IReadOnlyDictionary<SalesMeetingSessionStatus, string> Values =
        new Dictionary<SalesMeetingSessionStatus, string>
        {
            [SalesMeetingSessionStatus.Ready] = "ready",
            [SalesMeetingSessionStatus.Presenting] = "presenting",
            [SalesMeetingSessionStatus.Interrupted] = "interrupted",
            [SalesMeetingSessionStatus.Answering] = "answering",
            [SalesMeetingSessionStatus.Resuming] = "resuming",
            [SalesMeetingSessionStatus.Discussion] = "discussion",
            [SalesMeetingSessionStatus.Closing] = "closing",
            [SalesMeetingSessionStatus.Completed] = "completed",
            [SalesMeetingSessionStatus.Cancelled] = "cancelled",
            [SalesMeetingSessionStatus.Failed] = "failed"
        };

    private static readonly IReadOnlyDictionary<string, SalesMeetingSessionStatus> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToStorageValue(this SalesMeetingSessionStatus value) =>
        Values.TryGetValue(value, out var storageValue)
            ? storageValue
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported sales meeting session status.");

    public static SalesMeetingSessionStatus Parse(string value) =>
        !string.IsNullOrWhiteSpace(value) && ReverseValues.TryGetValue(value.Trim(), out var parsed)
            ? parsed
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported sales meeting session status.");
}

public enum SalesMeetingConsentStatus
{
    NotRequested = 1,
    Pending = 2,
    Granted = 3,
    Denied = 4,
    Revoked = 5
}

public static class SalesMeetingConsentStatusValues
{
    private static readonly IReadOnlyDictionary<SalesMeetingConsentStatus, string> Values =
        new Dictionary<SalesMeetingConsentStatus, string>
        {
            [SalesMeetingConsentStatus.NotRequested] = "not_requested",
            [SalesMeetingConsentStatus.Pending] = "pending",
            [SalesMeetingConsentStatus.Granted] = "granted",
            [SalesMeetingConsentStatus.Denied] = "denied",
            [SalesMeetingConsentStatus.Revoked] = "revoked"
        };

    private static readonly IReadOnlyDictionary<string, SalesMeetingConsentStatus> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToStorageValue(this SalesMeetingConsentStatus value) =>
        Values.TryGetValue(value, out var storageValue)
            ? storageValue
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported sales meeting consent status.");

    public static SalesMeetingConsentStatus Parse(string value) =>
        !string.IsNullOrWhiteSpace(value) && ReverseValues.TryGetValue(value.Trim(), out var parsed)
            ? parsed
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported sales meeting consent status.");
}

public enum SalesMeetingRetentionPolicy
{
    Standard = 1,
    Custom = 2
}

public static class SalesMeetingRetentionPolicyValues
{
    private static readonly IReadOnlyDictionary<SalesMeetingRetentionPolicy, string> Values =
        new Dictionary<SalesMeetingRetentionPolicy, string>
        {
            [SalesMeetingRetentionPolicy.Standard] = "standard",
            [SalesMeetingRetentionPolicy.Custom] = "custom"
        };

    private static readonly IReadOnlyDictionary<string, SalesMeetingRetentionPolicy> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToStorageValue(this SalesMeetingRetentionPolicy value) =>
        Values.TryGetValue(value, out var storageValue)
            ? storageValue
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported sales meeting retention policy.");

    public static SalesMeetingRetentionPolicy Parse(string value) =>
        !string.IsNullOrWhiteSpace(value) && ReverseValues.TryGetValue(value.Trim(), out var parsed)
            ? parsed
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported sales meeting retention policy.");
}
