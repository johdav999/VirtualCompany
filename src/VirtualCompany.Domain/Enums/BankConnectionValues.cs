namespace VirtualCompany.Domain.Enums;

public static class BankConnectionStatuses
{
    public const string PendingConsent = "pending_consent";
    public const string Active = "active";
    public const string AttentionRequired = "attention_required";
    public const string Suspended = "suspended";
    public const string Revoked = "revoked";
    public const string Disconnected = "disconnected";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        PendingConsent, Active, AttentionRequired, Suspended, Revoked, Disconnected
    };
}

public static class BankConsentStatuses
{
    public const string Active = "active";
    public const string Expired = "expired";
    public const string Revoked = "revoked";
    public const string Superseded = "superseded";
}

public static class BankConnectionReasonCodes
{
    public const string MissingConsent = "missing_consent";
    public const string ExpiredConsent = "expired_consent";
    public const string ScopeLoss = "scope_loss";
    public const string OwnershipMismatch = "account_ownership_mismatch";
    public const string ProviderOutage = "provider_outage";
    public const string ReconciliationRequired = "reconciliation_required_setup";
    public const string Suspended = "connection_suspended";
    public const string Revoked = "consent_revoked";
    public const string Disconnected = "connection_disconnected";
    public const string ProviderNotConfigured = "provider_not_configured";
    public const string CallbackReplay = "callback_replay";
    public const string CallbackStateInvalid = "callback_state_invalid";
    public const string ConcurrencyConflict = "concurrency_conflict";
}

public static class BankAccountOwnershipStatuses
{
    public const string Verified = "verified";
    public const string Unverified = "unverified";
    public const string Mismatch = "mismatch";
}

public static class BankConnectionHealthStatuses
{
    public const string Unknown = "unknown";
    public const string Healthy = "healthy";
    public const string Degraded = "degraded";
    public const string Outage = "outage";
}

