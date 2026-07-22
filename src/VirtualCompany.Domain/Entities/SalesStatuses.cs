namespace VirtualCompany.Domain.Entities;
public static class SalesStatuses
{
    public const string Active = "active";
    public const string Open = "open";
    public const string Converted = "converted";
    public const string Qualified = "qualified";
    public const string Rejected = "rejected";
    public const string Won = "won";
    public const string Lost = "lost";
    public const string Completed = "completed";
    public const string WaitingForApproval = "waiting_for_approval";
    public const string Linked = "linked";
    public const string Ignored = "ignored";
    public const string Blocked = "blocked";
    public const string Pending = "pending";
    public const string InProgress = "in_progress";
    public const string Approved = "approved";
    public const string Failed = "failed";
    public const string DraftCreated = "draft_created";
    public const string RetryableFailed = "retryable_failed";
    public const string Cancelled = "cancelled";
    public const string Draft = "draft";
    public const string Paused = "paused";
    public const string Stopped = "stopped";
    public const string Bounced = "bounced";
    public const string Delivered = "delivered";
    public const string Deferred = "deferred";
}

internal static class SalesEntityText
{
    public static void EnsureCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }
    }

    public static Guid? NormalizeOptionalId(Guid? value, string name) =>
        value is null ? null : value.Value == Guid.Empty ? throw new ArgumentException($"{name} cannot be empty.", name) : value;

    public static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    public static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    public static DateTime NormalizeUtc(DateTime value, string name) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                : value.ToUniversalTime();
}

