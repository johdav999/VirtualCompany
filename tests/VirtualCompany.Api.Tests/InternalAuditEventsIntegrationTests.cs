using System.Net;
using System.Net.Http.Json;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Tenancy;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class InternalAuditEventsIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public InternalAuditEventsIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Internal_audit_events_filters_by_agent_time_range_and_company_context()
    {
        var seed = await SeedInternalAuditEventsAsync();
        using var client = CreateAuthenticatedClient();
        client.DefaultRequestHeaders.Add(CompanyContextResolutionMiddleware.CompanyHeaderName, seed.CompanyId.ToString());

        var from = Uri.EscapeDataString(seed.FromUtc.ToString("O"));
        var to = Uri.EscapeDataString(seed.ToUtc.ToString("O"));
        var response = await client.GetAsync($"/internal/audit-events?agentId={seed.AgentId}&from={from}&to={to}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AuditHistoryResult>();
        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal(seed.MatchingAuditEventId, item.Id);
        Assert.Equal(seed.CompanyId, item.CompanyId);
        Assert.Equal(seed.AgentId, item.ActorId);
        Assert.Equal("Nora Ledger", item.AgentName);
        Assert.Equal("Finance Manager", item.AgentRole);
        Assert.Equal("finance.payments", item.ResponsibilityDomain);
        Assert.Equal("20260414120000", item.PromptProfileVersion);
        Assert.Equal(AuditBoundaryDecisionOutcomes.InScope, item.BoundaryDecisionOutcome);
        Assert.Null(item.IdentityReasonCode);
    }

    [Fact]
    public async Task Internal_audit_events_preserves_tenant_isolation_for_same_agent_id()
    {
        var seed = await SeedInternalAuditEventsAsync();
        using var client = CreateAuthenticatedClient();
        client.DefaultRequestHeaders.Add(CompanyContextResolutionMiddleware.CompanyHeaderName, seed.OtherCompanyId.ToString());

        var from = Uri.EscapeDataString(seed.FromUtc.ToString("O"));
        var to = Uri.EscapeDataString(seed.ToUtc.ToString("O"));
        var response = await client.GetAsync($"/internal/audit-events?agentId={seed.AgentId}&from={from}&to={to}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AuditHistoryResult>();
        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal(seed.OtherCompanyAuditEventId, item.Id);
        Assert.Equal(seed.OtherCompanyId, item.CompanyId);
    }

    [Fact]
    public async Task Internal_audit_events_requires_agent_and_bounded_time_range()
    {
        var seed = await SeedInternalAuditEventsAsync();
        using var client = CreateAuthenticatedClient();
        client.DefaultRequestHeaders.Add(CompanyContextResolutionMiddleware.CompanyHeaderName, seed.CompanyId.ToString());

        var from = Uri.EscapeDataString(seed.FromUtc.ToString("O"));
        var missingTo = await client.GetAsync($"/internal/audit-events?agentId={seed.AgentId}&from={from}");
        var missingAgent = await client.GetAsync($"/internal/audit-events?from={from}&to={from}");
        var invalidRange = await client.GetAsync($"/internal/audit-events?agentId={seed.AgentId}&from={Uri.EscapeDataString(seed.ToUtc.ToString("O"))}&to={Uri.EscapeDataString(seed.FromUtc.ToString("O"))}");

        Assert.Equal(HttpStatusCode.BadRequest, missingTo.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingAgent.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidRange.StatusCode);
    }

    [Fact]
    public async Task Internal_audit_events_requires_company_context_header()
    {
        var seed = await SeedInternalAuditEventsAsync();
        using var client = CreateAuthenticatedClient();

        var from = Uri.EscapeDataString(seed.FromUtc.ToString("O"));
        var to = Uri.EscapeDataString(seed.ToUtc.ToString("O"));
        var response = await client.GetAsync($"/internal/audit-events?agentId={seed.AgentId}&from={from}&to={to}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<InternalAuditSeed> SeedInternalAuditEventsAsync()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var matchingAuditEventId = Guid.NewGuid();
        var otherCompanyAuditEventId = Guid.NewGuid();
        var fromUtc = new DateTime(2026, 4, 14, 8, 0, 0, DateTimeKind.Utc);
        var toUtc = new DateTime(2026, 4, 14, 18, 0, 0, DateTimeKind.Utc);

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "internal.audit@example.com", "Audit Owner", "dev-header", "internal-audit-owner"));
            dbContext.Companies.AddRange(new Company(companyId, "Company A"), new Company(otherCompanyId, "Company B"));
            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), otherCompanyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            dbContext.Agents.Add(new Agent(
                agentId,
                companyId,
                "finance",
                "Nora Ledger",
                "Finance Manager",
                "Finance",
                null,
                AgentSeniority.Senior,
                AgentStatus.Active));
            dbContext.AuditEvents.AddRange(
                CreateGenerationAuditEvent(
                    matchingAuditEventId,
                    companyId,
                    agentId,
                    new DateTime(2026, 4, 14, 12, 0, 0, DateTimeKind.Utc),
                    "Nora Ledger",
                    null),
                CreateGenerationAuditEvent(
                    Guid.NewGuid(),
                    companyId,
                    agentId,
                    new DateTime(2026, 4, 13, 12, 0, 0, DateTimeKind.Utc),
                    "Nora Ledger",
                    AuditReasonCodes.IdentityFallbackMissingConfig),
                CreateGenerationAuditEvent(
                    Guid.NewGuid(),
                    companyId,
                    Guid.NewGuid(),
                    new DateTime(2026, 4, 14, 12, 30, 0, DateTimeKind.Utc),
                    "Other Agent",
                    null),
                CreateGenerationAuditEvent(
                    otherCompanyAuditEventId,
                    otherCompanyId,
                    agentId,
                    new DateTime(2026, 4, 14, 12, 0, 0, DateTimeKind.Utc),
                    "Other Company Agent",
                    AuditReasonCodes.IdentityFallbackMissingConfig));
            return Task.CompletedTask;
        });

        return new InternalAuditSeed(companyId, otherCompanyId, agentId, matchingAuditEventId, otherCompanyAuditEventId, fromUtc, toUtc);
    }

    private static AuditEvent CreateGenerationAuditEvent(
        Guid id,
        Guid companyId,
        Guid agentId,
        DateTime occurredUtc,
        string agentName,
        string? identityReasonCode) =>
        new(
            id,
            companyId,
            AuditActorTypes.Agent,
            agentId,
            AuditEventActions.AgentGeneration,
            AuditTargetTypes.AgentGeneration,
            Guid.NewGuid().ToString("N"),
            AuditEventOutcomes.Succeeded,
            "Generation completed.",
            ["single_agent_orchestration"],
            correlationId: $"audit-{id:N}",
            occurredUtc: occurredUtc,
            agentName: agentName,
            agentRole: "Finance Manager",
            responsibilityDomain: "finance.payments",
            promptProfileVersion: "20260414120000",
            boundaryDecisionOutcome: AuditBoundaryDecisionOutcomes.InScope,
            identityReasonCode: identityReasonCode);

    private static HttpClient CreateAuthenticatedClient()
    {
        var client = new TestWebApplicationFactory().CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, "internal-audit-owner");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, "internal.audit@example.com");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, "Audit Owner");
        return client;
    }

    private sealed record InternalAuditSeed(
        Guid CompanyId,
        Guid OtherCompanyId,
        Guid AgentId,
        Guid MatchingAuditEventId,
        Guid OtherCompanyAuditEventId,
        DateTime FromUtc,
        DateTime ToUtc);
}
