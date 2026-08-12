using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Api.Tests;

public sealed class MarketingMeasurementPolicyTests
{
    [Theory]
    [InlineData(100, 40, 0.00, true, false, "insufficient_evidence", false)]
    [InlineData(100, 100, 0.10, true, false, "insufficient_evidence", false)]
    [InlineData(100, 100, 0.00, false, false, "insufficient_evidence", false)]
    [InlineData(100, 100, 0.00, true, true, "stop_guardrail_breach", false)]
    [InlineData(100, 100, 0.00, true, false, "ready_for_decision", true)]
    public void Experiment_policy_is_deterministic_and_reserves_causal_eligibility_for_valid_evidence(
        int minimum, int sample, double contamination, bool quality, bool guardrail, string decision, bool causal)
    {
        var result = MarketingExperimentDecisionPolicy.Evaluate(minimum, sample, (decimal)contamination, quality, guardrail);
        Assert.Equal(decision, result.Decision); Assert.Equal(causal, result.CausalEligible);
    }

    [Fact]
    public void Attribution_only_accepts_explainable_model_types()
    {
        var valid = new MarketingAttributionModelDefinition(Guid.NewGuid(), Guid.NewGuid(), "Even touch",
            "even", 1, "{}", "This is configured attribution, not causal evidence.", 30, "model-1");
        Assert.Equal("even", valid.ModelType);
        Assert.Throws<ArgumentException>(() => new MarketingAttributionModelDefinition(Guid.NewGuid(),
            Guid.NewGuid(), "Black box", "ai_magic", 1, "{}", "Unknown", 30, "model-2"));
    }

    [Fact]
    public void Segment_learning_remains_a_review_proposal()
    {
        var item = new MarketingSegmentLearningProposal(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "{\"reach\":1200,\"conversion\":0.04}", "{\"size\":\"review upward\"}",
            "{\"sources\":[\"observation:1\"]}", .7m, "learning-1");
        Assert.Equal("review_proposed", item.Status);
    }
}
