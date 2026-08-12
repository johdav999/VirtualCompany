using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Tests;

public sealed class CompanyControlledActionIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Controlled_notification_requires_authoritative_approval_and_queues_exactly_once()
    {
        var seed = await SeedAsync();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, seed.Subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, seed.Email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, "Operations Manager");
        client.DefaultRequestHeaders.Add(CompanyContextResolutionMiddleware.CompanyHeaderName, seed.CompanyId.ToString());

        var proposal = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/operating/controlled-actions/notifications",
            new ProposeControlledNotificationCommand(seed.PlanId, seed.UserId, "Operating update", "The approved plan is ready."));
        Assert.Equal(HttpStatusCode.OK, proposal.StatusCode);
        var decision = await proposal.Content.ReadFromJsonAsync<OperatingDecisionDto>();
        Assert.NotNull(decision);

        var denied = await client.PostAsync($"/api/companies/{seed.CompanyId}/operating/controlled-actions/{decision!.Id}/execute", null);
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);

        var approval = await _factory.ExecuteDbContextAsync(db => db.ApprovalRequests.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.CompanyId == seed.CompanyId && x.TargetEntityType == "operating_decision" && x.TargetEntityId == decision.Id));
        var approvalResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/approvals/{approval.Id}/decisions",
            new ApprovalDecisionCommand(approval.Id, "approve", Comment: "Approved for controlled delivery."));
        Assert.Equal(HttpStatusCode.OK, approvalResponse.StatusCode);

        var first = await client.PostAsync($"/api/companies/{seed.CompanyId}/operating/controlled-actions/{decision.Id}/execute", null);
        var duplicate = await client.PostAsync($"/api/companies/{seed.CompanyId}/operating/controlled-actions/{decision.Id}/execute", null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);

        var queued = await _factory.ExecuteDbContextAsync(db => db.CompanyOutboxMessages.IgnoreQueryFilters()
            .CountAsync(x => x.CompanyId == seed.CompanyId && x.IdempotencyKey == $"controlled-action:{decision.Id:N}"));
        Assert.Equal(1, queued);
    }

    private async Task<(Guid CompanyId, Guid UserId, Guid PlanId, string Subject, string Email)> SeedAsync()
    {
        var companyId = Guid.NewGuid(); var userId = Guid.NewGuid(); var agentId = Guid.NewGuid();
        var cycleId = Guid.NewGuid(); var planId = Guid.NewGuid();
        const string subject = "controlled-manager"; const string email = "controlled.manager@example.com";
        await _factory.SeedAsync(db =>
        {
            db.Users.Add(new User(userId, email, "Operations Manager", "dev-header", subject));
            db.Companies.Add(new Company(companyId, "Controlled action company"));
            db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId,
                CompanyMembershipRole.Manager, CompanyMembershipStatus.Active));
            db.Agents.Add(new Agent(agentId, companyId, "operations", "Nina", "Operations Manager", "Operations",
                null, AgentSeniority.Lead, AgentStatus.Active, AgentAutonomyLevel.Level3));
            var config = new CompanyOperatingConfiguration(Guid.NewGuid(), companyId);
            config.Update(agentId, CompanyAutonomyLevel.ControlledExecution, "UTC", 6, 60, 4, 5, 12, 3, 120, 4, 20, null);
            db.CompanyOperatingConfigurations.Add(config);
            var cycle = new OperatingCycle(cycleId, companyId, "manual", null, agentId,
                "controlled-correlation", "controlled-cycle", config.Version);
            db.OperatingCycles.Add(cycle);
            var plan = new OperatingPlan(planId, companyId, cycleId, 1, "Inform operators", "Controlled notification test.");
            plan.SubmitForReview(); plan.Approve(); db.OperatingPlans.Add(plan);
            return Task.CompletedTask;
        });
        return (companyId, userId, planId, subject, email);
    }
}
