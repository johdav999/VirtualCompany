namespace VirtualCompany.Application.Authorization;

public static class CompanyPolicies
{
    public const string CompanyMembership = "RequireCompanyMembership";
    public const string CompanyManager = "RequireCompanyManager";
    public const string CompanyAdmin = "RequireCompanyAdmin";
}