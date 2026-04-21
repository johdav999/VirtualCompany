namespace VirtualCompany.Domain.Enums;

public static class PaymentTypes
{
    public const string Incoming = "incoming";
    public const string Outgoing = "outgoing";

    private static readonly string[] AllowedTypeValues =
    [
        Incoming,
        Outgoing
    ];

    private static readonly HashSet<string> AllowedTypeSet = new(AllowedTypeValues, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllowedValues => AllowedTypeValues;

    public static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();

    public static bool IsSupported(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        AllowedTypeSet.Contains(Normalize(value));

    public static string BuildCheckConstraintSql(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            throw new ArgumentException("Column name is required.", nameof(columnName));
        }

        var allowedValues = string.Join(", ", AllowedValues.Select(value => $"'{value}'"));
        return $"{columnName} IN ({allowedValues})";
    }
}

public static class PaymentMethods
{
    private static readonly string[] AllowedMethodValues =
    [
        "ach",
        "bank_transfer",
        "card",
        "cash",
        "check",
        "direct_debit"
    ];

    private static readonly HashSet<string> AllowedMethodSet = new(AllowedMethodValues, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllowedValues => AllowedMethodValues;

    public static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();

    public static bool IsSupported(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        AllowedMethodSet.Contains(Normalize(value));
}

public static class PaymentStatuses
{
    public const string Pending = "pending";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";

    private static readonly string[] AllowedStatusValues =
    [
        Pending,
        Completed,
        Failed,
        Cancelled
    ];

    private static readonly HashSet<string> AllowedStatusSet = new(AllowedStatusValues, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllowedValues => AllowedStatusValues;

    public static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();

    public static bool IsSupported(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        AllowedStatusSet.Contains(Normalize(value));
}
