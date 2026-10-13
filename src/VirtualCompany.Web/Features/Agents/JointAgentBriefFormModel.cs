using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Features.Agents;

public sealed class JointAgentBriefFormModel
{
    public string EmailAuthentication { get; set; } = "oauth_user_mailbox";
    public string PrimaryOwner { get; set; } = "shared_operations";
    public string ReviewMode { get; set; } = "human_review_required";
    public bool FinanceMailbox { get; set; }
    public bool SalesMailbox { get; set; }
    public bool SupportMailbox { get; set; }
    public bool WebContactForms { get; set; }
    public bool SalesLeadForms { get; set; }
    public bool SupportWebRequests { get; set; }
    public bool CompanyPolicies { get; set; } = true;
    public bool ProductCatalog { get; set; } = true;
    public bool KnowledgeDocuments { get; set; } = true;
    public bool FortnoxFinance { get; set; }
    public bool CustomerRecords { get; set; }
    public bool OrderAndInvoiceRecords { get; set; }

    public static JointAgentBriefFormModel ForAgent(AgentOperatingProfileViewModel profile)
    {
        var department = profile.Department?.Trim().ToLowerInvariant() ?? string.Empty;
        var role = $"{profile.RoleName} {profile.DisplayName}".Trim().ToLowerInvariant();

        if (department.Contains("finance", StringComparison.OrdinalIgnoreCase) ||
            role.Contains("finance", StringComparison.OrdinalIgnoreCase) ||
            role.Contains("laura", StringComparison.OrdinalIgnoreCase))
        {
            return new JointAgentBriefFormModel
            {
                PrimaryOwner = "finance",
                FinanceMailbox = true,
                CompanyPolicies = true,
                ProductCatalog = true,
                KnowledgeDocuments = true,
                FortnoxFinance = true,
                CustomerRecords = true,
                OrderAndInvoiceRecords = true,
                ReviewMode = "human_review_required"
            };
        }

        if (department.Contains("sales", StringComparison.OrdinalIgnoreCase) ||
            role.Contains("sales", StringComparison.OrdinalIgnoreCase))
        {
            return new JointAgentBriefFormModel
            {
                PrimaryOwner = "sales",
                SalesMailbox = true,
                WebContactForms = true,
                SalesLeadForms = true,
                CompanyPolicies = true,
                ProductCatalog = true,
                KnowledgeDocuments = true,
                CustomerRecords = true,
                OrderAndInvoiceRecords = true,
                ReviewMode = "human_review_required"
            };
        }

        if (department.Contains("support", StringComparison.OrdinalIgnoreCase) ||
            department.Contains("customer", StringComparison.OrdinalIgnoreCase) ||
            role.Contains("support", StringComparison.OrdinalIgnoreCase) ||
            role.Contains("ben", StringComparison.OrdinalIgnoreCase))
        {
            return new JointAgentBriefFormModel
            {
                PrimaryOwner = "customer_support",
                SupportMailbox = true,
                WebContactForms = true,
                SupportWebRequests = true,
                CompanyPolicies = true,
                ProductCatalog = true,
                KnowledgeDocuments = true,
                CustomerRecords = true,
                OrderAndInvoiceRecords = true,
                ReviewMode = "human_review_required"
            };
        }

        return new JointAgentBriefFormModel();
    }

    public object ToDataScopesPayload(AgentOperatingProfileViewModel profile) =>
        new
        {
            version = "joint-agent-brief-v1",
            agentId = profile.Id,
            department = profile.Department,
            primaryOwner = PrimaryOwner,
            reviewMode = ReviewMode,
            sourceAccess = new
            {
                email = new
                {
                    financeMailbox = FinanceMailbox,
                    salesMailbox = SalesMailbox,
                    supportMailbox = SupportMailbox
                },
                webEnquiries = new
                {
                    websiteContactForms = WebContactForms,
                    salesLeadForms = SalesLeadForms,
                    supportWebRequests = SupportWebRequests
                },
                documents = new
                {
                    companyPolicies = CompanyPolicies,
                    productCatalog = ProductCatalog,
                    uploadedKnowledgeDocuments = KnowledgeDocuments
                },
                businessSystems = new
                {
                    fortnoxFinance = FortnoxFinance,
                    customerRecords = CustomerRecords,
                    orderAndInvoiceRecords = OrderAndInvoiceRecords
                }
            }
        };

    public object ToToolPermissionsPayload(AgentOperatingProfileViewModel profile) =>
        new
        {
            version = "joint-agent-brief-v1",
            agentId = profile.Id,
            emailAuthentication = EmailAuthentication,
            canReadAuthenticatedEmail = FinanceMailbox || SalesMailbox || SupportMailbox,
            canReadWebEnquiries = WebContactForms || SalesLeadForms || SupportWebRequests,
            canReadDocuments = CompanyPolicies || ProductCatalog || KnowledgeDocuments,
            canReadFinanceSystems = FortnoxFinance,
            canReadCustomerRecords = CustomerRecords,
            canReadOrderAndInvoiceRecords = OrderAndInvoiceRecords,
            secretsPolicy = "store_credentials_in_provider_settings_or_secret_store",
            humanReviewRequired = string.Equals(ReviewMode, "human_review_required", StringComparison.OrdinalIgnoreCase)
        };
}

