namespace VirtualCompany.Domain.Enums;

public static class FinanceAssetFundingBehaviors
{
    public const string Cash = "cash";
    public const string Payable = "payable";

    private static readonly string[] AllowedValuesInternal =
    [
        Cash,
        Payable
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

        return $"{columnName} IN ('{Cash}', '{Payable}')";
    }
}

public static class FinanceAssetStatuses
{
    public const string Active = "active";
    public const string Disposed = "disposed";

    private static readonly string[] AllowedValuesInternal =
    [
        Active,
        Disposed
    ];

    private static readonly HashSet<string> AllowedValueSet = new(AllowedValuesInternal, StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();

    public static bool IsSupported(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        AllowedValueSet.Contains(Normalize(value));

    public static string BuildCheckConstraintSql(string columnName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
        return $"{columnName} IN ('{Active}', '{Disposed}')";
    }
}
