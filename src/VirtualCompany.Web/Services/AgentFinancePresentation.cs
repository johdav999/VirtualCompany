using System.Text.Json.Nodes;

namespace VirtualCompany.Web.Services;

public sealed record AgentFinanceWorkflowCard(
    string Title,
    string Description,
    string Href);

public static class AgentFinancePresentation
{
    public static bool IsFinanceProfile(AgentProfileViewModel profile) =>
        string.Equals(profile.Department, "Finance", StringComparison.OrdinalIgnoreCase) ||
        profile.RoleName.Contains("Finance", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(profile.DisplayName, "Laura", StringComparison.OrdinalIgnoreCase);

    public static bool IsLauraFinanceManagerProfile(AgentProfileViewModel profile) =>
        string.Equals(profile.TemplateId, "laura-finance-agent", StringComparison.OrdinalIgnoreCase) ||
        (string.Equals(profile.DisplayName, "Laura", StringComparison.OrdinalIgnoreCase) &&
         profile.RoleName.Contains("Finance Manager", StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> GetCapabilitySummaries(AgentProfileViewModel profile)
    {
        var capabilities = new List<string>();
        var defaults = TryReadStringArray(profile.Configuration.WorkflowCapabilities, "defaults");
        var allowedTools = TryReadStringArray(profile.ToolPermissions, "allowed");

        if (IsLauraFinanceManagerProfile(profile)) capabilities.Add("Preconfigured Finance Manager coverage");
        if (allowedTools.Contains("categorize_transaction", StringComparer.OrdinalIgnoreCase) ||
            allowedTools.Contains("approve_invoice", StringComparer.OrdinalIgnoreCase))
        {
            capabilities.Add("Finance policy-aware actions");
        }

        if (defaults.Contains("transaction_categorization_review", StringComparer.OrdinalIgnoreCase)) capabilities.Add("Transaction category governance");
        if (defaults.Contains("invoice_approval_review", StringComparer.OrdinalIgnoreCase)) capabilities.Add("Invoice approval workflow oversight");
        if (allowedTools.Contains("categorize_transaction", StringComparer.OrdinalIgnoreCase)) capabilities.Add("Transaction categorization actions");
        if (allowedTools.Contains("approve_invoice", StringComparer.OrdinalIgnoreCase) ||
            allowedTools.Contains("recommend_invoice_approval_decision", StringComparer.OrdinalIgnoreCase) ||
            defaults.Contains("invoice_approval_review", StringComparer.OrdinalIgnoreCase))
        {
            capabilities.Add("Invoice approval status decisions");
        }

        if (defaults.Contains("cash_balance_review", StringComparer.OrdinalIgnoreCase) ||
            defaults.Contains("finance_risk_detection", StringComparer.OrdinalIgnoreCase))
        {
            capabilities.Add("Cash risk and liquidity monitoring");
            capabilities.Add("Audit and reconciliation visibility");
        }

        if (capabilities.Count == 0)
        {
            capabilities.Add("Finance workflow ownership");
            capabilities.Add("Invoice review coordination");
            capabilities.Add("Transaction categorization review");
            capabilities.Add("Finance policy-aware actions");
            capabilities.Add("Audit and reconciliation visibility");
        }

        return capabilities
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<AgentFinanceWorkflowCard> BuildWorkflowCards(Guid? companyId) =>
    [
        new("Finance workspace", "Open the tenant-scoped finance workspace and summary routes.", FinanceRoutes.WithCompanyContext(FinanceRoutes.Home, companyId)),
        new("Transactions", "Open transaction detail and category editing for the active company.", FinanceRoutes.WithCompanyContext(FinanceRoutes.Transactions, companyId)),
        new("Invoices", "Open invoice review and approval status workflows.", FinanceRoutes.WithCompanyContext(FinanceRoutes.Invoices, companyId)),
        new("Approvals", "Open approval requests linked to finance review workflows.", companyId is Guid resolvedCompanyId ? $"/approvals?companyId={resolvedCompanyId:D}" : "/approvals"),
        new("Audit trail", "Review audit and reconciliation evidence tied to finance actions.", companyId is Guid auditCompanyId ? $"/audit?companyId={auditCompanyId:D}" : "/audit"),
        new("Cash position", "Review liquidity signals and runway warnings.", FinanceRoutes.WithCompanyContext(FinanceRoutes.CashPosition, companyId)),
        new("Monthly summary", "Open the finance summary page for current reporting context.", FinanceRoutes.WithCompanyContext(FinanceRoutes.MonthlySummary, companyId))
    ];

    private static IReadOnlyList<string> TryReadStringArray(IReadOnlyDictionary<string, JsonNode?> payload, string key) =>
        payload.TryGetValue(key, out var node) && node is JsonArray array
            ? array
                .Select(item => item is JsonValue value && value.TryGetValue<string>(out var text) ? text : null)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Select(text => text!)
                .ToArray()
            : [];
}
