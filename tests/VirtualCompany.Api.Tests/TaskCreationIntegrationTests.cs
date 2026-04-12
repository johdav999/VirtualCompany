using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class TaskCreationIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public TaskCreationIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_task_with_valid_agent_persists_task_defaults_and_human_creator()
    {
        var seed = await SeedTaskCompanyAsync(AgentStatus.Active);

        using var client = CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/tasks", new
        {
            type = "orchestration",
            title = "Prepare launch checklist",
            description = "Collect readiness facts.",
            priority = "high",
            dueAt = DateTime.UtcNow.AddDays(1),
            assignedAgentId = seed.AgentId,
            inputPayload = new Dictionary<string, object?>
            {
                ["launchId"] = "spring"
            },
            outputPayload = new Dictionary<string, object?>
            {
                ["source"] = "planner"
            },
            rationaleSummary = "Planner prepared initial inputs.",
            confidenceScore = 0.81m
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var task = await response.Content.ReadFromJsonAsync<TaskDetailResponse>();

        Assert.NotNull(task);
        Assert.Equal(seed.CompanyId, task!.CompanyId);
        Assert.Equal(seed.AgentId, task.AssignedAgentId);
        Assert.Equal("new", task.Status);
        Assert.Equal("high", task.Priority);
        Assert.Equal("human", task.CreatedByActorType);
        Assert.Equal(seed.UserId, task.CreatedByActorId);
        Assert.Equal("spring", task.InputPayload["launchId"].GetString());
        Assert.Equal("planner", task.OutputPayload["source"].GetString());
        Assert.Equal("Planner prepared initial inputs.", task.RationaleSummary);
        Assert.Equal(0.81m, task.ConfidenceScore);
    }

    [Theory]
    [InlineData(AgentStatus.Paused)]
    [InlineData(AgentStatus.Archived)]
    public async Task Create_task_rejects_paused_and_archived_agents(AgentStatus status)
    {
        var seed = await SeedTaskCompanyAsync(status);

        using var client = CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/tasks", new
        {
            type = "orchestration",
            title = "Prepare launch checklist",
            priority = "normal",
            assignedAgentId = seed.AgentId
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        Assert.NotNull(problem);
        Assert.True(problem!.Errors.ContainsKey("AssignedAgentId"));
    }

    [Fact]
    public async Task Create_task_rejects_agent_from_another_company_without_leaking_assignment_detail()
    {
        var seed = await SeedCrossTenantAgentAsync();

        using var client = CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/tasks", new
        {
            type = "orchestration",
            title = "Prepare launch checklist",
            priority = "normal",
            assignedAgentId = seed.OtherCompanyAgentId
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_task_requires_title_type_and_priority()
    {
        var seed = await SeedTaskCompanyAsync(AgentStatus.Active);

        using var client = CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/tasks", new
        {
            type = "",
            title = "",
            assignedAgentId = seed.AgentId
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();

        Assert.NotNull(problem);
        Assert.True(problem!.Errors.ContainsKey("Type"));
        Assert.True(problem.Errors.ContainsKey("Title"));
        Assert.True(problem.Errors.ContainsKey("Priority"));
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, "founder");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, "founder@example.com");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, "Founder");
        return client;
    }

    private async Task<TaskSeed> SeedTaskCompanyAsync(AgentStatus agentStatus)
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "founder@example.com", "Founder", "dev-header", "founder"));
            dbContext.Companies.Add(new Company(companyId, "Task Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            dbContext.Agents.Add(new Agent(agentId, companyId, "operations", "Ops Lead", "Operations Manager", "Operations", null, AgentSeniority.Lead, agentStatus));
            return Task.CompletedTask;
        });

        return new TaskSeed(companyId, userId, agentId);
    }

    private async Task<CrossTenantAgentSeed> SeedCrossTenantAgentAsync()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherCompanyAgentId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "founder@example.com", "Founder", "dev-header", "founder"));
            dbContext.Companies.AddRange(new Company(companyId, "Task Company"), new Company(otherCompanyId, "Other Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            dbContext.Agents.Add(new Agent(otherCompanyAgentId, otherCompanyId, "operations", "Other Ops", "Operations Manager", "Operations", null, AgentSeniority.Lead, AgentStatus.Active));
            return Task.CompletedTask;
        });

        return new CrossTenantAgentSeed(companyId, otherCompanyAgentId);
    }

    private sealed record TaskSeed(Guid CompanyId, Guid UserId, Guid AgentId);
    private sealed record CrossTenantAgentSeed(Guid CompanyId, Guid OtherCompanyAgentId);

    private sealed class TaskDetailResponse
    {
        public Guid Id { get; set; }
        public Guid CompanyId { get; set; }
        public string Priority { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public Guid? AssignedAgentId { get; set; }
        public string CreatedByActorType { get; set; } = string.Empty;
        public Guid? CreatedByActorId { get; set; }
        public Dictionary<string, JsonElement> InputPayload { get; set; } = [];
        public Dictionary<string, JsonElement> OutputPayload { get; set; } = [];
        public string? RationaleSummary { get; set; }
        public decimal? ConfidenceScore { get; set; }
    }

    private sealed class ValidationProblemResponse
    {
        public Dictionary<string, string[]> Errors { get; set; } = [];
    }
}