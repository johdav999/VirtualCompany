using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class TaskLifecycleIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public TaskLifecycleIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_task_with_valid_agent_returns_tenant_scoped_detail()
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
            }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var task = await response.Content.ReadFromJsonAsync<TaskDetailResponse>();

        Assert.NotNull(task);
        Assert.Equal(seed.CompanyId, task!.CompanyId);
        Assert.Equal(seed.AgentId, task.AssignedAgentId);
        Assert.Equal("new", task.Status);
        Assert.Equal("high", task.Priority);
        Assert.Equal("spring", task.InputPayload["launchId"].GetString());
    }

    [Theory]
    [InlineData(AgentStatus.Paused)]
    [InlineData(AgentStatus.Archived)]
    public async Task Create_task_rejects_unassignable_agents(AgentStatus status)
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
    public async Task Create_subtask_links_to_parent_task()
    {
        var seed = await SeedTaskCompanyAsync(AgentStatus.Active);

        using var client = CreateAuthenticatedClient();
        var parentResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/tasks", new
        {
            type = "orchestration",
            title = "Parent",
            priority = "normal"
        });

        var parent = await parentResponse.Content.ReadFromJsonAsync<TaskDetailResponse>();
        Assert.NotNull(parent);

        var childResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/tasks/{parent!.Id}/subtasks", new
        {
            type = "orchestration",
            title = "Child",
            priority = "low",
            assignedAgentId = seed.AgentId
        });

        Assert.Equal(HttpStatusCode.Created, childResponse.StatusCode);
        var child = await childResponse.Content.ReadFromJsonAsync<TaskDetailResponse>();

        Assert.NotNull(child);
        Assert.Equal(parent.Id, child!.ParentTaskId);
        Assert.Equal("Parent", child.ParentTask!.Title);
    }

    [Fact]
    public async Task Create_subtask_inherits_parent_workflow_and_parent_read_includes_subtasks()
    {
        var seed = await SeedTaskCompanyWithWorkflowAsync();

        using var client = CreateAuthenticatedClient();
        var parentResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/tasks", new
        {
            type = "orchestration",
            title = "Parent with workflow",
            priority = "normal",
            workflowInstanceId = seed.WorkflowInstanceId
        });

        Assert.Equal(HttpStatusCode.Created, parentResponse.StatusCode);
        var parent = await parentResponse.Content.ReadFromJsonAsync<TaskDetailResponse>();
        Assert.NotNull(parent);

        var childResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/tasks/{parent!.Id}/subtasks", new
        {
            type = "orchestration",
            title = "Worker subtask",
            priority = "low",
            assignedAgentId = seed.AgentId
        });

        Assert.Equal(HttpStatusCode.Created, childResponse.StatusCode);
        var child = await childResponse.Content.ReadFromJsonAsync<TaskDetailResponse>();
        Assert.NotNull(child);

        var detail = await client.GetFromJsonAsync<TaskDetailResponse>(
            $"/api/companies/{seed.CompanyId}/tasks/{parent.Id}");

        Assert.NotNull(detail);
        Assert.Equal(seed.WorkflowInstanceId, detail!.WorkflowInstanceId);
        var subtask = Assert.Single(detail.Subtasks);
        Assert.Equal(parent.Id, subtask.ParentTaskId);
        Assert.Equal(seed.AgentId, subtask.AssignedAgentId);
        Assert.Equal(seed.WorkflowInstanceId, subtask.WorkflowInstanceId);
        Assert.Equal("new", subtask.Status);
    }

    [Fact]
    public async Task Create_subtask_rejects_explicit_workflow_from_another_company()
    {
        var seed = await SeedTaskCompanyWithCrossTenantWorkflowAsync();

        using var client = CreateAuthenticatedClient();
        var parentResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/tasks", new
        {
            type = "orchestration",
            title = "Parent",
            priority = "normal"
        });

        Assert.Equal(HttpStatusCode.Created, parentResponse.StatusCode);
        var parent = await parentResponse.Content.ReadFromJsonAsync<TaskDetailResponse>();
        Assert.NotNull(parent);

        var childResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/tasks/{parent!.Id}/subtasks", new
        {
            type = "orchestration",
            title = "Worker subtask",
            priority = "low",
            assignedAgentId = seed.AgentId,
            workflowInstanceId = seed.OtherCompanyWorkflowInstanceId
        });

        Assert.Equal(HttpStatusCode.BadRequest, childResponse.StatusCode);
        var problem = await childResponse.Content.ReadFromJsonAsync<ValidationProblemResponse>();

        Assert.NotNull(problem);
        Assert.True(problem!.Errors.ContainsKey("WorkflowInstanceId"));

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var childCount = await dbContext.WorkTasks
            .AsNoTracking()
            .CountAsync(x => x.ParentTaskId == parent.Id);

        Assert.Equal(0, childCount);
    }

    [Theory]
    [InlineData(AgentStatus.Paused)]
    [InlineData(AgentStatus.Archived)]
    public async Task Create_subtask_rejects_unassignable_agents(AgentStatus status)
    {
        var seed = await SeedTaskCompanyAsync(status);

        using var client = CreateAuthenticatedClient();
        var parentResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/tasks", new
        {
            type = "orchestration",
            title = "Parent",
            priority = "normal"
        });

        var parent = await parentResponse.Content.ReadFromJsonAsync<TaskDetailResponse>();
        Assert.NotNull(parent);

        var childResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/tasks/{parent!.Id}/subtasks", new
        {
            type = "orchestration",
            title = "Child",
            priority = "low",
            assignedAgentId = seed.AgentId
        });

        Assert.Equal(HttpStatusCode.BadRequest, childResponse.StatusCode);
        var problem = await childResponse.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        Assert.NotNull(problem);
        Assert.True(problem!.Errors.ContainsKey("AssignedAgentId"));
    }

    [Fact]
    public async Task Task_reads_and_lists_are_tenant_scoped_and_filterable()
    {
        var seed = await SeedTwoCompanyTasksAsync();

        using var client = CreateAuthenticatedClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/companies/{seed.CompanyId}/tasks/{seed.OtherCompanyTaskId}")).StatusCode);

        var response = await client.GetFromJsonAsync<TaskListResponse>(
            $"/api/companies/{seed.CompanyId}/tasks?status=in_progress&assignedAgentId={seed.AgentId}");

        Assert.NotNull(response);
        var item = Assert.Single(response!.Items);
        Assert.Equal(seed.TaskId, item.Id);
        Assert.Equal(seed.AgentId, item.AssignedAgentId);
        Assert.Equal("in_progress", item.Status);
    }

    [Fact]
    public async Task Updating_status_to_completed_sets_completion_timestamp()
    {
        var seed = await SeedTwoCompanyTasksAsync();

        using var client = CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/companies/{seed.CompanyId}/tasks/{seed.TaskId}/status", new
        {
            status = "completed",
            outputPayload = new Dictionary<string, object?>
            {
                ["result"] = "done"
            },
            rationaleSummary = "Agent finished the checklist.",
            confidenceScore = 0.92m
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var task = await response.Content.ReadFromJsonAsync<TaskDetailResponse>();

        Assert.NotNull(task);
        Assert.Equal("completed", task!.Status);
        Assert.NotNull(task.CompletedAt);
        Assert.Equal("done", task.OutputPayload["result"].GetString());
        Assert.Equal("Agent finished the checklist.", task.RationaleSummary);
        Assert.Equal(0.92m, task.ConfidenceScore);
    }

    [Fact]
    public async Task Reassign_task_to_valid_agent_succeeds()
    {
        var seed = await SeedTwoCompanyTasksAsync();

        using var client = CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/companies/{seed.CompanyId}/tasks/{seed.TaskId}/assignment", new
        {
            assignedAgentId = seed.SecondAgentId
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var task = await response.Content.ReadFromJsonAsync<TaskDetailResponse>();

        Assert.NotNull(task);
        Assert.Equal(seed.SecondAgentId, task!.AssignedAgentId);
    }

    [Theory]
    [InlineData(AgentStatus.Paused)]
    [InlineData(AgentStatus.Archived)]
    public async Task Reassign_task_rejects_unassignable_agents(AgentStatus status)
    {
        var seed = await SeedTwoCompanyTasksAsync(status);

        using var client = CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/companies/{seed.CompanyId}/tasks/{seed.TaskId}/assignment", new
        {
            assignedAgentId = seed.SecondAgentId
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();

        Assert.NotNull(problem);
        var errors = Assert.Contains("AssignedAgentId", problem!.Errors);
        var expectedMessage = status == AgentStatus.Paused
            ? Agent.PausedAssignmentErrorMessage
            : Agent.ArchivedAssignmentErrorMessage;
        Assert.Equal(expectedMessage, Assert.Single(errors));
    }

    [Fact]
    public async Task Reassign_task_rejects_cross_tenant_agent_without_leaking_assignment_detail()
    {
        var seed = await SeedTwoCompanyTasksAsync();

        using var client = CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/companies/{seed.CompanyId}/tasks/{seed.TaskId}/assignment", new
        {
            assignedAgentId = seed.OtherCompanyAgentId
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var task = await dbContext.WorkTasks.AsNoTracking().SingleAsync(x => x.Id == seed.TaskId);

        Assert.Equal(seed.AgentId, task.AssignedAgentId);
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

        return new TaskSeed(companyId, agentId);
    }

    private async Task<TaskWorkflowSeed> SeedTaskCompanyWithWorkflowAsync()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var workflowDefinitionId = Guid.NewGuid();
        var workflowInstanceId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "founder@example.com", "Founder", "dev-header", "founder"));
            dbContext.Companies.Add(new Company(companyId, "Task Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            dbContext.Agents.Add(new Agent(agentId, companyId, "operations", "Ops Lead", "Operations Manager", "Operations", null, AgentSeniority.Lead, AgentStatus.Active));
            dbContext.WorkflowDefinitions.Add(new WorkflowDefinition(
                workflowDefinitionId,
                companyId,
                "task-workflow",
                "Task Workflow",
                "Operations",
                WorkflowTriggerType.Manual,
                1,
                new Dictionary<string, JsonNode?>
                {
                    ["steps"] = new JsonArray()
                }));
            dbContext.WorkflowInstances.Add(new WorkflowInstance(
                workflowInstanceId,
                companyId,
                workflowDefinitionId,
                null));
            return Task.CompletedTask;
        });

        return new TaskWorkflowSeed(companyId, agentId, workflowInstanceId);
    }

    private async Task<CrossTenantWorkflowSeed> SeedTaskCompanyWithCrossTenantWorkflowAsync()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var otherWorkflowDefinitionId = Guid.NewGuid();
        var otherWorkflowInstanceId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "founder@example.com", "Founder", "dev-header", "founder"));
            dbContext.Companies.AddRange(new Company(companyId, "Task Company"), new Company(otherCompanyId, "Other Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            dbContext.Agents.Add(new Agent(agentId, companyId, "operations", "Ops Lead", "Operations Manager", "Operations", null, AgentSeniority.Lead, AgentStatus.Active));
            dbContext.WorkflowDefinitions.Add(new WorkflowDefinition(
                otherWorkflowDefinitionId,
                otherCompanyId,
                "other-workflow",
                "Other Workflow",
                "Operations",
                WorkflowTriggerType.Manual,
                1,
                new Dictionary<string, JsonNode?>
                {
                    ["steps"] = new JsonArray()
                }));
            dbContext.WorkflowInstances.Add(new WorkflowInstance(
                otherWorkflowInstanceId,
                otherCompanyId,
                otherWorkflowDefinitionId,
                null));
            return Task.CompletedTask;
        });

        return new CrossTenantWorkflowSeed(companyId, agentId, otherWorkflowInstanceId);
    }

    private async Task<CrossTenantTaskSeed> SeedTwoCompanyTasksAsync(AgentStatus secondAgentStatus = AgentStatus.Active)
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var secondAgentId = Guid.NewGuid();
        var otherCompanyAgentId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var otherCompanyTaskId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "founder@example.com", "Founder", "dev-header", "founder"));
            dbContext.Companies.AddRange(new Company(companyId, "Task Company"), new Company(otherCompanyId, "Other Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            dbContext.Agents.AddRange(
                new Agent(agentId, companyId, "operations", "Ops Lead", "Operations Manager", "Operations", null, AgentSeniority.Lead, AgentStatus.Active),
                new Agent(secondAgentId, companyId, "support", "Support Lead", "Support Manager", "Support", null, AgentSeniority.Senior, secondAgentStatus),
                new Agent(otherCompanyAgentId, otherCompanyId, "finance", "Other Finance", "Finance Manager", "Finance", null, AgentSeniority.Senior, AgentStatus.Active));

            var task = new WorkTask(taskId, companyId, "orchestration", "Company task", null, WorkTaskPriority.High, agentId, null, "user", userId);
            task.UpdateStatus(WorkTaskStatus.InProgress);
            dbContext.WorkTasks.Add(task);
            dbContext.WorkTasks.Add(new WorkTask(otherCompanyTaskId, otherCompanyId, "orchestration", "Other task", null, WorkTaskPriority.High, otherCompanyAgentId, null, "user", userId));
            return Task.CompletedTask;
        });

        return new CrossTenantTaskSeed(companyId, agentId, secondAgentId, otherCompanyAgentId, taskId, otherCompanyTaskId);
    }

    private sealed record TaskSeed(Guid CompanyId, Guid AgentId);
    private sealed record TaskWorkflowSeed(Guid CompanyId, Guid AgentId, Guid WorkflowInstanceId);
    private sealed record CrossTenantWorkflowSeed(Guid CompanyId, Guid AgentId, Guid OtherCompanyWorkflowInstanceId);
    private sealed record CrossTenantTaskSeed(Guid CompanyId, Guid AgentId, Guid SecondAgentId, Guid OtherCompanyAgentId, Guid TaskId, Guid OtherCompanyTaskId);

    private sealed class TaskDetailResponse
    {
        public Guid Id { get; set; }
        public Guid CompanyId { get; set; }
        public string Priority { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public Guid? AssignedAgentId { get; set; }
        public Guid? ParentTaskId { get; set; }
        public Guid? WorkflowInstanceId { get; set; }
        public Dictionary<string, JsonElement> InputPayload { get; set; } = [];
        public Dictionary<string, JsonElement> OutputPayload { get; set; } = [];
        public string? RationaleSummary { get; set; }
        public decimal? ConfidenceScore { get; set; }
        public DateTime? CompletedAt { get; set; }
        public TaskParentResponse? ParentTask { get; set; }
        public List<TaskSubtaskResponse> Subtasks { get; set; } = [];
    }

    private sealed class TaskParentResponse
    {
        public string Title { get; set; } = string.Empty;
    }

    private sealed class TaskSubtaskResponse
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public Guid? AssignedAgentId { get; set; }
        public Guid? ParentTaskId { get; set; }
        public Guid? WorkflowInstanceId { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private sealed class TaskListResponse
    {
        public List<TaskListItemResponse> Items { get; set; } = [];
    }

    private sealed class TaskListItemResponse
    {
        public Guid Id { get; set; }
        public Guid? AssignedAgentId { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    private sealed class ValidationProblemResponse
    {
        public Dictionary<string, string[]> Errors { get; set; } = [];
    }
}
