namespace VirtualCompany.Shared;

public static class FinancePermissions
{
    public const string View = "finance.view";
    public const string Edit = "finance.edit";
    public const string Approve = "finance.approve";
    public const string SandboxAdmin = "finance.sandbox_admin";
}

public static class FinanceAccess
{
    private static readonly HashSet<string> FinanceViewRoleValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "owner",
        "admin",
        "tester",
        "manager",
        "finance_approver"
    };

    private static readonly HashSet<string> FinanceSandboxAdminRoleValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "owner",
        "admin",
        "tester"
    };

    private static readonly HashSet<string> FinanceTransactionEditRoleValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "owner",
        "admin",
        "manager"
    };

    private static readonly HashSet<string> FinanceInvoiceApprovalRoleValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "owner",
        "admin",
        "manager",
        "finance_approver"
    };

    private static readonly HashSet<string> FinanceSimulationManagerRoleValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "owner",
        "admin",
        "manager"
    };

    public static IReadOnlyCollection<string> ViewRoles => FinanceViewRoleValues;
    public static IReadOnlyCollection<string> SandboxAdminRoles => FinanceSandboxAdminRoleValues;
    public static IReadOnlyCollection<string> EditRoles => FinanceTransactionEditRoleValues;
    public static IReadOnlyCollection<string> InvoiceApprovalRoles => FinanceInvoiceApprovalRoleValues;

    public static bool CanView(string? membershipRole) =>
        !string.IsNullOrWhiteSpace(membershipRole) &&
        FinanceViewRoleValues.Contains(membershipRole.Trim());

    public static bool CanEdit(string? membershipRole) =>
        CanEditTransactionCategory(membershipRole);

    public static bool CanAccessSandboxAdmin(string? membershipRole) =>
        !string.IsNullOrWhiteSpace(membershipRole) &&
        FinanceSandboxAdminRoleValues.Contains(membershipRole.Trim());

    public static bool CanManageSimulation(string? membershipRole) =>
        !string.IsNullOrWhiteSpace(membershipRole) &&
        FinanceSimulationManagerRoleValues.Contains(membershipRole.Trim());


    public static bool CanEditTransactionCategory(string? membershipRole) =>
        !string.IsNullOrWhiteSpace(membershipRole) &&
        FinanceTransactionEditRoleValues.Contains(membershipRole.Trim());

    public static bool CanApproveInvoices(string? membershipRole) =>
        !string.IsNullOrWhiteSpace(membershipRole) &&
        FinanceInvoiceApprovalRoleValues.Contains(membershipRole.Trim());

    public static bool CanManagePolicies(string? membershipRole) =>
        CanEdit(membershipRole);
}

public static class FinanceTransactionCategories
{
    private static readonly string[] AllowedCategoryValues =
    [
        "accounts_payable",
        "accounts_receivable",
        "bank_fees",
        "chargeback",
        "consulting",
        "contractor",
        "customer_payment",
        "equipment",
        "hardware",
        "insurance",
        "interest_income",
        "marketing",
        "office_supplies",
        "other_income",
        "payroll",
        "refund",
        "rent",
        "services_revenue",
        "software",
        "software_subscriptions",
        "subscriptions",
        "tax",
        "taxes",
        "travel",
        "uncategorized",
        "utilities"
    ];

    private static readonly HashSet<string> AllowedCategorySet = new(AllowedCategoryValues, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> AllowedValues => AllowedCategoryValues;

    public static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();

    public static bool IsSupported(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        AllowedCategorySet.Contains(Normalize(value));
}

public static class FinanceInvoiceApprovalStatuses
{
    private static readonly string[] EditableStatusValues =
    [
        "open",
        "pending_approval",
        "approved",
        "rejected",
        "paid",
        "void"
    ];

    private static readonly HashSet<string> EditableStatusSet = new(EditableStatusValues, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> EditableValues => EditableStatusValues;

    public static IReadOnlyList<string> GetEditableValues(string? currentStatus)
    {
        var normalizedCurrent = Normalize(currentStatus ?? string.Empty);
        return normalizedCurrent switch
        {
            "open" => ["open", "pending_approval", "approved", "rejected"],
            "pending" => ["pending_approval", "approved", "rejected"],
            "pending_approval" => ["pending_approval", "approved", "rejected"],
            "approved" => ["approved", "paid", "void"],
            "rejected" => ["rejected", "open", "void"],
            "paid" => ["paid"],
            "void" => ["void"],
            _ => EditableStatusValues
        };
    }

    public static bool IsTransitionSupported(string? currentStatus, string? nextStatus)
    {
        var normalizedNext = Normalize(nextStatus ?? string.Empty);
        return GetEditableValues(currentStatus)
            .Contains(normalizedNext, StringComparer.OrdinalIgnoreCase);
    }

    public static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();

    public static bool IsSupported(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        EditableStatusSet.Contains(Normalize(value));
}