namespace VirtualCompany.Domain.Enums;

public enum FinanceSeedingState
{
    NotSeeded = 1,
    Seeding = 2,
    Seeded = 3,
    Failed = 4,
    PartiallySeeded = Seeding,
    FullySeeded = Seeded
}

public static class FinanceSeedingStateValues
{
    public static IReadOnlyList<string> AllowedValues { get; } =
    [
        "not_seeded",
        "seeding",
        "seeded",
        "failed"
    ];

    public static string ToStorageValue(this FinanceSeedingState state) =>
        state switch
        {
            FinanceSeedingState.NotSeeded => "not_seeded",
            FinanceSeedingState.Seeding => "seeding",
            FinanceSeedingState.Seeded => "seeded",
            FinanceSeedingState.Failed => "failed",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unsupported finance seeding state.")
        };

    public static FinanceSeedingState Parse(string value)
    {
        if (TryParse(value, out var state))
        {
            return state;
        }

        throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported finance seeding state value.");
    }

    public static bool TryParse(string? value, out FinanceSeedingState state)
    {
        state = FinanceSeedingState.NotSeeded;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "not_seeded" => SetState(FinanceSeedingState.NotSeeded, out state),
            "seeding" or "partially_seeded" => SetState(FinanceSeedingState.Seeding, out state),
            "seeded" or "fully_seeded" => SetState(FinanceSeedingState.Seeded, out state),
            "failed" => SetState(FinanceSeedingState.Failed, out state),
            _ => Enum.TryParse(value.Trim(), ignoreCase: true, out state)
        };
    }

    private static bool SetState(FinanceSeedingState value, out FinanceSeedingState state)
    {
        state = value;
        return true;
    }

    public static void EnsureSupported(FinanceSeedingState state, string? paramName = null)
    {
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(paramName ?? nameof(state), state, "Unsupported finance seeding state.");
        }
    }

    public static string BuildCheckConstraintSql(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            throw new ArgumentException("Column name is required.", nameof(columnName));
        }

        var allowedValues = string.Join(", ", AllowedValues.Select(value => $"'{value}'"));
        return $"{columnName} IN ({allowedValues})";
    }
}
