using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CountryNeutralBankingAccountingPolicyPack : IAccountingPolicyPack
{
    public CountryNeutralBankingAccountingPolicyPack()
    {
        var baseline = new CountryNeutralAccountingPolicyPack().Definition;
        var chart = baseline.ChartTemplates.Single();
        Definition = baseline with
        {
            Version = AccountingPolicyPackDefaults.CountryNeutralBankingVersion,
            DisplayName = "Country-neutral accrual bookkeeping with bank reconciliation",
            ChartTemplates =
            [
                chart with
                {
                    Accounts =
                    [
                        .. chart.Accounts,
                        new("1900", "Bank reconciliation suspense", "asset", "debit", AccountingAccountRoleKeys.Suspense),
                        new("5100", "Bank fees", "expense", "debit", AccountingAccountRoleKeys.BankFee),
                        new("5200", "Rounding differences", "expense", "debit", AccountingAccountRoleKeys.RoundingDifference),
                        new("5300", "Exchange losses", "expense", "debit", AccountingAccountRoleKeys.ExchangeLoss),
                        new("4100", "Exchange gains", "revenue", "credit", AccountingAccountRoleKeys.ExchangeGain),
                        new("5400", "Settlement discounts", "expense", "debit", AccountingAccountRoleKeys.SettlementDiscount)
                    ]
                }
            ],
            AccountRoles =
            [
                .. baseline.AccountRoles,
                new(AccountingAccountRoleKeys.Bank, "Bank account", false, true),
                new(AccountingAccountRoleKeys.Suspense, "Bank reconciliation suspense", false, true),
                new(AccountingAccountRoleKeys.BankFee, "Bank fees", false, false),
                new(AccountingAccountRoleKeys.RoundingDifference, "Rounding differences", false, false),
                new(AccountingAccountRoleKeys.ExchangeGain, "Exchange gains", false, false),
                new(AccountingAccountRoleKeys.ExchangeLoss, "Exchange losses", false, false),
                new(AccountingAccountRoleKeys.SettlementDiscount, "Settlement discounts", false, false)
            ],
            SupportedCapabilities = [.. baseline.SupportedCapabilities, "bank_reconciliation", "suspense_reclassification"]
        };

        var json = JsonSerializer.Serialize(Definition);
        DefinitionHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    public AccountingPolicyPackDefinition Definition { get; }
    public string DefinitionHash { get; }
}
