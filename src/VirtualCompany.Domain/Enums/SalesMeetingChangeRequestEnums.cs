namespace VirtualCompany.Domain.Enums;

public enum SalesMeetingChangeOperation
{
    Reschedule = 1,
    Cancel = 2
}

public static class SalesMeetingChangeOperationValues
{
    public static string ToStorageValue(this SalesMeetingChangeOperation value) => value switch
    {
        SalesMeetingChangeOperation.Reschedule => "reschedule",
        SalesMeetingChangeOperation.Cancel => "cancel",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported sales meeting change operation.")
    };

    public static SalesMeetingChangeOperation Parse(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "reschedule" => SalesMeetingChangeOperation.Reschedule,
        "cancel" => SalesMeetingChangeOperation.Cancel,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported sales meeting change operation.")
    };
}

public enum SalesMeetingChangeRequestStatus
{
    Draft = 1,
    WaitingForApproval = 2,
    Queued = 3,
    Executing = 4,
    Completed = 5,
    Rejected = 6,
    Failed = 7,
    ReconciliationRequired = 8
}

public static class SalesMeetingChangeRequestStatusValues
{
    public static string ToStorageValue(this SalesMeetingChangeRequestStatus value) => value switch
    {
        SalesMeetingChangeRequestStatus.Draft => "draft",
        SalesMeetingChangeRequestStatus.WaitingForApproval => "waiting_for_approval",
        SalesMeetingChangeRequestStatus.Queued => "queued",
        SalesMeetingChangeRequestStatus.Executing => "executing",
        SalesMeetingChangeRequestStatus.Completed => "completed",
        SalesMeetingChangeRequestStatus.Rejected => "rejected",
        SalesMeetingChangeRequestStatus.Failed => "failed",
        SalesMeetingChangeRequestStatus.ReconciliationRequired => "reconciliation_required",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported sales meeting change request status.")
    };

    public static SalesMeetingChangeRequestStatus Parse(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "draft" => SalesMeetingChangeRequestStatus.Draft,
        "waiting_for_approval" => SalesMeetingChangeRequestStatus.WaitingForApproval,
        "queued" => SalesMeetingChangeRequestStatus.Queued,
        "executing" => SalesMeetingChangeRequestStatus.Executing,
        "completed" => SalesMeetingChangeRequestStatus.Completed,
        "rejected" => SalesMeetingChangeRequestStatus.Rejected,
        "failed" => SalesMeetingChangeRequestStatus.Failed,
        "reconciliation_required" => SalesMeetingChangeRequestStatus.ReconciliationRequired,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported sales meeting change request status.")
    };
}
