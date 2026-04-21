namespace VirtualCompany.Domain.Enums;

public static class FinanceSettlementStatuses
{
    public const string Unpaid = "unpaid";
    public const string PartiallyPaid = "partially_paid";
    public const string Paid = "paid";

    private static readonly string[] AllowedValuesInternal =
    [
        Unpaid,
        PartiallyPaid,
        Paid
    ];

    private static readonly HashSet<string> AllowedValueSet = new(AllowedValuesInternal, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllowedValues => AllowedValuesInternal;

    public static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();

    public static bool IsSupported(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        AllowedValueSet.Contains(Normalize(value));

    public static string BuildCheckConstraintSql(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            throw new ArgumentException("Column name is required.", nameof(columnName));
        }

        return $"{columnName} IN ('{Unpaid}', '{PartiallyPaid}', '{Paid}')";
    }
}