using VirtualCompany.Infrastructure.Finance;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class CompanySimulationGenerationDecisionPolicyTests
{
    [Fact]
    public void Same_seed_start_date_and_configuration_produce_the_same_generation_schedule()
    {
        var policy = new SeededCompanySimulationGenerationDecisionPolicy();
        var startSimulatedUtc = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        const string deterministicConfiguration = """{"profile":"baseline","speed":"1d/10s","schedule":"default"}""";

        var first = policy.BuildDecisionSequence(startSimulatedUtc, true, 73, deterministicConfiguration, 20);
        var second = policy.BuildDecisionSequence(startSimulatedUtc, true, 73, deterministicConfiguration, 20);

        Assert.Equal(first.Count, second.Count);
        for (var index = 0; index < first.Count; index++)
        {
            Assert.Equal(first[index].SimulatedDateUtc, second[index].SimulatedDateUtc);
            Assert.Equal(first[index].DayIndex, second[index].DayIndex);
            Assert.Equal(first[index].GenerationEnabled, second[index].GenerationEnabled);
            Assert.Equal(first[index].ShouldGenerate, second[index].ShouldGenerate);
        }
    }

    [Fact]
    public void Different_seed_changes_the_generation_schedule()
    {
        var policy = new SeededCompanySimulationGenerationDecisionPolicy();
        var startSimulatedUtc = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        const string deterministicConfiguration = """{"profile":"baseline","speed":"1d/10s","schedule":"default"}""";

        var first = policy.BuildDecisionSequence(startSimulatedUtc, true, 11, deterministicConfiguration, 40);
        var second = policy.BuildDecisionSequence(startSimulatedUtc, true, 12, deterministicConfiguration, 40);

        var diverged = false;
        for (var index = 0; index < first.Count; index++)
        {
            if (first[index].ShouldGenerate != second[index].ShouldGenerate)
            {
                diverged = true;
                break;
            }
        }

        Assert.True(diverged);
    }

    [Fact]
    public void Generation_disabled_forces_all_scheduled_decisions_off()
    {
        var policy = new SeededCompanySimulationGenerationDecisionPolicy();
        var startSimulatedUtc = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);

        var decisions = policy.BuildDecisionSequence(
            startSimulatedUtc,
            generationEnabled: false,
            seed: 99,
            deterministicConfigurationJson: """{"profile":"baseline"}""",
            days: 10);

        Assert.Equal(11, decisions.Count);
        foreach (var decision in decisions)
        {
            Assert.False(decision.GenerationEnabled);
            Assert.False(decision.ShouldGenerate);
            Assert.True(decision.DayIndex >= 0);
            Assert.True(decision.SimulatedDateUtc >= startSimulatedUtc);
        }
    }
}
