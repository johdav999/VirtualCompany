namespace VirtualCompany.Application.Authorization;

public static class CompanyPolicies
{
    public const string AuthenticatedUser = "AuthenticatedUser";
    public const string CompanyMember = "CompanyMember";
    public const string CompanyManager = "CompanyManager";
    public const string CompanyOwnerOrAdmin = "CompanyOwnerOrAdmin";

    public const string CompanyMembership = CompanyMember;
    public const string CompanyAdmin = CompanyOwnerOrAdmin;
}