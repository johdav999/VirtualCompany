namespace VirtualCompany.Domain.Enums;

public enum CompanySimulationStatus
{
    Running = 1,
    Paused = 2,
    Stopped = 3
}

public static class CompanySimulationStatusValues
{
    public static IReadOnlyList<string> AllowedValues { get; } =
    [
        "running",
        "paused",
        "stopped"
    ];

    public static string ToStorageValue(this CompanySimulationStatus status) =>
        status switch
        {
            CompanySimulationStatus.Running => "running",
            CompanySimulationStatus.Paused => "paused",
            CompanySimulationStatus.Stopped => "stopped",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported company simulation status.")
        };

    public static CompanySimulationStatus Parse(string value)
    {
        if (TryParse(value, out var status))
        {
            return status;
        }

        throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported company simulation status value.");
    }

    public static bool TryParse(string? value, out CompanySimulationStatus status)
    {
        status = CompanySimulationStatus.Running;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "running" => SetStatus(CompanySimulationStatus.Running, out status),
            "paused" => SetStatus(CompanySimulationStatus.Paused, out status),
            "stopped" => SetStatus(CompanySimulationStatus.Stopped, out status),
            _ => Enum.TryParse(value.Trim(), ignoreCase: true, out status)
        };
    }

    private static bool SetStatus(CompanySimulationStatus value, out CompanySimulationStatus status)
    {
        status = value;
        return true;
    }

    public static void EnsureSupported(CompanySimulationStatus status, string? paramName = null)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(paramName ?? nameof(status), status, "Unsupported company simulation status.");
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
