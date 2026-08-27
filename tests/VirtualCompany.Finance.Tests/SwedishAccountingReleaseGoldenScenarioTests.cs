using System.Text.Json;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Finance;

namespace VirtualCompany.Finance.Tests;

public sealed class SwedishAccountingReleaseGoldenScenarioTests
{
    private readonly AccountingTaxDecisionPolicy _policy = new();

    [Fact]
    public void Every_checked_in_vat_golden_fixture_executes_with_its_exact_expected_decision()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureDirectory(), "golden-fixtures.json")));
        var pack = new SwedishStatutoryArchiveCandidatePack();
        var executed = new List<string>();

        foreach (var fixture in document.RootElement.GetProperty("fixtures").EnumerateArray())
        {
            var input = fixture.GetProperty("input");
            var expected = fixture.GetProperty("expectedDecision");
            var evidence = input.GetProperty("evidenceClassifications").EnumerateArray()
                .Select(value => value.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var decision = _policy.Decide(pack, new AccountingTaxDecisionInput(
                input.GetProperty("ruleKey").GetString()!,
                DateOnly.Parse(input.GetProperty("accountingDate").GetString()!),
                input.GetProperty("direction").GetString()!,
                input.GetProperty("documentType").GetString()!,
                input.GetProperty("lineClassification").GetString()!,
                input.GetProperty("lineAmount").GetDecimal(),
                2,
                AccountingRoundingModeValues.MidpointToEven,
                input.GetProperty("companyVatRegistrationStatus").GetString()!,
                input.TryGetProperty("counterpartyJurisdiction", out var jurisdiction) ? jurisdiction.GetString()! : "SE",
                input.TryGetProperty("counterpartyVatStatus", out var vatStatus) ? vatStatus.GetString()! : "unknown",
                evidence,
                "SE",
                "SEK",
                StatutoryBookkeepingMethodValues.Accrual,
                "SEK"));

            Assert.Equal(expected.GetProperty("isAllowed").GetBoolean(), decision.IsAllowed);
            if (expected.TryGetProperty("reasonCode", out var reasonCode))
                Assert.Equal(reasonCode.GetString(), decision.ReasonCode);
            Assert.Equal(expected.GetProperty("taxableBasis").GetDecimal(), decision.TaxableBasis);
            Assert.Equal(expected.GetProperty("taxAmount").GetDecimal(), decision.TaxAmount);
            Assert.Equal(expected.GetProperty("grossAmount").GetDecimal(), decision.GrossAmount);
            executed.Add(fixture.GetProperty("fixtureId").GetString()!);
        }

        Assert.Equal(8, executed.Count);
        Assert.Equal(executed.Count, executed.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_documented_unsupported_tax_boundary_blocks_without_a_fallback_treatment()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureDirectory(), "unsupported-scenarios.json")));
        var pack = new SwedishStatutoryArchiveCandidatePack();
        var scenarios = document.RootElement.GetProperty("scenarios").EnumerateArray().ToArray();

        foreach (var scenario in scenarios)
        {
            var decision = _policy.Decide(pack, new AccountingTaxDecisionInput(
                scenario.GetProperty("key").GetString()!,
                new DateOnly(2026, 8, 25),
                AccountingTaxDirectionValues.Sales,
                "customer_invoice",
                "unsupported_fixture",
                100m,
                2,
                AccountingRoundingModeValues.MidpointToEven,
                StatutoryVatRegistrationStatusValues.Registered,
                "SE",
                "unknown",
                new HashSet<string>(),
                "SE",
                "SEK",
                StatutoryBookkeepingMethodValues.Accrual,
                "SEK"));

            Assert.False(decision.IsAllowed);
            Assert.Equal(AccountingTaxDecisionReasonCodes.RuleUnavailable, decision.ReasonCode);
            Assert.Equal(0m, decision.TaxableBasis);
            Assert.Equal(0m, decision.TaxAmount);
            Assert.Equal(0m, decision.GrossAmount);
            Assert.Null(decision.RuleKey);
            Assert.Null(decision.LiabilityAccountRoleKey);
            Assert.Null(decision.RecoverableAccountRoleKey);
            Assert.Empty(decision.VatBoxMappings);
        }

        Assert.Equal(18, scenarios.Length);
    }

    private static string FixtureDirectory() => Path.Combine(RepositoryRoot(), "docs", "finance", "swedish-domestic-vat-launch-2026.1");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "financial-app-r1-prompts.md")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
