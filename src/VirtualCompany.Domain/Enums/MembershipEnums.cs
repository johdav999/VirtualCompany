namespace VirtualCompany.Domain.Enums;

public enum CompanyMembershipRole
{
    Owner = 1,
    Admin = 2,
    Manager = 3,
    Employee = 4,
    FinanceApprover = 5,
    SupportSupervisor = 6
}

public enum CompanyMembershipStatus
{
    Pending = 1,
    Active = 2,
    Revoked = 3
}

public static class CompanyMembershipRoleValues
{
    public static string ToStorageValue(this CompanyMembershipRole role) =>
        role switch
        {
            CompanyMembershipRole.Owner => "owner",
            CompanyMembershipRole.Admin => "admin",
            CompanyMembershipRole.Manager => "manager",
            CompanyMembershipRole.Employee => "employee",
            CompanyMembershipRole.FinanceApprover => "finance_approver",
            CompanyMembershipRole.SupportSupervisor => "support_supervisor",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unsupported company membership role.")
        };

    public static CompanyMembershipRole Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Membership role is required.", nameof(value));
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "owner" => CompanyMembershipRole.Owner,
            "admin" => CompanyMembershipRole.Admin,
            "manager" => CompanyMembershipRole.Manager,
            "employee" => CompanyMembershipRole.Employee,
            "finance_approver" => CompanyMembershipRole.FinanceApprover,
            "support_supervisor" => CompanyMembershipRole.SupportSupervisor,
            _ when Enum.TryParse<CompanyMembershipRole>(value.Trim(), ignoreCase: true, out var legacyRole) => legacyRole,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported company membership role value.")
        };
    }
}

public static class CompanyMembershipStatusValues
{
    public static string ToStorageValue(this CompanyMembershipStatus status) =>
        status switch
        {
            CompanyMembershipStatus.Pending => "pending",
            CompanyMembershipStatus.Active => "active",
            CompanyMembershipStatus.Revoked => "revoked",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported company membership status.")
        };

    public static CompanyMembershipStatus Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Membership status is required.", nameof(value));
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "pending" => CompanyMembershipStatus.Pending,
            "active" => CompanyMembershipStatus.Active,
            "revoked" => CompanyMembershipStatus.Revoked,
            _ when Enum.TryParse<CompanyMembershipStatus>(value.Trim(), ignoreCase: true, out var legacyStatus) => legacyStatus,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported company membership status value.")
        };
    }
}