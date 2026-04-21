namespace VirtualCompany.Domain.Enums;

public static class ReconciliationRecordTypes
{
    public const string Payment = "payment";
    public const string BankTransaction = "bank_transaction";
    public const string Invoice = "invoice";
    public const string Bill = "bill";

    private static readonly string[] AllowedTypeValues =
    [
        Payment,
        BankTransaction,
        Invoice,
        Bill
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

    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ({string.Join(", ", AllowedValues.Select(value => $"'{value}'"))})";
}

public static class ReconciliationMatchTypes
{
    public const string Exact = "exact";
    public const string Near = "near";
    public const string RuleBased = "rule_based";
    public const string Manual = "manual";

    private static readonly string[] AllowedTypeValues =
    [
        Exact,
        Near,
        RuleBased,
        Manual
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

    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ({string.Join(", ", AllowedValues.Select(value => $"'{value}'"))})";
}

public static class ReconciliationSuggestionStatuses
{
    public const string Open = "open";
    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
    public const string Superseded = "superseded";

    private static readonly string[] AllowedStatusValues =
    [
        Open,
        Accepted,
        Rejected,
        Superseded
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

    public static bool IsActionable(string? value) =>
        string.Equals(Normalize(value ?? string.Empty), Open, StringComparison.Ordinal);

    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ({string.Join(", ", AllowedValues.Select(value => $"'{value}'"))})";
}
