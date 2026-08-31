namespace VirtualCompany.Domain.Enums;

public static class ExchangeRateSourceKinds
{
    public const string Manual = "manual";
    public const string Provider = "provider";
}

public static class ExchangeRateSetStatuses
{
    public const string PendingReview = "pending_review";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}

public static class ExchangeRateQuotationConventions
{
    // A rate of 11 for SEK/EUR means one EUR equals eleven SEK.
    public const string BaseCurrencyPerQuoteCurrency = "base_per_quote";
    // A rate of 0.09 for SEK/EUR means one SEK equals 0.09 EUR.
    public const string QuoteCurrencyPerBaseCurrency = "quote_per_base";
}

public static class ExchangeRateLookupPurposes
{
    public const string TransactionDate = "transaction_date";
    public const string SettlementDate = "settlement_date";
    public const string PeriodEnd = "period_end";

    public static bool IsSupported(string value) => value is TransactionDate or SettlementDate or PeriodEnd;
}

public static class ExchangeRateDecisionStatuses
{
    public const string Ready = "ready";
    public const string Blocked = "blocked";
    public const string ReviewRequired = "review_required";
}

public static class ExchangeRateReasonCodes
{
    public const string None = "none";
    public const string IdentityConversion = "identity_conversion";
    public const string MissingAccountingConfiguration = "missing_accounting_configuration";
    public const string UnsupportedCurrency = "unsupported_currency";
    public const string MissingRate = "missing_rate";
    public const string StaleRate = "stale_rate";
    public const string AmbiguousRate = "ambiguous_rate";
    public const string PendingApproval = "pending_approval";
    public const string InvalidRate = "invalid_rate";
    public const string ImportConflict = "import_conflict";
    public const string CorrectionRequired = "correction_required";
    public const string CorrectionMismatch = "correction_mismatch";
    public const string ConcurrencyConflict = "concurrency_conflict";
    public const string ProviderUnavailable = "provider_unavailable";
    public const string ProviderFailure = "provider_failure";
    public const string ProviderPayloadInvalid = "provider_payload_invalid";
    public const string RefreshQueued = "refresh_queued";
}

public static class ExchangeRateRefreshJobStatuses
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string RetryScheduled = "retry_scheduled";
    public const string Completed = "completed";
    public const string Failed = "failed";
}
