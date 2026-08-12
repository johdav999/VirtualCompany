using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Tests;

public sealed class SalesOperationsApiIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Dashboard_and_leads_are_scoped_to_current_tenant()
    {
        var seed = await SeedAsync();
        using var client = Client(seed.CompanyAId);

        var dashboard = await client.GetFromJsonAsync<SalesDashboardResponse>("/api/sales/dashboard");
        var leads = await client.GetFromJsonAsync<IReadOnlyList<SalesLeadSummaryResponse>>("/api/sales/leads");

        Assert.NotNull(dashboard);
        Assert.Equal(1000m, dashboard!.PipelineValue);
        Assert.All(dashboard.DealsRequiringAction, deal => Assert.NotEqual(seed.DealBId, deal.Id));
        Assert.NotNull(leads);
        Assert.Contains(leads!, lead => lead.Id == seed.LeadAId);
        Assert.Contains(leads!, lead => lead.Id == seed.RejectLeadId);
        Assert.DoesNotContain(leads!, lead => lead.Id == seed.LeadBId);
    }

    [Fact]
    public async Task Cross_tenant_lead_and_deal_access_returns_not_found()
    {
        var seed = await SeedAsync();
        using var client = Client(seed.CompanyAId);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/sales/leads/{seed.LeadBId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/sales/deals/{seed.DealBId}")).StatusCode);
    }

    [Fact]
    public async Task Qualify_reject_convert_and_deal_actions_persist_state_and_audit_events()
    {
        var seed = await SeedAsync();
        using var client = Client(seed.CompanyAId);

        var qualify = await client.PostAsJsonAsync($"/api/sales/leads/{seed.LeadAId}/qualify", new SalesActionRequest("Good fit"));
        Assert.Equal(HttpStatusCode.OK, qualify.StatusCode);

        var convert = await client.PostAsJsonAsync($"/api/sales/leads/{seed.LeadAId}/convert", new ConvertLeadRequest(2500m, "USD", DateTime.UtcNow.AddDays(30), "Create opportunity"));
        Assert.Equal(HttpStatusCode.OK, convert.StatusCode);
        var createdDeal = await convert.Content.ReadFromJsonAsync<SalesDealDetailResponse>();
        Assert.NotNull(createdDeal);

        var stage = await client.PostAsJsonAsync($"/api/sales/deals/{createdDeal!.Id}/stage", new ChangeDealStageRequest(SalesPipelineStage.ProposalStageId, "Proposal sent"));
        Assert.Equal(HttpStatusCode.OK, stage.StatusCode);

        var won = await client.PostAsJsonAsync($"/api/sales/deals/{createdDeal.Id}/won", new SalesActionRequest("Customer accepted"));
        Assert.Equal(HttpStatusCode.OK, won.StatusCode);

        var auditActions = await _factory.ExecuteDbContextAsync(async dbContext =>
            await dbContext.AuditEvents.IgnoreQueryFilters()
                .Where(x => x.CompanyId == seed.CompanyAId)
                .Select(x => x.Action)
                .ToListAsync());

        Assert.Contains(AuditEventActions.SalesLeadQualified, auditActions);
        Assert.Contains(AuditEventActions.SalesLeadConverted, auditActions);
        Assert.Contains(AuditEventActions.SalesDealStageChanged, auditActions);
        Assert.Contains(AuditEventActions.SalesDealWon, auditActions);
    }

    [Fact]
    public async Task Reject_lead_writes_audit_event()
    {
        var seed = await SeedAsync();
        using var client = Client(seed.CompanyAId);

        var response = await client.PostAsJsonAsync($"/api/sales/leads/{seed.RejectLeadId}/reject", new SalesActionRequest("Not ready"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(await _factory.ExecuteDbContextAsync(dbContext =>
            dbContext.AuditEvents.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == seed.CompanyAId && x.Action == AuditEventActions.SalesLeadRejected)));
    }

    [Fact]
    public async Task Qualified_lead_conversion_creates_a_deal_in_the_qualified_pipeline_stage()
    {
        var seed = await SeedAsync();
        using var client = Client(seed.CompanyAId);

        var qualify = await client.PostAsJsonAsync(
            $"/api/sales/leads/{seed.LeadAId}/qualify",
            new SalesActionRequest("Ready for the pipeline"));
        Assert.Equal(HttpStatusCode.OK, qualify.StatusCode);

        var convert = await client.PostAsJsonAsync(
            $"/api/sales/leads/{seed.LeadAId}/convert",
            new ConvertLeadRequest(2500m, "USD", DateTime.UtcNow.AddDays(30), "Create qualified opportunity"));
        Assert.Equal(HttpStatusCode.OK, convert.StatusCode);
        var deal = await convert.Content.ReadFromJsonAsync<SalesDealDetailResponse>();
        Assert.NotNull(deal);
        Assert.Equal(SalesPipelineStage.QualifiedStageId, deal!.StageId);

        var pipeline = await client.GetFromJsonAsync<SalesPipelineResponse>("/api/sales/pipeline");
        var qualifiedStage = Assert.Single(pipeline!.Stages, x => x.StageId == SalesPipelineStage.QualifiedStageId);
        Assert.Contains(qualifiedStage.Deals, x => x.Id == deal.Id);
    }
    [Fact]
    public async Task Convert_requires_a_qualified_lead()
    {
        var seed = await SeedAsync();
        using var client = Client(seed.CompanyAId);
        var dealCountBefore = await _factory.ExecuteDbContextAsync(dbContext =>
            dbContext.Deals.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyAId));

        var response = await client.PostAsJsonAsync(
            $"/api/sales/leads/{seed.RejectLeadId}/convert",
            new ConvertLeadRequest(2500m, "USD", DateTime.UtcNow.AddDays(30), "Attempt conversion before qualification"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains("qualified", problem?.Detail, StringComparison.OrdinalIgnoreCase);
        var dealCountAfter = await _factory.ExecuteDbContextAsync(dbContext =>
            dbContext.Deals.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyAId));
        Assert.Equal(dealCountBefore, dealCountAfter);
    }
    [Fact]
    public async Task Validation_errors_are_structured_and_field_specific()
    {
        var seed = await SeedAsync();
        using var client = Client(seed.CompanyAId);

        var convert = await client.PostAsJsonAsync($"/api/sales/leads/{seed.LeadAId}/convert", new ConvertLeadRequest(0, "", null, null));
        var email = await client.PostAsJsonAsync("/api/sales/email/process", new ProcessSalesEmailRequest("", "not-an-email", null, null, "", "", null, null, 1.5m));

        Assert.Equal(HttpStatusCode.BadRequest, convert.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, email.StatusCode);
        var problem = await convert.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains(nameof(ConvertLeadRequest.Amount), problem!.Errors.Keys);
        Assert.Contains(nameof(ConvertLeadRequest.Currency), problem.Errors.Keys);
    }

    [Fact]
    public async Task Pipeline_detail_activities_and_recommendations_are_tenant_scoped()
    {
        var seed = await SeedAsync();
        using var client = Client(seed.CompanyAId);

        var pipeline = await client.GetFromJsonAsync<SalesPipelineResponse>("/api/sales/pipeline");
        var deal = await client.GetFromJsonAsync<SalesDealDetailResponse>($"/api/sales/deals/{seed.DealAId}");
        var activities = await client.GetFromJsonAsync<IReadOnlyList<SalesActivityResponse>>($"/api/sales/deals/{seed.DealAId}/activities");
        var emails = await client.GetFromJsonAsync<IReadOnlyList<SalesEmailTimelineResponse>>($"/api/sales/deals/{seed.DealAId}/emails");
        var recommendations = await client.GetFromJsonAsync<IReadOnlyList<SalesRecommendationResponse>>("/api/sales/recommendations");

        Assert.NotNull(pipeline);
        Assert.Contains(pipeline!.Stages.SelectMany(x => x.Deals), x => x.Id == seed.DealAId);
        Assert.NotNull(deal);
        Assert.Equal(seed.DealAId, deal!.Id);
        Assert.Single(activities!);
        Assert.NotNull(emails);
        Assert.Single(emails!);
        Assert.Single(recommendations!);
        Assert.DoesNotContain(recommendations!, x => x.DealId == seed.DealBId);
    }

    [Fact]
    public async Task Contact_profile_returns_tenant_scoped_customer_memory()
    {
        var seed = await SeedAsync();
        using var client = Client(seed.CompanyAId);

        var profile = await client.GetFromJsonAsync<CustomerMemoryContext>($"/api/sales/contacts/{seed.ContactAId}/profile");

        Assert.NotNull(profile);
        Assert.Equal(seed.CompanyAId, profile!.CompanyId);
        Assert.Equal(seed.ContactAId, profile.ContactId);
        Assert.Contains("Buyer A", profile.AiSummary);
        Assert.Contains("conversation", profile.RelationshipMemory, StringComparison.OrdinalIgnoreCase);
        Assert.True(profile.EngagementScore > 0);
        Assert.NotEmpty(profile.PastConversations);
        Assert.Contains(profile.PreviousDeals, x => x.DealId == seed.DealAId);
        Assert.NotEmpty(profile.PriceSensitivityIndicators);

        var crossTenantResponse = await client.GetAsync($"/api/sales/contacts/{seed.ContactBId}/profile");
        Assert.Equal(HttpStatusCode.NotFound, crossTenantResponse.StatusCode);

        var deal = await client.GetFromJsonAsync<SalesDealDetailResponse>($"/api/sales/deals/{seed.DealAId}");
        Assert.NotNull(deal?.CustomerMemory);
        Assert.Equal(seed.ContactAId, deal!.CustomerMemory!.ContactId);
    }

    [Fact]
    public async Task Email_processing_validates_persists_lead_and_writes_audit_event()
    {
        var seed = await SeedAsync();
        using var client = Client(seed.CompanyAId);

        var response = await client.PostAsJsonAsync("/api/sales/email/process", new ProcessSalesEmailRequest(
            "sales-message-1",
            "buyer@example.com",
            "Buyer One",
            "Buyer Co",
            "Pricing request",
            "Can we get pricing?",
            "pricing request",
            "Platform",
            0.91m));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ProcessSalesEmailResponse>();
        Assert.NotNull(result);
        Assert.NotNull(result!.LeadId);

        var state = await _factory.ExecuteDbContextAsync(async dbContext => new
        {
            LeadCount = await dbContext.Leads.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyAId && x.Id == result.LeadId),
            AuditCount = await dbContext.AuditEvents.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyAId && x.Action == AuditEventActions.SalesEmailProcessed)
        });
        Assert.Equal(1, state.LeadCount);
        Assert.Equal(1, state.AuditCount);
    }

    private HttpClient Client(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, "sales-user");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, "sales-user@example.com");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, "Sales User");
        client.DefaultRequestHeaders.Add(CompanyContextResolutionMiddleware.CompanyHeaderName, companyId.ToString("D"));
        return client;
    }

    private async Task<SeedIds> SeedAsync()
    {
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var leadAId = Guid.NewGuid();
        var rejectLeadId = Guid.NewGuid();
        var leadBId = Guid.NewGuid();
        var dealAId = Guid.NewGuid();
        var dealBId = Guid.NewGuid();
        var contactAId = Guid.Empty;
        var contactBId = Guid.Empty;

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "sales-user@example.com", "Sales User", "dev-header", "sales-user"));
            dbContext.Companies.AddRange(new Company(companyAId, "Sales Tenant A"), new Company(companyBId, "Sales Tenant B"));
            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyAId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyBId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));

            contactAId = AddSalesRecords(dbContext, companyAId, leadAId, rejectLeadId, dealAId, "A");
            contactBId = AddSalesRecords(dbContext, companyBId, leadBId, Guid.NewGuid(), dealBId, "B");
            return Task.CompletedTask;
        });

        return new SeedIds(companyAId, companyBId, leadAId, rejectLeadId, leadBId, dealAId, dealBId, contactAId, contactBId);
    }

    private static Guid AddSalesRecords(VirtualCompanyDbContext dbContext, Guid companyId, Guid leadId, Guid rejectLeadId, Guid dealId, string suffix)
    {
        var customerId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        dbContext.CustomerCompanies.Add(new CustomerCompany(customerId, companyId, $"Customer {suffix}", industry: "Software"));
        dbContext.Contacts.Add(new Contact(contactId, companyId, $"Buyer {suffix}", $"buyer-{suffix}@example.com", customerId));
        dbContext.Leads.Add(new Lead(leadId, companyId, $"Lead {suffix}", SalesPipelineStage.NewStageId, primaryContactId: contactId, customerCompanyId: customerId));
        dbContext.Leads.Add(new Lead(rejectLeadId, companyId, $"Reject Lead {suffix}", SalesPipelineStage.NewStageId, primaryContactId: contactId, customerCompanyId: customerId));
        dbContext.Deals.Add(new Deal(dealId, companyId, $"Deal {suffix}", SalesPipelineStage.QualifiedStageId, 1000m, "USD", primaryContactId: contactId, customerCompanyId: customerId));
        dbContext.SalesEmailLinks.Add(new SalesEmailLink(Guid.NewGuid(), companyId, $"message-{suffix}", leadId, dealId, contactId, customerId, SalesStatuses.Linked, detectedIntent: "pricing request", productOrServiceInterest: "Platform", confidence: 0.87m, rationale: $"Buyer {suffix} asked for pricing and prefers a concise email summary."));
        dbContext.SalesActivities.Add(new SalesActivity(Guid.NewGuid(), companyId, "email", $"Email {suffix}", DateTime.UtcNow, leadId: leadId, dealId: dealId, contactId: contactId, customerCompanyId: customerId));
        dbContext.SalesAgentRecommendations.Add(new SalesAgentRecommendation(Guid.NewGuid(), companyId, $"Follow up {suffix}", "The buyer asked for pricing.", leadId, dealId));
        return contactId;
    }

    private sealed record SeedIds(Guid CompanyAId, Guid CompanyBId, Guid LeadAId, Guid RejectLeadId, Guid LeadBId, Guid DealAId, Guid DealBId, Guid ContactAId, Guid ContactBId);
}
