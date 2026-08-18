using System.Text.Json.Nodes;
using VirtualCompany.Application.GuidedWork;

namespace VirtualCompany.Api.Tests;

public sealed class GuidedDialogueEvaluationCorpusTests
{
    [Fact]
    public void Corpus_covers_every_definition_and_required_adversarial_scenario()
    {
        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "tests", "VirtualCompany.Api.Tests", "Fixtures", "guided-dialogue-evaluation-corpus.json")))!.AsObject();
        var artifacts = root["artifactTypes"]!.AsArray().Select(x => x!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var scenarios = root["scenarios"]!.AsArray().Select(x => x!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var expectedArtifacts = new[] { GuidedArtifactTypes.AgentOperatingBrief, GuidedArtifactTypes.MarketingStrategy, GuidedArtifactTypes.MarketingSegment,
            GuidedArtifactTypes.FinanceBudget, GuidedArtifactTypes.SalesCampaignPlan, GuidedArtifactTypes.SupportSlaPolicy };
        var expectedScenarios = new[] { "complete_session", "ambiguous_answer", "direct_correction", "recommendation_not_fact", "contradictory_evidence",
            "stale_evidence", "missing_information", "malicious_prompt_content", "provider_refusal", "invalid_structured_output", "version_conflict" };

        Assert.Equal(expectedArtifacts.Order(), artifacts.Order());
        Assert.All(expectedScenarios, scenario => Assert.Contains(scenario, scenarios));
        Assert.Equal(66, artifacts.Count * scenarios.Count);
        Assert.Equal(0, root["thresholds"]!["unsafeCommitPercent"]!.GetValue<int>());
        Assert.Equal(0, root["thresholds"]!["tenantLeakagePercent"]!.GetValue<int>());
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "VirtualCompany.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
