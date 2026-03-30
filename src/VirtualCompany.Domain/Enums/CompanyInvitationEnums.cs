namespace VirtualCompany.Domain.Enums;

public enum CompanyInvitationStatus
{
    Pending = 1,
    Accepted = 2,
    Revoked = 3,
    Expired = 4,
    Cancelled = 5
}

public enum CompanyInvitationDeliveryStatus
{
    Pending = 1,
    Delivered = 2,
    Failed = 3,
    Skipped = 4
}

public static class CompanyInvitationStatusValues
{
    public static string ToStorageValue(this CompanyInvitationStatus status) =>
        status switch
        {
            CompanyInvitationStatus.Pending => "pending",
            CompanyInvitationStatus.Accepted => "accepted",
            CompanyInvitationStatus.Revoked => "revoked",
            CompanyInvitationStatus.Expired => "expired",
            CompanyInvitationStatus.Cancelled => "cancelled",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported company invitation status.")
        };

    public static CompanyInvitationStatus Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Invitation status is required.", nameof(value));
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "pending" => CompanyInvitationStatus.Pending,
            "accepted" => CompanyInvitationStatus.Accepted,
            "revoked" => CompanyInvitationStatus.Revoked,
            "expired" => CompanyInvitationStatus.Expired,
            "cancelled" => CompanyInvitationStatus.Cancelled,
            _ when Enum.TryParse<CompanyInvitationStatus>(value.Trim(), ignoreCase: true, out var legacyStatus) => legacyStatus,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported company invitation status value.")
        };
    }
}

public static class CompanyInvitationDeliveryStatusValues
{
    public static string ToStorageValue(this CompanyInvitationDeliveryStatus status) =>
        status switch
        {
            CompanyInvitationDeliveryStatus.Pending => "pending",
            CompanyInvitationDeliveryStatus.Delivered => "delivered",
            CompanyInvitationDeliveryStatus.Failed => "failed",
            CompanyInvitationDeliveryStatus.Skipped => "skipped",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported company invitation delivery status.")
        };

    public static CompanyInvitationDeliveryStatus Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Invitation delivery status is required.", nameof(value));
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "pending" => CompanyInvitationDeliveryStatus.Pending,
            "delivered" => CompanyInvitationDeliveryStatus.Delivered,
            "failed" => CompanyInvitationDeliveryStatus.Failed,
            "skipped" => CompanyInvitationDeliveryStatus.Skipped,
            _ when Enum.TryParse<CompanyInvitationDeliveryStatus>(value.Trim(), ignoreCase: true, out var legacyStatus) => legacyStatus,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported company invitation delivery status value.")
        };
    }
}
