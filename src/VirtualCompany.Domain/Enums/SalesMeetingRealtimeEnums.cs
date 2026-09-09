namespace VirtualCompany.Domain.Enums;

public enum SalesMeetingVoiceSessionStatus
{
    Connecting = 1,
    Active = 2,
    Reconnecting = 3,
    Degraded = 4,
    Stopped = 5,
    Failed = 6,
    QuotaExceeded = 7,
    ConsentRevoked = 8
}

public static class SalesMeetingVoiceSessionStatusValues
{
    private static readonly IReadOnlyDictionary<SalesMeetingVoiceSessionStatus, string> Values =
        new Dictionary<SalesMeetingVoiceSessionStatus, string>
        {
            [SalesMeetingVoiceSessionStatus.Connecting] = "connecting",
            [SalesMeetingVoiceSessionStatus.Active] = "active",
            [SalesMeetingVoiceSessionStatus.Reconnecting] = "reconnecting",
            [SalesMeetingVoiceSessionStatus.Degraded] = "degraded",
            [SalesMeetingVoiceSessionStatus.Stopped] = "stopped",
            [SalesMeetingVoiceSessionStatus.Failed] = "failed",
            [SalesMeetingVoiceSessionStatus.QuotaExceeded] = "quota_exceeded",
            [SalesMeetingVoiceSessionStatus.ConsentRevoked] = "consent_revoked"
        };

    private static readonly IReadOnlyDictionary<string, SalesMeetingVoiceSessionStatus> Reverse =
        Values.ToDictionary(x => x.Value, x => x.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToStorageValue(this SalesMeetingVoiceSessionStatus value) =>
        Values.TryGetValue(value, out var result) ? result : throw new ArgumentOutOfRangeException(nameof(value));

    public static SalesMeetingVoiceSessionStatus Parse(string value) =>
        !string.IsNullOrWhiteSpace(value) && Reverse.TryGetValue(value.Trim(), out var result)
            ? result : throw new ArgumentOutOfRangeException(nameof(value));
}

public enum SalesMeetingVoiceEventOutcome
{
    Processed = 1,
    Duplicate = 2,
    IgnoredReordered = 3,
    ToolRejected = 4,
    Failed = 5
}

public static class SalesMeetingVoiceEventOutcomeValues
{
    private static readonly IReadOnlyDictionary<SalesMeetingVoiceEventOutcome, string> Values =
        new Dictionary<SalesMeetingVoiceEventOutcome, string>
        {
            [SalesMeetingVoiceEventOutcome.Processed] = "processed",
            [SalesMeetingVoiceEventOutcome.Duplicate] = "duplicate",
            [SalesMeetingVoiceEventOutcome.IgnoredReordered] = "ignored_reordered",
            [SalesMeetingVoiceEventOutcome.ToolRejected] = "tool_rejected",
            [SalesMeetingVoiceEventOutcome.Failed] = "failed"
        };

    private static readonly IReadOnlyDictionary<string, SalesMeetingVoiceEventOutcome> Reverse =
        Values.ToDictionary(x => x.Value, x => x.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToStorageValue(this SalesMeetingVoiceEventOutcome value) =>
        Values.TryGetValue(value, out var result) ? result : throw new ArgumentOutOfRangeException(nameof(value));

    public static SalesMeetingVoiceEventOutcome Parse(string value) =>
        !string.IsNullOrWhiteSpace(value) && Reverse.TryGetValue(value.Trim(), out var result)
            ? result : throw new ArgumentOutOfRangeException(nameof(value));
}
