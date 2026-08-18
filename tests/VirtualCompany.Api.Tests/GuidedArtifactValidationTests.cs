using System.Text.Json.Nodes;
using VirtualCompany.Infrastructure.Marketing;
using VirtualCompany.Infrastructure.Support;

namespace VirtualCompany.Api.Tests;

public sealed class GuidedArtifactValidationTests
{
    [Fact]
    public async Task Segment_requires_complete_authoritative_scorecard_and_ordered_size_range()
    {
        var definition = new MarketingSegmentGuidedArtifactDefinition(null!, null!);
        var values = CompleteSegment();
        values["size_low"] = 200;
        values["size_high"] = 100;
        values["score_dimensions"] = "{\"sizeGrowth\":50}";

        var gaps = await definition.ValidateAsync(Guid.NewGuid(), Guid.NewGuid(), null, values, default);

        Assert.Contains(gaps, x => x.Contains("size high", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(gaps, x => x.Contains("nine segment score", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Segment_review_uses_backend_score_and_states_commit_boundary()
    {
        var definition = new MarketingSegmentGuidedArtifactDefinition(null!, null!);
        var insights = await definition.BuildReviewInsightsAsync(Guid.NewGuid(), Guid.NewGuid(), null, CompleteSegment(), default);

        Assert.Contains(insights, x => x.Label == "Backend attractiveness score" && x.Value == "68.00");
        Assert.Contains(insights, x => x.Meaning.Contains("cannot override", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(insights, x => x.Meaning.Contains("does not approve", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Support_rejects_resolution_before_response_and_risk_after_resolution()
    {
        var definition = new SupportSlaGuidedArtifactDefinition(null!, null!);
        var values = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"]="Priority response", ["category"]="incident", ["priority"]="high",
            ["first_response_minutes"]=120, ["resolution_minutes"]=60, ["risk_threshold_minutes"]=90,
            ["is_active"]=true, ["time_basis"]="elapsed", ["escalation_recipient_role"]="support_supervisor"
        };

        var gaps = await definition.ValidateAsync(Guid.NewGuid(), Guid.NewGuid(), null, values, default);

        Assert.Contains(gaps, x => x.Contains("resolution time", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(gaps, x => x.Contains("risk threshold", StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, JsonNode?> CompleteSegment() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"]="Nordic operations teams", ["description"]="Mid-market operations teams in the Nordics.",
        ["criteria"]="{\"region\":\"Nordics\"}", ["needs"]="{\"need\":\"coordination\"}",
        ["behaviors"]="{\"behavior\":\"evaluates quarterly\"}", ["channels"]="{\"channel\":\"search\"}",
        ["pricing"]="{\"sensitivity\":\"medium\"}", ["size_low"]=100, ["size_high"]=200,
        ["size_method"]="Company registry estimate", ["confidence"]=0.8m,
        ["economics"]="{\"ltv\":12000}", ["evidence"]="{\"source\":\"reviewed registry\"}",
        ["score_dimensions"]="{\"sizeGrowth\":60,\"needIntensity\":70,\"productFit\":80,\"differentiation\":70,\"reachability\":60,\"priceValueFit\":70,\"economics\":80,\"evidenceQuality\":70,\"risk\":50}",
        ["target_rationale"]="Strong fit with observable buying signals."
    };
}
