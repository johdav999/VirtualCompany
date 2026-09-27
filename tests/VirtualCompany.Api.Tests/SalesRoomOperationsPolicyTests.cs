using VirtualCompany.Infrastructure.Sales;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Api.Tests;

public sealed class SalesRoomOperationsPolicyTests
{
    private static readonly DateTime Now = new(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Audio_limit_is_cumulative_across_restarts_and_enforced_at_boundary(bool input)
    {
        var organizer = Guid.NewGuid(); var owner = Guid.NewGuid();
        var room = new SalesBrowserRoom(Guid.NewGuid(), Guid.NewGuid(), organizer, Now.AddHours(1), Now);
        room.Provisioned("fixture"); room.Start(Now, 60);
        room.StartAgent(Guid.NewGuid(), organizer, owner, Now.AddSeconds(30), Now);
        var options = Valid(); options.MaximumInputAudioSeconds = options.MaximumOutputAudioSeconds = 60;
        if (input) room.RecordAgentAudio(owner, room.AgentGeneration, 60_000, 60_000, 59_999, 0, 0, 0);
        else room.AgentSpeechCompleted(owner, room.AgentGeneration, 59_999);
        Assert.Null(SalesRoomOperationsPolicy.AudioLimitProblem(room, options));
        if (input) room.RecordAgentAudio(owner, room.AgentGeneration, 1, 1, 1, 0, 0, 0);
        else room.AgentSpeechCompleted(owner, room.AgentGeneration, 1);
        var problem = SalesRoomOperationsPolicy.AudioLimitProblem(room, options);
        Assert.Contains(input ? "microphone input" : "spoken output", problem);
        Assert.Contains("earlier runs", problem);
        room.StopAgent("quota_exceeded", problem, Now);
        room.StartAgent(Guid.NewGuid(), organizer, Guid.NewGuid(), Now.AddSeconds(30), Now);
        Assert.Equal(problem, SalesRoomOperationsPolicy.AudioLimitProblem(room, options));
        if (input) options.MaximumInputAudioSeconds = 120;
        else options.MaximumOutputAudioSeconds = 120;
        Assert.Null(SalesRoomOperationsPolicy.AudioLimitProblem(room, options));
    }

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
    public void Enabled_agent_accepts_an_explicit_local_rate_timestamp_from_configuration_binding()
    {
        var options = Valid();
        options.ProviderRateCheckedUtc = Now.ToLocalTime();

        Assert.Null(SalesRoomOperationsPolicy.ConfigurationProblem(options, Now));
    }

    [Fact]
    public void Enabled_agent_rejects_an_ambiguous_rate_timestamp()
    {
        var options = Valid();
        options.ProviderRateCheckedUtc = DateTime.SpecifyKind(Now, DateTimeKind.Unspecified);

        Assert.Equal("provider_rates_stale", SalesRoomOperationsPolicy.ConfigurationProblem(options, Now));
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
