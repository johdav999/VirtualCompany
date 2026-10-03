using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Approvals;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed class MarketingOperationalJourneyTests : IDisposable
{
    private readonly TestWebApplicationFactory factory = new RetryAwareFactory();
    private readonly Guid company = Guid.NewGuid(), other = Guid.NewGuid(), campaign = Guid.NewGuid(), brief = Guid.NewGuid(), variant = Guid.NewGuid(), privateBrief = Guid.NewGuid();
    private readonly DateTime now = DateTime.UtcNow;
    public void Dispose() => factory.Dispose();
    private sealed class RetryAwareFactory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => services.AddDbContext<VirtualCompanyDbContext>(options =>
                options.UseSqlite(sqlite => sqlite.ExecutionStrategy(dependencies => new RetryAwareStrategy(dependencies)))));
        }
    }
    private sealed class RetryAwareStrategy(ExecutionStrategyDependencies dependencies) : ExecutionStrategy(dependencies, 1, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => false;
    }
    private HttpClient Client(string subject = "p05-owner")
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Company-Id", company.ToString());
        client.DefaultRequestHeaders.Add("X-Dev-Auth-Subject", subject); client.DefaultRequestHeaders.Add("X-Dev-Auth-Email", subject + "@example.com");
        return client;
    }
    private async Task Seed()
    {
        await factory.SeedAsync(db =>
        {
            var user = new User(Guid.NewGuid(), "p05-owner@example.com", "P05 Owner", "dev-header", "p05-owner");
            var member = new User(Guid.NewGuid(), "p05-member@example.com", "P05 Member", "dev-header", "p05-member");
            db.Users.AddRange(user, member); db.Companies.AddRange(new Company(company, "P05"), new Company(other, "Private"));
            var membership = new CompanyMembership(Guid.NewGuid(), company, user.Id, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active);
            db.CompanyMemberships.AddRange(membership, new CompanyMembership(Guid.NewGuid(), company, member.Id, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active));
            db.CompanyResponsibilityAssignments.Add(new CompanyResponsibilityAssignment(Guid.NewGuid(), company, ResponsibilityArea.Marketing,
                ResponsibilityAssignmentKind.Primary, membership.Id, null, AgentAutonomyLevel.Level1, null, null));
            var sequence = Guid.NewGuid(); db.SalesSequences.Add(new SalesSequence(sequence, company, "Fixture sequence"));
            var c = new SalesCampaign(campaign, company, sequence, "P05 launch", "b2b");
            c.ConfigureInitiative(CampaignTypes.ProductLaunch, "Test launch", user.Id, null, "leads", 5, "count", now.AddDays(5), now.AddDays(-2), now.AddMinutes(-1), now.AddDays(7), "UTC", 100, "SEK");
            db.SalesCampaigns.Add(c);
            var b = new MarketingContentBrief(brief, company, "Launch content", "Explain the offer", "Renewal customers", "email", "English", "Clear", "Review offer", campaign, null, now.AddMinutes(-1), user.Id, null);
            b.Submit(); db.MarketingContentBriefs.Add(b);
            db.MarketingContentVariants.Add(new MarketingContentVariant(variant, company, brief, "Exact message", "Review the renewal options.", "[\"approved-offer\"]", false));
            db.MarketingContentBriefs.Add(new MarketingContentBrief(privateBrief, other, "Private content", "Private", "Private audience", "email", "English", "Clear", "Read", null, null, null, null, null));
            db.MarketingAttributionTouches.AddRange(new MarketingAttributionTouch(Guid.NewGuid(), company, "campaign", campaign, "view", "email", "cost-1", 1, now.AddHours(-2), 120, "SEK", "{}", "cost-1"),
                new MarketingAttributionTouch(Guid.NewGuid(), company, "campaign", campaign, "view", "email", "unknown", 1, now.AddHours(-1), null, null, "{}", "unknown"),
                new MarketingAttributionTouch(Guid.NewGuid(), company, "campaign", campaign, "view", "email", "usd", 1, now.AddHours(-1), 9, "USD", "{}", "usd"));
            db.MarketingChannelObservations.Add(new MarketingChannelObservation(Guid.NewGuid(), company, "fixture", "leads", 4, "count", now.AddDays(-1), now, campaign, null, "lead-source", "lead-source"));
            var connection = Guid.NewGuid(); db.MarketingChannelConnections.Add(new MarketingChannelConnection(connection, company, "linkedin", "test", "test", "{}", "fixture-secret-ref", user.Id));
            var action = new MarketingChannelAction(Guid.NewGuid(), company, connection, campaign, brief, "test-destination", "publish", "{}", null, "fixture-action", b.Version);
            action.Submit(Guid.NewGuid()); action.Queue(); action.ClaimForDispatch(); action.RecordFailure("provider_unavailable", true); db.MarketingChannelActions.Add(action);
            return Task.CompletedTask;
        });
    }
    private string ReportRoute(string suffix = "") => $"api/marketing/operational/report?fromUtc={Uri.EscapeDataString(now.AddDays(-2).ToString("O"))}&toUtc={Uri.EscapeDataString(now.AddDays(1).ToString("O"))}{suffix}";

    [Fact]
    public async Task Reports_reconcile_known_costs_currencies_outcomes_and_delivery_without_writing()
    {
        await Seed(); using var client = Client();
        var result = (await client.GetFromJsonAsync<MarketingOperationalReport>(ReportRoute()))!;
        var row = Assert.Single(result.Campaigns);
        Assert.Equal(100, row.Budget); Assert.Equal(120, row.KnownSpend); Assert.Equal(4, row.ObservedLeads); Assert.Equal(1, row.UnknownCostTouches);
        Assert.Equal("retry_scheduled", Assert.Single(result.Deliveries).State);
        Assert.Equal(120, result.Spend.Where(x => x.Currency == "SEK").Sum(x => x.Cost)); Assert.Equal(9, result.Spend.Where(x => x.Currency == "USD").Sum(x => x.Cost));
        Assert.Contains(row.Gaps, x => x.Contains("Attribution")); Assert.Contains(row.Gaps, x => x.Contains("incomplete"));
        var sek = (await client.GetFromJsonAsync<MarketingOperationalReport>(ReportRoute("&currency=SEK")))!;
        Assert.Equal(2, sek.Spend.Count); Assert.Equal(1, sek.Campaigns.Single().UnknownCostTouches); Assert.Equal(120, sek.Campaigns.Single().KnownSpend);
        Assert.Empty((await client.GetFromJsonAsync<MarketingOperationalReport>(ReportRoute("&state=delivered")))!.Deliveries);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(ReportRoute("&currency=invalid"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(ReportRoute("&state=success"))).StatusCode);
        Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.AuditEvents.IgnoreQueryFilters().CountAsync(x => x.CompanyId == company)));
    }
    [Fact]
    public async Task Overlapping_or_superseded_observations_do_not_invent_lead_totals()
    {
        await Seed(); using var client = Client();
        await factory.SeedAsync(db => { db.MarketingChannelObservations.Add(new MarketingChannelObservation(Guid.NewGuid(), company, "other", "leads", 99, "count", now.AddDays(-1), now, campaign, null, "overlap", "overlap")); return Task.CompletedTask; });
        var report = (await client.GetFromJsonAsync<MarketingOperationalReport>(ReportRoute()))!;
        Assert.Null(report.Campaigns.Single().ObservedLeads); Assert.Contains(report.Campaigns.Single().Gaps, x => x.Contains("overlap"));
        await factory.ExecuteDbContextAsync(async db => { (await db.MarketingChannelObservations.IgnoreQueryFilters().SingleAsync(x => x.SourceReference == "overlap")).Supersede(); await db.SaveChangesAsync(); return true; });
        Assert.Equal(4, (await client.GetFromJsonAsync<MarketingOperationalReport>(ReportRoute()))!.Campaigns.Single().ObservedLeads);
    }
    [Fact]
    public async Task Review_revision_reload_invalidates_content_approval_and_stale_commands_conflict()
    {
        await Seed(); using var client = Client();
        var route = $"api/marketing/operational/review?campaignId={campaign}&briefId={brief}";
        var before = (await client.GetFromJsonAsync<MarketingCampaignReview>(route))!;
        var content = before.Content.Single(); Assert.Equal(variant, content.Variants.Single().Id);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"api/marketing/content/{brief}/review", new ReviewMarketingContentRequest(true, 99))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"api/marketing/content/{brief}/review", new ReviewMarketingContentRequest(true, content.Version))).StatusCode);
        var approved = (await client.GetFromJsonAsync<MarketingCampaignReview>(route))!.Content.Single(); Assert.Equal("approved", approved.Status);
        var revision = new CreateMarketingContentVariantVersionRequest("Revised", "Clearer renewal message.", "[\"approved-offer\"]", approved.Version);
        var response = await client.PostAsJsonAsync($"api/marketing/content-variants/{variant}/versions", revision); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await client.GetFromJsonAsync<MarketingCampaignReview>(route))!.Content.Single(); Assert.Equal("draft", updated.Status); Assert.Equal(2, updated.Variants.Count); Assert.True(updated.Version > approved.Version);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"api/marketing/content-variants/{variant}/versions", revision)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"api/marketing/content/{brief}/review", new ReviewMarketingContentRequest(true, approved.Version))).StatusCode);
        var audit = await factory.ExecuteDbContextAsync(db => db.AuditEvents.IgnoreQueryFilters().Where(x => x.CompanyId == company && x.Action == "marketing.content.revised").ToListAsync()); Assert.Single(audit); Assert.NotNull(audit[0].ActorId);
    }
    [Fact]
    public async Task Unassigned_members_and_foreign_records_cannot_read_assets_reports_or_mutate_content()
    {
        await Seed(); using var member = Client("p05-member"); using var owner = Client();
        foreach (var path in new[] { ReportRoute(), $"api/marketing/operational/review?briefId={brief}", "api/marketing/creative-assets", "api/marketing/content" })
            Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"api/marketing/operational/review?briefId={privateBrief}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync($"api/marketing/content/{privateBrief}/review", new ReviewMarketingContentRequest(false))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync($"api/marketing/content/{brief}/review", new ReviewMarketingContentRequest(false))).StatusCode);
    }
    [Fact]
    public async Task Approved_delivery_queues_only_the_exact_approved_content_version()
    {
        await Seed(); using var client = Client();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"api/marketing/content/{brief}/review", new ReviewMarketingContentRequest(true, 2))).StatusCode);
        var currentAction = Guid.NewGuid(); var staleAction = Guid.NewGuid();
        await factory.SeedAsync(async db => {
            var connection = await db.MarketingChannelConnections.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == company);
            db.MarketingChannelActions.AddRange(new MarketingChannelAction(currentAction, company, connection.Id, campaign, brief, "exact", "publish", "{}", null, "exact-version", 3),
                new MarketingChannelAction(staleAction, company, connection.Id, campaign, brief, "stale", "publish", "{}", null, "stale-version", 2));
        });
        foreach (var id in new[] { currentAction, staleAction })
        {
            var submitted = await client.PostAsync($"api/marketing/channel-actions/{id}/submit", null); submitted.EnsureSuccessStatusCode();
            var action = (await submitted.Content.ReadFromJsonAsync<MarketingChannelActionDto>())!;
            var replay = await client.PostAsync($"api/marketing/channel-actions/{id}/submit", null); replay.EnsureSuccessStatusCode();
            Assert.Equal(action.ApprovalRequestId, (await replay.Content.ReadFromJsonAsync<MarketingChannelActionDto>())!.ApprovalRequestId);
            var decision = await client.PostAsJsonAsync($"api/companies/{company}/approvals/{action.ApprovalRequestId}/decisions", new ApprovalDecisionCommand(action.ApprovalRequestId!.Value, "approve"));
            decision.EnsureSuccessStatusCode();
        }
        var queued = await client.PostAsync($"api/marketing/channel-actions/{currentAction}/synchronize-approval", null); queued.EnsureSuccessStatusCode();
        Assert.Equal("queued", (await queued.Content.ReadFromJsonAsync<MarketingChannelActionDto>())!.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"api/marketing/channel-actions/{staleAction}/synchronize-approval", null)).StatusCode);
        Assert.Equal("awaiting_approval", await factory.ExecuteDbContextAsync(db => db.MarketingChannelActions.IgnoreQueryFilters().Where(x => x.Id == staleAction).Select(x => x.Status).SingleAsync()));
    }
    [Fact]
    public async Task Missing_budget_and_rejected_asset_are_explicit_and_scoped()
    {
        await Seed(); using var client = Client(); var missing = Guid.NewGuid(); var assetId = Guid.NewGuid();
        await factory.SeedAsync(async db => {
            var owner = await db.Users.SingleAsync(x => x.AuthSubject == "p05-owner");
            var sequence = await db.SalesSequences.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == company);
            db.SalesCampaigns.Add(new SalesCampaign(missing, company, sequence.Id, "Missing budget campaign", "b2b"));
            var asset = new MarketingCreativeAsset(assetId, company, brief, campaign, "Rejected asset", "image/png", "1024x1024", "en", "Fixture", "v1", "fixture", "brand-v1", "unverified", "Fixture illustration", "fixture/asset", "fixture-checksum", owner.Id, "p05-asset", contentVariantId: variant);
            asset.Submit(); asset.Review(false); db.MarketingCreativeAssets.Add(asset);
        });
        var row = (await client.GetFromJsonAsync<MarketingOperationalReport>(ReportRoute($"&campaignId={missing}")))!.Campaigns.Single();
        Assert.Null(row.Budget); Assert.Null(row.KnownSpend); Assert.Null(row.ObservedLeads); Assert.Contains(row.Gaps, x => x.Contains("budget", StringComparison.OrdinalIgnoreCase));
        var review = (await client.GetFromJsonAsync<MarketingCampaignReview>($"api/marketing/operational/review?briefId={brief}"))!;
        Assert.Equal("rejected", review.Assets.Single().Status); Assert.Equal(assetId, review.Assets.Single().Id);
        using var member = Client("p05-member");
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync($"api/marketing/creative-assets/{assetId}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync($"api/marketing/creative-assets/{assetId}/scans")).StatusCode);
        var today = (await client.GetFromJsonAsync<TodayWorkspaceDto>($"api/companies/{company}/workspace/today?lens=marketing&refresh=true"))!;
        Assert.Contains(today.Marketing!.Items, x => x.DeepLink.Contains(assetId.ToString()));
    }
    [Fact]
    public async Task Today_shows_due_launch_spend_exception_attribution_gap_and_delivery_recovery()
    {
        await Seed(); using var client = Client();
        var today = (await client.GetFromJsonAsync<TodayWorkspaceDto>($"api/companies/{company}/workspace/today?lens=marketing&refresh=true"))!;
        Assert.Equal(1, today.Marketing!.DueLaunches); Assert.Equal(1, today.Marketing.SpendExceptions); Assert.Equal(1, today.Marketing.AttributionGaps);
        Assert.Contains(today.Marketing.Items, x => x.Key.StartsWith("marketing-delivery:"));
        Assert.Contains(today.Priorities, x => x.EvidenceSourceType == "marketing_campaign");
    }
}

