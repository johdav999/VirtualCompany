namespace VirtualCompany.Domain.Enums;

public enum SalesMeetingInvitationStatus
{
    Draft = 1,
    WaitingForApproval = 2,
    Queued = 3,
    Scheduling = 4,
    Scheduled = 5,
    Rejected = 6,
    Failed = 7,
    ReconciliationRequired = 8,
    Cancelled = 9
}

public static class SalesMeetingInvitationStatusValues
{
    private static readonly IReadOnlyDictionary<SalesMeetingInvitationStatus, string> Values =
        new Dictionary<SalesMeetingInvitationStatus, string>
        {
            [SalesMeetingInvitationStatus.Draft] = "draft",
            [SalesMeetingInvitationStatus.WaitingForApproval] = "waiting_for_approval",
            [SalesMeetingInvitationStatus.Queued] = "queued",
            [SalesMeetingInvitationStatus.Scheduling] = "scheduling",
            [SalesMeetingInvitationStatus.Scheduled] = "scheduled",
            [SalesMeetingInvitationStatus.Rejected] = "rejected",
            [SalesMeetingInvitationStatus.Failed] = "failed",
            [SalesMeetingInvitationStatus.ReconciliationRequired] = "reconciliation_required",
            [SalesMeetingInvitationStatus.Cancelled] = "cancelled"
        };

    private static readonly IReadOnlyDictionary<string, SalesMeetingInvitationStatus> ReverseValues =
        Values.ToDictionary(x => x.Value, x => x.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToStorageValue(this SalesMeetingInvitationStatus value) =>
        Values.TryGetValue(value, out var storageValue)
            ? storageValue
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported sales meeting invitation status.");

    public static SalesMeetingInvitationStatus Parse(string value) =>
        !string.IsNullOrWhiteSpace(value) && ReverseValues.TryGetValue(value.Trim(), out var parsed)
            ? parsed
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported sales meeting invitation status.");
}

public enum SalesMeetingConfirmationStatus
{
    NotQueued = 1,
    Queued = 2,
    Sending = 3,
    Sent = 4,
    Failed = 5,
    ReconciliationRequired = 6,
    Unavailable = 7
}

public static class SalesMeetingConfirmationStatusValues
{
    private static readonly IReadOnlyDictionary<SalesMeetingConfirmationStatus, string> Values =
        new Dictionary<SalesMeetingConfirmationStatus, string>
        {
            [SalesMeetingConfirmationStatus.NotQueued] = "not_queued",
            [SalesMeetingConfirmationStatus.Queued] = "queued",
            [SalesMeetingConfirmationStatus.Sending] = "sending",
            [SalesMeetingConfirmationStatus.Sent] = "sent",
            [SalesMeetingConfirmationStatus.Failed] = "failed",
            [SalesMeetingConfirmationStatus.ReconciliationRequired] = "reconciliation_required",
            [SalesMeetingConfirmationStatus.Unavailable] = "unavailable"
        };

    private static readonly IReadOnlyDictionary<string, SalesMeetingConfirmationStatus> ReverseValues =
        Values.ToDictionary(x => x.Value, x => x.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToStorageValue(this SalesMeetingConfirmationStatus value) =>
        Values.TryGetValue(value, out var storageValue)
            ? storageValue
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported sales meeting confirmation status.");

    public static SalesMeetingConfirmationStatus Parse(string value) =>
        !string.IsNullOrWhiteSpace(value) && ReverseValues.TryGetValue(value.Trim(), out var parsed)
            ? parsed
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported sales meeting confirmation status.");
}
public enum MailboxReplyThreadingMode
{
    Unknown = 1,
    Native = 2,
    HeaderBased = 3
}

public static class MailboxReplyThreadingModeValues
{
    public static string ToStorageValue(this MailboxReplyThreadingMode value) => value switch
    {
        MailboxReplyThreadingMode.Unknown => "unknown",
        MailboxReplyThreadingMode.Native => "native",
        MailboxReplyThreadingMode.HeaderBased => "header_based",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported mailbox reply threading mode.")
    };

    public static MailboxReplyThreadingMode Parse(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "unknown" => MailboxReplyThreadingMode.Unknown,
        "native" => MailboxReplyThreadingMode.Native,
        "header_based" => MailboxReplyThreadingMode.HeaderBased,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported mailbox reply threading mode.")
    };
}