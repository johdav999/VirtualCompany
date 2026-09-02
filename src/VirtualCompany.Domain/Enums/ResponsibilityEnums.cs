namespace VirtualCompany.Domain.Enums;

public enum CompanySizeBand { Unspecified = 0, Micro = 1, Small = 2, Medium = 3 }
public enum ResponsibilityArea { CompanyPerformance = 1, CashAndAccounting = 2, Sales = 3, Marketing = 4, CustomerSupport = 5, Compliance = 6 }
public enum ResponsibilityAssignmentKind { Primary = 1, ExecutiveOversight = 2 }
public enum ResponsibilityPresetMode { FillMissing = 1, ReplaceExisting = 2 }

public static class CompanySizeBandValues
{
    public const string Unspecified = "unspecified";
    public static IReadOnlyList<CompanySizeBand> All { get; } = [CompanySizeBand.Micro, CompanySizeBand.Small, CompanySizeBand.Medium];
    public static string ToStorageValue(this CompanySizeBand value) => value switch
    {
        CompanySizeBand.Unspecified => Unspecified,
        CompanySizeBand.Micro => "micro",
        CompanySizeBand.Small => "small",
        CompanySizeBand.Medium => "medium",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported company size.")
    };
    public static CompanySizeBand Parse(string value) => value?.Trim().ToLowerInvariant() switch
    {
        Unspecified => CompanySizeBand.Unspecified,
        "micro" => CompanySizeBand.Micro,
        "small" => CompanySizeBand.Small,
        "medium" => CompanySizeBand.Medium,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported company size.")
    };
}

public static class ResponsibilityAreaValues
{
    public static IReadOnlyList<ResponsibilityArea> All { get; } = Enum.GetValues<ResponsibilityArea>();
    public static string ToStorageValue(this ResponsibilityArea value) => value switch
    {
        ResponsibilityArea.CompanyPerformance => "company_performance",
        ResponsibilityArea.CashAndAccounting => "cash_and_accounting",
        ResponsibilityArea.Sales => "sales",
        ResponsibilityArea.Marketing => "marketing",
        ResponsibilityArea.CustomerSupport => "customer_support",
        ResponsibilityArea.Compliance => "compliance",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported responsibility area.")
    };
    public static ResponsibilityArea Parse(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "company_performance" => ResponsibilityArea.CompanyPerformance,
        "cash_and_accounting" => ResponsibilityArea.CashAndAccounting,
        "sales" => ResponsibilityArea.Sales,
        "marketing" => ResponsibilityArea.Marketing,
        "customer_support" => ResponsibilityArea.CustomerSupport,
        "compliance" => ResponsibilityArea.Compliance,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported responsibility area.")
    };
}

public static class ResponsibilityAssignmentKindValues
{
    public static string ToStorageValue(this ResponsibilityAssignmentKind value) => value switch
    {
        ResponsibilityAssignmentKind.Primary => "primary",
        ResponsibilityAssignmentKind.ExecutiveOversight => "executive_oversight",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported assignment kind.")
    };
    public static ResponsibilityAssignmentKind Parse(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "primary" => ResponsibilityAssignmentKind.Primary,
        "executive_oversight" => ResponsibilityAssignmentKind.ExecutiveOversight,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported assignment kind.")
    };
}
