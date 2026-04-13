using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class AgentExecutionPolicyGuardrailTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public AgentExecutionPolicyGuardrailTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.ToolExecutor.Reset();
    }

    [Fact]
    public async Task Denied_execution_is_blocked_before_tool_executor_runs_and_attempt_is_auditable()
    {
        var seed = await SeedAgentAsync(
            autonomyLevel: AgentAutonomyLevel.Level2,
            tools: Payload(("allowed", new JsonArray(JsonValue.Create("erp")))),
            scopes: Payload(("execute", new JsonArray(JsonValue.Create("payments")))),
            thresholds: Payload(("approval", new JsonObject { ["expenseUsd"] = 1000 })),
            escalationRules: Payload(("escalateTo", JsonValue.Create("founder"))));

        using var client = CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
        {
            toolName = "wire_transfer",
            actionType = "execute",
            scope = "payments",
            requestPayload = new { paymentId = "pay-100" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();
        Assert.NotNull(payload);
        Assert.Equal("denied", payload!.Status);
        Assert.Equal("deny", payload.PolicyDecision.Outcome);
        Assert.Contains("tool_not_permitted", payload.PolicyDecision.ReasonCodes);
        Assert.Equal("This action was blocked by policy because the agent is not permitted to use that tool.", payload.Message);
        Assert.Equal(0, _factory.ToolExecutor.ExecutionCount);

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var attempt = await dbContext.ToolExecutionAttempts.AsNoTracking().SingleAsync(x => x.Id == payload.ExecutionId);
        Assert.Equal(ToolExecutionStatus.Denied, attempt.Status);
        Assert.Equal("wire_transfer", attempt.ToolName);
        Assert.Equal("deny", attempt.PolicyDecision["outcome"]!.GetValue<string>());
        Assert.Equal(PolicyDecisionSchemaVersions.V1, attempt.PolicyDecision["schemaVersion"]!.GetValue<string>());
        Assert.Equal(seed.CompanyId, attempt.PolicyDecision["tenant"]!["companyId"]!.GetValue<Guid>());
        Assert.Equal("tool_not_permitted", attempt.PolicyDecision["reasons"]![0]!["code"]!.GetValue<string>());
        Assert.Null(attempt.ApprovalRequestId);

        var auditEvent = await dbContext.AuditEvents.AsNoTracking().SingleAsync(x =>
            x.CompanyId == seed.CompanyId &&
            x.Action == AuditEventActions.AgentToolExecutionDenied &&
            x.TargetId == payload.ExecutionId.ToString("N"));

        Assert.Equal(AuditActorTypes.Agent, auditEvent.ActorType);
        Assert.Equal(seed.AgentId, auditEvent.ActorId);
        Assert.Equal("tool_not_permitted", auditEvent.Metadata["primaryReasonCode"]);
        Assert.Equal(PolicyDecisionSchemaVersions.V1, auditEvent.Metadata["policyDecisionSchemaVersion"]);
        Assert.Contains("blocked by policy before execution", auditEvent.RationaleSummary);
    }

    [Fact]
    public async Task Missing_execute_scope_configuration_is_blocked_before_tool_executor_runs()
    {
        var seed = await SeedAgentAsync(
            autonomyLevel: AgentAutonomyLevel.Level2,
            tools: Payload(("allowed", new JsonArray(JsonValue.Create("erp")))),
            scopes: Payload(("write", new JsonArray(JsonValue.Create("payments")))),
            thresholds: Payload(("approval", new JsonObject { ["expenseUsd"] = 1000 })),
            escalationRules: Payload(("escalateTo", JsonValue.Create("founder"))));

        using var client = CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
        {
            toolName = "erp",
            actionType = "execute",
            scope = "payments",
            requestPayload = new { paymentId = "pay-legacy-scope" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();
        Assert.NotNull(payload);
        Assert.Equal("denied", payload!.Status);
        Assert.Equal("deny", payload.PolicyDecision.Outcome);
        Assert.Contains(PolicyDecisionReasonCodes.MissingPolicyConfiguration, payload.PolicyDecision.ReasonCodes);
        Assert.Equal("This action was blocked by policy because the required guardrail configuration could not be verified.", payload.Message);
        Assert.Equal(0, _factory.ToolExecutor.ExecutionCount);

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var attempt = await dbContext.ToolExecutionAttempts.AsNoTracking().SingleAsync(x => x.Id == payload.ExecutionId);
        Assert.Equal(ToolExecutionStatus.Denied, attempt.Status);
        Assert.Equal("missing", attempt.PolicyDecision["metadata"]!["scopeConfigState"]!.GetValue<string>());
        Assert.Equal("execute", attempt.PolicyDecision["metadata"]!["scopePolicyBucket"]!.GetValue<string>());
    }

    [Fact]
    public async Task Level_0_execute_is_blocked_before_tool_executor_runs()
    {
        var seed = await SeedAgentAsync(
            autonomyLevel: AgentAutonomyLevel.Level0,
            tools: Payload(("allowed", new JsonArray(JsonValue.Create("erp")))),
            scopes: Payload(("execute", new JsonArray(JsonValue.Create("payments"))), ("read", new JsonArray(JsonValue.Create("finance")))),
            thresholds: Payload(("approval", new JsonObject { ["expenseUsd"] = 1000 })),
            escalationRules: Payload(("escalateTo", JsonValue.Create("founder"))));

        using var client = CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
        {
            toolName = "erp",
            actionType = "execute",
            scope = "payments",
            requestPayload = new { paymentId = "pay-101" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();
        Assert.NotNull(payload);
        Assert.Equal("denied", payload!.Status);
        Assert.Equal("deny", payload.PolicyDecision.Outcome);
        Assert.Equal("execute", payload.PolicyDecision.EvaluatedActionType);
        Assert.Contains("autonomy_level_blocks_action", payload.PolicyDecision.ReasonCodes);
        Assert.Equal(0, _factory.ToolExecutor.ExecutionCount);

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var attempt = await dbContext.ToolExecutionAttempts.AsNoTracking().SingleAsync(x => x.Id == payload.ExecutionId);
        Assert.Equal("execute", attempt.PolicyDecision["evaluatedActionType"]!.GetValue<string>());
        Assert.Equal("autonomy", attempt.PolicyDecision["reasons"]![0]!["category"]!.GetValue<string>());
        Assert.Equal(seed.AgentId, attempt.PolicyDecision["actor"]!["agentId"]!.GetValue<Guid>());
    }

    [Fact]
    public async Task Above_threshold_execution_creates_pending_approval_and_skips_tool_executor()
    {
        var seed = await SeedAgentAsync(
            autonomyLevel: AgentAutonomyLevel.Level2,
            tools: Payload(("allowed", new JsonArray(JsonValue.Create("erp")))),
            scopes: Payload(("execute", new JsonArray(JsonValue.Create("payments")))),
            thresholds: Payload(("approval", new JsonObject { ["expenseUsd"] = 1000 })),
            escalationRules: Payload(("critical", new JsonArray(JsonValue.Create("over_limit"))), ("escalateTo", JsonValue.Create("founder"))));

        using var client = CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
        {
            toolName = "erp",
            actionType = "execute",
            scope = "payments",
            thresholdCategory = "approval",
            thresholdKey = "expenseUsd",
            thresholdValue = 1500,
            sensitiveAction = true,
            requestPayload = new { expenseId = "exp-200" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();
        Assert.NotNull(payload);
        Assert.Equal("awaiting_approval", payload!.Status);
        Assert.Equal("require_approval", payload.PolicyDecision.Outcome);
        Assert.Contains("threshold_exceeded_requires_approval", payload.PolicyDecision.ReasonCodes);
        Assert.Contains("sensitive_action_requires_approval", payload.PolicyDecision.ReasonCodes);
        Assert.Equal("This sensitive action is pending approval and was not executed.", payload.Message);
        Assert.NotNull(payload.ApprovalRequestId);
        Assert.Equal(0, _factory.ToolExecutor.ExecutionCount);
        Assert.NotNull(payload.ApprovalDecisionChain);
        Assert.Equal("pending", payload.ApprovalDecisionChain!["status"].GetString());
        Assert.Equal("approval_request_created", payload.ApprovalDecisionChain["currentStep"].GetString());
        Assert.Equal("policy_evaluation", payload.ApprovalDecisionChain["steps"][0].GetProperty("step").GetString());

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var approval = await dbContext.ApprovalRequests.AsNoTracking().SingleAsync(x => x.Id == payload.ApprovalRequestId);
        Assert.Equal(ApprovalRequestStatus.Pending, approval.Status);
        Assert.Equal("founder", approval.ApprovalTarget);
        Assert.True(approval.ThresholdContext["sensitiveAction"]!.GetValue<bool>());
        Assert.Equal("expenseUsd", approval.ThresholdContext["thresholdKey"]!.GetValue<string>());
        Assert.Equal("execute", approval.ThresholdContext["actionType"]!.GetValue<string>());
        Assert.Equal("awaiting_approval", approval.ThresholdContext["executionState"]!.GetValue<string>());
        Assert.Equal("pending", approval.DecisionChain["status"]!.GetValue<string>());
        Assert.Equal("approval_request_created", approval.DecisionChain["currentStep"]!.GetValue<string>());
        Assert.Equal("policy_evaluation", approval.DecisionChain["steps"]![0]!["step"]!.GetValue<string>());
        Assert.Equal(payload.ExecutionId, approval.DecisionChain["executionId"]!.GetValue<Guid>());
        Assert.Equal(payload.ApprovalRequestId, approval.DecisionChain["approvalRequestId"]!.GetValue<Guid>());

        var attempt = await dbContext.ToolExecutionAttempts.AsNoTracking().SingleAsync(x => x.Id == payload.ExecutionId);
        Assert.Equal(ToolExecutionStatus.AwaitingApproval, attempt.Status);
        Assert.Equal(payload.ApprovalRequestId, attempt.ApprovalRequestId);
        Assert.True(attempt.PolicyDecision["approvalRequired"]!.GetValue<bool>());
        Assert.Equal("awaiting_approval", attempt.PolicyDecision["metadata"]!["executionState"]!.GetValue<string>());
        Assert.True(attempt.PolicyDecision["metadata"]!["blockedPendingApproval"]!.GetValue<bool>());
        Assert.Equal("threshold", attempt.PolicyDecision["approvalRequirement"]!["requirementType"]!.GetValue<string>());
        Assert.Equal("expenseUsd", attempt.PolicyDecision["thresholdEvaluations"]![0]!["key"]!.GetValue<string>());
        Assert.True(attempt.PolicyDecision["metadata"]!["thresholdEvaluation"]!["sensitiveAction"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Policy_required_approval_skips_tool_executor_and_creates_pending_approval()
    {
        var seed = await SeedAgentAsync(
            autonomyLevel: AgentAutonomyLevel.Level3,
            tools: Payload(("allowed", new JsonArray(JsonValue.Create("erp")))),
            scopes: Payload(("execute", new JsonArray(JsonValue.Create("payments")))),
            thresholds: Payload(("approval", new JsonObject { ["expenseUsd"] = 5000 })),
            escalationRules: Payload(
                ("escalateTo", JsonValue.Create("founder")),
                ("requireApproval", new JsonObject
                {
                    ["actions"] = new JsonArray(JsonValue.Create("execute")),
                    ["tools"] = new JsonArray(JsonValue.Create("erp")),
                    ["scopes"] = new JsonArray(JsonValue.Create("payments"))
                })));

        using var client = CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
        {
            toolName = "erp",
            actionType = "execute",
            scope = "payments",
            requestPayload = new { paymentId = "pay-500" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();
        Assert.NotNull(payload);
        Assert.Equal("awaiting_approval", payload!.Status);
        Assert.Equal("require_approval", payload.PolicyDecision.Outcome);
        Assert.Contains("approval_required_by_policy", payload.PolicyDecision.ReasonCodes);
        Assert.Equal(0, _factory.ToolExecutor.ExecutionCount);
        Assert.NotNull(payload.ApprovalRequestId);
        Assert.True(payload.PolicyDecision.Metadata.ContainsKey("approvalRequirementPolicy"));
        Assert.Equal("execute", payload.PolicyDecision.Metadata["approvalRequirementPolicy"].GetProperty("actions")[0].GetString());
    }

    [Fact]
    public async Task Allowed_execution_runs_tool_executor_and_persists_structured_policy_decision_metadata()
    {
        var seed = await SeedAgentAsync(
            autonomyLevel: AgentAutonomyLevel.Level2,
            tools: Payload(("allowed", new JsonArray(JsonValue.Create("erp")))),
            scopes: Payload(("execute", new JsonArray(JsonValue.Create("payments")))),
            thresholds: Payload(("approval", new JsonObject { ["expenseUsd"] = 5000 })),
            escalationRules: Payload(("critical", new JsonArray(JsonValue.Create("over_limit"))), ("escalateTo", JsonValue.Create("founder"))));

        using var client = CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
        {
            toolName = "erp",
            actionType = "execute",
            scope = "payments",
            thresholdCategory = "approval",
            thresholdKey = "expenseUsd",
            thresholdValue = 750,
            requestPayload = new { expenseId = "exp-300" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();
        Assert.NotNull(payload);
        Assert.Equal("executed", payload!.Status);
        Assert.Equal("allow", payload.PolicyDecision.Outcome);
        Assert.Equal("execute", payload.PolicyDecision.EvaluatedActionType);
        Assert.Equal("default_deny", payload.PolicyDecision.Metadata["policyMode"].GetString());
        Assert.Equal(1, _factory.ToolExecutor.ExecutionCount);
        Assert.NotNull(payload.ExecutionResult);
        Assert.Equal("erp", payload.ExecutionResult!["toolName"].GetString());

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var attempt = await dbContext.ToolExecutionAttempts.AsNoTracking().SingleAsync(x => x.Id == payload.ExecutionId);
        Assert.Equal(ToolExecutionStatus.Executed, attempt.Status);
        Assert.Equal("execute", attempt.PolicyDecision["evaluatedActionType"]!.GetValue<string>());
        Assert.Equal("default_deny", attempt.PolicyDecision["metadata"]!["policyMode"]!.GetValue<string>());
        Assert.Equal(PolicyDecisionSchemaVersions.V1, attempt.PolicyDecision["schemaVersion"]!.GetValue<string>());
        Assert.Equal("allow", attempt.PolicyDecision["outcome"]!.GetValue<string>());
        Assert.Equal("erp", attempt.PolicyDecision["tool"]!["toolName"]!.GetValue<string>());
        Assert.Equal(PolicyDecisionEvaluationVersions.Current, attempt.PolicyDecision["audit"]!["policyVersion"]!.GetValue<string>());
        Assert.Equal("erp", attempt.ResultPayload["toolName"]!.GetValue<string>());
        Assert.Equal("expenseUsd", attempt.PolicyDecision["thresholdEvaluations"]![0]!["key"]!.GetValue<string>());
        Assert.NotNull(attempt.ExecutedUtc);
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, "founder");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, "founder@example.com");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, "Founder");
        return client;
    }

    private async Task<SeededExecutionAgent> SeedAgentAsync(
        AgentAutonomyLevel autonomyLevel,
        Dictionary<string, JsonNode?> tools,
        Dictionary<string, JsonNode?> scopes,
        Dictionary<string, JsonNode?> thresholds,
        Dictionary<string, JsonNode?> escalationRules)
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "founder@example.com", "Founder", "dev-header", "founder"));
            dbContext.Companies.Add(new Company(companyId, "Company A"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(),
                companyId,
                userId,
                CompanyMembershipRole.Owner,
                CompanyMembershipStatus.Active));
            dbContext.Agents.Add(new Agent(
                agentId,
                companyId,
                "finance",
                "Nora Ledger",
                "Finance Manager",
                "Finance",
                null,
                AgentSeniority.Senior,
                AgentStatus.Active,
                autonomyLevel: autonomyLevel,
                objectives: Payload(("primary", new JsonArray(JsonValue.Create("Protect cash flow")))),
                kpis: Payload(("targets", new JsonArray(JsonValue.Create("forecast_accuracy")))),
                tools: tools,
                scopes: scopes,
                thresholds: thresholds,
                escalationRules: escalationRules,
                roleBrief: "Execution-ready finance profile.",
                triggerLogic: Payload(("enabled", JsonValue.Create(false))),
                workingHours: Payload(
                    ("timezone", JsonValue.Create("UTC")),
                    ("windows", new JsonArray(
                        new JsonObject
                        {
                            ["day"] = "monday",
                            ["start"] = "08:00",
                            ["end"] = "16:00"
                        })))));
            return Task.CompletedTask;
        });

        return new SeededExecutionAgent(companyId, agentId);
    }

    private static Dictionary<string, JsonNode?> Payload(params (string Key, JsonNode? Value)[] properties)
    {
        var payload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in properties)
        {
            payload[key] = value?.DeepClone();
        }

        return payload;
    }

    private sealed record SeededExecutionAgent(Guid CompanyId, Guid AgentId);

    private sealed class AgentToolExecutionResponse
    {
        public Guid ExecutionId { get; set; }
        public string Status { get; set; } = string.Empty;
        public Guid? ApprovalRequestId { get; set; }
        public PolicyDecisionResponse PolicyDecision { get; set; } = new();
        public Dictionary<string, JsonElement>? ExecutionResult { get; set; }
        public Dictionary<string, JsonElement>? ApprovalDecisionChain { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    private sealed class PolicyDecisionResponse
    {
        public string Outcome { get; set; } = string.Empty;
        public List<string> ReasonCodes { get; set; } = [];
        public string Explanation { get; set; } = string.Empty;
        public string EvaluatedAutonomyLevel { get; set; } = string.Empty;
        public string EvaluatedActionType { get; set; } = string.Empty;
        public bool ApprovalRequired { get; set; }
        public string? EvaluatedScope { get; set; }
        public Dictionary<string, JsonElement> Metadata { get; set; } = [];
    }
}
