using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class SwedishStatutoryDocumentCandidatePack : IAccountingPolicyPack
{
    public const string Version = "1.2.0";

    public SwedishStatutoryDocumentCandidatePack()
    {
        var prior = new SwedishCandidateAccountingPolicyPack().Definition;
        var capabilities = prior.SupportedCapabilities.Concat(["native_statutory_invoice_issuance"]).Distinct(StringComparer.Ordinal).ToArray();
        var states = new Dictionary<string, string>(prior.CapabilityStates ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase)
        {
            ["native_statutory_invoice_issuance"] = "supported_unvalidated_limited_scope"
        };
        var metadata = new Dictionary<string, string>(prior.PolicyMetadata ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase)
        {
            ["document_specification"] = "swedish-statutory-documents-2026.1",
            ["invoice_policy_hook"] = "supported_unvalidated_limited_scope"
        };

        Definition = prior with
        {
            Version = Version,
            DisplayName = "Sweden domestic VAT and statutory documents candidate",
            ComplianceNotice = "This Swedish candidate is not statutorily validated. Native issuance is limited to the checked-in full-invoice and credit-note engineering specification.",
            InvoicePolicy = new AccountingInvoicePolicyDefinition(true,
            [
                "seller_legal_identity", "seller_address", "seller_vat_identifier", "buyer_legal_identity",
                "buyer_address", "document_number", "issue_date", "supply_date", "accounting_date", "due_date",
                "currency", "line_descriptions", "quantities", "unit_prices", "net_total", "vat_total",
                "gross_total", "payment_terms", "explanatory_text", "original_document_reference_for_credit"
            ],
            ["invoice", "credit_note", "customer_invoice", "customer_credit_note", "supplier_invoice", "supplier_credit_note"]),
            SupportedCapabilities = capabilities,
            PolicyMetadata = metadata,
            CapabilityStates = states
        };
        DefinitionHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Definition)))).ToLowerInvariant();
    }

    public AccountingPolicyPackDefinition Definition { get; }
    public string DefinitionHash { get; }
}
