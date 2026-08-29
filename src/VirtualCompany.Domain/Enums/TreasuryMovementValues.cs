namespace VirtualCompany.Domain.Enums;

public static class TreasurySourceTypes
{
    public const string AccountTransfer = "account_transfer";
    public const string BankAdjustment = "bank_adjustment";
    public const string CardSettlement = "card_settlement";
    public const string PayoutSettlement = "payout_settlement";

    private static readonly string[] Values = [AccountTransfer, BankAdjustment, CardSettlement, PayoutSettlement];
    private static readonly HashSet<string> ValueSet = new(Values, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllowedValues => Values;
    public static string Normalize(string? value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : value.Trim().Replace('-', '_').Replace(' ', '_').ToLowerInvariant();
    public static bool IsSupported(string? value) => ValueSet.Contains(Normalize(value));
    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ({string.Join(", ", Values.Select(value => $"'{value}'"))})";
}

public static class TreasuryMovementStatuses
{
    public const string NeedsReview = "needs_review";
    public const string AwaitingBankEvidence = "awaiting_bank_evidence";
    public const string InTransit = "in_transit";
    public const string AwaitingApproval = "awaiting_approval";
    public const string ReadyToPost = "ready_to_post";
    public const string Posted = "posted";
    public const string Reversed = "reversed";

    private static readonly string[] Values =
        [NeedsReview, AwaitingBankEvidence, InTransit, AwaitingApproval, ReadyToPost, Posted, Reversed];

    public static IReadOnlyList<string> AllowedValues => Values;
    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ({string.Join(", ", Values.Select(value => $"'{value}'"))})";
}

public static class TreasuryTransferLegRoles
{
    public const string Outbound = "outbound";
    public const string Inbound = "inbound";

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        Outbound => Outbound,
        Inbound => Inbound,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Transfer leg must be outbound or inbound.")
    };
}

public static class BankAdjustmentKinds
{
    public const string BankFee = "bank_fee";
    public const string InterestIncome = "interest_income";
    public const string InterestExpense = "interest_expense";

    private static readonly string[] Values = [BankFee, InterestIncome, InterestExpense];
    private static readonly HashSet<string> ValueSet = new(Values, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllowedValues => Values;
    public static string Normalize(string? value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : value.Trim().Replace('-', '_').Replace(' ', '_').ToLowerInvariant();
    public static bool IsSupported(string? value) => ValueSet.Contains(Normalize(value));
    public static bool IsIncome(string? value) => Normalize(value) == InterestIncome;
    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ({string.Join(", ", Values.Select(value => $"'{value}'"))})";
}

public static class TreasuryEvidenceTypes
{
    public const string BankTransaction = "bank_transaction";
    public const string ProviderSettlement = "provider_settlement";
    public const string MerchantBatch = "merchant_batch";
    public const string CardProcessor = "card_processor";
    public const string OperatorDocument = "operator_document";
}

public static class TreasuryLedgerLinkRoles
{
    public const string Posting = "posting";
    public const string Reversal = "reversal";
}
