namespace VirtualCompany.Domain.Enums;

public static class FinanceDocumentPostingStatuses
{
    public const string Draft = "draft";
    public const string Booked = "booked";
    public const string Cancelled = "cancelled";

    private static readonly string[] AllowedValuesInternal =
    [
        Draft,
        Booked,
        Cancelled
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

        return $"{columnName} IN ('{Draft}', '{Booked}', '{Cancelled}')";
    }
}

public static class FinanceDocumentDueStatuses
{
    public const string NotDue = "not_due";
    public const string DueSoon = "due_soon";
    public const string Overdue = "overdue";

    private static readonly string[] AllowedValuesInternal =
    [
        NotDue,
        DueSoon,
        Overdue
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

        return $"{columnName} IN ('{NotDue}', '{DueSoon}', '{Overdue}')";
    }
}

public static class FinanceDocumentProcessingStatuses
{
    public const string None = "none";
    public const string PaymentPending = "payment_pending";
    public const string AuthorizationPending = "authorization_pending";

    private static readonly string[] AllowedValuesInternal =
    [
        None,
        PaymentPending,
        AuthorizationPending
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

        return $"{columnName} IN ('{None}', '{PaymentPending}', '{AuthorizationPending}')";
    }
}

public static class FinanceDocumentKinds
{
    public const string Invoice = "invoice";
    public const string CreditNote = "credit_note";
    public const string SupplierInvoice = "supplier_invoice";
    public const string SupplierCreditNote = "supplier_credit_note";

    private static readonly string[] AllowedValuesInternal =
    [
        Invoice,
        CreditNote,
        SupplierInvoice,
        SupplierCreditNote
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

        return $"{columnName} IN ('{Invoice}', '{CreditNote}', '{SupplierInvoice}', '{SupplierCreditNote}')";
    }
}
