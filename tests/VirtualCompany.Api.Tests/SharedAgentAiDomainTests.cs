using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class SharedAgentAiDomainTests
{
    [Fact]
    public void Run_completion_is_terminal_and_validates_confidence()
    {
        var run = new AgentOrchestrationRun(Guid.NewGuid(), Guid.NewGuid(), null, AgentCapabilityIds.Planning,
            "1.0.0", "plan-v1", "1.0", "correlation");

        run.Complete("completed", "test", "deterministic", .8m, "Validated plan", "{}", "[]", 10, 20, 25);

        Assert.Equal("completed", run.Status);
        Assert.Throws<InvalidOperationException>(() => run.Fail("failed", "late_failure", "Too late", 30));
    }

    [Fact]
    public void Handoff_enforces_legal_transitions_and_different_agents()
    {
        var companyId = Guid.NewGuid(); var requester = Guid.NewGuid(); var receiver = Guid.NewGuid();
        var handoff = new AgentHandoff(companyId, AgentHandoffTypes.InternalRequest, requester, receiver,
            "Review invoice readiness", "Confirm required evidence", null, "[]", "handoff-correlation", null);

        handoff.Transition("accepted"); handoff.Transition("in_progress"); handoff.Transition("completed", "Evidence confirmed", .9m);

        Assert.Equal("completed", handoff.Status);
        Assert.NotNull(handoff.CompletedUtc);
        Assert.Throws<InvalidOperationException>(() => handoff.Transition("in_progress"));
        Assert.Throws<ArgumentException>(() => new AgentHandoff(companyId, AgentHandoffTypes.InternalRequest, requester,
            requester, "Objective", "Outcome", null, "[]", "correlation", null));
    }

    [Fact]
    public void Memory_candidate_requires_review_before_activation()
    {
        var candidate = new AgentMemoryCandidate(Guid.NewGuid(), Guid.NewGuid(), "company_memory", "company_wide",
            "Approved support hours are 09:00 to 17:00.", "[\"policy-1\"]", .85m, "internal",
            DateTime.UtcNow.AddDays(30), "fingerprint", null);

        Assert.Throws<InvalidOperationException>(() => candidate.Activate(Guid.NewGuid()));
        candidate.Approve(Guid.NewGuid()); var memoryId = Guid.NewGuid(); candidate.Activate(memoryId);

        Assert.Equal("activated", candidate.Status);
        Assert.Equal(memoryId, candidate.ActivatedMemoryItemId);
    }

    [Fact]
    public void Capability_catalog_defines_all_shared_capabilities_once()
    {
        using var factory = new TestWebApplicationFactory(); using var scope = factory.Services.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<IAgentCapabilityCatalog>();

        var manifests = catalog.ListManifests();

        Assert.Equal(7, manifests.Count);
        Assert.Equal(manifests.Count, manifests.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(manifests, manifest => Assert.True(manifest.IsImplemented));
    }

    [Fact]
    public void Quality_event_rejects_invalid_confidence()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentAiQualityEvent(Guid.NewGuid(), Guid.NewGuid(),
            AgentCapabilityIds.WorkPrioritization, null, AgentAiQualityEventTypes.Accepted, "identity", null, null, 1.1m, "correlation"));
    }

    [Fact]
    public async Task Shared_ai_records_are_filtered_to_the_active_company()
    {
        using var factory = new TestWebApplicationFactory(); var companyA = Guid.NewGuid(); var companyB = Guid.NewGuid();
        var agentA = Guid.NewGuid(); var agentB = Guid.NewGuid();
        await factory.SeedAsync(db =>
        {
            db.Companies.AddRange(new Company(companyA, "Company A"), new Company(companyB, "Company B"));
            db.Agents.AddRange(
                new Agent(agentA, companyA, "finance", "Laura", "Finance Manager", "Finance", null, AgentSeniority.Senior),
                new Agent(agentB, companyB, "sales", "Alex", "Sales Manager", "Sales", null, AgentSeniority.Senior));
            db.AgentOrchestrationRuns.AddRange(
                new AgentOrchestrationRun(companyA, agentA, null, AgentCapabilityIds.WorkPrioritization, "1.0", "v1", "1.0", "a"),
                new AgentOrchestrationRun(companyB, agentB, null, AgentCapabilityIds.WorkPrioritization, "1.0", "v1", "1.0", "b"));
            return Task.CompletedTask;
        });
        using var scope = factory.Services.CreateScope(); var context = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>(); context.SetCompanyId(companyA);
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>(); var runs = await db.AgentOrchestrationRuns.AsNoTracking().ToListAsync();
        var run = Assert.Single(runs); Assert.Equal(companyA, run.CompanyId); Assert.Equal(agentA, run.AgentId);
    }

    [Fact]
    public async Task Shared_ai_endpoint_requires_authentication()
    {
        using var factory = new TestWebApplicationFactory(); using var client = factory.CreateClient();
        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/agents/{Guid.NewGuid()}/priorities");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
