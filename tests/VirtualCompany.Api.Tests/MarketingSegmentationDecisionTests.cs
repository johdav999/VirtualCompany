using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class MarketingSegmentationDecisionTests
{
    [Fact]
    public void Bootstrap_grounding_creates_a_reviewable_assumption_when_ai_claims_are_unusable()
    {
        var result = MarketingSegmentProposalGrounding.Evaluate(AgentAiRunStatuses.NeedsReview,
            [new AgentAiClaim("Unsupported", "recommendation", .8m, ["missing-source"])],
            ["marketing-state:empty"]);

        Assert.True(result.CanCreateDraft);
        Assert.True(result.UsedBootstrapFallback);
        Assert.Equal(1, result.RejectedClaimCount);
        var claim = Assert.Single(result.Claims);
        Assert.Equal("unknown", claim.Type);
        Assert.Equal(["marketing-state:empty"], claim.SourceIds);
    }

    [Fact]
    public void Bootstrap_grounding_does_not_make_failed_ai_runs_reviewable()
    {
        var result = MarketingSegmentProposalGrounding.Evaluate(AgentAiRunStatuses.Failed, [],
            ["marketing-state:empty"]);

        Assert.False(result.CanCreateDraft);
        Assert.Empty(result.Claims);
    }

    [Fact]
    public void Bootstrap_grounding_keeps_a_reviewable_draft_available_when_the_provider_times_out()
    {
        var result = MarketingSegmentProposalGrounding.Evaluate(AgentAiRunStatuses.Failed, [],
            ["marketing-state:empty"], "provider_timeout");

        Assert.True(result.CanCreateDraft);
        Assert.True(result.UsedBootstrapFallback);
        var claim = Assert.Single(result.Claims);
        Assert.Equal("unknown", claim.Type);
        Assert.Equal(.2m, claim.Confidence);
    }

    [Fact]
    public void Segment_grounding_accepts_the_shared_reasoning_claim_schema()
    {
        var result = MarketingSegmentProposalGrounding.Evaluate(AgentAiRunStatuses.Completed,
            [new AgentAiClaim("Customers report a recurring need.", "confirmed_fact", .8m, ["customer-source"])],
            ["customer-source"]);

        Assert.True(result.CanCreateDraft);
        Assert.False(result.UsedBootstrapFallback);
        Assert.Equal(0, result.RejectedClaimCount);
        Assert.Single(result.Claims);
    }
    [Fact]
    public void Size_estimate_requires_explicit_supported_method_and_valid_range()
    {
        var companyId = Guid.NewGuid(); var versionId = Guid.NewGuid();
        var item = new MarketingSegmentSizeEstimate(Guid.NewGuid(), companyId, versionId, 4_800, 7_400,
            "companies", "FY2026", "Nordics", null, "triangulated", "[]", "[\"source-1\"]",
            0.72m, DateTime.UtcNow.AddDays(-5), DateTime.UtcNow, "estimated");

        Assert.Equal("triangulated", item.Method);
        Assert.Throws<ArgumentException>(() => new MarketingSegmentSizeEstimate(Guid.NewGuid(), companyId,
            versionId, 10, 5, "companies", "FY2026", "Nordics", null, "top_down", "[]", "[]", .5m,
            DateTime.UtcNow, DateTime.UtcNow, "estimated"));
        Assert.Throws<ArgumentException>(() => new MarketingSegmentSizeEstimate(Guid.NewGuid(), companyId,
            versionId, 1, 2, "companies", "FY2026", "Nordics", null, "made_up", "[]", "[]", .5m,
            DateTime.UtcNow, DateTime.UtcNow, "estimated"));
    }

    [Fact]
    public void Score_policy_makes_missing_evidence_behavior_explicit()
    {
        var companyId = Guid.NewGuid(); var versionId = Guid.NewGuid();
        var policy = new MarketingSegmentScorePolicy(Guid.NewGuid(), companyId, versionId, 70,
            "needs_review", "[]", "{\"fairness\":\"reviewed\"}");
        var dimension = new MarketingSegmentScoreDimension(Guid.NewGuid(), companyId, policy.Id,
            "market_attractiveness", .3m, null, "[]");

        Assert.Equal("needs_review", policy.MissingEvidenceBehavior);
        Assert.Null(dimension.Score);
        Assert.Throws<ArgumentException>(() => new MarketingSegmentScorePolicy(Guid.NewGuid(), companyId,
            versionId, 70, "guess", "[]", "{}"));
    }

    [Fact]
    public void Target_recommendation_is_immutable_and_not_an_activation()
    {
        var item = new MarketingSegmentTargetDecision(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "primary", "The evidence supports review as a primary target.", "{\"expected\":\"qualified reach\"}",
            .74m, "[]", DateTime.UtcNow.AddMonths(3), "recommended", Guid.NewGuid(), null, "recommendation-1");

        Assert.Equal("recommended", item.ApprovalStatus);
        Assert.Null(item.ApprovalRequestId);
        Assert.Equal("primary", item.TargetType);
    }
}
