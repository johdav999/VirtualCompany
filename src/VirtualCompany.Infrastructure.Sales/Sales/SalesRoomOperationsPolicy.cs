using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Sales;

internal static class SalesRoomOperationsPolicy
{
    internal static string? AudioLimitProblem(SalesBrowserRoom room, SalesRoomAgentOptions options)
    {
        var input = room.AgentForwardedAudioMilliseconds >= options.MaximumInputAudioSeconds * 1000L;
        var output = room.AgentOutputAudioMilliseconds >= options.MaximumOutputAudioSeconds * 1000L;
        if (!input && !output) return null;
        var used = input ? room.AgentForwardedAudioMilliseconds : room.AgentOutputAudioMilliseconds;
        var limit = input ? options.MaximumInputAudioSeconds : options.MaximumOutputAudioSeconds;
        var kind = input ? "microphone input" : "spoken output";
        return FormattableString.Invariant($"This room has used {used / 60000m:0.0} minutes of AI {kind}, reaching its {limit / 60m:0.0}-minute allowance. Usage includes earlier runs in this room; restarting Alex does not reset it. Ask an operator to review the allowance. Human calling and manual slides remain available.");
    }

    internal static string? ConfigurationProblem(SalesRoomAgentOptions options, DateTime nowUtc)
    {
        if (!options.Enabled) return null;
        if (options.DialogueConfigurationProblem is { } dialogueProblem) return dialogueProblem;
        if (options.MaximumActiveAgentsGlobal is < 1 or > 10_000 ||
            options.MaximumActiveAgentsPerCompany is < 1 or > 100 ||
            options.MaximumActiveAgentsPerCompany > options.MaximumActiveAgentsGlobal)
            return "invalid_concurrency_limits";
        if (options.MaximumInputTokenCostPerMillionUsd <= 0 ||
            options.MaximumOutputTokenCostPerMillionUsd <= 0 ||
            options.TranscriptionCostPerMinuteUsd <= 0 ||
            options.MaximumSpendPerCallUsd <= 0 ||
            options.MaximumMonthlySpendPerCompanyUsd <= 0 ||
            !string.Equals(options.CostCurrency, "USD", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(options.ProviderRateCardReference) ||
            options.ProviderRateCardReference.Length > 500)
            return "cost_policy_missing";
        if (options.ProviderRateCheckedUtc.Kind == DateTimeKind.Unspecified)
            return "provider_rates_stale";
        var checkedUtc = options.ProviderRateCheckedUtc.ToUniversalTime();
        if (options.ProviderRateMaximumAgeDays is < 1 or > 90 ||
            checkedUtc > nowUtc.AddMinutes(5) ||
            checkedUtc < nowUtc.AddDays(-options.ProviderRateMaximumAgeDays))
            return "provider_rates_stale";
        return null;
    }

    internal static decimal EstimatedSpend(SalesBrowserRoom room, SalesRoomAgentOptions options) =>
        EstimatedSpend(room.AgentProviderBilledAudioMilliseconds, room.AgentInputTokens,
            room.AgentOutputTokens, options);

    internal static decimal EstimatedSpend(long billedAudioMilliseconds, long inputTokens, long outputTokens,
        SalesRoomAgentOptions options) => Math.Round(
            Math.Max(0, billedAudioMilliseconds) / 60_000m * options.TranscriptionCostPerMinuteUsd +
            Math.Max(0, inputTokens) / 1_000_000m * options.MaximumInputTokenCostPerMillionUsd +
            Math.Max(0, outputTokens) / 1_000_000m * options.MaximumOutputTokenCostPerMillionUsd,
            6, MidpointRounding.AwayFromZero);
}
