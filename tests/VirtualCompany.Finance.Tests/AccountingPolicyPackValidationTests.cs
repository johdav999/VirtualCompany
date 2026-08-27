using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Finance;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace VirtualCompany.Finance.Tests;

public sealed class AccountingPolicyPackValidationTests
{
    private static readonly DateOnly ReviewDate = new(2026, 8, 25);

    [Fact]
    public void Swedish_candidate_without_real_reviewer_evidence_remains_blocked()
    {
        var pack = new SwedishStatutoryArchiveCandidatePack();
        var registry = new AccountingPolicyPackValidationRegistry([]);

        var decision = registry.Evaluate(pack, ReviewDate);

        Assert.False(decision.IsValidated);
        Assert.Equal(AccountingPolicyPackValidationStates.MissingEvidence, decision.State);
        Assert.False(pack.Definition.IsStatutoryComplianceValidated);
    }

    [Fact]
    public void Exact_current_evidence_validates_only_a_pack_that_declares_a_new_reviewed_version()
    {
        var pack = ReviewedPack(new string('a', 64));
        var evidence = Evidence(pack.DefinitionHash);
        var registry = new AccountingPolicyPackValidationRegistry([evidence]);

        var decision = registry.Evaluate(pack, ReviewDate);

        Assert.True(decision.IsValidated);
        Assert.Equal(AccountingPolicyPackValidationStates.Validated, decision.State);
        Assert.Same(evidence, decision.Evidence);
    }

    [Fact]
    public void One_byte_definition_change_invalidates_prior_sign_off()
    {
        var reviewed = ReviewedPack(new string('a', 64));
        var changed = ReviewedPack(new string('a', 63) + "b");
        var registry = new AccountingPolicyPackValidationRegistry([Evidence(reviewed.DefinitionHash)]);

        var decision = registry.Evaluate(changed, ReviewDate);

        Assert.False(decision.IsValidated);
        Assert.Equal(AccountingPolicyPackValidationStates.DefinitionHashMismatch, decision.State);
    }

    [Fact]
    public void Expired_evidence_requires_revalidation()
    {
        var pack = ReviewedPack(new string('a', 64));
        var evidence = Evidence(pack.DefinitionHash) with { ExpiresOn = ReviewDate.AddDays(1) };
        var registry = new AccountingPolicyPackValidationRegistry([evidence]);

        var decision = registry.Evaluate(pack, ReviewDate.AddDays(2));

        Assert.False(decision.IsValidated);
        Assert.Equal(AccountingPolicyPackValidationStates.EvidenceExpired, decision.State);
    }

    [Fact]
    public async Task Startup_rejects_a_statutory_claim_without_exact_evidence()
    {
        var pack = ReviewedPack(new string('c', 64));
        var resolver = new AccountingPolicyPackResolver([pack]);
        var validator = new AccountingPolicyPackCatalogStartupValidator(
            resolver,
            new AccountingTaxDecisionPolicy(),
            new AccountingPolicyPackValidationRegistry([]),
            TimeProvider.System);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None));

        Assert.Contains(AccountingPolicyPackValidationStates.MissingEvidence, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Startup_accepts_only_the_exact_new_reviewed_pack_and_evidence_pair()
    {
        var pack = ReviewedPack(new string('d', 64));
        var registry = new AccountingPolicyPackValidationRegistry([Evidence(pack.DefinitionHash)]);
        var validator = new AccountingPolicyPackCatalogStartupValidator(
            new AccountingPolicyPackResolver([pack]),
            new AccountingTaxDecisionPolicy(),
            registry,
            TimeProvider.System);

        await validator.StartAsync(CancellationToken.None);
    }

    [Fact]
    public void Evidence_requires_bounded_attributable_metadata_and_sha256_hashes()
    {
        var invalid = Evidence("not-a-hash");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new AccountingPolicyPackValidationRegistry([invalid]));

        Assert.Contains(nameof(AccountingPolicyPackValidationEvidence.DefinitionHash), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Health_reports_an_operational_gate_without_treating_expected_missing_review_as_an_outage()
    {
        var resolver = new AccountingPolicyPackResolver([new SwedishStatutoryArchiveCandidatePack()]);
        var check = new SwedishAccountingValidationHealthCheck(
            resolver,
            new AccountingPolicyPackValidationRegistry([]),
            TimeProvider.System);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("release remains blocked", result.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, result.Data["validatedSwedishPackCount"]);
    }

    private static AccountingPolicyPackValidationEvidence Evidence(string definitionHash) => new(
        "reviewed-se-pack",
        "2.0.0",
        definitionHash,
        "Qualified reviewer",
        "professional-register-reference",
        "Exact policy definition, VAT fixtures, document rules, and SIE 4B output.",
        ReviewDate,
        "controlled-evidence/review.pdf",
        new string('e', 64),
        ["domestic-sale-25-exclusive", "domestic-purchase-25-inclusive-full-recovery"],
        ["Only the explicitly enumerated launch cases are approved."],
        ReviewDate.AddYears(1),
        ["definition_hash_change", "vat_fixture_change", "document_rule_change", "sie_format_change"]);

    private static IAccountingPolicyPack ReviewedPack(string hash)
    {
        var source = new SwedishStatutoryArchiveCandidatePack().Definition;
        return new TestPack(source with
        {
            PackKey = "reviewed-se-pack",
            Version = "2.0.0",
            IsStatutoryComplianceValidated = true,
            ComplianceNotice = "Validated only for the retained reviewer scope and limitations."
        }, hash);
    }

    private sealed record TestPack(AccountingPolicyPackDefinition Definition, string DefinitionHash) : IAccountingPolicyPack;
}
