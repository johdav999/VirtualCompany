using VirtualCompany.Application.Marketing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Sales;
using VirtualCompany.Application.Security;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;

namespace VirtualCompany.Api.Tests;

public sealed class MarketingStrategyAndOperatingLoopTests
{
    [Fact]
    public void Strategy_RequiresReviewAndApprovalBeforeActivation()
    {
        var item = new MarketingStrategy(Guid.NewGuid(), Guid.NewGuid(), "Growth strategy", "Summary", "Context",
            DateTime.UtcNow, DateTime.UtcNow.AddMonths(3), Guid.NewGuid(), "{}", "[]", "[]", "strategy-1");

        Assert.Equal(MarketingStrategicStatuses.Draft, item.Status);
        Assert.Throws<InvalidOperationException>(() => item.Activate());
        item.Submit(Guid.NewGuid());
        item.MarkApproved();
        item.Activate();
        Assert.Equal(MarketingStrategicStatuses.Active, item.Status);
    }

    [Fact]
    public void Strategy_CancellationRequiresCurrentVersionAndLeavesTerminalEvidence()
    {
        var item = new MarketingStrategy(Guid.NewGuid(), Guid.NewGuid(), "Growth strategy", "Summary", "Context",
            DateTime.UtcNow, DateTime.UtcNow.AddMonths(3), Guid.NewGuid(), "{}", "[]", "[]", "strategy-cancel");
        Assert.Throws<InvalidOperationException>(() => item.Cancel(2));
        item.Cancel(1);
        Assert.Equal(MarketingStrategicStatuses.Cancelled, item.Status);
        Assert.Equal(2, item.Version);
        Assert.Throws<InvalidOperationException>(() => item.Cancel(2));
    }

    [Fact]
    public void SegmentVersion_RejectsSensitiveTargetingCriteria()
    {
        Assert.Throws<InvalidOperationException>(() => CreateSegmentVersion("{\"race\":\"example\"}"));
    }

    [Fact]
    public void Intelligence_UpdateRequiresCurrentVersionAndArchivePreservesTerminalState()
    {
        var item = new MarketingIntelligenceRecord(Guid.NewGuid(), Guid.NewGuid(), "competitor_claim",
            "Competitor message", "A dated claim", "inferred", .55m, "external_reference", "source-1",
            DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(10), "{}", Guid.NewGuid());
        item.Update(1, "Competitor message updated", "Updated bounded summary", "estimated", .65m,
            "external_reference", "source-2", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(20), "{\"price\":true}");
        Assert.Equal(2, item.Version);
        Assert.Equal("pending", item.ReviewStatus);
        Assert.Throws<InvalidOperationException>(() => item.Update(1, "Stale", "Stale", "inferred", .5m,
            "external_reference", "source", DateTime.UtcNow, DateTime.UtcNow.AddDays(1), "{}"));
        item.Archive(2);
        Assert.True(item.IsArchived);
        Assert.Throws<InvalidOperationException>(() => item.Update(3, "No", "No", "inferred", .5m,
            "external_reference", "source", DateTime.UtcNow, DateTime.UtcNow.AddDays(1), "{}"));
    }

    [Fact]
    public void IntelligenceReview_PreservesBeforeAndAfterEvidence()
    {
        var review = new MarketingIntelligenceReview(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2,
            Guid.NewGuid(), "verified", "Checked against the source.", "{\"confidence\":0.5}", "{\"confidence\":0.8}");
        Assert.Equal(2, review.ReviewNumber);
        Assert.Contains("0.5", review.BeforeJson);
        Assert.Contains("0.8", review.AfterJson);
    }

    [Fact]
    public void SegmentScore_IsDeterministicAndRiskAdjusted()
    {
        var score = SegmentAttractivenessPolicy.Calculate(new Dictionary<string, decimal>
        {
            ["sizeGrowth"] = 80, ["needIntensity"] = 80, ["productFit"] = 80,
            ["differentiation"] = 80, ["reachability"] = 80, ["priceValueFit"] = 80,
            ["economics"] = 80, ["evidenceQuality"] = 80, ["risk"] = 20
        });
        Assert.Equal(80m, score);
    }

    [Fact]
    public void SegmentDimension_PreservesQueryableCategoryClassificationAndNumericValue()
    {
        var companyId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var item = new MarketingSegmentDimension(Guid.NewGuid(), companyId, versionId,
            "price_sensitivity", "$.elasticity", "0.35", "estimated", .35m);

        Assert.Equal(companyId, item.CompanyId);
        Assert.Equal(versionId, item.MarketingCustomerSegmentVersionId);
        Assert.Equal("price_sensitivity", item.Category);
        Assert.Equal("estimated", item.Classification);
        Assert.Equal(.35m, item.NumericValue);
    }

    [Fact]
    public void MarketingToolRegistry_ContainsReadAndRecommendToolsButNoExecuteTools()
    {
        var registry = new StaticCompanyToolRegistry();
        foreach (var tool in MarketingToolIds.ReadTools.Concat(MarketingToolIds.RecommendTools))
            Assert.True(registry.TryGetToolDefinition(tool, out _), tool);

        var definitions = registry.ListToolDefinitions().Where(x => x.ToolName.StartsWith("marketing.")).ToArray();
        Assert.NotEmpty(definitions);
        Assert.DoesNotContain(definitions, x => x.ActionType == ToolActionType.Execute);
    }

    [Fact]
    public void OperatingRun_RecordsBlockedPauseAsRecoverableTerminalState()
    {
        var run = new MarketingOperatingRun(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "cadence", "daily",
            "key", "correlation", null, null, null, "recommend", 1, "evidence-v1", 100m);
        run.Block("company_paused", "The company is paused.");
        Assert.Equal("blocked", run.Status);
        Assert.Equal("company_paused", run.RecoveryCode);
        Assert.NotNull(run.CompletedUtc);
    }

    [Theory]
    [InlineData("linkedin", "publish_post", 3000)]
    [InlineData("meta", "publish_facebook_post", 2200)]
    [InlineData("x", "publish_post", 280)]
    public void ChannelAdapters_EnforceProviderSpecificTextLimits(string provider, string action, int limit)
    {
        IMarketingChannelAdapter adapter = provider switch
        {
            "linkedin" => new LinkedInMarketingChannelAdapter(),
            "meta" => new MetaMarketingChannelAdapter(),
            _ => new XMarketingChannelAdapter()
        };
        Assert.True(adapter.Validate(action, $"{{\"text\":\"{new string('a', limit)}\"}}", "{}").Allowed);
        Assert.False(adapter.Validate(action, $"{{\"text\":\"{new string('a', limit + 1)}\"}}", "{}").Allowed);
    }

    [Fact]
    public void ChannelAction_CannotQueueWithoutApprovalBoundary()
    {
        var action = new MarketingChannelAction(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null,
            "company-page", "publish_post", "{\"text\":\"Hello\"}", null, "action-1");
        Assert.Throws<InvalidOperationException>(() => action.Queue());
        action.Submit(Guid.NewGuid());
        action.Queue();
        Assert.Equal("queued", action.Status);
    }

    [Fact]
    public void ChannelAction_ClaimsBeforeDispatchAndParksAmbiguousOutcome()
    {
        var action = new MarketingChannelAction(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null,
            "company-page", "publish_post", "{\"text\":\"Hello\"}", null, "action-ambiguous");
        action.Submit(Guid.NewGuid());
        action.Queue();
        Assert.Throws<InvalidOperationException>(() => action.RecordDispatch("provider-id"));
        action.ClaimForDispatch();
        action.RecordAmbiguous("provider_outcome_unknown");
        Assert.Equal("ambiguous", action.Status);
        Assert.Equal(1, action.AttemptCount);
    }

    [Fact]
    public void ChannelAction_PinsContentVersionAndCanReconcileAmbiguousOutcome()
    {
        var action = new MarketingChannelAction(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(),
            "company-page", "publish_post", "{\"text\":\"Hello\"}", null, "action-versioned", 4);
        Assert.Equal(4, action.ContentBriefVersion);
        action.Submit(Guid.NewGuid()); action.Queue(); action.ClaimForDispatch(); action.RecordAmbiguous("provider_outcome_unknown");
        action.Reconcile(true);
        Assert.Equal("delivered", action.Status);
        Assert.Null(action.FailureCode);
    }

    [Fact]
    public void ChannelAction_RequiresVersionWhenContentBriefIsLinked()
    {
        Assert.Throws<ArgumentException>(() => new MarketingChannelAction(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            null, Guid.NewGuid(), "company-page", "publish_post", "{\"text\":\"Hello\"}", null, "missing-version"));
    }

    [Fact]
    public void OAuthSession_IsSingleUseAndExpiresClosed()
    {
        var now = DateTime.UtcNow;
        var session = new MarketingChannelOAuthSession(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "linkedin",
            new string('a', 64), "https://company.example/api/marketing/channel-oauth/callback", now.AddMinutes(10));
        session.Consume(now);
        Assert.Equal("consumed", session.Status);
        Assert.Throws<InvalidOperationException>(() => session.Consume(now.AddSeconds(1)));
        var expired = new MarketingChannelOAuthSession(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "x",
            new string('b', 64), "https://company.example/api/marketing/channel-oauth/callback", now.AddSeconds(1));
        Assert.Throws<InvalidOperationException>(() => expired.Consume(now.AddSeconds(2)));
    }

    [Fact]
    public void ChannelAdapter_BlocksCapabilityNotGrantedByConnection()
    {
        var adapter = new LinkedInMarketingChannelAdapter();
        var result = adapter.Validate("publish_post", "{\"text\":\"Hello\"}", "{\"actions\":[\"read_posts\"]}");
        Assert.False(result.Allowed);
        Assert.Equal("connection_capability_missing", result.ReasonCode);
    }

    [Fact]
    public void LifecycleJourney_RequiresApprovalBeforeActivation()
    {
        var journey = new MarketingLifecycleJourney(Guid.NewGuid(), Guid.NewGuid(), "Onboarding", "{}", "{}", "[]", "{}",
            3, DateTime.UtcNow, DateTime.UtcNow.AddMonths(1), Guid.NewGuid(), "journey-1");
        Assert.Throws<InvalidOperationException>(() => journey.Activate());
        journey.Submit(Guid.NewGuid());
        journey.Activate();
        journey.Pause();
        journey.Resume();
        Assert.Equal("active", journey.Status);
        Assert.Equal(1, journey.Version);
        Assert.Equal(5, journey.ConcurrencyVersion);
    }

    [Fact]
    public void LifecycleJourney_NewDefinitionVersionHasExplicitLineageAndStableVersion()
    {
        var priorId = Guid.NewGuid();
        var journey = new MarketingLifecycleJourney(Guid.NewGuid(), Guid.NewGuid(), "Onboarding v2", "{}", "{}", "[]", "{}",
            3, DateTime.UtcNow, DateTime.UtcNow.AddMonths(1), Guid.NewGuid(), "journey-2", priorId, 2);
        journey.Submit(Guid.NewGuid());
        journey.Activate();
        Assert.Equal(priorId, journey.SupersedesJourneyId);
        Assert.Equal(2, journey.Version);
        Assert.Equal(3, journey.ConcurrencyVersion);
    }

    [Fact]
    public void JourneyEnrollment_EnforcesFrequencyStateAndTerminalCompletion()
    {
        var enrollment = new MarketingJourneyEnrollment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), 2, "audience-snapshot:1", "enrollment-1", DateTime.UtcNow);
        enrollment.Advance(Guid.NewGuid(), DateTime.UtcNow.AddHours(1));
        Assert.Equal(1, enrollment.NextStepIndex);
        Assert.Equal(1, enrollment.ActionsInWindow);
        enrollment.Complete();
        Assert.Equal("completed", enrollment.Status);
        Assert.Null(enrollment.NextStepUtc);
    }

    [Fact]
    public async Task OpenAiCreativeGenerator_DecodesImageAndRetainsProviderProvenance()
    {
        var expected = Encoding.UTF8.GetBytes("image-bytes");
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent($"{{\"data\":[{{\"b64_json\":\"{Convert.ToBase64String(expected)}\"}}]}}", Encoding.UTF8, "application/json") };
            response.Headers.Add("x-request-id", "req-creative-1");
            return response;
        });
        var generator = new OpenAiMarketingCreativeImageGenerator(new StubHttpClientFactory(handler),
            new StubSecretStore(), Options.Create(new MarketingCreativeImageOptions { Enabled = true, Model = "gpt-image-2" }));
        var result = await generator.GenerateAsync(new MarketingCreativeImageRequest("Grounded prompt", "1024x1024", "medium", "png"), default);
        Assert.Equal(expected, result.Content);
        Assert.Equal("image/png", result.ContentType);
        Assert.Equal("gpt-image-2", result.ProviderModel);
        Assert.Equal("req-creative-1", result.ProviderRequestId);
    }

    [Fact]
    public void CreativeAsset_RegenerationRetainsFamilyAndIncrementsImmutableVersion()
    {
        var companyId = Guid.NewGuid(); var briefId = Guid.NewGuid(); var ownerId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var asset = new MarketingCreativeAsset(Guid.NewGuid(), companyId, briefId, null, "Hero", "image/png",
            "1024x1024", "en", "Generated", "creative-v1", "request-2", "brand-v3", "passed",
            "Product illustration", "companies/example/marketing/creative/hero.png", "checksum", ownerId,
            "asset-version-2", familyId, 2, Guid.NewGuid(), "[]",
            "{\"origin\":\"ai_generated\",\"copyrightStatus\":\"not_established\"}");
        Assert.Equal(familyId, asset.AssetFamilyId);
        Assert.Equal(2, asset.VersionNumber);
        Assert.Equal(MarketingStatuses.Draft, asset.Status);
        Assert.Contains("copyrightStatus", asset.ProvenanceJson);
        Assert.Contains("marketing-creative:", asset.AuditReference);
        asset.UpdateMetadata("Hero updated", "sv", "Tillgänglig beskrivning");
        Assert.Equal("Hero updated", asset.Name);
        asset.Submit();
        Assert.Throws<InvalidOperationException>(() => asset.UpdateMetadata("Late edit", "sv", "Alt"));
    }

    [Fact]
    public void ContentVariant_RetainsGenerationProvenanceAndVersionFamily()
    {
        var familyId = Guid.NewGuid(); var runId = Guid.NewGuid();
        var variant = new MarketingContentVariant(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "LinkedIn A",
            "Grounded copy", "[\"knowledge-chunk:1\"]", true, "social_post", runId, "1.0.0",
            "marketing-content-v1", "generation-batch-1", 0, familyId, 3);
        Assert.Equal(familyId, variant.VariantFamilyId);
        Assert.Equal(3, variant.VersionNumber);
        Assert.Equal(runId, variant.GenerationRunId);
        Assert.Equal("social_post", variant.ContentFormat);
    }

    [Fact]
    public void MarketingPolicy_RequiresApprovalAndNeverLetsApprovalOverrideConsent()
    {
        var policies = new MarketingPolicyService();
        var targetId = Guid.NewGuid();
        var review = policies.Evaluate(new MarketingPolicyRequest(MarketingPolicyActions.ContentPublication,
            "marketing_channel_action", targetId, 3, true));
        Assert.False(review.Allowed);
        Assert.True(review.RequiresApproval);
        Assert.Equal("approval_required", review.ReasonCode);

        var denied = policies.Evaluate(new MarketingPolicyRequest(MarketingPolicyActions.OutboundCommunication,
            "marketing_channel_action", targetId, 3, true, ApprovalCompleted: true, ConsentCurrent: false));
        Assert.False(denied.Allowed);
        Assert.False(denied.RequiresApproval);
        Assert.Equal("consent_not_current", denied.ReasonCode);
    }

    [Fact]
    public void MarketingPolicy_BlocksSensitiveTargetingAndMissingSpendConfiguration()
    {
        var policies = new MarketingPolicyService();
        var sensitive = policies.Evaluate(new MarketingPolicyRequest(MarketingPolicyActions.TargetSelection,
            "marketing_segment_version", Guid.NewGuid(), 1, true, SegmentCriteriaJson: "{\"religion\":\"any\"}"));
        Assert.Equal("sensitive_segment_criteria", sensitive.ReasonCode);

        var spend = policies.Evaluate(new MarketingPolicyRequest(MarketingPolicyActions.PaidSpend,
            "marketing_campaign", Guid.NewGuid(), 1, true, Amount: 1000m));
        Assert.Equal("spend_policy_missing", spend.ReasonCode);
    }

    private static MarketingCustomerSegmentVersion CreateSegmentVersion(string criteria) => new(Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), 1, criteria, "{}", "{}", "{}", "{}", 100, 200,
        "triangulated", .7m, "{}", "{}", 75m, "[]", DateTime.UtcNow, Guid.NewGuid(), "segment-v1");

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(responder(request)); }
    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, disposeHandler: false); }
    private sealed class StubSecretStore : IPlatformSecretStore
    {
        public string BackendName => "test"; public bool SupportsWrites => false;
        public Task<PlatformSecretValue?> GetAsync(string name, string? version, CancellationToken cancellationToken) =>
            Task.FromResult<PlatformSecretValue?>(new("test-key", "v1", DateTime.UtcNow));
        public Task<PlatformSecretWriteResult> SetAsync(string name, string value, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
