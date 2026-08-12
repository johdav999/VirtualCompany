using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class IcpSuggestionServiceTests
{
    [Fact]
    public async Task Suggestion_uses_company_product_and_market_evidence_through_shared_gateway()
    {
        await using var db = CreateDb();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var company = new Company(companyId, "Northstar Software");
        company.UpdateWorkspaceProfile("Northstar Software", "Software", "B2B SaaS", "Europe/Stockholm", "SEK", "en", "EU");

        var communicationProfile = new Dictionary<string, JsonNode?>
        {
            ["briefing"] = new JsonObject
            {
                [AgentBriefingCategories.CompanyInformation] = "Northstar serves finance teams in Nordic growth companies.",
                [AgentBriefingCategories.ProductsAndServices] = "A subscription platform automates supplier spend and recurring payment review."
            }
        };
        var agent = new Agent(
            agentId,
            companyId,
            "sales-manager",
            "Alex",
            "Sales Manager",
            "Sales",
            null,
            AgentSeniority.Senior,
            AgentStatus.Active,
            communicationProfile: communicationProfile);

        db.Companies.Add(company);
        db.Agents.Add(agent);
        await db.SaveChangesAsync();

        var gateway = new CapturingGateway(companyId);
        var service = new IcpSuggestionService(db, new EmptyKnowledgeSearch(), gateway);

        var result = await service.SuggestAsync(
            companyId,
            userId,
            new SuggestIcpRequest(agentId),
            CancellationToken.None);

        Assert.Equal("Nordic growth finance teams", result.Profile.Name);
        Assert.Equal("Sweden, Norway", result.Profile.Countries);
        Assert.Equal("CFO, Finance Director", result.Profile.BuyerRoles);
        Assert.True(result.RequiresReview);
        Assert.Equal("Alex", result.AgentName);
        Assert.NotEmpty(result.Evidence);

        var request = Assert.Single(gateway.Requests);
        Assert.Equal(companyId, request.CompanyId);
        Assert.Equal(agentId, request.AgentId);
        Assert.Equal(userId, request.ActorUserId);
        Assert.Equal(AgentCapabilityIds.SalesLeadIntelligence, request.CapabilityId);
        Assert.Equal(["recommend"], request.AllowedActionTypes);
        Assert.Empty(request.AllowedTools);
        Assert.Contains(request.Sources, x => x.Type == "company_profile" && x.Title == "Northstar Software");
        Assert.Contains(request.Sources, x => x.Type == "product_brief" && x.Snippet.Contains("supplier spend"));
    }

    [Fact]
    public async Task Suggestion_rejects_an_agent_from_another_company()
    {
        await using var db = CreateDb();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var otherAgentId = Guid.NewGuid();

        db.Companies.AddRange(new Company(companyId, "First"), new Company(otherCompanyId, "Second"));
        db.Agents.Add(new Agent(
            otherAgentId,
            otherCompanyId,
            "sales-manager",
            "Alex",
            "Sales Manager",
            "Sales",
            null,
            AgentSeniority.Senior,
            AgentStatus.Active));
        await db.SaveChangesAsync();

        var gateway = new CapturingGateway(companyId);
        var service = new IcpSuggestionService(db, new EmptyKnowledgeSearch(), gateway);

        var error = await Assert.ThrowsAsync<LeadGenerationValidationException>(() =>
            service.SuggestAsync(companyId, Guid.NewGuid(), new SuggestIcpRequest(otherAgentId), CancellationToken.None));

        Assert.Equal("The Sales agent was not found.", error.Message);
        Assert.Empty(gateway.Requests);
    }

    private static VirtualCompanyDbContext CreateDb() => new(
        new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private sealed class EmptyKnowledgeSearch : ICompanyKnowledgeSearchService
    {
        public Task<IReadOnlyList<CompanyKnowledgeSearchResultDto>> SearchAsync(
            CompanyKnowledgeSemanticSearchQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CompanyKnowledgeSearchResultDto>>([]);
    }

    private sealed class CapturingGateway(Guid companyId) : IAgentReasoningGateway
    {
        public List<AgentReasoningRequest> Requests { get; } = [];

        public Task<AgentReasoningResult> ReasonAsync(
            AgentReasoningRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var companySourceId = $"company:{companyId:N}";
            var summary = JsonSerializer.Serialize(new
            {
                name = "Nordic growth finance teams",
                countries = "Sweden, Norway",
                industries = "B2B SaaS",
                employeeMin = 50,
                employeeMax = 500,
                revenueMin = (decimal?)null,
                revenueMax = (decimal?)null,
                buyerRoles = "CFO, Finance Director",
                technologies = "cloud accounting",
                painHypotheses = "Manual supplier spend review",
                positiveCriteria = "Recurring supplier payments and a growing finance team",
                disqualifiers = "No recurring supplier spend",
                rationale = "The reviewed product and company brief point to Nordic growth-company finance teams.",
                missingEvidence = new[] { "Typical annual contract value" }
            });
            var claims = new[]
            {
                new AgentAiClaim(
                    "The company serves Nordic finance teams.",
                    "confirmed_fact",
                    .9m,
                    new[] { companySourceId })
            };
            return Task.FromResult(new AgentReasoningResult(
                Guid.NewGuid(),
                AgentAiRunStatuses.Completed,
                "1.0.0",
                summary,
                claims,
                .82m,
                [],
                [],
                [new AgentAiNextAction("Review the suggested ICP", "recommend", null, true)],
                [companySourceId]));
        }

        public Task<AgentReasoningResult?> GetRunAsync(
            Guid companyId,
            Guid agentId,
            Guid runId,
            CancellationToken cancellationToken) =>
            Task.FromResult<AgentReasoningResult?>(null);
    }
}