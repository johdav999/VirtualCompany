using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Tests;

public sealed class SalesCampaignsIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Audience_options_are_tenant_scoped_and_grouped_for_campaign_builder()
    {
        var seed = await SeedAsync();
        using var client = Client(seed.CompanyAId);

        var options = await client.GetFromJsonAsync<OutboundAudienceOptionsResponse>("/api/sales/campaigns/audience-options");

        Assert.NotNull(options);
        Assert.Contains(options!.Contacts, x => x.ContactId == seed.ContactAId && x.SourceTypes.Contains("existing_contacts"));
        Assert.Contains(options.Contacts, x => x.ContactId == seed.ContactAId && x.SourceTypes.Contains("past_customers"));
        Assert.DoesNotContain(options.Contacts, x => x.ContactId == seed.ContactBId);
    }

    [Fact]
    public async Task Create_campaign_validates_required_fields_and_four_step_sequence()
    {
        var seed = await SeedAsync();
        using var client = Client(seed.CompanyAId);

        var response = await client.PostAsJsonAsync("/api/sales/campaigns", new CreateOutboundCampaignRequest(
            "",
            null,
            "existing_contacts",
            [],
            new OutboundPolicyRequest(true, 50, false),
            [
                new CreateSequenceStepRequest(1, 0, "One", "Body", true)
            ]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Launch_schedules_steps_idempotently_and_state_controls_update_without_cross_tenant_access()
    {
        var seed = await SeedAsync();
        using var client = Client(seed.CompanyAId);

        var create = await client.PostAsJsonAsync("/api/sales/campaigns", ValidCampaign(seed.ContactAId));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var campaign = await create.Content.ReadFromJsonAsync<OutboundCampaignDetailResponse>();
        Assert.NotNull(campaign);

        var launch = await client.PostAsJsonAsync($"/api/sales/campaigns/{campaign!.Id}/launch", new { });
        Assert.Equal(HttpStatusCode.OK, launch.StatusCode);
        var launchAgain = await client.PostAsJsonAsync($"/api/sales/campaigns/{campaign.Id}/launch", new { });
        Assert.Equal(HttpStatusCode.OK, launchAgain.StatusCode);

        var state = await _factory.ExecuteDbContextAsync(async dbContext => new
        {
            Executions = await dbContext.SalesSequenceExecutions.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyAId && x.SalesCampaignId == campaign.Id),
            Steps = await dbContext.SalesSequenceExecutionSteps.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyAId && x.SalesCampaignId == campaign.Id)
        });

        Assert.Equal(1, state.Executions);
        Assert.Equal(4, state.Steps);

        var pause = await client.PostAsJsonAsync($"/api/sales/campaigns/{campaign.Id}/pause", new { });
        Assert.Equal(HttpStatusCode.OK, pause.StatusCode);

        var stop = await client.PostAsJsonAsync($"/api/sales/campaigns/{campaign.Id}/stop", new { Reason = "No longer needed" });
        Assert.Equal(HttpStatusCode.OK, stop.StatusCode);
        Assert.True(await _factory.ExecuteDbContextAsync(dbContext =>
            dbContext.SalesSequenceExecutionSteps.IgnoreQueryFilters().AllAsync(x => x.CompanyId != seed.CompanyAId || x.SalesCampaignId != campaign.Id || x.Status == SalesStatuses.Cancelled)));

        using var otherTenantClient = Client(seed.CompanyBId);
        Assert.Equal(HttpStatusCode.NotFound, (await otherTenantClient.GetAsync($"/api/sales/campaigns/{campaign.Id}")).StatusCode);
    }

    [Fact]
    public async Task Launch_persists_personalized_drafts_before_send()
    {
        var seed = await SeedAsync();
        using var client = Client(seed.CompanyAId);

        var create = await client.PostAsJsonAsync("/api/sales/campaigns", ValidCampaign(seed.ContactAId, "Memory offer"));
        var campaign = await create.Content.ReadFromJsonAsync<OutboundCampaignDetailResponse>();

        var launch = await client.PostAsJsonAsync($"/api/sales/campaigns/{campaign!.Id}/launch", new { });

        Assert.Equal(HttpStatusCode.OK, launch.StatusCode);
        var state = await _factory.ExecuteDbContextAsync(async dbContext =>
            await dbContext.SalesSequenceExecutionSteps.IgnoreQueryFilters()
                .Where(x => x.CompanyId == seed.CompanyAId && x.SalesCampaignId == campaign.Id)
                .OrderBy(x => x.StepOrder)
                .Select(x => new
                {
                    x.SentUtc,
                    x.OriginalGeneratedSubject,
                    x.OriginalGeneratedBody,
                    x.CurrentDraftSubject,
                    x.CurrentDraftBody,
                    x.GeneratedDraftUtc
                })
                .FirstAsync());

        Assert.Null(state.SentUtc);
        Assert.NotNull(state.GeneratedDraftUtc);
        Assert.Equal(state.OriginalGeneratedSubject, state.CurrentDraftSubject);
        Assert.Equal(state.OriginalGeneratedBody, state.CurrentDraftBody);
        Assert.Contains("Personal note:", state.OriginalGeneratedBody);
        Assert.Contains("Won deal A", state.OriginalGeneratedBody);
    }

    [Fact]
    public async Task Draft_edit_preserves_original_generated_content_for_audit()
    {
        var seed = await SeedAsync();
        using var client = Client(seed.CompanyAId);
        var create = await client.PostAsJsonAsync("/api/sales/campaigns", ValidCampaign(seed.ContactAId, "Editable offer"));
        var campaign = await create.Content.ReadFromJsonAsync<OutboundCampaignDetailResponse>();
        await client.PostAsJsonAsync($"/api/sales/campaigns/{campaign!.Id}/launch", new { });

        var step = await _factory.ExecuteDbContextAsync(dbContext =>
            dbContext.SalesSequenceExecutionSteps.IgnoreQueryFilters()
                .Where(x => x.CompanyId == seed.CompanyAId && x.SalesCampaignId == campaign.Id)
                .OrderBy(x => x.StepOrder)
                .FirstAsync());

        var originalBody = step.OriginalGeneratedBody;
        var edit = await client.PutAsJsonAsync($"/api/sales/campaigns/{campaign.Id}/steps/{step.Id}/draft", new SaveSequenceDraftRequest("Edited subject", "Edited body"));

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var saved = await edit.Content.ReadFromJsonAsync<SequenceExecutionStepResponse>();
        Assert.Equal("Edited subject", saved!.CurrentDraftSubject);
        Assert.Equal("Edited body", saved.CurrentDraftBody);
        Assert.Equal(originalBody, saved.OriginalGeneratedBody);
    }

    [Fact]
    public async Task Launch_blocks_duplicate_offer_when_matching_campaign_was_sent_within_lookback()
    {
        var seed = await SeedAsync();
        using var client = Client(seed.CompanyAId);
        var first = await client.PostAsJsonAsync("/api/sales/campaigns", ValidCampaign(seed.ContactAId, "Renewal duplicate"));
        var firstCampaign = await first.Content.ReadFromJsonAsync<OutboundCampaignDetailResponse>();
        await client.PostAsJsonAsync($"/api/sales/campaigns/{firstCampaign!.Id}/launch", new { });
        await _factory.ExecuteDbContextAsync(async dbContext =>
        {
            var sent = await dbContext.SalesSequenceExecutionSteps.IgnoreQueryFilters().FirstAsync(x => x.CompanyId == seed.CompanyAId && x.SalesCampaignId == firstCampaign.Id);
            sent.MarkSent("test", null, "duplicate-provider-message", "duplicate-thread", null, SalesStatuses.Delivered, DateTime.UtcNow, sent.CurrentDraftSubject, sent.CurrentDraftBody);
            await dbContext.SaveChangesAsync();
        });

        var second = await client.PostAsJsonAsync("/api/sales/campaigns", ValidCampaign(seed.ContactAId, "Renewal duplicate"));
        var secondCampaign = await second.Content.ReadFromJsonAsync<OutboundCampaignDetailResponse>();
        await client.PostAsJsonAsync($"/api/sales/campaigns/{secondCampaign!.Id}/launch", new { });

        Assert.Equal(0, await _factory.ExecuteDbContextAsync(dbContext => dbContext.SalesSequenceExecutions.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyAId && x.SalesCampaignId == secondCampaign.Id)));
        Assert.True(await _factory.ExecuteDbContextAsync(dbContext => dbContext.AuditEvents.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == seed.CompanyAId && x.Action == "sales.sequence.duplicate_offer_blocked")));
    }

    [Fact]
    public async Task Reply_and_deal_created_cancel_pending_future_steps_for_contact()
    {
        var seed = await SeedAsync();
        using var client = Client(seed.CompanyAId);

        var create = await client.PostAsJsonAsync("/api/sales/campaigns", ValidCampaign(seed.ContactAId));
        var campaign = await create.Content.ReadFromJsonAsync<OutboundCampaignDetailResponse>();
        await client.PostAsJsonAsync($"/api/sales/campaigns/{campaign!.Id}/launch", new { });

        await _factory.ExecuteDbContextAsync(async dbContext =>
        {
            var first = await dbContext.SalesSequenceExecutionSteps.IgnoreQueryFilters()
                .Where(x => x.CompanyId == seed.CompanyAId && x.SalesCampaignId == campaign.Id)
                .OrderBy(x => x.StepOrder)
                .FirstAsync();
            first.MarkSent("test", null, "provider-message-1", "thread-1", null, SalesStatuses.Delivered, DateTime.UtcNow);
            await dbContext.SaveChangesAsync();
        });

        var reply = await client.PostAsJsonAsync("/api/sales/campaigns/provider/reply", new OutboundReplyReceived("provider-message-1", "thread-1", null, "buyer-a@example.com"));
        Assert.Equal(HttpStatusCode.Accepted, reply.StatusCode);
        await DispatchOutboxAsync();
        Assert.True(await PendingStepsCancelledAsync(seed.CompanyAId, campaign.Id));
        await AssertCancellationAuditAsync(seed.CompanyAId, seed.ContactAId, SalesStopReasons.ReplyReceived);

        var createSecond = await client.PostAsJsonAsync("/api/sales/campaigns", ValidCampaign(seed.ContactAId, "Deal stop campaign"));
        var second = await createSecond.Content.ReadFromJsonAsync<OutboundCampaignDetailResponse>();
        await client.PostAsJsonAsync($"/api/sales/campaigns/{second!.Id}/launch", new { });
        var dealStop = await client.PostAsJsonAsync($"/api/sales/campaigns/contacts/{seed.ContactAId}/deal-created", new { DealId = seed.DealAId });
        Assert.Equal(HttpStatusCode.Accepted, dealStop.StatusCode);
        await DispatchOutboxAsync();
        Assert.True(await PendingStepsCancelledAsync(seed.CompanyAId, second.Id));
        await AssertCancellationAuditAsync(seed.CompanyAId, seed.ContactAId, SalesStopReasons.DealCreated);
    }

    private async Task<bool> PendingStepsCancelledAsync(Guid companyId, Guid campaignId) =>
        await _factory.ExecuteDbContextAsync(dbContext =>
            dbContext.SalesSequenceExecutionSteps.IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && x.SalesCampaignId == campaignId && x.Status == SalesStatuses.Pending)
                .CountAsync())
        == 0;

    private async Task DispatchOutboxAsync() =>
        await _factory.ExecuteScopeAsync(async scope =>
        {
            var processor = scope.ServiceProvider.GetRequiredService<ICompanyOutboxProcessor>();
            await processor.DispatchPendingAsync(CancellationToken.None);
        });

    private async Task AssertCancellationAuditAsync(Guid companyId, Guid contactId, string reason)
    {
        var state = await _factory.ExecuteDbContextAsync(async dbContext => new
        {
            AuditExists = await dbContext.AuditEvents.IgnoreQueryFilters()
                .AnyAsync(x => x.CompanyId == companyId &&
                    x.Action == "sales.sequence.pending_steps_cancelled" &&
                    x.TargetId == contactId.ToString("D")),
            CancelledSteps = await dbContext.SalesSequenceExecutionSteps.IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && x.ContactId == contactId && x.Status == SalesStatuses.Cancelled)
                .ToListAsync()
        });

        Assert.True(state.AuditExists);
        Assert.Contains(state.CancelledSteps, x => x.CancellationReason == reason && x.CancellationSourceReference != null);
    }

    [Fact]
    public async Task Sequence_stop_events_are_idempotent_and_tenant_scoped()
    {
        var seed = await SeedAsync();
        using var client = Client(seed.CompanyAId);
        var create = await client.PostAsJsonAsync("/api/sales/campaigns", ValidCampaign(seed.ContactAId, "Tenant safe campaign"));
        var campaign = await create.Content.ReadFromJsonAsync<OutboundCampaignDetailResponse>();
        await client.PostAsJsonAsync($"/api/sales/campaigns/{campaign!.Id}/launch", new { });

        var first = await client.PostAsJsonAsync($"/api/sales/campaigns/contacts/{seed.ContactAId}/deal-created", new { DealId = seed.DealAId });
        var duplicate = await client.PostAsJsonAsync($"/api/sales/campaigns/contacts/{seed.ContactAId}/deal-created", new { DealId = seed.DealAId });
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, duplicate.StatusCode);
        await DispatchOutboxAsync();
        await DispatchOutboxAsync();

        var pendingOtherTenant = await _factory.ExecuteDbContextAsync(dbContext =>
            dbContext.SalesSequenceExecutionSteps.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyBId && x.ContactId == seed.ContactBId && x.Status == SalesStatuses.Pending));
        Assert.Equal(0, pendingOtherTenant);
        Assert.True(await PendingStepsCancelledAsync(seed.CompanyAId, campaign.Id));
    }

    private static CreateOutboundCampaignRequest ValidCampaign(Guid contactId, string name = "Renewal outreach") =>
        new(
            name,
            "Four-step renewal sequence",
            "existing_contacts",
            [contactId],
            new OutboundPolicyRequest(true, 50, false),
            [
                new CreateSequenceStepRequest(1, 0, "First touch", "Hi, checking in.", true),
                new CreateSequenceStepRequest(2, 2, "Second touch", "Following up with a useful detail.", true),
                new CreateSequenceStepRequest(3, 5, "Third touch", "Would a short review help?", true),
                new CreateSequenceStepRequest(4, 9, "Close the loop", "Should I close the loop?", false)
            ]);

    private HttpClient Client(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, "campaign-user");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, "campaign-user@example.com");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, "Campaign User");
        client.DefaultRequestHeaders.Add(CompanyContextResolutionMiddleware.CompanyHeaderName, companyId.ToString("D"));
        return client;
    }

    private async Task<SeedIds> SeedAsync()
    {
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var customerAId = Guid.NewGuid();
        var customerBId = Guid.NewGuid();
        var contactAId = Guid.NewGuid();
        var contactBId = Guid.NewGuid();
        var dealAId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "campaign-user@example.com", "Campaign User", "dev-header", "campaign-user"));
            dbContext.Companies.AddRange(new Company(companyAId, "Campaign Tenant A"), new Company(companyBId, "Campaign Tenant B"));
            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyAId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyBId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            dbContext.CustomerCompanies.AddRange(new CustomerCompany(customerAId, companyAId, "Customer A"), new CustomerCompany(customerBId, companyBId, "Customer B"));
            dbContext.Contacts.AddRange(new Contact(contactAId, companyAId, "Buyer A", "buyer-a@example.com", customerAId), new Contact(contactBId, companyBId, "Buyer B", "buyer-b@example.com", customerBId));
            dbContext.Deals.Add(new Deal(dealAId, companyAId, "Won deal A", SalesPipelineStage.WonStageId, 1000m, "USD", SalesStatuses.Won, primaryContactId: contactAId, customerCompanyId: customerAId));
            dbContext.SalesEmailLinks.Add(new SalesEmailLink(Guid.NewGuid(), companyAId, "imported-message-a", contactId: contactAId, customerCompanyId: customerAId, provider: "test"));
            return Task.CompletedTask;
        });

        return new SeedIds(companyAId, companyBId, contactAId, contactBId, dealAId);
    }

    private sealed record SeedIds(Guid CompanyAId, Guid CompanyBId, Guid ContactAId, Guid ContactBId, Guid DealAId);
}
