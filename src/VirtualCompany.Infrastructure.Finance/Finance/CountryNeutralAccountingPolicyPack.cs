using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CountryNeutralAccountingPolicyPack : IAccountingPolicyPack
{
    public CountryNeutralAccountingPolicyPack()
    {
        Definition = new AccountingPolicyPackDefinition(
            AccountingPolicyPackDefaults.CountryNeutralPackKey,
            AccountingPolicyPackDefaults.CountryNeutralVersion,
            "Country-neutral accrual bookkeeping",
            CountryOrRegion: null,
            IsCountryNeutral: true,
            IsStatutoryComplianceValidated: false,
            "This policy pack supports general bookkeeping but does not provide country-specific tax or statutory compliance.",
            [
                new AccountingChartTemplateDefinition(
                    "generic-accrual",
                    "Generic accrual chart",
                    [
                        new("1000", "Cash and cash equivalents", "asset", "debit", "cash"),
                        new("1100", "Accounts receivable", "asset", "debit", "accounts_receivable"),
                        new("2000", "Accounts payable", "liability", "credit", "accounts_payable"),
                        new("3000", "Owner's equity", "equity", "credit", "equity"),
                        new("4000", "Revenue", "revenue", "credit", "revenue"),
                        new("5000", "Operating expenses", "expense", "debit", "operating_expense")
                    ])
            ],
            [
                new("cash", "Cash account", true, true),
                new("accounts_receivable", "Accounts receivable", true, true),
                new("accounts_payable", "Accounts payable", true, true),
                new("equity", "Equity", true, true),
                new("revenue", "Revenue", true, false),
                new("operating_expense", "Operating expense", true, false)
            ],
            TaxRules:
            [
                new("generic-exempt", "No tax", new DateOnly(2000, 1, 1), 0m, null, null,
                    CustomerInvoiceTaxMethodValues.Exempt)
            ],
            new AccountingInvoicePolicyDefinition(
                RequiresSequentialNumbers: true,
                RequiredFields: ["document_number", "issue_date", "counterparty", "currency", "line_items"],
                SupportedDocumentTypes: ["invoice", "credit_note"]),
            [
                new("asset", "balance_sheet", "assets"),
                new("liability", "balance_sheet", "liabilities"),
                new("equity", "balance_sheet", "equity"),
                new("revenue", "profit_and_loss", "revenue"),
                new("expense", "profit_and_loss", "expenses")
            ],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["customer_document"] = "Customer invoice",
                ["supplier_document"] = "Supplier bill",
                ["journal"] = "Journal entry",
                ["credit_document"] = "Credit note"
            },
            new AccountingRetentionAndLockPolicyDefinition(
                MinimumRetentionYears: null,
                RequiresEvidenceForPosting: true,
                AllowsPeriodReopening: true,
                "No country-specific retention period or statutory lock rule is supplied. Local requirements must be configured separately."),
            SupportedExports: ["generic_csv", "generic_json"],
            SupportedCapabilities: ["double_entry_bookkeeping", "generic_financial_statements"]);

        var json = JsonSerializer.Serialize(Definition);
        DefinitionHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    public AccountingPolicyPackDefinition Definition { get; }
    public string DefinitionHash { get; }
}
