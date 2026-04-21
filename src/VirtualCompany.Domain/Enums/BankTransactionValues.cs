namespace VirtualCompany.Domain.Enums;

public static class BankTransactionReconciliationStatuses
{
    public const string Unreconciled = "unreconciled";
    public const string PartiallyReconciled = "partially_reconciled";
    public const string Reconciled = "reconciled";

    private static readonly string[] AllowedValuesInternal =
    [
        Unreconciled,
        PartiallyReconciled,
        Reconciled
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

        return $"{columnName} IN ('{Unreconciled}', '{PartiallyReconciled}', '{Reconciled}')";
    }

    public static string Resolve(decimal absoluteTransactionAmount, decimal reconciledAmount)
    {
        var normalizedTransactionAmount = decimal.Round(Math.Abs(absoluteTransactionAmount), 2, MidpointRounding.AwayFromZero);
        var normalizedReconciledAmount = decimal.Round(Math.Max(0m, reconciledAmount), 2, MidpointRounding.AwayFromZero);

        if (normalizedReconciledAmount <= 0m)
        {
            return Unreconciled;
        }

        return normalizedReconciledAmount >= normalizedTransactionAmount
            ? Reconciled
            : PartiallyReconciled;
    }
}

public static class BankTransactionMatchingStatuses
{
    public const string Unknown = "unknown";
    public const string Matched = "matched";
    public const string ManuallyClassified = "manually_classified";
    public const string Unmatched = "unmatched";

    private static readonly string[] AllowedValuesInternal =
    [
        Unknown,
        ManuallyClassified,
        Matched,
        Unmatched
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

        return $"{columnName} IN ('{Unknown}', '{Matched}', '{ManuallyClassified}', '{Unmatched}')";
    }

    public static bool AllowsSettlementPosting(string? value)
    {
        var normalized = Normalize(value ?? string.Empty);
        return string.Equals(normalized, Matched, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, ManuallyClassified, StringComparison.OrdinalIgnoreCase);
    }
}

public static class BankTransactionPostingStates
{
    public const string Pending = "pending";
    public const string Posted = "posted";
    public const string SkippedUnmatched = "skipped_unmatched";
    public const string Conflict = "conflict";

    private static readonly string[] AllowedValuesInternal =
    [
        Pending,
        Posted,
        SkippedUnmatched,
        Conflict
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

        return $"{columnName} IN ('{Pending}', '{Posted}', '{SkippedUnmatched}', '{Conflict}')";
    }

    public static string Resolve(string matchingStatus, bool hasLedgerEntry, bool hasConflict) =>
        hasConflict
            ? Conflict
            : hasLedgerEntry
                ? Posted
                : BankTransactionMatchingStatuses.AllowsSettlementPosting(matchingStatus)
                    ? Pending
                    : SkippedUnmatched;

    public static string Resolve(bool hasPaymentLinks, bool hasLedgerEntry, bool hasConflict) =>
        Resolve(
            hasPaymentLinks ? BankTransactionMatchingStatuses.Matched : BankTransactionMatchingStatuses.Unmatched,
            hasLedgerEntry,
            hasConflict);
}