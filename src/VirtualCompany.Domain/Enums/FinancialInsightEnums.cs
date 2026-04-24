namespace VirtualCompany.Domain.Enums;

public enum FinancialCheckSeverity
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum FinanceInsightStatus
{
    Active = 1,
    Resolved = 2
}

public static class FinancialCheckSeverityValues
{
    private static readonly IReadOnlyDictionary<FinancialCheckSeverity, string> Values = new Dictionary<FinancialCheckSeverity, string>
    {
        [FinancialCheckSeverity.Low] = "low",
        [FinancialCheckSeverity.Medium] = "medium",
        [FinancialCheckSeverity.High] = "high",
        [FinancialCheckSeverity.Critical] = "critical"
    };

    private static readonly IReadOnlyDictionary<string, FinancialCheckSeverity> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToStorageValue(this FinancialCheckSeverity severity) =>
        Values.TryGetValue(severity, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(severity), severity, "Unsupported financial check severity.");

    public static FinancialCheckSeverity Parse(string value) =>
        ReverseValues.TryGetValue(value.Trim(), out var severity)
            ? severity
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported financial check severity.");

    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ('low', 'medium', 'high', 'critical')";
}

public static class FinanceInsightStatusValues
{
    private static readonly IReadOnlyDictionary<FinanceInsightStatus, string> Values = new Dictionary<FinanceInsightStatus, string>
    {
        [FinanceInsightStatus.Active] = "active",
        [FinanceInsightStatus.Resolved] = "resolved"
    };

    private static readonly IReadOnlyDictionary<string, FinanceInsightStatus> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToStorageValue(this FinanceInsightStatus status) =>
        Values.TryGetValue(status, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported finance insight status.");

    public static FinanceInsightStatus Parse(string value) =>
        ReverseValues.TryGetValue(value.Trim(), out var status)
            ? status
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported finance insight status.");

    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ('active', 'resolved')";
}