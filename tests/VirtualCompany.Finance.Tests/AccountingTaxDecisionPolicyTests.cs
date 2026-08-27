using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Finance;

namespace VirtualCompany.Finance.Tests;

public sealed class AccountingTaxDecisionPolicyTests
{
    private readonly AccountingTaxDecisionPolicy _policy = new();

    [Fact]
    public void Swedish_candidate_blocks_an_unsupported_case_instead_of_inventing_a_treatment()
    {
        var pack = new SwedishCandidateAccountingPolicyPack();

        var decision = _policy.Decide(pack, Input("domestic_standard"));

        Assert.False(decision.IsAllowed);
        Assert.Equal(AccountingTaxDecisionReasonCodes.RuleUnavailable, decision.ReasonCode);
        Assert.Contains("No approved Swedish VAT rule", decision.Explanation);
        Assert.Equal(0m, decision.TaxAmount);
    }

    [Fact]
    public void Swedish_domestic_standard_sale_matches_golden_boxes_and_amounts()
    {
        var pack = new SwedishCandidateAccountingPolicyPack();
        var input = new AccountingTaxDecisionInput(
            SwedishCandidateAccountingPolicyPack.DomesticSales25RuleKey,
            new DateOnly(2026, 8, 24), AccountingTaxDirectionValues.Sales,
            "customer_invoice", "standard_goods_or_services", 100m, 2,
            AccountingRoundingModeValues.MidpointToEven, "registered", "SE",
            EvidenceClassifications: new HashSet<string> { "operator_classified_domestic_standard_25" },
            CompanyCountryCode: "SE", AccountingCurrency: "SEK", BookkeepingMethod: StatutoryBookkeepingMethodValues.Accrual,
            DocumentCurrency: "SEK");

        var decision = _policy.Decide(pack, input);

        Assert.True(decision.IsAllowed);
        Assert.Equal(100m, decision.TaxableBasis);
        Assert.Equal(25m, decision.TaxAmount);
        Assert.Equal(125m, decision.GrossAmount);
        Assert.Equal(["05", "10"], decision.VatBoxMappings);
        Assert.Equal(AccountingAccountRoleKeys.TaxOutput25, decision.LiabilityAccountRoleKey);
    }

    [Fact]
    public void Swedish_domestic_fully_recoverable_purchase_matches_golden_box_and_amounts()
    {
        var pack = new SwedishCandidateAccountingPolicyPack();
        var input = new AccountingTaxDecisionInput(
            SwedishCandidateAccountingPolicyPack.DomesticPurchase25RuleKey,
            new DateOnly(2026, 8, 24), AccountingTaxDirectionValues.Purchase,
            "supplier_invoice", "expense", 125m, 2,
            AccountingRoundingModeValues.MidpointToEven, "registered", "SE",
            EvidenceClassifications: new HashSet<string>
            {
                "operator_classified_domestic_standard_25", "business_use_full_recovery"
            }, CompanyCountryCode: "SE", AccountingCurrency: "SEK", BookkeepingMethod: StatutoryBookkeepingMethodValues.Accrual,
            DocumentCurrency: "SEK");

        var decision = _policy.Decide(pack, input);

        Assert.True(decision.IsAllowed);
        Assert.Equal(100m, decision.TaxableBasis);
        Assert.Equal(25m, decision.TaxAmount);
        Assert.Equal(125m, decision.GrossAmount);
        Assert.Equal(["48"], decision.VatBoxMappings);
        Assert.Equal(AccountingAccountRoleKeys.TaxInput, decision.RecoverableAccountRoleKey);
        Assert.Equal(AccountingTaxRecoverabilityValues.Full, decision.Recoverability);
    }

    [Theory]
    [InlineData("se_eu_sale_25")]
    [InlineData("se_import_purchase_25")]
    [InlineData("se_non_eu_sale_25")]
    [InlineData("se_reverse_charge_25")]
    [InlineData("se_partial_recovery_25")]
    [InlineData("se_cash_method_25")]
    [InlineData("se_mixed_use_25")]
    [InlineData("se_exempt_sale")]
    [InlineData("se_reduced_rate_12")]
    [InlineData("se_reduced_rate_6")]
    public void Swedish_declared_boundaries_are_blocked(string ruleKey)
    {
        var decision = _policy.Decide(new SwedishCandidateAccountingPolicyPack(),
            Input(ruleKey) with { CompanyVatRegistrationStatus = "registered" });

        Assert.False(decision.IsAllowed);
        Assert.Equal(AccountingTaxDecisionReasonCodes.RuleUnavailable, decision.ReasonCode);
    }

    [Fact]
    public void Effective_dated_evidence_backed_rule_is_deterministic()
    {
        var pack = Pack(new AccountingTaxRuleDefinition(
            "fixture", "Approved fixture", new DateOnly(2026, 1, 1), .20m,
            "tax_payable", null, CustomerInvoiceTaxMethodValues.Inclusive,
            Direction: AccountingTaxDirectionValues.Sales, Treatment: AccountingTaxTreatmentValues.Taxable,
            RuleVersion: "2026.1", Jurisdiction: "SE", DocumentTypes: ["customer_invoice"],
            LineClassifications: ["revenue"], VatBoxMappings: ["fixture_box"],
            RequiredEvidence: ["fixture_evidence"]));
        var evidence = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "fixture_evidence" };
        var input = Input("fixture") with
        {
            LineAmount = 120m,
            CompanyVatRegistrationStatus = "registered",
            EvidenceClassifications = evidence
        };

        var first = _policy.Decide(pack, input);
        var second = _policy.Decide(pack, input);

        Assert.Equal(first.IsAllowed, second.IsAllowed);
        Assert.Equal(first.ReasonCode, second.ReasonCode);
        Assert.Equal(first.TaxableBasis, second.TaxableBasis);
        Assert.Equal(first.TaxAmount, second.TaxAmount);
        Assert.Equal(first.EvidenceClassification, second.EvidenceClassification);
        Assert.True(first.IsAllowed);
        Assert.Equal(100m, first.TaxableBasis);
        Assert.Equal(20m, first.TaxAmount);
        Assert.Equal("2026.1", first.RuleVersion);
        Assert.Equal(["fixture_box"], first.VatBoxMappings);
    }

    [Fact]
    public void Missing_evidence_blocks_before_calculation()
    {
        var pack = Pack(new AccountingTaxRuleDefinition(
            "fixture", "Evidence fixture", new DateOnly(2026, 1, 1), .20m,
            "tax_payable", null, RequiredEvidence: ["counterparty_status"]));

        var decision = _policy.Decide(pack, Input("fixture") with
        {
            CompanyVatRegistrationStatus = StatutoryVatRegistrationStatusValues.Registered
        });

        Assert.False(decision.IsAllowed);
        Assert.Equal(AccountingTaxDecisionReasonCodes.EvidenceMissing, decision.ReasonCode);
        Assert.Equal(["counterparty_status"], decision.RequiredEvidence);
    }

    [Theory]
    [InlineData("cash", AccountingTaxDecisionReasonCodes.BookkeepingMethodUnsupported)]
    [InlineData(StatutoryBookkeepingMethodValues.Accrual, AccountingTaxDecisionReasonCodes.DocumentCurrencyUnsupported)]
    public void Swedish_launch_scope_is_enforced_for_a_real_supported_rule(string bookkeepingMethod, string expectedReason)
    {
        var input = Input(SwedishCandidateAccountingPolicyPack.DomesticSales25RuleKey) with
        {
            CompanyVatRegistrationStatus = StatutoryVatRegistrationStatusValues.Registered,
            BookkeepingMethod = bookkeepingMethod,
            DocumentCurrency = bookkeepingMethod == StatutoryBookkeepingMethodValues.Accrual ? "EUR" : "SEK",
            LineClassification = "standard_goods_or_services",
            CounterpartyJurisdiction = "SE",
            EvidenceClassifications = new HashSet<string> { "operator_classified_domestic_standard_25" }
        };

        var decision = _policy.Decide(new SwedishCandidateAccountingPolicyPack(), input);

        Assert.False(decision.IsAllowed);
        Assert.Equal(expectedReason, decision.ReasonCode);
    }

    [Fact]
    public void Configuration_validation_rejects_missing_roles_and_partial_recovery()
    {
        var pack = Pack(new AccountingTaxRuleDefinition(
            "fixture", "Invalid fixture", new DateOnly(2026, 1, 1), .20m,
            "undefined_output_role", "undefined_input_role",
            Recoverability: AccountingTaxRecoverabilityValues.Partial));

        var issues = _policy.Validate(pack);

        Assert.Contains(issues, x => x.Explanation.Contains("partial recovery", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, issues.Count(x => x.Explanation.Contains("undefined account role", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Configuration_validation_rejects_duplicate_versions_intersecting_applicability_and_unbalanced_shapes()
    {
        var rules = new[]
        {
            new AccountingTaxRuleDefinition("fixture", "Wildcard", new DateOnly(2026, 1, 1), .25m,
                "tax_payable", null, Direction: AccountingTaxDirectionValues.Sales, RuleVersion: "2026.1",
                DocumentTypes: [], LineClassifications: ["goods"]),
            new AccountingTaxRuleDefinition("fixture", "Specific", new DateOnly(2026, 6, 1), .25m,
                "tax_payable", null, Direction: AccountingTaxDirectionValues.Sales, RuleVersion: "2026.1",
                DocumentTypes: ["customer_invoice"], LineClassifications: ["goods"]),
            new AccountingTaxRuleDefinition("purchase", "Invalid purchase", new DateOnly(2026, 1, 1), .25m,
                null, null, Direction: AccountingTaxDirectionValues.Purchase, RuleVersion: "2026.1",
                Recoverability: AccountingTaxRecoverabilityValues.Full)
        };

        var issues = _policy.Validate(Pack(rules));

        Assert.Contains(issues, issue => issue.Explanation.Contains("duplicate rule version", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(issues, issue => issue.Explanation.Contains("overlapping transaction applicability", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(issues, issue => issue.Explanation.Contains("balanced recoverable purchase", StringComparison.OrdinalIgnoreCase));
    }

    private static AccountingTaxDecisionInput Input(string key) => new(
        key, new DateOnly(2026, 8, 24), AccountingTaxDirectionValues.Sales,
        "customer_invoice", "revenue", 100m, 2, AccountingRoundingModeValues.MidpointToEven,
        CompanyCountryCode: "SE", AccountingCurrency: "SEK", BookkeepingMethod: StatutoryBookkeepingMethodValues.Accrual,
        DocumentCurrency: "SEK");

    private static IAccountingPolicyPack Pack(params AccountingTaxRuleDefinition[] rules) => new TestPack(
        new AccountingPolicyPackDefinition("tax-fixture", "1.0.0", "Tax fixture", "SE", false, false,
            "Test fixture only.", [], [new("tax_payable", "Tax payable", true, true)], rules,
            new(false, [], ["invoice"]), [], new Dictionary<string, string>(),
            new(null, false, true, "Test fixture."), [], ["double_entry_bookkeeping"]));

    private sealed class TestPack(AccountingPolicyPackDefinition definition) : IAccountingPolicyPack
    {
        public AccountingPolicyPackDefinition Definition { get; } = definition;
        public string DefinitionHash => new('a', 64);
    }
}
