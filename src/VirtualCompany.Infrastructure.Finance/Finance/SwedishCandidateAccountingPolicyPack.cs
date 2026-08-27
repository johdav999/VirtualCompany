using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class SwedishCandidateAccountingPolicyPack : IAccountingPolicyPack
{
    public const string ChartTemplateKey = "swedish-domestic-vat-candidate-v2";
    public const string DomesticSales25RuleKey = "se_domestic_sales_25";
    public const string DomesticPurchase25RuleKey = "se_domestic_purchase_25_full_recovery";

    public SwedishCandidateAccountingPolicyPack()
    {
        Definition = new AccountingPolicyPackDefinition(
            AccountingPolicyPackDefaults.SwedishCandidatePackKey,
            AccountingPolicyPackDefaults.SwedishCandidateVersion,
            "Sweden statutory foundation candidate",
            CountryOrRegion: "SE",
            IsCountryNeutral: false,
            IsStatutoryComplianceValidated: false,
            "This Swedish candidate has not been validated by a qualified reviewer. It supports only the explicitly sourced domestic 25% VAT launch cases and a human-filing VAT return package; authority submission, statutory exports, and legal certification remain unavailable.",
            ChartTemplates:
            [
                new AccountingChartTemplateDefinition(
                    ChartTemplateKey,
                    "Swedish bookkeeping foundation (unvalidated candidate)",
                    [
                        new("1930", "Business bank account", "asset", "debit", AccountingAccountRoleKeys.Bank),
                        new("1510", "Accounts receivable", "asset", "debit", AccountingAccountRoleKeys.AccountsReceivable),
                        new("2440", "Accounts payable", "liability", "credit", AccountingAccountRoleKeys.AccountsPayable),
                        new("2081", "Share capital", "equity", "credit", "equity"),
                        new("2611", "Output VAT on sales in Sweden, 25%", "liability", "credit", AccountingAccountRoleKeys.TaxOutput25),
                        new("2641", "Input VAT charged", "asset", "debit", AccountingAccountRoleKeys.TaxInput),
                        new("3001", "Sales in Sweden, 25% VAT", "revenue", "credit", "revenue"),
                        new("4000", "Purchases of goods for resale", "expense", "debit", "operating_expense"),
                        new("2999", "Observation account", "liability", "credit", AccountingAccountRoleKeys.Suspense),
                        new("3740", "Rounding differences", "expense", "debit", AccountingAccountRoleKeys.RoundingDifference),
                        new("6570", "Bank charges", "expense", "debit", AccountingAccountRoleKeys.BankFee),
                        new("7960", "Exchange losses on operating receivables and liabilities", "expense", "debit", AccountingAccountRoleKeys.ExchangeLoss),
                        new("3960", "Exchange gains on operating receivables and liabilities", "revenue", "credit", AccountingAccountRoleKeys.ExchangeGain)
                    ])
            ],
            AccountRoles:
            [
                new(AccountingAccountRoleKeys.Bank, "Bank account", true, true),
                new(AccountingAccountRoleKeys.AccountsReceivable, "Accounts receivable", true, true),
                new(AccountingAccountRoleKeys.AccountsPayable, "Accounts payable", true, true),
                new("equity", "Equity", true, true),
                new("revenue", "Revenue", true, false),
                new("operating_expense", "Operating expense", true, false),
                new(AccountingAccountRoleKeys.TaxOutput25, "Output VAT 25%", true, true),
                new(AccountingAccountRoleKeys.TaxInput, "Deductible input VAT", true, true),
                new(AccountingAccountRoleKeys.Suspense, "Unresolved accounting suspense", true, true),
                new(AccountingAccountRoleKeys.RoundingDifference, "Rounding differences", true, false),
                new(AccountingAccountRoleKeys.BankFee, "Bank charges", true, false),
                new(AccountingAccountRoleKeys.ExchangeGain, "Exchange gains", true, false),
                new(AccountingAccountRoleKeys.ExchangeLoss, "Exchange losses", true, false)
            ],
            TaxRules:
            [
                new AccountingTaxRuleDefinition(
                    DomesticSales25RuleKey,
                    "Domestic sales, standard VAT 25%",
                    new DateOnly(2026, 1, 1),
                    0.25m,
                    AccountingAccountRoleKeys.TaxOutput25,
                    null,
                    CustomerInvoiceTaxMethodValues.Exclusive,
                    Direction: AccountingTaxDirectionValues.Sales,
                    Treatment: AccountingTaxTreatmentValues.Taxable,
                    RuleVersion: "2026.1",
                    Jurisdiction: "SE",
                    CounterpartyJurisdictions: ["SE"],
                    DocumentTypes: ["customer_invoice", "customer_credit_note"],
                    LineClassifications: ["standard_goods_or_services"],
                    VatBoxMappings: ["05", "10"],
                    Recoverability: AccountingTaxRecoverabilityValues.None,
                    RequiredEvidence: ["operator_classified_domestic_standard_25"]),
                new AccountingTaxRuleDefinition(
                    DomesticPurchase25RuleKey,
                    "Domestic purchase, standard VAT 25%, fully deductible",
                    new DateOnly(2026, 1, 1),
                    0.25m,
                    null,
                    AccountingAccountRoleKeys.TaxInput,
                    CustomerInvoiceTaxMethodValues.Inclusive,
                    Direction: AccountingTaxDirectionValues.Purchase,
                    Treatment: AccountingTaxTreatmentValues.Taxable,
                    RuleVersion: "2026.1",
                    Jurisdiction: "SE",
                    CounterpartyJurisdictions: ["SE"],
                    DocumentTypes: ["supplier_invoice", "supplier_credit_note"],
                    LineClassifications: ["expense", "asset"],
                    VatBoxMappings: ["48"],
                    Recoverability: AccountingTaxRecoverabilityValues.Full,
                    RequiredEvidence: ["operator_classified_domestic_standard_25", "business_use_full_recovery"])
            ],
            InvoicePolicy: new AccountingInvoicePolicyDefinition(
                RequiresSequentialNumbers: true,
                RequiredFields: ["seller_legal_identity", "document_number", "issue_date", "counterparty", "currency", "line_items"],
                SupportedDocumentTypes: ["invoice", "credit_note"]),
            ReportingMappings:
            [
                new("asset", "balance_sheet", "assets"),
                new("liability", "balance_sheet", "liabilities"),
                new("equity", "balance_sheet", "equity"),
                new("revenue", "profit_and_loss", "revenue"),
                new("expense", "profit_and_loss", "expenses")
            ],
            Terminology: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["customer_document"] = "Customer invoice",
                ["supplier_document"] = "Supplier invoice",
                ["journal"] = "Voucher",
                ["credit_document"] = "Credit note"
            },
            RetentionAndLockPolicy: new AccountingRetentionAndLockPolicyDefinition(
                MinimumRetentionYears: null,
                RequiresEvidenceForPosting: true,
                AllowsPeriodReopening: false,
                "Swedish retention duration and period-reopening behavior are unverified in this candidate. Statutory archive capabilities remain disabled."),
            SupportedExports: ["generic_csv", "generic_json"],
            SupportedCapabilities: ["double_entry_bookkeeping", "generic_financial_statements", "swedish_statutory_profile", AccountingPolicyCapabilityKeys.CountrySpecificTax, AccountingPolicyCapabilityKeys.CountrySpecificReporting],
            PolicyMetadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["jurisdiction"] = "SE",
                ["chart_template"] = ChartTemplateKey,
                ["tax_specification"] = "sweden-domestic-vat-launch-2026.1",
                ["tax_scope"] = "domestic_standard_25_sales_and_full_recovery_purchases",
                ["review_state"] = "review_pending",
                ["invoice_policy_hook"] = "unverified",
                ["retention_policy"] = "unverified",
                ["lock_policy"] = "unverified"
            },
            CapabilityStates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AccountingPolicyCapabilityKeys.CountrySpecificTax] = "supported_unvalidated_limited_scope",
                [AccountingPolicyCapabilityKeys.CountrySpecificReporting] = "supported_unvalidated_human_filing_only",
                [AccountingPolicyCapabilityKeys.StatutoryExport] = "unsupported",
                ["native_statutory_invoice_issuance"] = "unsupported",
                ["swedish_statutory_profile"] = "supported_unvalidated"
            });

        var json = JsonSerializer.Serialize(Definition);
        DefinitionHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    public AccountingPolicyPackDefinition Definition { get; }
    public string DefinitionHash { get; }
}
