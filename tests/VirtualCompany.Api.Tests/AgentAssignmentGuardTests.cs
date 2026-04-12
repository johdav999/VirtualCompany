using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class AgentAssignmentGuardTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public AgentAssignmentGuardTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Active_agents_can_still_be_selected_for_new_task_assignment()
    {
        var seed = await SeedAgentAsync(AgentStatus.Active);

        using var scope = _factory.Services.CreateScope();
        var guard = scope.ServiceProvider.GetRequiredService<IAgentAssignmentGuard>();

        await guard.EnsureAgentCanReceiveNewTasksAsync(
            seed.CompanyId,
            seed.AgentId,
            "assignedAgentId",
            CancellationToken.None);

        var resolved = await guard.GetAssignableAgentAsync(seed.CompanyId, seed.AgentId, CancellationToken.None);

        Assert.Equal(seed.AgentId, resolved.Id);
        Assert.Equal(seed.CompanyId, resolved.CompanyId);
        Assert.Equal("active", resolved.Status);
        Assert.True(resolved.CanReceiveAssignments);
    }

    [Fact]
    public async Task Archived_agents_are_rejected_for_new_task_assignment_with_field_level_validation()
    {
        var seed = await SeedAgentAsync(AgentStatus.Archived);

        using var scope = _factory.Services.CreateScope();
        var guard = scope.ServiceProvider.GetRequiredService<IAgentAssignmentGuard>();

        var exception = await Assert.ThrowsAsync<AgentAssignmentValidationException>(() =>
            guard.EnsureAgentCanReceiveNewTasksAsync(
                seed.CompanyId,
                seed.AgentId,
                "assignedAgentId",
                CancellationToken.None));

        var error = Assert.Single(exception.Errors);
        Assert.Equal("assignedAgentId", error.Key);
        Assert.Equal(Agent.ArchivedAssignmentErrorMessage, Assert.Single(error.Value));
    }

    [Fact]
    public async Task Paused_agents_are_rejected_for_new_task_assignment_with_field_level_validation()
    {
        var seed = await SeedAgentAsync(AgentStatus.Paused);

        using var scope = _factory.Services.CreateScope();
        var guard = scope.ServiceProvider.GetRequiredService<IAgentAssignmentGuard>();

        var exception = await Assert.ThrowsAsync<AgentAssignmentValidationException>(() =>
            guard.EnsureAgentCanReceiveNewTasksAsync(
                seed.CompanyId,
                seed.AgentId,
                "assignedAgentId",
                CancellationToken.None));

        var error = Assert.Single(exception.Errors);
        Assert.Equal("assignedAgentId", error.Key);
        Assert.Equal(Agent.PausedAssignmentErrorMessage, Assert.Single(error.Value));
    }

    [Fact]
    public async Task Agent_assignment_resolution_remains_company_scoped()
    {
        var seed = await SeedCrossTenantAgentsAsync();

        using var scope = _factory.Services.CreateScope();
        var guard = scope.ServiceProvider.GetRequiredService<IAgentAssignmentGuard>();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            guard.GetAssignableAgentAsync(seed.CompanyId, seed.OtherCompanyAgentId, CancellationToken.None));

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            guard.EnsureAgentCanReceiveNewTasksAsync(
                seed.CompanyId,
                seed.OtherCompanyAgentId,
                "assignedAgentId",
                CancellationToken.None));
    }

    private async Task<(Guid CompanyId, Guid AgentId)> SeedAgentAsync(AgentStatus status)
    {
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Companies.Add(new Company(companyId, "Assignment Company"));
            dbContext.Agents.Add(new Agent(
                agentId,
                companyId,
                "operations",
                status == AgentStatus.Archived ? "Archived Ops" : "Active Ops",
                "Operations Manager",
                "Operations",
                null,
                AgentSeniority.Lead,
                status));

            return Task.CompletedTask;
        });

        return (companyId, agentId);
    }

    private async Task<(Guid CompanyId, Guid CompanyAgentId, Guid OtherCompanyId, Guid OtherCompanyAgentId)> SeedCrossTenantAgentsAsync()
    {
        var companyId = Guid.NewGuid();
        var companyAgentId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var otherCompanyAgentId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Companies.AddRange(
                new Company(companyId, "Company A"),
                new Company(otherCompanyId, "Company B"));

            dbContext.Agents.AddRange(
                new Agent(
                    companyAgentId,
                    companyId,
                    "finance",
                    "Company A Finance",
                    "Finance Manager",
                    "Finance",
                    null,
                    AgentSeniority.Senior,
                    AgentStatus.Active),
                new Agent(
                    otherCompanyAgentId,
                    otherCompanyId,
                    "support",
                    "Company B Support",
                    "Support Lead",
                    "Support",
                    null,
                    AgentSeniority.Lead,
                    AgentStatus.Archived));

            return Task.CompletedTask;
        });

        return (companyId, companyAgentId, otherCompanyId, otherCompanyAgentId);
    }
}
