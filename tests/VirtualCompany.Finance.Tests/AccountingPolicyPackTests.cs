using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Finance;

namespace VirtualCompany.Finance.Tests;

public sealed class AccountingPolicyPackTests
{
    [Fact]
    public void Country_neutral_pack_is_bookkeeping_capable_without_claiming_statutory_compliance()
    {
        var pack = new CountryNeutralAccountingPolicyPack();

        Assert.Equal(AccountingPolicyPackDefaults.CountryNeutralPackKey, pack.Definition.PackKey);
        Assert.True(pack.Definition.IsCountryNeutral);
        Assert.False(pack.Definition.IsStatutoryComplianceValidated);
        Assert.Contains(pack.Definition.TaxRules, rule =>
            rule.Key == "generic-exempt" && rule.AmountMethod == CustomerInvoiceTaxMethodValues.Exempt && rule.Rate == 0m);
        Assert.Contains("double_entry_bookkeeping", pack.Definition.SupportedCapabilities);
        Assert.DoesNotContain(AccountingPolicyCapabilityKeys.CountrySpecificReporting, pack.Definition.SupportedCapabilities);
        Assert.All(pack.Definition.AccountRoles.Where(role => role.IsRequired), role => Assert.NotEmpty(role.Key));
        Assert.Equal(64, pack.DefinitionHash.Length);
    }

    [Fact]
    public void Resolver_rejects_duplicate_pack_key_and_version()
    {
        var first = new CountryNeutralAccountingPolicyPack();
        var second = new CountryNeutralAccountingPolicyPack();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new AccountingPolicyPackResolver([first, second]));

        Assert.Contains("Duplicate accounting policy-pack registration", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Banking_pack_declares_only_configured_reconciliation_and_difference_roles()
    {
        var pack = new CountryNeutralBankingAccountingPolicyPack();

        Assert.Equal(AccountingPolicyPackDefaults.CountryNeutralBankingVersion, pack.Definition.Version);
        Assert.Contains("bank_reconciliation", pack.Definition.SupportedCapabilities);
        Assert.Contains("suspense_reclassification", pack.Definition.SupportedCapabilities);
        Assert.Contains(pack.Definition.AccountRoles, x => x.Key == AccountingAccountRoleKeys.Bank);
        Assert.Contains(pack.Definition.AccountRoles, x => x.Key == AccountingAccountRoleKeys.AccountsReceivable);
        Assert.Contains(pack.Definition.AccountRoles, x => x.Key == AccountingAccountRoleKeys.AccountsPayable);
        Assert.Contains(pack.Definition.AccountRoles, x => x.Key == AccountingAccountRoleKeys.Suspense);
        Assert.Contains(pack.Definition.AccountRoles, x => x.Key == AccountingAccountRoleKeys.BankFee);
        Assert.Contains(pack.Definition.AccountRoles, x => x.Key == AccountingAccountRoleKeys.RoundingDifference);
        Assert.Contains(pack.Definition.AccountRoles, x => x.Key == AccountingAccountRoleKeys.ExchangeGain);
        Assert.Contains(pack.Definition.AccountRoles, x => x.Key == AccountingAccountRoleKeys.ExchangeLoss);
        Assert.Contains(pack.Definition.AccountRoles, x => x.Key == AccountingAccountRoleKeys.SettlementDiscount);
        Assert.DoesNotContain(pack.Definition.AccountRoles, x => x.Key.Contains("fallback", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolver_never_substitutes_an_unknown_version()
    {
        var resolver = new AccountingPolicyPackResolver([new CountryNeutralAccountingPolicyPack()]);

        var exception = Assert.Throws<AccountingConfigurationException>(() =>
            resolver.Resolve(AccountingPolicyPackDefaults.CountryNeutralPackKey, "99.0.0"));

        Assert.Equal(AccountingConfigurationReasonCodes.UnsupportedPackVersion, exception.ReasonCode);
    }

    [Fact]
    public void New_configuration_normalizes_currency_and_defaults_to_internal_authority()
    {
        var configuration = new AccountingConfiguration(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "eur",
            7,
            1,
            "country-neutral",
            "1.0.0",
            new DateOnly(2026, 7, 1),
            2,
            "midpoint-to-even",
            Guid.NewGuid(),
            new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc));

        Assert.Equal("EUR", configuration.BaseCurrency);
        Assert.Equal(AccountingAuthorityValues.InternalLedger, configuration.Authority);
        Assert.Equal(AccountingSetupStateValues.Incomplete, configuration.SetupState);
        Assert.Equal(AccountingRoundingModeValues.MidpointToEven, configuration.RoundingMode);
    }
}
