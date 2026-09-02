namespace VirtualCompany.Domain.Enums;

public enum FinanceAutonomyBudgetReservationStatus
{
    Reserved = 1,
    Reconciled = 2,
    Released = 3
}

public enum FinanceAutonomyCircuitStatus
{
    Closed = 1,
    Open = 2
}

public enum FinanceAutonomyBudgetAlertStatus
{
    Open = 1,
    Resolved = 2
}

public static class FinanceAutonomyBudgetEnumValues
{
    public static string ToStorageValue(this FinanceAutonomyBudgetReservationStatus value) => value switch
    {
        FinanceAutonomyBudgetReservationStatus.Reserved => "reserved",
        FinanceAutonomyBudgetReservationStatus.Reconciled => "reconciled",
        FinanceAutonomyBudgetReservationStatus.Released => "released",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static FinanceAutonomyBudgetReservationStatus ParseReservationStatus(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "reserved" => FinanceAutonomyBudgetReservationStatus.Reserved,
        "reconciled" => FinanceAutonomyBudgetReservationStatus.Reconciled,
        "released" => FinanceAutonomyBudgetReservationStatus.Released,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static string ToStorageValue(this FinanceAutonomyCircuitStatus value) => value switch
    {
        FinanceAutonomyCircuitStatus.Closed => "closed",
        FinanceAutonomyCircuitStatus.Open => "open",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static FinanceAutonomyCircuitStatus ParseCircuitStatus(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "closed" => FinanceAutonomyCircuitStatus.Closed,
        "open" => FinanceAutonomyCircuitStatus.Open,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static string ToStorageValue(this FinanceAutonomyBudgetAlertStatus value) => value switch
    {
        FinanceAutonomyBudgetAlertStatus.Open => "open",
        FinanceAutonomyBudgetAlertStatus.Resolved => "resolved",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static FinanceAutonomyBudgetAlertStatus ParseAlertStatus(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "open" => FinanceAutonomyBudgetAlertStatus.Open,
        "resolved" => FinanceAutonomyBudgetAlertStatus.Resolved,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}
