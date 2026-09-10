using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class SalesRoomOperationsPolicyTests
{
    private static readonly DateTime Now = new(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Spend_uses_provider_billed_audio_and_reported_tokens()
    {
        var options = Valid();

        var spend = SalesRoomOperationsPolicy.EstimatedSpend(120_000, 1_000_000, 500_000, options);

        Assert.Equal(20.034m, spend);
    }

    [Fact]
    public void Enabled_agent_requires_current_rate_evidence_and_bounded_capacity()
    {
        var options = Valid();
        Assert.Null(SalesRoomOperationsPolicy.ConfigurationProblem(options, Now));

        options.ProviderRateCheckedUtc = Now.AddDays(-32);
        Assert.Equal("provider_rates_stale", SalesRoomOperationsPolicy.ConfigurationProblem(options, Now));

        options.ProviderRateCheckedUtc = Now;
        options.MaximumActiveAgentsPerCompany = options.MaximumActiveAgentsGlobal + 1;
        Assert.Equal("invalid_concurrency_limits", SalesRoomOperationsPolicy.ConfigurationProblem(options, Now));
    }

    [Fact]
    public void Disabled_agent_does_not_require_provider_cost_secrets()
    {
        Assert.Null(SalesRoomOperationsPolicy.ConfigurationProblem(new SalesRoomAgentOptions(), Now));
    }

    private static SalesRoomAgentOptions Valid() => new()
    {
        Enabled = true,
        MaximumActiveAgentsGlobal = 50,
        MaximumActiveAgentsPerCompany = 2,
        MaximumInputTokenCostPerMillionUsd = 10m,
        MaximumOutputTokenCostPerMillionUsd = 20m,
        TranscriptionCostPerMinuteUsd = 0.017m,
        MaximumSpendPerCallUsd = 10m,
        MaximumMonthlySpendPerCompanyUsd = 100m,
        CostCurrency = "USD",
        ProviderRateCheckedUtc = Now,
        ProviderRateMaximumAgeDays = 31,
        ProviderRateCardReference = "approved-change/VC-123"
    };
}
