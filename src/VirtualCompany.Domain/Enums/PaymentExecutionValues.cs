namespace VirtualCompany.Domain.Enums;

public static class PaymentExecutionStatuses
{
    public const string Queued = "queued";
    public const string Submitting = "submitting";
    public const string AwaitingAuthorization = "awaiting_authorization";
    public const string ProviderAccepted = "provider_accepted";
    public const string Processing = "processing";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
    public const string ReconciliationRequired = "reconciliation_required";
    public const string ProviderCompleted = "provider_completed";
    public const string Settled = "settled";

    private static readonly string[] Values =
    [
        Queued, Submitting, AwaitingAuthorization, ProviderAccepted, Processing, Rejected,
        Cancelled, ReconciliationRequired, ProviderCompleted, Settled
    ];

    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ({string.Join(", ", Values.Select(value => $"'{value}'"))})";

    public static bool IsTerminal(string value) => value is Rejected or Cancelled or Settled;
}

public static class PaymentExecutionAttemptOperations
{
    public const string Submit = "submit";
    public const string Status = "status";
    public const string Cancel = "cancel";
    public const string Remittance = "remittance";
}

public static class PaymentExecutionAttemptOutcomes
{
    public const string Started = "started";
    public const string Succeeded = "succeeded";
    public const string RetryableFailure = "retryable_failure";
    public const string PermanentFailure = "permanent_failure";
    public const string Ambiguous = "ambiguous";
}

public static class PaymentRemittanceStatuses
{
    public const string Ready = "ready";
    public const string Sending = "sending";
    public const string Accepted = "accepted";
    public const string ReconciliationRequired = "reconciliation_required";
    public const string Failed = "failed";
    public const string Blocked = "blocked";
}
