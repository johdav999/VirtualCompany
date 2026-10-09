using System.Text.Json.Nodes;
using VirtualCompany.Web.Services;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class AgentFinancePresentationTests
{
    [Fact]
    public void Laura_finance_manager_profile_exposes_capabilities_and_route_cards()
    {
        var companyId = Guid.NewGuid();
        var profile = new AgentProfileViewModel
        {
            TemplateId = "laura-finance-agent",
            DisplayName = "Laura",
            RoleName = "Finance Manager",
            Department = "Finance",
            Configuration = new AgentConfigurationViewModel
            {
                WorkflowCapabilities = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["defaults"] = new JsonArray(
                        JsonValue.Create("transaction_categorization_review"),
                        JsonValue.Create("invoice_approval_review"),
                        JsonValue.Create("finance_risk_detection"))
                }
            },
            ToolPermissions = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["allowed"] = new JsonArray(
                    JsonValue.Create("categorize_transaction"),
                    JsonValue.Create("approve_invoice"))
            }
        };

        Assert.True(AgentFinancePresentation.IsFinanceProfile(profile));
        Assert.True(AgentFinancePresentation.IsLauraFinanceManagerProfile(profile));

        var capabilities = AgentFinancePresentation.GetCapabilitySummaries(profile);
        Assert.Contains("Preconfigured Finance Manager coverage", capabilities);
        Assert.Contains("Transaction category governance", capabilities);
        Assert.Contains("Finance policy-aware actions", capabilities);
        Assert.Contains("Audit and reconciliation visibility", capabilities);
        Assert.Contains("Invoice approval workflow oversight", capabilities);
        Assert.Contains("Invoice approval status decisions", capabilities);
        Assert.Contains("Cash risk and liquidity monitoring", capabilities);

        var cards = AgentFinancePresentation.BuildWorkflowCards(companyId);
        Assert.Equal(7, cards.Count);
        Assert.Contains(cards, card => card.Href == FinanceRoutes.WithCompanyContext(FinanceRoutes.Home, companyId));
        Assert.Contains(cards, card => card.Href == FinanceRoutes.WithCompanyContext(FinanceRoutes.Activity, companyId));
        Assert.Contains(cards, card => card.Href == FinanceRoutes.WithCompanyContext(FinanceRoutes.Invoices, companyId));
        Assert.Contains(cards, card => card.Href == FinanceRoutes.WithCompanyContext(FinanceRoutes.CashPosition, companyId));
        Assert.Contains(cards, card => card.Href == FinanceRoutes.WithCompanyContext(FinanceRoutes.MonthlySummary, companyId));
        Assert.Contains(cards, card => card.Href == $"/approvals?companyId={companyId:D}");
        Assert.Contains(cards, card => card.Href == $"/audit?companyId={companyId:D}");
    }
}
