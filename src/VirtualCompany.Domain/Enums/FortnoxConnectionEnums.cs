namespace VirtualCompany.Domain.Enums;

public enum FortnoxConnectionStatus
{
    Pending = 1,
    Connected = 2,
    NeedsReconnect = 3,
    Revoked = 4,
    Error = 5,
    Disconnected = 6
}

public static class FortnoxConnectionStatusValues
{
    public const string Pending = "pending";
    public const string Connected = "connected";
    public const string NeedsReconnect = "needs_reconnect";
    public const string Revoked = "revoked";
    public const string Error = "error";
    public const string Disconnected = "disconnected";

    private static readonly string[] AllowedStorageValues =
    [
        Pending,
        Connected,
        NeedsReconnect,
        Revoked,
        Error,
        Disconnected
    ];

    public static IReadOnlyList<string> AllowedValues => AllowedStorageValues;

    public static string ToStorageValue(this FortnoxConnectionStatus status) =>
        status switch
        {
            FortnoxConnectionStatus.Pending => Pending,
            FortnoxConnectionStatus.Connected => Connected,
            FortnoxConnectionStatus.NeedsReconnect => NeedsReconnect,
            FortnoxConnectionStatus.Revoked => Revoked,
            FortnoxConnectionStatus.Error => Error,
            FortnoxConnectionStatus.Disconnected => Disconnected,
            _ => throw new ArgumentOutOfRangeException(nameof(status), "Unsupported Fortnox connection status.")
        };

    public static FortnoxConnectionStatus Parse(string value) =>
        Normalize(value) switch
        {
            Pending => FortnoxConnectionStatus.Pending,
            Connected => FortnoxConnectionStatus.Connected,
            NeedsReconnect => FortnoxConnectionStatus.NeedsReconnect,
            Revoked => FortnoxConnectionStatus.Revoked,
            Error => FortnoxConnectionStatus.Error,
            Disconnected => FortnoxConnectionStatus.Disconnected,
            _ => throw new ArgumentOutOfRangeException(nameof(value), "Unsupported Fortnox connection status.")
        };

    public static void EnsureSupported(FortnoxConnectionStatus status, string parameterName)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Unsupported Fortnox connection status.");
        }
    }

    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ({string.Join(", ", AllowedValues.Select(value => $"'{value}'"))})";

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().Replace("-", "_").Replace(" ", "_").ToLowerInvariant();
}
