namespace VirtualCompany.Domain.Enums;

public static class BankFeedCheckpointStatuses
{
    public const string Ready = "ready";
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Failed = "failed";
    public const string AttentionRequired = "attention_required";
    public const string Paused = "paused";
}

public static class BankFeedSynchronizationPhases
{
    public const string Booked = "booked";
    public const string Pending = "pending";
}

public static class BankFeedSourceTransactionStatuses
{
    public const string Pending = "pending";
    public const string Booked = "booked";
}

public static class BankFeedGapKinds
{
    public const string MissingRange = "missing_range";
    public const string CursorRegression = "cursor_regression";
    public const string PayloadConflict = "payload_conflict";
    public const string MalformedSource = "malformed_source";
}

public static class BankFeedGapStatuses
{
    public const string Open = "open";
    public const string Resolved = "resolved";
}

public static class BankFeedReasonCodes
{
    public const string MissingRange = "bank_feed_missing_range";
    public const string CursorRegression = "bank_feed_cursor_regression";
    public const string PayloadConflict = "bank_feed_payload_conflict";
    public const string MalformedSource = "bank_feed_malformed_source";
    public const string RateLimited = "bank_feed_rate_limited";
    public const string ProviderUnavailable = "bank_feed_provider_unavailable";
    public const string MappingChanged = "bank_feed_mapping_changed";
    public const string RecoveryRequired = "bank_feed_recovery_required";
}
