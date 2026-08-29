namespace VirtualCompany.Domain.Enums;

public static class PaymentBatchStatuses
{
    public const string Draft = "draft";
    public const string Validated = "validated";
    public const string AwaitingApproval = "awaiting_approval";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";

    private static readonly string[] Values =
        [Draft, Validated, AwaitingApproval, Approved, Rejected, Cancelled];

    public static IReadOnlyList<string> AllowedValues => Values;
    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ({string.Join(", ", Values.Select(value => $"'{value}'"))})";
}

public static class PaymentBatchObligationTypes
{
    public const string SupplierPaymentProposal = "supplier_payment_proposal";
    public const string CustomerRefund = "customer_refund";

    private static readonly string[] Values = [SupplierPaymentProposal, CustomerRefund];
    private static readonly HashSet<string> ValueSet = new(Values, StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string? value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : value.Trim().Replace('-', '_').Replace(' ', '_').ToLowerInvariant();
    public static bool IsSupported(string? value) => ValueSet.Contains(Normalize(value));
    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ({string.Join(", ", Values.Select(value => $"'{value}'"))})";
}

public static class PaymentRails
{
    public const string Bankgiro = "bankgiro";
    public const string Plusgiro = "plusgiro";
    public const string SepaCreditTransfer = "sepa_credit_transfer";
    public const string RefundOriginalMethod = "refund_original_method";

    private static readonly string[] Values =
        [Bankgiro, Plusgiro, SepaCreditTransfer, RefundOriginalMethod];
    private static readonly HashSet<string> ValueSet = new(Values, StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string? value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : value.Trim().Replace('-', '_').Replace(' ', '_').ToLowerInvariant();
    public static bool IsSupported(string? value) => ValueSet.Contains(Normalize(value));
    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ({string.Join(", ", Values.Select(value => $"'{value}'"))})";
}

public static class PaymentBeneficiaryVerificationStatuses
{
    public const string Verified = "verified";
    public const string Superseded = "superseded";
    public const string Revoked = "revoked";

    private static readonly string[] Values = [Verified, Superseded, Revoked];
    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ({string.Join(", ", Values.Select(value => $"'{value}'"))})";
}

public static class PaymentInstructionStatuses
{
    public const string Draft = "draft";
    public const string Approved = "approved";
    public const string Superseded = "superseded";

    private static readonly string[] Values = [Draft, Approved, Superseded];
    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ({string.Join(", ", Values.Select(value => $"'{value}'"))})";
}

public static class PaymentBatchApprovalBindingStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
    public const string Stale = "stale";

    private static readonly string[] Values = [Pending, Approved, Rejected, Cancelled, Stale];
    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ({string.Join(", ", Values.Select(value => $"'{value}'"))})";
}

public static class PaymentBatchOperationTypes
{
    public const string Create = "create";
    public const string AddObligation = "add_obligation";
    public const string RemoveObligation = "remove_obligation";
    public const string Validate = "validate";
    public const string Submit = "submit";
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Cancel = "cancel";
    public const string Regenerate = "regenerate";
}

public static class PaymentBatchValidationSeverities
{
    public const string Error = "error";
    public const string Warning = "warning";
}
