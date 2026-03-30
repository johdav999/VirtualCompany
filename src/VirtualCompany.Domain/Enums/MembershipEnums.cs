namespace VirtualCompany.Domain.Enums;

public enum CompanyMembershipRole
{
    Owner = 1,
    Admin = 2,
    Manager = 3,
    Employee = 4
}

public enum CompanyMembershipStatus
{
    Pending = 1,
    Active = 2,
    Revoked = 3
}