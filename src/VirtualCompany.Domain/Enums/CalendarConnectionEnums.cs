namespace VirtualCompany.Domain.Enums;

public enum ExternalAccountProvider
{
    Google = 1,
    Microsoft365 = 2
}

public enum ExternalConnectionStatus
{
    Pending = 1,
    Active = 2,
    TokenExpired = 3,
    Revoked = 4,
    Failed = 5,
    Disconnected = 6
}

[Flags]
public enum CalendarCapability
{
    None = 0,
    ReadAvailability = 1 << 0,
    CreateEvents = 1 << 1,
    UpdateEvents = 1 << 2,
    CancelEvents = 1 << 3,
    CreateConferenceLinks = 1 << 4
}

public static class ExternalAccountProviderValues
{
    public const string Google = "google";
    public const string Microsoft365 = "microsoft365";

    public static string ToStorageValue(this ExternalAccountProvider provider) => provider switch
    {
        ExternalAccountProvider.Google => Google,
        ExternalAccountProvider.Microsoft365 => Microsoft365,
        _ => throw new ArgumentOutOfRangeException(nameof(provider), "Unsupported external account provider.")
    };

    public static ExternalAccountProvider Parse(string value) => value?.Trim().ToLowerInvariant() switch
    {
        Google => ExternalAccountProvider.Google,
        Microsoft365 => ExternalAccountProvider.Microsoft365,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Unsupported external account provider.")
    };

    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ('{Google}', '{Microsoft365}')";
}

public static class ExternalConnectionStatusValues
{
    public static string ToStorageValue(this ExternalConnectionStatus status) => status switch
    {
        ExternalConnectionStatus.Pending => "pending",
        ExternalConnectionStatus.Active => "active",
        ExternalConnectionStatus.TokenExpired => "token_expired",
        ExternalConnectionStatus.Revoked => "revoked",
        ExternalConnectionStatus.Failed => "failed",
        ExternalConnectionStatus.Disconnected => "disconnected",
        _ => throw new ArgumentOutOfRangeException(nameof(status), "Unsupported connection status.")
    };

    public static ExternalConnectionStatus Parse(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "pending" => ExternalConnectionStatus.Pending,
        "active" => ExternalConnectionStatus.Active,
        "token_expired" => ExternalConnectionStatus.TokenExpired,
        "revoked" => ExternalConnectionStatus.Revoked,
        "failed" => ExternalConnectionStatus.Failed,
        "disconnected" => ExternalConnectionStatus.Disconnected,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Unsupported connection status.")
    };

    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ('pending', 'active', 'token_expired', 'revoked', 'failed', 'disconnected')";
}
