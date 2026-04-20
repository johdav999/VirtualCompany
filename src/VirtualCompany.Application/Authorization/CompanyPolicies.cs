namespace VirtualCompany.Application.Authorization;

public static class CompanyPolicies
{
    public const string AuthenticatedUser = "AuthenticatedUser";
    public const string CompanyMember = "CompanyMember";
    public const string CompanyManager = "CompanyManager";
    public const string AuditReview = "AuditReview";
    public const string CompanyOwnerOrAdmin = "CompanyOwnerOrAdmin";
    public const string FinanceView = "FinanceView";
    public const string FinanceEdit = "FinanceEdit";
    public const string FinanceApproval = "FinanceApproval";
    public const string FinanceSandboxAdmin = "FinanceSandboxAdmin";

    public const string CompanyMembership = CompanyMember;
    public const string CompanyAuditReview = AuditReview;
    public const string CompanyAdmin = CompanyOwnerOrAdmin;
    public const string CompanyFinanceView = FinanceView;
    public const string CompanyFinanceEdit = FinanceEdit;
    public const string CompanyFinanceApproval = FinanceApproval;
    public const string CompanyFinanceSandboxAdmin = FinanceSandboxAdmin;
}