namespace VirtualCompany.Domain.Enums;

public enum SalesMeetingTranscriptSubscriptionStatus
{
    Active = 1,
    RenewalRequired = 2,
    PermissionRequired = 3,
    Expired = 4,
    Disabled = 5,
    Failed = 6
}

public enum SalesMeetingTranscriptIngestionStatus
{
    Pending = 1,
    Processing = 2,
    Completed = 3,
    RetryPending = 4,
    PermanentFailure = 5,
    Ignored = 6
}

public enum SalesMeetingTranscriptMatchKind
{
    Equivalent = 1,
    GapAdded = 2,
    SpeakerCorrected = 3,
    ReviewConflict = 4,
    Unchanged = 5
}

public static class SalesMeetingTranscriptReconciliationEnumValues
{
    public static string ToStorageValue(this SalesMeetingTranscriptSubscriptionStatus value) => value switch
    {
        SalesMeetingTranscriptSubscriptionStatus.Active => "active",
        SalesMeetingTranscriptSubscriptionStatus.RenewalRequired => "renewal_required",
        SalesMeetingTranscriptSubscriptionStatus.PermissionRequired => "permission_required",
        SalesMeetingTranscriptSubscriptionStatus.Expired => "expired",
        SalesMeetingTranscriptSubscriptionStatus.Disabled => "disabled",
        SalesMeetingTranscriptSubscriptionStatus.Failed => "failed",
        _ => throw Unsupported(value)
    };

    public static SalesMeetingTranscriptSubscriptionStatus ParseSubscriptionStatus(string value) => Normalize(value) switch
    {
        "active" => SalesMeetingTranscriptSubscriptionStatus.Active,
        "renewal_required" => SalesMeetingTranscriptSubscriptionStatus.RenewalRequired,
        "permission_required" => SalesMeetingTranscriptSubscriptionStatus.PermissionRequired,
        "expired" => SalesMeetingTranscriptSubscriptionStatus.Expired,
        "disabled" => SalesMeetingTranscriptSubscriptionStatus.Disabled,
        "failed" => SalesMeetingTranscriptSubscriptionStatus.Failed,
        _ => throw Unsupported(value)
    };

    public static string ToStorageValue(this SalesMeetingTranscriptIngestionStatus value) => value switch
    {
        SalesMeetingTranscriptIngestionStatus.Pending => "pending",
        SalesMeetingTranscriptIngestionStatus.Processing => "processing",
        SalesMeetingTranscriptIngestionStatus.Completed => "completed",
        SalesMeetingTranscriptIngestionStatus.RetryPending => "retry_pending",
        SalesMeetingTranscriptIngestionStatus.PermanentFailure => "permanent_failure",
        SalesMeetingTranscriptIngestionStatus.Ignored => "ignored",
        _ => throw Unsupported(value)
    };

    public static SalesMeetingTranscriptIngestionStatus ParseIngestionStatus(string value) => Normalize(value) switch
    {
        "pending" => SalesMeetingTranscriptIngestionStatus.Pending,
        "processing" => SalesMeetingTranscriptIngestionStatus.Processing,
        "completed" => SalesMeetingTranscriptIngestionStatus.Completed,
        "retry_pending" => SalesMeetingTranscriptIngestionStatus.RetryPending,
        "permanent_failure" => SalesMeetingTranscriptIngestionStatus.PermanentFailure,
        "ignored" => SalesMeetingTranscriptIngestionStatus.Ignored,
        _ => throw Unsupported(value)
    };

    public static string ToStorageValue(this SalesMeetingTranscriptMatchKind value) => value switch
    {
        SalesMeetingTranscriptMatchKind.Equivalent => "equivalent",
        SalesMeetingTranscriptMatchKind.GapAdded => "gap_added",
        SalesMeetingTranscriptMatchKind.SpeakerCorrected => "speaker_corrected",
        SalesMeetingTranscriptMatchKind.ReviewConflict => "review_conflict",
        SalesMeetingTranscriptMatchKind.Unchanged => "unchanged",
        _ => throw Unsupported(value)
    };

    public static SalesMeetingTranscriptMatchKind ParseMatchKind(string value) => Normalize(value) switch
    {
        "equivalent" => SalesMeetingTranscriptMatchKind.Equivalent,
        "gap_added" => SalesMeetingTranscriptMatchKind.GapAdded,
        "speaker_corrected" => SalesMeetingTranscriptMatchKind.SpeakerCorrected,
        "review_conflict" => SalesMeetingTranscriptMatchKind.ReviewConflict,
        "unchanged" => SalesMeetingTranscriptMatchKind.Unchanged,
        _ => throw Unsupported(value)
    };

    private static string Normalize(string value) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException("A transcript reconciliation value is required.", nameof(value))
        : value.Trim().ToLowerInvariant();

    private static ArgumentOutOfRangeException Unsupported<T>(T value) =>
        new(nameof(value), value, "Unsupported transcript reconciliation value.");
}
