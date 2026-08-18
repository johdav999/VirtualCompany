using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class MarketingPlanPortfolioTests
{
    private static readonly DateTime Start = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Grounded_plan_requires_review_before_activation_and_preserves_strategy_version()
    {
        var strategyId = Guid.NewGuid();
        var plan = new MarketingPlan(Guid.NewGuid(), Guid.NewGuid(), "Autumn growth", "Grounded plan", Start,
            Start.AddMonths(3), 100_000m, "sek", idempotencyKey: "plan:autumn", strategyId: strategyId,
            strategyVersion: 7, rationale: "Based on approved strategy", evidenceReferencesJson: "[\"strategy:7\"]");

        Assert.Equal(strategyId, plan.MarketingStrategyId);
        Assert.Equal(7, plan.MarketingStrategyVersion);
        Assert.Throws<InvalidOperationException>(plan.Activate);

        plan.SubmitForReview(Guid.NewGuid());
        plan.MarkApproved();
        plan.Activate();

        Assert.Equal(MarketingStatuses.Active, plan.Status);
    }

    [Fact]
    public void Portfolio_relationships_validate_roles_budgets_and_exact_plan_segments()
    {
        var companyId = Guid.NewGuid(); var planId = Guid.NewGuid(); var segmentVersionId = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => new MarketingPlanSegment(Guid.NewGuid(), companyId, planId,
            segmentVersionId, "invented", 1, "Reason", "Contribution"));
        var segment = new MarketingPlanSegment(Guid.NewGuid(), companyId, planId, segmentVersionId,
            MarketingPlanSegmentRoles.Primary, 1, "Approved target", "Pipeline contribution");
        Assert.Throws<ArgumentOutOfRangeException>(() => new MarketingPlanCampaign(Guid.NewGuid(), companyId,
            planId, Guid.NewGuid(), "Purpose", -1, "SEK", 1, "Contribution", null, "key"));
        var campaign = new MarketingPlanCampaign(Guid.NewGuid(), companyId, planId, Guid.NewGuid(), "Purpose",
            10m, "sek", 1, "Contribution", null, "key");
        var link = new MarketingPlanCampaignSegment(Guid.NewGuid(), companyId, campaign.Id, segment.Id,
            "Exact plan audience", "Reach the primary audience");
        Assert.Equal(segment.Id, link.MarketingPlanSegmentId);
        Assert.Equal("SEK", campaign.BudgetCurrency);
    }

    [Fact]
    public void Marketing_tool_registry_separates_recommendation_and_execution()
    {
        var registry = new StaticCompanyToolRegistry();
        Assert.True(registry.TryGetTool(MarketingToolIds.PreparePlan, out var prepare));
        Assert.Contains(ToolActionType.Recommend, prepare.SupportedActions);
        Assert.DoesNotContain(ToolActionType.Execute, prepare.SupportedActions);
        Assert.True(registry.TryGetTool(MarketingToolIds.CreatePlanDraft, out var create));
        Assert.Contains(ToolActionType.Execute, create.SupportedActions);
        Assert.DoesNotContain(ToolActionType.Recommend, create.SupportedActions);
    }

    [Fact]
    public async Task Work_need_assessment_is_deterministic_and_reports_missing_strategy_without_writes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options;
        await using var db = new VirtualCompanyDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var service = new MarketingWorkNeedAssessment(db); var companyId = Guid.NewGuid();

        var first = await service.AssessAsync(companyId, Start, default);
        var second = await service.AssessAsync(companyId, Start, default);

        var need = Assert.Single(first.Needs, x => x.ReasonCode == "strategy_missing_or_expired");
        Assert.True(need.Actionable);
        Assert.Equal(need.Fingerprint, Assert.Single(second.Needs, x => x.ReasonCode == need.ReasonCode).Fingerprint);
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Sales_campaign_draft_has_no_contacts_steps_or_external_delivery()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options;
        await using var db = new VirtualCompanyDbContext(options); await db.Database.EnsureCreatedAsync();
        var companyId = Guid.NewGuid(); db.Companies.Add(new Company(companyId, "Portfolio test")); await db.SaveChangesAsync();
        var service = new SalesCampaignDraftService(db);
        var result = await service.CreateDraftAsync(new CreateSalesCampaignDraftCommand(companyId, Guid.NewGuid(), null,
            "Awareness draft", "Test the approved message", CampaignTypes.LeadGeneration, "marketing_segment",
            "pipeline", 10, "leads", Start.AddMonths(1), Start, Start.AddDays(7), Start.AddDays(21), Start.AddMonths(1),
            "UTC", 10_000, "SEK", "en", "draft:one"), default);

        var campaign = await db.SalesCampaigns.IgnoreQueryFilters().Include(x => x.Contacts).SingleAsync(x => x.Id == result.CampaignId);
        var sequence = await db.SalesSequences.IgnoreQueryFilters().Include(x => x.Steps).SingleAsync(x => x.Id == result.SequenceId);
        Assert.Empty(campaign.Contacts); Assert.Empty(sequence.Steps);
        Assert.Contains("Add at least one campaign activity.", campaign.ReadinessGaps());
        Assert.Equal(CampaignLifecycleStatuses.Planning, campaign.LifecycleStatus);

        await service.PopulateDraftAsync(new PopulateSalesCampaignDraftCommand(companyId, campaign.Id, Guid.NewGuid(), null,
            Enumerable.Range(1, 4).Select(x => new SalesCampaignDraftStepCommand(x, x - 1, $"Draft {x}", "Internal review draft")).ToArray(),
            "populate:one"), default);
        var populatedSequence = await db.SalesSequences.IgnoreQueryFilters().Include(x => x.Steps).SingleAsync(x => x.Id == result.SequenceId);
        Assert.Equal(4, populatedSequence.Steps.Count);
        Assert.Equal(SalesStatuses.Draft, populatedSequence.Status);
        Assert.Empty(await db.CompanyOutboxMessages.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToArrayAsync());
    }

    [Fact]
    public async Task Grounded_plan_and_campaign_portfolio_are_tenant_scoped_audited_and_idempotent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options;
        await using var db = new VirtualCompanyDbContext(options); await db.Database.EnsureCreatedAsync();
        var companyId = Guid.NewGuid(); var otherCompanyId = Guid.NewGuid(); var userId = Guid.NewGuid();
        db.Companies.AddRange(new Company(companyId, "Portfolio company"), new Company(otherCompanyId, "Other company"));
        var strategy = new MarketingStrategy(Guid.NewGuid(), companyId, "Growth strategy", "Approved direction", "Business context",
            Start.AddDays(-10), Start.AddMonths(6), userId, "{}", "[\"strategy-source\"]", "[]", "strategy:one");
        strategy.Submit(Guid.NewGuid()); strategy.MarkApproved(); strategy.Activate();
        var segment = new MarketingCustomerSegment(Guid.NewGuid(), companyId, "Operations leaders", "Primary audience", userId);
        var segmentVersion = new MarketingCustomerSegmentVersion(Guid.NewGuid(), companyId, segment.Id, 1, "{}", "{}", "{}", "{}", "{}",
            100, 200, "bottom_up", .8m, "{}", "{}", 80, "[\"segment-source\"]", Start.AddDays(-1), userId, "segment:one");
        segmentVersion.Submit(Guid.NewGuid()); segmentVersion.MarkApproved(); segmentVersion.ActivateTarget("primary", "Approved target");
        var objective = new MarketingObjective(Guid.NewGuid(), companyId, "Qualified demand", "pipeline", 20, "leads", Start, Start.AddMonths(3), userId);
        objective.Activate();
        db.AddRange(strategy, segment, segmentVersion, objective,
            new MarketingStrategySegment(Guid.NewGuid(), companyId, strategy.Id, segment.Id, segmentVersion.Id));
        await db.SaveChangesAsync();

        var service = new MarketingOperationsService(db, campaignDrafts: new SalesCampaignDraftService(db));
        var planRequest = new CreateGroundedMarketingPlanRequest("Autumn demand", "Strategy-grounded plan", strategy.Id, strategy.Version,
            Start, Start.AddMonths(3), 50_000, "SEK", [objective.Id],
            [new MarketingPlanSegmentSelection(segmentVersion.Id, MarketingPlanSegmentRoles.Primary, 1, "Approved target", "Create qualified demand")],
            "Use the approved strategy and audience.", ["strategy-source", "segment-source"], [], [], [], "plan:autumn", null);
        var created = await service.CreateGroundedPlanAsync(companyId, userId, planRequest, default);
        var repeated = await service.CreateGroundedPlanAsync(companyId, userId, planRequest, default);

        Assert.Equal(created.Summary.Id, repeated.Summary.Id);
        Assert.Null(await service.GetPlanPortfolioAsync(otherCompanyId, created.Summary.Id, default));
        Assert.Single(await service.ListPlanPortfolioAsync(companyId, default));
        Assert.Empty(await service.ListPlanPortfolioAsync(otherCompanyId, default));

        var campaign = new MarketingCampaignPortfolioItemRequest("Operations demand", "Create qualified demand", objective.Id,
            "Contribute qualified leads", [segmentVersion.Id], 25_000, "SEK", 1, CampaignTypes.LeadGeneration, "marketing_segment",
            20, "leads", Start.AddMonths(3), Start, Start.AddDays(14), Start.AddMonths(2), Start.AddMonths(3), "UTC", "en", ["internal"],
            "Approved offer basis", ["Prepare assets"], ["Campaign brief"], "Operations leaders", "Measure qualified leads",
            ["strategy-source", "segment-source"], []);
        var portfolio = new PrepareMarketingCampaignPortfolioRequest(created.Summary.Id, created.Summary.Version, [campaign], "portfolio:autumn");
        var committed = await service.CommitCampaignPortfolioAsync(companyId, userId, new CommitMarketingCampaignPortfolioRequest(portfolio), default);
        var repeatedCommit = await service.CommitCampaignPortfolioAsync(companyId, userId, new CommitMarketingCampaignPortfolioRequest(portfolio), default);

        Assert.False(committed.Idempotent); Assert.True(repeatedCommit.Idempotent);
        Assert.Single(await db.SalesCampaigns.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToArrayAsync());
        Assert.Single(await db.MarketingPlanCampaigns.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToArrayAsync());
        Assert.Single(await db.SalesCampaignActivities.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToArrayAsync());
        Assert.Single(await db.MarketingContentBriefs.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToArrayAsync());
        Assert.Equal(2, await db.AuditEvents.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId && x.TargetId == created.Summary.Id.ToString("D")));
    }
}
