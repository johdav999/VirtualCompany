using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class SalesAgentDecisionServiceTests
{
    [Fact]
    public async Task Forecast_is_repeatable_and_keeps_currencies_separate()
    {
        await using var db = CreateDb();
        var companyId = Guid.NewGuid();
        var now = new DateTime(2026, 7, 16, 9, 0, 0, DateTimeKind.Utc);
        db.Deals.AddRange(
            new Deal(Guid.NewGuid(), companyId, "SEK proposal", SalesPipelineStage.ProposalStageId,
                1000m, "SEK", expectedCloseUtc: now.AddDays(20), createdUtc: now.AddDays(-20), updatedUtc: now),
            new Deal(Guid.NewGuid(), companyId, "EUR qualified", SalesPipelineStage.QualifiedStageId,
                2000m, "EUR", expectedCloseUtc: now.AddDays(30), createdUtc: now.AddDays(-20), updatedUtc: now));
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var request = new SalesForecastScenarioRequest(90, .10m, -.10m, now);

        var first = await service.AnalyzeForecastAsync(companyId, Guid.NewGuid(), null, request, CancellationToken.None);
        var second = await service.AnalyzeForecastAsync(companyId, Guid.NewGuid(), null, request, CancellationToken.None);

        Assert.Equal(
            first.Scenarios.Select(x => (x.Scenario, x.GrossPipeline, x.ExpectedRevenue, x.ChangeFromBaseline,
                x.Currency, x.DealCount, x.HighRiskDeals, x.UnknownRiskDeals, x.SourceId)),
            second.Scenarios.Select(x => (x.Scenario, x.GrossPipeline, x.ExpectedRevenue, x.ChangeFromBaseline,
                x.Currency, x.DealCount, x.HighRiskDeals, x.UnknownRiskDeals, x.SourceId)));
        for (var index = 0; index < first.Scenarios.Count; index++)
            Assert.Equal(first.Scenarios[index].Assumptions, second.Scenarios[index].Assumptions);
        Assert.Equal(6, first.Scenarios.Count);
        Assert.Equal(["EUR", "SEK"], first.Scenarios.Select(x => x.Currency).Distinct().Order().ToArray());
        Assert.Equal(650m, first.Scenarios.Single(x => x.Currency == "SEK" && x.Scenario == "commit").ExpectedRevenue);
        Assert.Equal(700m, first.Scenarios.Single(x => x.Currency == "EUR" && x.Scenario == "commit").ExpectedRevenue);
    }

    [Fact]
    public async Task Next_action_cannot_make_outreach_eligible_without_contact_permission()
    {
        await using var db = CreateDb();
        var companyId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var now = new DateTime(2026, 7, 16, 9, 0, 0, DateTimeKind.Utc);
        var lead = new Lead(Guid.NewGuid(), companyId, "Unconsented lead", SalesPipelineStage.NewStageId,
            primaryContactId: contactId, createdUtc: now.AddDays(-20), updatedUtc: now.AddDays(-10));
        db.AddRange(lead, new SalesAutomationPolicy(Guid.NewGuid(), companyId, "assisted"));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.RecommendNextActionsAsync(companyId, Guid.NewGuid(), null,
            new SalesNextBestActionRequest(LeadId: lead.Id, AsOfUtc: now), CancellationToken.None);

        var action = Assert.Single(result.Actions);
        Assert.False(action.CommunicationAllowed);
        Assert.Equal("internal_research", action.Channel);
        Assert.Contains("email_permission_missing", action.ReasonCodes);
        Assert.True(result.RequiresReview);
    }

    [Fact]
    public async Task Proposal_without_catalog_evidence_requires_pricing_approval_and_review()
    {
        await using var db = CreateDb();
        var companyId = Guid.NewGuid();
        var deal = new Deal(Guid.NewGuid(), companyId, "Enterprise proposal",
            SalesPipelineStage.ProposalStageId, 25000m, "SEK", primaryContactId: Guid.NewGuid());
        db.AddRange(deal, new SalesAutomationPolicy(Guid.NewGuid(), companyId, "assisted"));
        await db.SaveChangesAsync();
        var service = CreateService(db, new EmptyKnowledgeSearch());

        var result = await service.AdviseProposalAsync(companyId, Guid.NewGuid(), null,
            new SalesProposalAdviceRequest(deal.Id, "Enterprise", 25000m, "SEK", "Net 60"),
            CancellationToken.None);

        Assert.True(result.PricingApprovalRequired);
        Assert.True(result.TermsApprovalRequired);
        Assert.True(result.RequiresReview);
        Assert.Contains(result.Validations, x => x.Code == "product" && x.Status == "unsupported");
        Assert.Contains(result.Validations, x => x.Code == "price" && x.Status == "review_required");
        Assert.Contains(result.Unknowns, x => x.Contains("catalog", StringComparison.OrdinalIgnoreCase));
    }

    private static SalesAgentDecisionService CreateService(VirtualCompanyDbContext db,
        ICompanyKnowledgeSearchService? knowledge = null) =>
        new(db, new StubAnalysis(), null!, new EmptyCampaignPlanning(), knowledge!);

    private static VirtualCompanyDbContext CreateDb() => new(
        new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private sealed class StubAnalysis : ISalesAgentAnalysisService
    {
        public Task<RoleAgentAnalysisResult> AnalyzeAsync(Guid companyId, Guid agentId, Guid? actorUserId,
            RoleAgentAnalysisRequest request, CancellationToken cancellationToken) => Task.FromResult(
            new RoleAgentAnalysisResult(Guid.Parse("00000000-0000-0000-0000-000000000001"), "test",
                AgentAiRunStatuses.Completed, "Test advice", .8m, request.AsOfUtc ?? DateTime.UtcNow,
                [], [], [], [], [], [], false));
    }

    private sealed class EmptyKnowledgeSearch : ICompanyKnowledgeSearchService
    {
        public Task<IReadOnlyList<CompanyKnowledgeSearchResultDto>> SearchAsync(
            CompanyKnowledgeSemanticSearchQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CompanyKnowledgeSearchResultDto>>([]);
    }

    private sealed class EmptyCampaignPlanning : ICampaignPlanningService
    {
        public Task<CampaignInitiativeResponse?> GetInitiativeAsync(
            Guid companyId, Guid campaignId, CancellationToken cancellationToken) =>
            Task.FromResult<CampaignInitiativeResponse?>(null);

        public Task<CampaignInitiativeResponse?> ConfigureInitiativeAsync(
            Guid companyId, Guid userId, Guid campaignId, ConfigureCampaignInitiativeRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult<CampaignInitiativeResponse?>(null);

        public Task<CampaignReadinessResponse?> GetReadinessAsync(
            Guid companyId, Guid campaignId, CancellationToken cancellationToken) =>
            Task.FromResult<CampaignReadinessResponse?>(null);

        public Task<CampaignInitiativeResponse?> RequestReadinessAsync(
            Guid companyId, Guid userId, Guid campaignId, long expectedVersion,
            CancellationToken cancellationToken) =>
            Task.FromResult<CampaignInitiativeResponse?>(null);

        public Task<IReadOnlyList<CampaignSegmentResponse>> ListSegmentsAsync(
            Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CampaignSegmentResponse>>([]);

        public Task<CampaignSegmentResponse> CreateSegmentAsync(
            Guid companyId, Guid userId, CreateCampaignSegmentRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CampaignAudiencePreviewResponse> PreviewSegmentAsync(
            Guid companyId, Guid segmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CampaignAudienceSnapshotResponse?> CaptureAudienceAsync(
            Guid companyId, Guid userId, Guid campaignId, Guid segmentId,
            CancellationToken cancellationToken) =>
            Task.FromResult<CampaignAudienceSnapshotResponse?>(null);

        public Task<IReadOnlyList<CampaignActivityResponse>> ListActivitiesAsync(
            Guid companyId, Guid campaignId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CampaignActivityResponse>>([]);

        public Task<CampaignActivityResponse?> AddActivityAsync(
            Guid companyId, Guid userId, Guid campaignId, CreateCampaignActivityRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult<CampaignActivityResponse?>(null);

        public Task<CampaignPerformanceResponse?> GetPerformanceAsync(
            Guid companyId, Guid campaignId, CancellationToken cancellationToken) =>
            Task.FromResult<CampaignPerformanceResponse?>(null);

        public Task<CampaignPerformanceResponse?> CapturePerformanceSnapshotAsync(
            Guid companyId, Guid userId, Guid campaignId, CancellationToken cancellationToken) =>
            Task.FromResult<CampaignPerformanceResponse?>(null);
    }
}
