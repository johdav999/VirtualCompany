using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Orchestration;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class CommunicationStyleRuleCheckerTests
{
    [Fact]
    public void Check_passes_when_output_avoids_forbidden_rules()
    {
        var checker = new CommunicationStyleRuleChecker();
        var profile = CreateProfile();

        var result = checker.Check("The next step is to verify the invoice and request approval.", profile);

        Assert.True(result.Passed);
        Assert.Empty(result.Violations);
        Assert.Empty(result.RuleIds);
    }

    [Fact]
    public void Check_detects_forbidden_tone_marker()
    {
        var checker = new CommunicationStyleRuleChecker();
        var profile = CreateProfile();

        var result = checker.Check("This is flippant and should not be sent.", profile);

        Assert.False(result.Passed);
        var violation = Assert.Single(result.Violations);
        Assert.Equal("forbidden_tone_rule", violation.RuleType);
        Assert.Contains("flippant", violation.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(violation.RuleId, result.RuleIds);
    }

    [Theory]
    [InlineData(PromptGenerationPathValues.Chat, "chat response is flippant")]
    [InlineData(PromptGenerationPathValues.TaskOutput, "task summary is flippant")]
    [InlineData(PromptGenerationPathValues.DocumentGeneration, "generated artifact is flippant")]
    public void Check_applies_forbidden_tone_rules_across_output_paths(string outputPath, string output)
    {
        var checker = new CommunicationStyleRuleChecker();
        var profile = CreateProfile();

        var result = checker.Check(output, profile);

        Assert.False(result.Passed);
        Assert.Contains(result.Violations, violation => violation.RuleType == "forbidden_tone_rule");
        Assert.Contains(outputPath, [PromptGenerationPathValues.Chat, PromptGenerationPathValues.TaskOutput, PromptGenerationPathValues.DocumentGeneration]);
    }

    private static AgentCommunicationProfileDto CreateProfile() =>
        new(
            "professional",
            "reliable business assistant",
            ["Be concise"],
            ["Use business-appropriate language"],
            ["Do not use flippant"],
            AgentCommunicationProfileSources.Explicit,
            false);
}
