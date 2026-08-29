namespace VirtualCompany.Domain.Enums;

public static class AdvancedReconciliationGroupStatuses
{
    public const string Proposed = "proposed";
    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
    public const string Reversed = "reversed";
    public const string Conflict = "conflict";

    private static readonly string[] Values = [Proposed, Accepted, Rejected, Reversed, Conflict];
    private static readonly HashSet<string> ValueSet = new(Values, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllowedValues => Values;
    public static string Normalize(string? value) => NormalizeValue(value);
    public static bool IsSupported(string? value) => ValueSet.Contains(Normalize(value));
    public static bool IsActionable(string? value) => Normalize(value) == Proposed;
    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ({string.Join(", ", Values.Select(value => $"'{value}'"))})";

    private static string NormalizeValue(string? value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();
}

public static class AdvancedReconciliationNodeTypes
{
    public const string BankTransaction = "bank_transaction";
    public const string Payment = "payment";
    public const string Invoice = "invoice";
    public const string Bill = "bill";
    public const string Adjustment = "adjustment";
    public const string Residual = "residual";

    private static readonly string[] Values = [BankTransaction, Payment, Invoice, Bill, Adjustment, Residual];
    private static readonly HashSet<string> ValueSet = new(Values, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllowedValues => Values;
    public static string Normalize(string? value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();
    public static bool IsSupported(string? value) => ValueSet.Contains(Normalize(value));
    public static bool IsRecordBacked(string? value) => Normalize(value) is BankTransaction or Payment or Invoice or Bill;
    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ({string.Join(", ", Values.Select(value => $"'{value}'"))})";
}

public static class AdvancedReconciliationEdgeTypes
{
    public const string BankPayment = "bank_payment";
    public const string PaymentDocument = "payment_document";
    public const string BankAdjustment = "bank_adjustment";

    private static readonly string[] Values = [BankPayment, PaymentDocument, BankAdjustment];
    private static readonly HashSet<string> ValueSet = new(Values, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllowedValues => Values;
    public static string Normalize(string? value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();
    public static bool IsSupported(string? value) => ValueSet.Contains(Normalize(value));
    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ({string.Join(", ", Values.Select(value => $"'{value}'"))})";
}

public static class AdvancedReconciliationDirections
{
    public const string Incoming = "incoming";
    public const string Outgoing = "outgoing";

    public static string Normalize(string? value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : value.Trim().ToLowerInvariant();
    public static bool IsSupported(string? value) => Normalize(value) is Incoming or Outgoing;
}

public static class AdvancedReconciliationResultOutcomes
{
    public const string Accepted = "accepted";
    public const string Reversal = "reversal";
}

