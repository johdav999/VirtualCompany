using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class SwedishFoundationAccountingPolicyPack : IAccountingPolicyPack
{
    public const string ChartTemplateKey = "swedish-foundation-candidate-v1";

    public SwedishFoundationAccountingPolicyPack()
    {
        Definition = new AccountingPolicyPackDefinition(
            AccountingPolicyPackDefaults.SwedishCandidatePackKey,
            AccountingPolicyPackDefaults.SwedishFoundationVersion,
            "Sweden statutory foundation candidate",
            "SE", false, false,
            "This Swedish candidate has not been validated by a qualified reviewer. It supports legal-profile setup and general bookkeeping only; Swedish tax, statutory reporting, statutory exports, and legal certification remain unavailable.",
            [new(ChartTemplateKey, "Swedish bookkeeping foundation (unvalidated candidate)",
            [
                new("1930", "Business bank account", "asset", "debit", AccountingAccountRoleKeys.Bank),
                new("1510", "Accounts receivable", "asset", "debit", AccountingAccountRoleKeys.AccountsReceivable),
                new("2440", "Accounts payable", "liability", "credit", AccountingAccountRoleKeys.AccountsPayable),
                new("2081", "Equity", "equity", "credit", "equity"),
                new("3001", "Revenue without configured VAT treatment", "revenue", "credit", "revenue"),
                new("4000", "Operating expenses without configured VAT treatment", "expense", "debit", "operating_expense"),
                new("1799", "Unresolved accounting suspense", "asset", "debit", AccountingAccountRoleKeys.Suspense),
                new("3740", "Rounding differences", "expense", "debit", AccountingAccountRoleKeys.RoundingDifference),
                new("7960", "Exchange losses", "expense", "debit", AccountingAccountRoleKeys.ExchangeLoss),
                new("3960", "Exchange gains", "revenue", "credit", AccountingAccountRoleKeys.ExchangeGain)
            ])],
            [
                new(AccountingAccountRoleKeys.Bank, "Bank account", true, true),
                new(AccountingAccountRoleKeys.AccountsReceivable, "Accounts receivable", true, true),
                new(AccountingAccountRoleKeys.AccountsPayable, "Accounts payable", true, true),
                new("equity", "Equity", true, true), new("revenue", "Revenue", true, false),
                new("operating_expense", "Operating expense", true, false),
                new(AccountingAccountRoleKeys.Suspense, "Unresolved accounting suspense", true, true),
                new(AccountingAccountRoleKeys.RoundingDifference, "Rounding differences", true, false),
                new(AccountingAccountRoleKeys.ExchangeGain, "Exchange gains", true, false),
                new(AccountingAccountRoleKeys.ExchangeLoss, "Exchange losses", true, false)
            ],
            [],
            new(true, ["seller_legal_identity", "document_number", "issue_date", "counterparty", "currency", "line_items"], []),
            [new("asset", "balance_sheet", "assets"), new("liability", "balance_sheet", "liabilities"),
                new("equity", "balance_sheet", "equity"), new("revenue", "profit_and_loss", "revenue"),
                new("expense", "profit_and_loss", "expenses")],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["customer_document"] = "Customer invoice", ["supplier_document"] = "Supplier invoice",
                ["journal"] = "Voucher", ["credit_document"] = "Credit note"
            },
            new(null, true, false,
                "Swedish retention duration and lock behavior are unverified in this candidate. Statutory close and archive capabilities remain disabled."),
            ["generic_csv", "generic_json"],
            ["double_entry_bookkeeping", "generic_financial_statements", "swedish_statutory_profile"],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["jurisdiction"] = "SE", ["chart_template"] = ChartTemplateKey,
                ["review_state"] = "review_pending", ["invoice_policy_hook"] = "unverified",
                ["retention_policy"] = "unverified", ["lock_policy"] = "unverified"
            },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AccountingPolicyCapabilityKeys.CountrySpecificTax] = "unsupported",
                [AccountingPolicyCapabilityKeys.CountrySpecificReporting] = "unsupported",
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
