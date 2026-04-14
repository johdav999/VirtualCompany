namespace VirtualCompany.Domain.Enums;

public enum EscalationStatus
{
    Triggered = 1
}

public static class EscalationStatusValues
{
    private static readonly IReadOnlyDictionary<EscalationStatus, string> Values = new Dictionary<EscalationStatus, string>
    {
        [EscalationStatus.Triggered] = "triggered"
    };

    private static readonly IReadOnlyDictionary<string, EscalationStatus> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static EscalationStatus DefaultStatus => EscalationStatus.Triggered;
    public static IReadOnlyList<string> AllowedValues { get; } = ReverseValues.Keys.OrderBy(x => x).ToArray();

    public static string ToStorageValue(this EscalationStatus status) =>
        Values.TryGetValue(status, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(status), status, BuildValidationMessage());

    public static EscalationStatus Parse(string value) =>
        TryParse(value, out var status)
            ? status
            : throw new ArgumentOutOfRangeException(nameof(value), value, BuildValidationMessage(value));

    public static bool TryParse(string? value, out EscalationStatus status)
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

    public static string BuildValidationMessage(string? attemptedValue = null)
    {
        var allowedValues = string.Join(", ", AllowedValues);
        return string.IsNullOrWhiteSpace(attemptedValue)
            ? $"Escalation status is required. Allowed values: {allowedValues}."
            : $"Unsupported escalation status value '{attemptedValue}'. Allowed values: {allowedValues}.";
    }
}
