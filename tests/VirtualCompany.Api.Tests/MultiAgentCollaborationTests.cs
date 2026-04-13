using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class MultiAgentCollaborationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public MultiAgentCollaborationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.ToolExecutor.Reset();
    }

    [Fact]
    public async Task Manager_worker_collaboration_creates_linked_subtasks_and_consolidates_attribution()
    {
        var seed = await SeedScenarioAsync();
        using var client = CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/tasks/manager-worker-collaborations", new
        {
            objective = "Prepare a cross-functional launch readiness answer.",
            coordinatorAgentId = seed.CoordinatorAgentId,
            initiatingActorId = seed.UserId,
            initiatingActorType = "user",
            correlationId = "mw-collaboration-001",
            limits = new
            {
                maxWorkers = 2,
                maxDepth = 1,
                maxRuntimeSeconds = 30,
                maxTotalSteps = 2
            },
            workers = new[]
            {
                new
                {
                    agentId = seed.FinanceAgentId,
                    objective = "Summarize launch budget readiness.",
                    instructions = "Use the finance view only."
                },
                new
                {
                    agentId = seed.OperationsAgentId,
                    objective = "Summarize operations readiness.",
                    instructions = "Use the operations view only."
                }
            }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<MultiAgentCollaborationResponse>();
        Assert.NotNull(payload);
        Assert.Equal(seed.CompanyId, payload!.CompanyId);
        Assert.Equal(seed.CoordinatorAgentId, payload.CoordinatorAgentId);
        Assert.Equal("completed", payload.Status);
        Assert.Equal("mw-collaboration-001", payload.CorrelationId);
        Assert.Equal(2, payload.Contributions.Count);
        Assert.All(payload.Contributions, contribution =>
        {
            Assert.NotEqual(Guid.Empty, contribution.SubtaskId);
            Assert.Equal(contribution.SubtaskId, contribution.SourceTaskId);
            Assert.False(string.IsNullOrWhiteSpace(contribution.AgentName));
            Assert.False(string.IsNullOrWhiteSpace(contribution.AgentRole));
            Assert.False(string.IsNullOrWhiteSpace(contribution.RationaleSummary));
            Assert.Contains(contribution.AgentId, new[] { seed.FinanceAgentId, seed.OperationsAgentId });
        });
        Assert.Equal(payload.FinalResponse, payload.ConsolidatedResponse.FinalResponse);
        Assert.Equal(2, payload.ConsolidatedResponse.Contributions.Count);
        Assert.Contains("Attributed worker sources:", payload.FinalResponse);
        Assert.Contains("Finance Worker", payload.FinalResponse);
        Assert.Contains("Operations Worker", payload.FinalResponse);
        Assert.Contains(seed.FinanceAgentId.ToString("N"), payload.FinalResponse);
        Assert.Contains(seed.OperationsAgentId.ToString("N"), payload.FinalResponse);
        Assert.All(payload.ConsolidatedResponse.Contributions, contribution => Assert.False(string.IsNullOrWhiteSpace(contribution.AgentName)));
        Assert.Equal(2, payload.ContributorRationaleSummaries.Count);
        Assert.Equal(2, payload.ConsolidatedResponse.ContributorRationaleSummaries.Count);
        foreach (var contribution in payload.Contributions)
        {
            var rationaleSummary = Assert.Single(
                payload.ContributorRationaleSummaries,
                summary => summary.AgentId == contribution.AgentId);
            Assert.Equal(contribution.AgentName, rationaleSummary.AgentName);
            Assert.Equal(contribution.AgentRole, rationaleSummary.AgentRole);
            Assert.Equal(contribution.SubtaskId, rationaleSummary.SubtaskId);
            Assert.Equal(contribution.SourceTaskId, rationaleSummary.SourceTaskId);
            Assert.Equal(contribution.Sequence, rationaleSummary.Sequence);
            Assert.Equal(contribution.Status, rationaleSummary.Status);
            Assert.Equal(contribution.RationaleSummary, rationaleSummary.RationaleSummary);
        }

        Assert.Equal(2, payload.Steps.Count);
        Assert.All(payload.Steps, step =>
        {
            Assert.Equal(payload.ParentTaskId, step.ParentTaskId);
            Assert.True(step.SubtaskId.HasValue);
            Assert.Equal(1, step.DelegationDepth);
        });

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var parent = await dbContext.WorkTasks.AsNoTracking().SingleAsync(x => x.Id == payload.ParentTaskId);
        Assert.Equal(MultiAgentCollaborationTaskTypes.Parent, parent.Type);
        Assert.Equal(seed.CoordinatorAgentId, parent.AssignedAgentId);
        Assert.Equal(WorkTaskStatus.Completed, parent.Status);
        Assert.Equal("mw-collaboration-001", parent.CorrelationId);
        Assert.Equal(payload.PlanId.ToString("N"), parent.OutputPayload["planId"]!.GetValue<string>());
        Assert.True(parent.OutputPayload.ContainsKey("contributions"));
        Assert.Equal("completed", parent.OutputPayload["terminationReason"]!.GetValue<string>());
        Assert.False(parent.OutputPayload["isRetryable"]!.GetValue<bool>());
        Assert.Equal(2, parent.OutputPayload["metrics"]!["plannedStepCount"]!.GetValue<int>());
        Assert.True(parent.OutputPayload.ContainsKey("sourceAttribution"));
        Assert.True(parent.OutputPayload.ContainsKey("consolidatedResponse"));
        Assert.DoesNotContain("chain-of-thought", parent.RationaleSummary ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        var sourceAttribution = parent.OutputPayload["sourceAttribution"]!.AsArray();
        Assert.Equal(2, sourceAttribution.Count);
        Assert.All(sourceAttribution, source =>
        {
            Assert.False(string.IsNullOrWhiteSpace(source!["agentName"]!.GetValue<string>()));
            Assert.False(string.IsNullOrWhiteSpace(source["rationaleSummary"]!.GetValue<string>()));
            Assert.NotEqual(Guid.Empty, Guid.Parse(source["sourceTaskId"]!.GetValue<string>()));
        });
        var contributorRationaleSummaries = parent.OutputPayload["contributorRationaleSummaries"]!.AsArray();
        Assert.Equal(2, contributorRationaleSummaries.Count);
        Assert.All(contributorRationaleSummaries, summary =>
        {
            Assert.False(string.IsNullOrWhiteSpace(summary!["agentName"]!.GetValue<string>()));
            Assert.False(string.IsNullOrWhiteSpace(summary["rationaleSummary"]!.GetValue<string>()));
            Assert.NotEqual(Guid.Empty, Guid.Parse(summary["agentId"]!.GetValue<string>()));
            Assert.NotEqual(Guid.Empty, Guid.Parse(summary["subtaskId"]!.GetValue<string>()));
            Assert.NotEqual(Guid.Empty, Guid.Parse(summary["sourceTaskId"]!.GetValue<string>()));
            Assert.Contains(Guid.Parse(summary["agentId"]!.GetValue<string>()), new[] { seed.FinanceAgentId, seed.OperationsAgentId });
        });
        var consolidatedResponse = parent.OutputPayload["consolidatedResponse"]!.AsObject();
        Assert.Equal(payload.FinalResponse, consolidatedResponse["finalResponse"]!.GetValue<string>());
        Assert.Equal(2, consolidatedResponse["contributions"]!.AsArray().Count);
        Assert.Equal(2, consolidatedResponse["contributorRationaleSummaries"]!.AsArray().Count);

        var subtasks = await dbContext.WorkTasks
            .AsNoTracking()
            .Where(x => x.ParentTaskId == payload.ParentTaskId)
            .OrderBy(x => x.CreatedUtc)
            .ToListAsync();
        Assert.Equal(2, subtasks.Count);
        Assert.All(subtasks, subtask =>
        {
            Assert.Equal(MultiAgentCollaborationTaskTypes.WorkerSubtask, subtask.Type);
            Assert.Equal(WorkTaskStatus.Completed, subtask.Status);
            Assert.Equal(payload.ParentTaskId.ToString("N"), subtask.InputPayload["parentTaskId"]!.GetValue<string>());
            Assert.False(subtask.InputPayload["allowFurtherDelegation"]!.GetValue<bool>());
        });

        var auditActions = await dbContext.AuditEvents
            .AsNoTracking()
            .Where(x => x.CompanyId == seed.CompanyId && x.CorrelationId == "mw-collaboration-001")
            .Select(x => x.Action)
            .ToListAsync();
        Assert.Contains(AuditEventActions.MultiAgentCollaborationStarted, auditActions);
        Assert.Contains(AuditEventActions.MultiAgentCollaborationPlanCreated, auditActions);
        Assert.Contains(AuditEventActions.MultiAgentWorkerSubtaskCreated, auditActions);
        Assert.Contains(AuditEventActions.MultiAgentWorkerCompleted, auditActions);
        Assert.Contains(AuditEventActions.MultiAgentCollaborationConsolidated, auditActions);
    }

    [Fact]
    public async Task Manager_worker_collaboration_enforces_fanout_and_depth_limits()
    {
        var seed = await SeedScenarioAsync();
        using var client = CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/tasks/manager-worker-collaborations", new
        {
            objective = "Invalid deep collaboration.",
            coordinatorAgentId = seed.CoordinatorAgentId,
            initiatingActorId = seed.UserId,
            initiatingActorType = "user",
            correlationId = "mw-collaboration-invalid",
            limits = new
            {
                maxWorkers = 1,
                maxDepth = 1,
                maxRuntimeSeconds = 30,
                maxTotalSteps = 1
            },
            workers = new[]
            {
                new { agentId = seed.FinanceAgentId, objective = "Finance view." },
                new { agentId = seed.OperationsAgentId, objective = "Operations view." }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Worker fan-out cannot exceed", body);

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var auditEvent = await dbContext.AuditEvents
            .AsNoTracking()
            .SingleAsync(x =>
                x.CompanyId == seed.CompanyId &&
                x.CorrelationId == "mw-collaboration-invalid" &&
                x.Action == AuditEventActions.MultiAgentCollaborationGuardrailDenied);

        Assert.Equal(AuditEventOutcomes.Denied, auditEvent.Outcome);
        Assert.Equal(MultiAgentCollaborationTerminationReasons.FanOutExceeded, auditEvent.Metadata["terminationReason"]);
        Assert.Equal("fan_out", auditEvent.Metadata["limitType"]);
        Assert.Equal("1", auditEvent.Metadata["configuredThreshold"]);
        Assert.Equal("2", auditEvent.Metadata["observedValue"]);
    }

    [Fact]
    public async Task Manager_worker_collaboration_rejects_self_assignment_cycle()
    {
        var seed = await SeedScenarioAsync();
        using var client = CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/tasks/manager-worker-collaborations", new
        {
            objective = "Invalid recursive collaboration.",
            coordinatorAgentId = seed.CoordinatorAgentId,
            initiatingActorId = seed.UserId,
            initiatingActorType = "user",
            correlationId = "mw-collaboration-cycle",
            workers = new[]
            {
                new { agentId = seed.CoordinatorAgentId, objective = "Coordinator should not self-assign." }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Coordinator agents cannot self-assign", body);
    }

    [Fact]
    public async Task Manager_worker_collaboration_rejects_missing_explicit_plan()
    {
        var seed = await SeedScenarioAsync();
        using var client = CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/tasks/manager-worker-collaborations", new
        {
            objective = "Prepare a cross-functional launch readiness answer.",
            coordinatorAgentId = seed.CoordinatorAgentId,
            initiatingActorId = seed.UserId,
            initiatingActorType = "user",
            correlationId = "mw-collaboration-auto-plan",
            limits = new
            {
                maxWorkers = 2,
                maxDepth = 1,
                maxRuntimeSeconds = 30,
                maxTotalSteps = 2
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("An explicit manager-worker collaboration plan is required before delegation.", body);
    }

    [Fact]
    public async Task Manager_worker_collaboration_rejects_worker_initiated_nested_delegation()
    {
        var seed = await SeedScenarioAsync();
        using var client = CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/tasks/manager-worker-collaborations", new
        {
            objective = "Invalid nested collaboration.",
            coordinatorAgentId = seed.CoordinatorAgentId,
            initiatingActorId = seed.FinanceAgentId,
            initiatingActorType = "agent",
            correlationId = "mw-collaboration-nested",
            inputPayload = new Dictionary<string, object?>
            {
                ["collaborationRole"] = "worker_subtask",
                ["delegationDepth"] = 1,
                ["allowFurtherDelegation"] = false
            },
            workers = new[]
            {
                new { agentId = seed.OperationsAgentId, objective = "Operations view." }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Worker subtasks cannot start nested manager-worker coordination.", body);
        Assert.Contains("Further delegation is disabled for this task context.", body);
    }

    [Fact]
    public async Task Manager_worker_collaboration_rejects_duplicate_worker_agent_assignments()
    {
        var seed = await SeedScenarioAsync();
        using var client = CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/tasks/manager-worker-collaborations", new
        {
            objective = "Invalid duplicate assignment.",
            coordinatorAgentId = seed.CoordinatorAgentId,
            initiatingActorId = seed.UserId,
            initiatingActorType = "user",
            correlationId = "mw-collaboration-duplicate-agent",
            workers = new[]
            {
                new { agentId = seed.FinanceAgentId, objective = "Finance budget view." },
                new { agentId = seed.FinanceAgentId, objective = "Finance risk view." }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Each worker agent can be assigned at most one planned subtask.", body);
    }

    [Fact]
    public async Task Manager_worker_collaboration_rejects_total_step_budget_exhaustion_before_execution()
    {
        var seed = await SeedScenarioAsync();
        using var client = CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/tasks/manager-worker-collaborations", new
        {
            objective = "Invalid step budget.",
            coordinatorAgentId = seed.CoordinatorAgentId,
            initiatingActorId = seed.UserId,
            initiatingActorType = "user",
            correlationId = "mw-collaboration-step-limit",
            limits = new
            {
                maxWorkers = 2,
                maxDepth = 1,
                maxRuntimeSeconds = 30,
                maxTotalSteps = 1
            },
            workers = new[]
            {
                new { agentId = seed.FinanceAgentId, objective = "Finance view." },
                new { agentId = seed.OperationsAgentId, objective = "Operations view." }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Collaboration step count cannot exceed 1.", body);
    }

    [Fact]
    public async Task Manager_worker_collaboration_rejects_cyclic_delegation_chain()
    {
        var seed = await SeedScenarioAsync();
        using var client = CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/tasks/manager-worker-collaborations", new
        {
            objective = "Invalid cyclic delegation chain.",
            coordinatorAgentId = seed.CoordinatorAgentId,
            initiatingActorId = seed.UserId,
            initiatingActorType = "user",
            correlationId = "mw-collaboration-chain-cycle",
            inputPayload = new Dictionary<string, object?>
            {
                ["delegationChain"] = new[] { seed.FinanceAgentId.ToString("N"), seed.FinanceAgentId.ToString("N") }
            },
            workers = new[]
            {
                new { agentId = seed.OperationsAgentId, objective = "Operations view." }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Delegation chain contains a repeated agent", body);
    }

    [Fact]
    public async Task Worker_subtask_cannot_create_unplanned_child_subtask()
    {
        var seed = await SeedScenarioAsync();
        using var client = CreateAuthenticatedClient();

        var parent = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/tasks/manager-worker-collaborations", new
        {
            objective = "Prepare a bounded answer.",
            coordinatorAgentId = seed.CoordinatorAgentId,
            initiatingActorId = seed.UserId,
            initiatingActorType = "user",
            correlationId = "mw-collaboration-unplanned-child",
            workers = new[]
            {
                new { agentId = seed.FinanceAgentId, objective = "Finance view." }
            }
        });
        var payload = await parent.Content.ReadFromJsonAsync<MultiAgentCollaborationResponse>();
        Assert.NotNull(payload);
        var workerTaskId = Assert.Single(payload!.Steps).SubtaskId!.Value;

        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/tasks/{workerTaskId}/subtasks", new
        {
            type = "manager_worker_collaboration",
            title = "Unplanned nested collaboration",
            description = "This should be denied.",
            assignedAgentId = seed.OperationsAgentId
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Worker subtasks cannot create additional subtasks outside an approved manager-worker plan.", body);
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, "founder-multi");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, "founder-multi@example.com");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, "Founder Multi");
        return client;
    }

    private async Task<SeededCollaborationScenario> SeedScenarioAsync()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var coordinatorAgentId = Guid.NewGuid();
        var financeAgentId = Guid.NewGuid();
        var operationsAgentId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "founder-multi@example.com", "Founder Multi", "dev-header", "founder-multi"));
            dbContext.Companies.Add(new Company(companyId, "Collaboration Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(),
                companyId,
                userId,
                CompanyMembershipRole.Owner,
                CompanyMembershipStatus.Active));
            dbContext.Agents.Add(CreateAgent(coordinatorAgentId, companyId, "Coordinator", "General Management", "Coordinate bounded collaboration."));
            dbContext.Agents.Add(CreateAgent(financeAgentId, companyId, "Finance Worker", "Finance", "Assess finance readiness."));
            dbContext.Agents.Add(CreateAgent(operationsAgentId, companyId, "Operations Worker", "Operations", "Assess operations readiness."));
            return Task.CompletedTask;
        });

        return new SeededCollaborationScenario(companyId, userId, coordinatorAgentId, financeAgentId, operationsAgentId);
    }

    private static Agent CreateAgent(Guid agentId, Guid companyId, string displayName, string department, string roleBrief) =>
        new(
            agentId,
            companyId,
            displayName.ToLowerInvariant().Replace(' ', '-'),
            displayName,
            displayName,
            department,
            null,
            AgentSeniority.Senior,
            AgentStatus.Active,
            autonomyLevel: AgentAutonomyLevel.Level2,
            objectives: Payload(("primary", new JsonArray(JsonValue.Create(roleBrief)))),
            kpis: Payload(("targets", new JsonArray(JsonValue.Create("readiness")))),
            tools: Payload(("allowed", new JsonArray(JsonValue.Create("erp")))),
            scopes: Payload(("read", new JsonArray(JsonValue.Create("launch")))),
            thresholds: Payload(("approval", new JsonObject { ["expenseUsd"] = 1000 })),
            escalationRules: Payload(("escalateTo", JsonValue.Create("founder"))),
            roleBrief: roleBrief);

    private static Dictionary<string, JsonNode?> Payload(params (string Key, JsonNode? Value)[] properties)
    {
        var payload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in properties)
        {
            payload[key] = value?.DeepClone();
        }

        return payload;
    }

    private sealed record SeededCollaborationScenario(Guid CompanyId, Guid UserId, Guid CoordinatorAgentId, Guid FinanceAgentId, Guid OperationsAgentId);

    private sealed class MultiAgentCollaborationResponse
    {
        public Guid PlanId { get; set; }
        public Guid CompanyId { get; set; }
        public Guid ParentTaskId { get; set; }
        public Guid CoordinatorAgentId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string FinalResponse { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public List<ContributionResponse> Contributions { get; set; } = [];
        public List<ContributorRationaleSummaryResponse> ContributorRationaleSummaries { get; set; } = [];
        public ConsolidatedResponse ConsolidatedResponse { get; set; } = new();
        public List<StepResponse> Steps { get; set; } = [];
    }

    private sealed class ContributionResponse
    {
        public Guid AgentId { get; set; }
        public string AgentName { get; set; } = string.Empty;
        public string? AgentRole { get; set; }
        public Guid SubtaskId { get; set; }
        public Guid SourceTaskId { get; set; }
        public int Sequence { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Output { get; set; } = string.Empty;
        public string? RationaleSummary { get; set; }
    }

    private sealed class ContributorRationaleSummaryResponse
    {
        public Guid AgentId { get; set; }
        public string AgentName { get; set; } = string.Empty;
        public string? AgentRole { get; set; }
        public Guid SubtaskId { get; set; }
        public Guid SourceTaskId { get; set; }
        public int Sequence { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? RationaleSummary { get; set; }
        public decimal? ConfidenceScore { get; set; }
        public string CorrelationId { get; set; } = string.Empty;
    }

    private sealed class ConsolidatedResponse
    {
        public string FinalResponse { get; set; } = string.Empty;
        public List<ContributionResponse> Contributions { get; set; } = [];
        public List<ContributorRationaleSummaryResponse> ContributorRationaleSummaries { get; set; } = [];
    }

    private sealed class StepResponse
    {
        public int Sequence { get; set; }
        public Guid ParentTaskId { get; set; }
        public Guid? SubtaskId { get; set; }
        public Guid AssignedAgentId { get; set; }
        public int DelegationDepth { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
