namespace VirtualCompany.Domain.Enums;

public enum AlertType
{
    Risk = 1,
    Anomaly = 2,
    Opportunity = 3
}

public enum AlertSeverity
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum AlertStatus
{
    Open = 1,
    Acknowledged = 2,
    Resolved = 3,
    Closed = 4
}

public static class AlertTypeValues
{
    private static readonly IReadOnlyDictionary<AlertType, string> Values = new Dictionary<AlertType, string>
    {
        [AlertType.Risk] = "risk",
        [AlertType.Anomaly] = "anomaly",
        [AlertType.Opportunity] = "opportunity"
    };

    private static readonly IReadOnlyDictionary<string, AlertType> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllowedValues { get; } = ReverseValues.Keys.OrderBy(x => x).ToArray();

    public static string ToStorageValue(this AlertType type) =>
        Values.TryGetValue(type, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(type), type, BuildValidationMessage());

    public static AlertType Parse(string value) =>
        TryParse(value, out var type)
            ? type
            : throw new ArgumentOutOfRangeException(nameof(value), value, BuildValidationMessage(value));

    public static bool TryParse(string? value, out AlertType type)
    {
        type = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return ReverseValues.TryGetValue(trimmed, out type) ||
            Enum.TryParse(trimmed, ignoreCase: true, out type) && Values.ContainsKey(type);
    }

    public static string BuildValidationMessage(string? attemptedValue = null) =>
        string.IsNullOrWhiteSpace(attemptedValue)
            ? $"Alert type is required. Allowed values: {string.Join(", ", AllowedValues)}."
            : $"Unsupported alert type value '{attemptedValue}'. Allowed values: {string.Join(", ", AllowedValues)}.";
}

public static class AlertSeverityValues
{
    private static readonly IReadOnlyDictionary<AlertSeverity, string> Values = new Dictionary<AlertSeverity, string>
    {
        [AlertSeverity.Low] = "low",
        [AlertSeverity.Medium] = "medium",
        [AlertSeverity.High] = "high",
        [AlertSeverity.Critical] = "critical"
    };

    private static readonly IReadOnlyDictionary<string, AlertSeverity> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllowedValues { get; } = ReverseValues.Keys.OrderBy(x => x).ToArray();

    public static string ToStorageValue(this AlertSeverity severity) =>
        Values.TryGetValue(severity, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(severity), severity, BuildValidationMessage());

    public static AlertSeverity Parse(string value) =>
        TryParse(value, out var severity)
            ? severity
            : throw new ArgumentOutOfRangeException(nameof(value), value, BuildValidationMessage(value));

    public static bool TryParse(string? value, out AlertSeverity severity)
    {
        severity = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return ReverseValues.TryGetValue(trimmed, out severity) ||
            Enum.TryParse(trimmed, ignoreCase: true, out severity) && Values.ContainsKey(severity);
    }

    public static string BuildValidationMessage(string? attemptedValue = null) =>
        string.IsNullOrWhiteSpace(attemptedValue)
            ? $"Alert severity is required. Allowed values: {string.Join(", ", AllowedValues)}."
            : $"Unsupported alert severity value '{attemptedValue}'. Allowed values: {string.Join(", ", AllowedValues)}.";
}

public static class AlertStatusValues
{
    private static readonly IReadOnlyDictionary<AlertStatus, string> Values = new Dictionary<AlertStatus, string>
    {
        [AlertStatus.Open] = "open",
        [AlertStatus.Acknowledged] = "acknowledged",
        [AlertStatus.Resolved] = "resolved",
        [AlertStatus.Closed] = "closed"
    };

    private static readonly IReadOnlyDictionary<string, AlertStatus> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static AlertStatus DefaultStatus => AlertStatus.Open;
    public static IReadOnlyList<string> AllowedValues { get; } = ReverseValues.Keys.OrderBy(x => x).ToArray();

    public static string ToStorageValue(this AlertStatus status) =>
        Values.TryGetValue(status, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(status), status, BuildValidationMessage());

    public static AlertStatus Parse(string value) =>
        TryParse(value, out var status)
            ? status
            : throw new ArgumentOutOfRangeException(nameof(value), value, BuildValidationMessage(value));

    public static bool TryParse(string? value, out AlertStatus status)
    {
        status = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return ReverseValues.TryGetValue(trimmed, out status) ||
            Enum.TryParse(trimmed, ignoreCase: true, out status) && Values.ContainsKey(status);
    }

    public static bool IsOpenForDeduplication(this AlertStatus status) =>
        status is AlertStatus.Open or AlertStatus.Acknowledged;

    public static string BuildValidationMessage(string? attemptedValue = null) =>
        string.IsNullOrWhiteSpace(attemptedValue)
            ? $"Alert status is required. Allowed values: {string.Join(", ", AllowedValues)}."
            : $"Unsupported alert status value '{attemptedValue}'. Allowed values: {string.Join(", ", AllowedValues)}.";
}
