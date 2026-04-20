namespace VirtualCompany.Domain.Enums;

public enum FocusSourceType
{
    Approval = 1,
    Task = 2,
    Anomaly = 3,
    FinanceAlert = 4
}

public static class FocusSourceTypeValues
{
    private static readonly IReadOnlyDictionary<FocusSourceType, string> Values = new Dictionary<FocusSourceType, string>
    {
        [FocusSourceType.Approval] = "approval",
        [FocusSourceType.Task] = "task",
        [FocusSourceType.Anomaly] = "anomaly",
        [FocusSourceType.FinanceAlert] = "finance_alert"
    };

    private static readonly IReadOnlyDictionary<string, FocusSourceType> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllowedValues { get; } = ReverseValues.Keys.OrderBy(x => x).ToArray();

    public static string ToStorageValue(this FocusSourceType sourceType) =>
        Values.TryGetValue(sourceType, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(sourceType), sourceType, "Unsupported focus source type.");

    public static bool TryParse(string? value, out FocusSourceType sourceType)
    {
        sourceType = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return ReverseValues.TryGetValue(trimmed, out sourceType) ||
            Enum.TryParse(trimmed, ignoreCase: true, out sourceType) && Values.ContainsKey(sourceType);
    }
}

public static class FocusActionTypes
{
    public const string Review = "review";
    public const string Resolve = "resolve";
    public const string Investigate = "investigate";
    public const string Open = "open";

    private static readonly HashSet<string> Allowed =
    [
        Review,
        Resolve,
        Investigate,
        Open
    ];

    public static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? Open
            : Allowed.Contains(value.Trim().ToLowerInvariant())
                ? value.Trim().ToLowerInvariant()
                : Open;
}
