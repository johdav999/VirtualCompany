using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Finance;
using System.Security.Cryptography;
using System.Text.Json;

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
    public void Resolver_rejects_duplicate_definition_hash_across_different_catalog_entries()
    {
        var first = new CountryNeutralAccountingPolicyPack();
        var duplicateHash = new TestPolicyPack(
            first.Definition with { PackKey = "different-key", Version = "2.0.0" },
            first.DefinitionHash);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new AccountingPolicyPackResolver([first, duplicateHash]));

        Assert.Contains("Duplicate accounting policy-pack definition hash", exception.Message, StringComparison.Ordinal);
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
    public void Swedish_candidate_chart_and_tax_rules_are_complete_for_the_sourced_launch_scope()
    {
        var pack = new SwedishCandidateAccountingPolicyPack();
        var chart = Assert.Single(pack.Definition.ChartTemplates);
        var policyIssues = new AccountingTaxDecisionPolicy().Validate(pack);

        Assert.Empty(policyIssues);
        Assert.False(pack.Definition.IsStatutoryComplianceValidated);
        Assert.Contains(chart.Accounts, x => x.Code == "2611" && x.DefaultRoleKey == AccountingAccountRoleKeys.TaxOutput25);
        Assert.Contains(chart.Accounts, x => x.Code == "2641" && x.DefaultRoleKey == AccountingAccountRoleKeys.TaxInput);
        Assert.Contains(chart.Accounts, x => x.Code == "3001" && x.DefaultRoleKey == "revenue");
        Assert.Contains(chart.Accounts, x => x.Code == "4000" && x.DefaultRoleKey == "operating_expense");
        Assert.Contains(pack.Definition.TaxRules, x => x.Key == SwedishCandidateAccountingPolicyPack.DomesticSales25RuleKey);
        Assert.Contains(pack.Definition.TaxRules, x => x.Key == SwedishCandidateAccountingPolicyPack.DomesticPurchase25RuleKey);
        Assert.Equal("supported_unvalidated_limited_scope",
            pack.Definition.CapabilityStates![AccountingPolicyCapabilityKeys.CountrySpecificTax]);
        Assert.Equal(AccountingChartCatalogDefaults.Bas2026CatalogKey, pack.Definition.PolicyMetadata!["chart_catalog_key"]);
        Assert.Equal(AccountingChartCatalogDefaults.Bas2026CatalogVersion, pack.Definition.PolicyMetadata["chart_catalog_version"]);
        Assert.Equal(Bas2026AccountingChartCatalog.ExpectedCatalogSha256, pack.Definition.PolicyMetadata["chart_catalog_sha256"]);
        Assert.Equal(Bas2026AccountingChartCatalog.ExpectedSourceSha256, pack.Definition.PolicyMetadata["chart_catalog_source_sha256"]);
    }

    [Fact]
    public void Swedish_foundation_history_remains_resolvable_after_vat_pack_upgrade()
    {
        var foundation = new SwedishFoundationAccountingPolicyPack();
        var previousCandidate = new SwedishDomesticVatCandidatePackV1_1();
        var vatCandidate = new SwedishCandidateAccountingPolicyPack();
        var resolver = new AccountingPolicyPackResolver([foundation, previousCandidate, vatCandidate]);

        Assert.Same(foundation, resolver.Resolve(AccountingPolicyPackDefaults.SwedishCandidatePackKey, "1.0.0"));
        Assert.Same(previousCandidate, resolver.Resolve(AccountingPolicyPackDefaults.SwedishCandidatePackKey, "1.1.0"));
        Assert.Same(vatCandidate, resolver.Resolve(AccountingPolicyPackDefaults.SwedishCandidatePackKey, "1.4.0"));
        Assert.NotEqual(foundation.DefinitionHash, vatCandidate.DefinitionHash);
        Assert.Equal("f81f0a5d7480d84b54c92541aaa23133006c568fc525634dfd7ebb94ce2b4fc2", previousCandidate.DefinitionHash);
        Assert.NotEqual(previousCandidate.DefinitionHash, vatCandidate.DefinitionHash);
        Assert.Empty(foundation.Definition.TaxRules);
    }

    [Fact]
    public void Swedish_runtime_pack_is_semantically_identical_to_checked_in_rule_and_chart_artifacts()
    {
        var pack = new SwedishCandidateAccountingPolicyPack();
        var root = RepositoryRoot();
        using var rulesDocument = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs", "finance", "swedish-domestic-vat-launch-2026.1", "vat-rules.json")));
        using var chartDocument = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs", "finance", "swedish-domestic-vat-launch-2026.1", "chart-role-mappings.json")));

        foreach (var source in rulesDocument.RootElement.GetProperty("rules").EnumerateArray())
        {
            var rule = Assert.Single(pack.Definition.TaxRules, candidate =>
                candidate.Key == source.GetProperty("key").GetString());
            Assert.Equal(source.GetProperty("ruleVersion").GetString(), rule.RuleVersion);
            Assert.Equal(DateOnly.Parse(source.GetProperty("effectiveFrom").GetString()!), rule.EffectiveFrom);
            Assert.Equal(source.GetProperty("direction").GetString(), rule.Direction);
            Assert.Equal(source.GetProperty("rate").GetDecimal(), rule.Rate);
            Assert.Equal(source.GetProperty("amountMethod").GetString(), rule.AmountMethod);
            Assert.Equal(source.GetProperty("treatment").GetString(), rule.Treatment);
            Assert.Equal(source.GetProperty("recoverability").GetString(), rule.Recoverability);
            Assert.Equal(source.GetProperty("documentTypes").EnumerateArray().Select(item => item.GetString()!).ToArray(), rule.DocumentTypes);
            Assert.Equal(source.GetProperty("lineClassifications").EnumerateArray().Select(item => item.GetString()!).ToArray(), rule.LineClassifications);
            Assert.Equal(source.GetProperty("requiredEvidence").EnumerateArray().Select(item => item.GetString()!).ToArray(), rule.RequiredEvidence);
            Assert.Equal(source.GetProperty("vatBoxMappings").EnumerateArray()
                .Select(item => item.GetProperty("box").GetString()!).ToArray(), rule.VatBoxMappings);
        }

        var chart = Assert.Single(pack.Definition.ChartTemplates);
        foreach (var source in chartDocument.RootElement.GetProperty("accounts").EnumerateArray())
        {
            var account = Assert.Single(chart.Accounts, candidate =>
                candidate.Code == source.GetProperty("accountNumber").GetInt32().ToString());
            Assert.Equal(source.GetProperty("displayNameEn").GetString(), account.Label);
            Assert.Equal(source.GetProperty("accountClass").GetString(), account.AccountClass);
            Assert.Equal(source.GetProperty("normalBalance").GetString(), account.NormalBalance);
            Assert.Equal(source.GetProperty("roleKey").GetString(), account.DefaultRoleKey);
        }
    }

    [Fact]
    public void Swedish_candidate_artifact_manifest_matches_runtime_and_normative_files()
    {
        var root = RepositoryRoot();
        var package = Path.Combine(root, "docs", "finance", "swedish-domestic-vat-launch-2026.1");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(package, "artifact-manifest.json")));
        var document = manifest.RootElement;

        Assert.Equal("review_pending", document.GetProperty("reviewState").GetString());
        Assert.Equal(new SwedishCandidateAccountingPolicyPack().DefinitionHash,
            document.GetProperty("runtimeDefinitionSha256").GetString());

        foreach (var artifact in document.GetProperty("artifacts").EnumerateObject())
        {
            var bytes = File.ReadAllBytes(Path.Combine(package, artifact.Name));
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), artifact.Value.GetString());
        }
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


    private sealed class TestPolicyPack(AccountingPolicyPackDefinition definition, string definitionHash) : IAccountingPolicyPack
    {
        public AccountingPolicyPackDefinition Definition { get; } = definition;
        public string DefinitionHash { get; } = definitionHash;
    }


    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "financial-app-r1-prompts.md")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
