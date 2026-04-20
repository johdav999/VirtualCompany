using System.Security.Cryptography;
using System.Text;

namespace VirtualCompany.Infrastructure.Finance;

public sealed record CompanySimulationGenerationDecision(
    DateTime SimulatedDateUtc,
    int DayIndex,
    bool GenerationEnabled,
    bool ShouldGenerate);

public interface ICompanySimulationGenerationDecisionPolicy
{
    CompanySimulationGenerationDecision GetDecision(
        DateTime startSimulatedUtc,
        DateTime simulatedDateUtc,
        bool generationEnabled,
        int seed,
        string? deterministicConfigurationJson);

    IReadOnlyList<CompanySimulationGenerationDecision> BuildDecisionSequence(
        DateTime startSimulatedUtc,
        bool generationEnabled,
        int seed,
        string? deterministicConfigurationJson,
        int days);
}

public sealed class SeededCompanySimulationGenerationDecisionPolicy : ICompanySimulationGenerationDecisionPolicy
{
    public CompanySimulationGenerationDecision GetDecision(
        DateTime startSimulatedUtc,
        DateTime simulatedDateUtc,
        bool generationEnabled,
        int seed,
        string? deterministicConfigurationJson)
    {
        var normalizedStartSimulatedUtc = NormalizeUtc(startSimulatedUtc).Date;
        var normalizedSimulatedDateUtc = NormalizeUtc(simulatedDateUtc).Date;
        var dayIndex = (int)(normalizedSimulatedDateUtc - normalizedStartSimulatedUtc).TotalDays;
        if (dayIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(simulatedDateUtc), "Simulated date cannot be before the simulation start date.");
        }

        return new CompanySimulationGenerationDecision(
            normalizedSimulatedDateUtc,
            dayIndex,
            generationEnabled,
            generationEnabled && ComputeShouldGenerate(normalizedStartSimulatedUtc, dayIndex, seed, deterministicConfigurationJson));
    }

    public IReadOnlyList<CompanySimulationGenerationDecision> BuildDecisionSequence(
        DateTime startSimulatedUtc,
        bool generationEnabled,
        int seed,
        string? deterministicConfigurationJson,
        int days)
    {
        if (days < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(days), "Days must be zero or greater.");
        }

        var normalizedStartSimulatedUtc = NormalizeUtc(startSimulatedUtc).Date;
        var decisions = new List<CompanySimulationGenerationDecision>(days + 1);
        for (var dayIndex = 0; dayIndex <= days; dayIndex++)
        {
            decisions.Add(GetDecision(
                normalizedStartSimulatedUtc,
                normalizedStartSimulatedUtc.AddDays(dayIndex),
                generationEnabled,
                seed,
                deterministicConfigurationJson));
        }

        return decisions;
    }

    private static bool ComputeShouldGenerate(DateTime startSimulatedUtc, int dayIndex, int seed, string? deterministicConfigurationJson)
    {
        // Hash the stable inputs so replayed schedules do not depend on Random implementation details.
        var payload = FormattableString.Invariant($"{seed}|{startSimulatedUtc:O}|{dayIndex}|{NormalizeConfiguration(deterministicConfigurationJson)}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return (hash[0] & 1) == 0;
    }

    private static string NormalizeConfiguration(string? deterministicConfigurationJson) => deterministicConfigurationJson?.Trim() ?? string.Empty;

    private static DateTime NormalizeUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
