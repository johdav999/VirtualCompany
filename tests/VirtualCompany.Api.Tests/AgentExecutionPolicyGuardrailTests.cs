using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Companies;
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
        Assert.Equal("This action was blocked by policy.", payload.PolicyDecision.Explanation);
        Assert.NotNull(payload.Denial);
        Assert.Equal("policy_denied", payload.Denial!.Code);
        Assert.Equal(payload.Message, payload.Denial.UserFacingMessage);
        Assert.DoesNotContain("outside the agent's allowed tool scope", payload.Denial.UserFacingMessage, StringComparison.OrdinalIgnoreCase);

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var attempt = await dbContext.ToolExecutionAttempts.AsNoTracking().SingleAsync(x => x.Id == payload.ExecutionId);
        Assert.Equal(ToolExecutionStatus.Denied, attempt.Status);
        Assert.Equal("wire_transfer", attempt.ToolName);
        Assert.Equal("unknown", attempt.ToolVersion);
        Assert.Equal(payload.Message, attempt.DenialReason);
        Assert.Equal("deny", attempt.PolicyDecision["outcome"]!.GetValue<string>());
        Assert.Equal(PolicyDecisionSchemaVersions.V1, attempt.PolicyDecision["schemaVersion"]!.GetValue<string>());
        Assert.Equal(seed.CompanyId, attempt.PolicyDecision["tenant"]!["companyId"]!.GetValue<Guid>());
        Assert.Equal("tool_not_permitted", attempt.PolicyDecision["reasons"]![0]!["code"]!.GetValue<string>());
        Assert.Null(attempt.ApprovalRequestId);
        Assert.Equal("The requested tool is outside the agent's allowed tool scope.", attempt.PolicyDecision["metadata"]!["internalRationaleSummary"]!.GetValue<string>());
        Assert.Equal("This action was blocked by policy.", attempt.PolicyDecision["metadata"]!["safeUserFacingExplanation"]!.GetValue<string>());
        Assert.Equal("tool_not_permitted", attempt.ResultPayload["metadata"]!["primaryReasonCode"]!.GetValue<string>());
        Assert.Equal(payload.Message, attempt.ResultPayload["metadata"]!["userFacingMessage"]!.GetValue<string>());
        Assert.DoesNotContain("outside the agent's allowed tool scope", attempt.ResultPayload["errorMessage"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);

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
    public async Task Approval_create_and_chain_advancement_enqueue_notification_outbox_without_inline_fanout()
    {
        var seed = await SeedAgentAsync(
            autonomyLevel: AgentAutonomyLevel.Level3,
            tools: Payload(("allowed", new JsonArray(JsonValue.Create("erp")))),
            scopes: Payload(("execute", new JsonArray(JsonValue.Create("payments")))),
            thresholds: Payload(("approval", new JsonObject { ["expenseUsd"] = 5000 })),
            escalationRules: Payload(("escalateTo", JsonValue.Create("founder"))));
        var taskId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.WorkTasks.Add(new WorkTask(
                taskId,
                seed.CompanyId,
                "approval",
                "Review payment threshold",
                null,
                WorkTaskPriority.High,
                seed.AgentId,
                null,
                AuditActorTypes.User,
                seed.UserId));
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient();
        var createResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/approvals", new
        {
            targetEntityType = ApprovalTargetEntityType.Task.ToStorageValue(),
            targetEntityId = taskId,
            requestedByActorType = AuditActorTypes.User,
            requestedByActorId = seed.UserId,
            approvalType = "threshold",
            thresholdContext = new
            {
                thresholdKey = "expenseUsd",
                thresholdValue = 7500
            },
            steps = new[]
            {
                new
                {
                    sequenceNo = 1,
                    approverType = ApprovalStepApproverType.Role.ToStorageValue(),
                    approverRef = CompanyMembershipRole.Owner.ToStorageValue()
                },
                new
                {
                    sequenceNo = 2,
                    approverType = ApprovalStepApproverType.Role.ToStorageValue(),
                    approverRef = CompanyMembershipRole.Admin.ToStorageValue()
                }
            }
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var approval = await createResponse.Content.ReadFromJsonAsync<ApprovalRequestDto>();
        Assert.NotNull(approval);
        Assert.NotNull(approval!.CurrentStep);
        Assert.Equal(2, approval.Steps.Count);

        using (var assertionScope = _factory.Services.CreateScope())
        {
            var companyContextAccessor = assertionScope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
            companyContextAccessor.SetCompanyId(seed.CompanyId);
            var dbContext = assertionScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

            var initialOutbox = await dbContext.CompanyOutboxMessages
                .AsNoTracking()
                .SingleAsync(x =>
                    x.CompanyId == seed.CompanyId &&
                    x.Topic == CompanyOutboxTopics.NotificationDeliveryRequested &&
                    x.IdempotencyKey == $"notification:approval-requested:{approval.Id:N}:step:{approval.CurrentStep!.Id:N}");

            Assert.Equal(CompanyOutboxMessageStatus.Pending, initialOutbox.Status);
            Assert.Null(initialOutbox.ProcessedUtc);
            Assert.Equal(approval.Id.ToString("N"), initialOutbox.CausationId);
            Assert.Empty(await dbContext.CompanyNotifications
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == seed.CompanyId && x.RelatedEntityId == approval.Id)
                .ToListAsync());
        }

        var decisionResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/approvals/{approval.Id}/decisions", new
        {
            decision = "approve",
            stepId = approval.CurrentStep.Id,
            comment = "Advance to final reviewer."
        });

        Assert.Equal(HttpStatusCode.OK, decisionResponse.StatusCode);
        var decision = await decisionResponse.Content.ReadFromJsonAsync<ApprovalDecisionResultDto>();
        Assert.NotNull(decision);
        Assert.False(decision!.IsFinalized);
        Assert.NotNull(decision.NextStep);

        using var finalScope = _factory.Services.CreateScope();
        var finalCompanyContextAccessor = finalScope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        finalCompanyContextAccessor.SetCompanyId(seed.CompanyId);
        var finalDbContext = finalScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var approvalNotificationOutbox = await finalDbContext.CompanyOutboxMessages
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == seed.CompanyId &&
                x.Topic == CompanyOutboxTopics.NotificationDeliveryRequested &&
                x.IdempotencyKey != null &&
                x.IdempotencyKey.Contains($"notification:approval-requested:{approval.Id:N}"))
            .ToListAsync();

        Assert.Equal(2, approvalNotificationOutbox.Count);
        Assert.Contains(approvalNotificationOutbox, x => x.IdempotencyKey == $"notification:approval-requested:{approval.Id:N}:step:{approval.Steps[0].Id:N}");
        Assert.Contains(approvalNotificationOutbox, x => x.IdempotencyKey == $"notification:approval-requested:{approval.Id:N}:step:{approval.Steps[1].Id:N}");
        Assert.All(approvalNotificationOutbox, x => Assert.Equal(CompanyOutboxMessageStatus.Pending, x.Status));
        Assert.Empty(await finalDbContext.CompanyNotifications
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == seed.CompanyId && x.RelatedEntityId == approval.Id)
            .ToListAsync());
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
    public async Task Approved_policy_required_execution_runs_tool_executor_with_original_execution_context_and_marks_attempt_executed()
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
        var taskId = Guid.NewGuid();
        var workflowInstanceId = Guid.NewGuid();

        using var client = CreateAuthenticatedClient();
        var executionResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
        {
            toolName = "erp",
            actionType = "execute",
            scope = "payments",
            taskId,
            workflowInstanceId,
            correlationId = "corr-approved-policy-execution",
            requestPayload = new { paymentId = "pay-approved-500" }
        });

        Assert.Equal(HttpStatusCode.OK, executionResponse.StatusCode);

        var executionPayload = await executionResponse.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();
        Assert.NotNull(executionPayload);
        Assert.Equal("awaiting_approval", executionPayload!.Status);
        Assert.NotNull(executionPayload.ApprovalRequestId);
        Assert.Equal(0, _factory.ToolExecutor.ExecutionCount);

        var approvalResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/approvals/{executionPayload.ApprovalRequestId}/decisions", new
        {
            decision = "approve",
            comment = "Approved for execution."
        });

        Assert.Equal(HttpStatusCode.OK, approvalResponse.StatusCode);
        Assert.Equal(1, _factory.ToolExecutor.ExecutionCount);

        var executedRequest = Assert.Single(_factory.ToolExecutor.Requests);
        Assert.Equal(seed.CompanyId, executedRequest.CompanyId);
        Assert.Equal(seed.AgentId, executedRequest.AgentId);
        Assert.Equal("erp", executedRequest.ToolName);
        Assert.Equal(executionPayload.ExecutionId, executedRequest.ExecutionId);
        Assert.Equal("execute", executedRequest.ActionType);
        Assert.Equal("payments", executedRequest.Scope);
        Assert.Equal(taskId, executedRequest.TaskId);
        Assert.Equal(workflowInstanceId, executedRequest.WorkflowInstanceId);
        Assert.Equal("corr-approved-policy-execution", executedRequest.CorrelationId);

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var attempt = await dbContext.ToolExecutionAttempts.AsNoTracking().SingleAsync(x => x.Id == executionPayload.ExecutionId);
        Assert.Equal(ToolExecutionStatus.Executed, attempt.Status);
        Assert.Equal("allow", attempt.PolicyDecision["outcome"]!.GetValue<string>());
        Assert.False(attempt.PolicyDecision["approvalRequired"]!.GetValue<bool>());
        Assert.Equal("approved", attempt.PolicyDecision["approvalStatus"]!.GetValue<string>());
        Assert.Equal("executed", attempt.PolicyDecision["metadata"]!["executionState"]!.GetValue<string>());
        Assert.Equal("erp", attempt.ResultPayload["toolName"]!.GetValue<string>());
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
        var taskId = Guid.NewGuid();
        var workflowInstanceId = Guid.NewGuid();

        using var client = CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
        {
            toolName = "erp",
            actionType = "execute",
            scope = "payments",
            thresholdCategory = "approval",
            thresholdKey = "expenseUsd",
            thresholdValue = 750,
            taskId,
            workflowInstanceId,
            correlationId = "corr-allowed-execution-persistence",
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
        Assert.Equal("2026-04-13", payload.ExecutionResult["contractSchemaVersion"].GetString());
        Assert.Equal("Test tool execution completed.", payload.ExecutionResult["userSafeSummary"].GetString());

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var attempt = await dbContext.ToolExecutionAttempts.AsNoTracking().SingleAsync(x => x.Id == payload.ExecutionId);
        Assert.Equal(seed.CompanyId, attempt.CompanyId);
        Assert.Equal(seed.AgentId, attempt.AgentId);
        Assert.Equal(taskId, attempt.TaskId);
        Assert.Equal(workflowInstanceId, attempt.WorkflowInstanceId);
        Assert.Equal("corr-allowed-execution-persistence", attempt.CorrelationId);
        Assert.Equal("erp", attempt.ToolName);
        Assert.Equal(ToolActionType.Execute, attempt.ActionType);
        Assert.Equal(ToolExecutionStatus.Executed, attempt.Status);
        Assert.True(attempt.StartedUtc <= attempt.CompletedUtc);
        Assert.NotNull(attempt.CompletedUtc);
        Assert.Equal("execute", attempt.PolicyDecision["evaluatedActionType"]!.GetValue<string>());
        Assert.Equal("default_deny", attempt.PolicyDecision["metadata"]!["policyMode"]!.GetValue<string>());
        Assert.Equal(PolicyDecisionSchemaVersions.V1, attempt.PolicyDecision["schemaVersion"]!.GetValue<string>());
        Assert.Equal("allow", attempt.PolicyDecision["outcome"]!.GetValue<string>());
        Assert.Equal("allow", attempt.PolicyDecision["metadata"]!["decisionOutcome"]!.GetValue<string>());
        Assert.Equal("erp", attempt.PolicyDecision["tool"]!["toolName"]!.GetValue<string>());
        Assert.Equal(PolicyDecisionEvaluationVersions.Current, attempt.PolicyDecision["audit"]!["policyVersion"]!.GetValue<string>());
        Assert.Equal("exp-300", attempt.RequestPayload["expenseId"]!.GetValue<string>());
        Assert.Equal("erp", attempt.ResultPayload["toolName"]!.GetValue<string>());
        Assert.True(attempt.ResultPayload["success"]!.GetValue<bool>());
        Assert.Equal("expenseUsd", attempt.PolicyDecision["thresholdEvaluations"]![0]!["key"]!.GetValue<string>());
        Assert.NotNull(attempt.ExecutedUtc);
    }

    [Fact]
    public async Task Action_scope_denial_is_blocked_before_tool_executor_runs_and_persists_policy_decision()
    {
        var seed = await SeedAgentAsync(
            autonomyLevel: AgentAutonomyLevel.Level2,
            tools: Payload(
                ("allowed", new JsonArray(JsonValue.Create("erp"))),
                ("actions", new JsonArray(JsonValue.Create("read"), JsonValue.Create("recommend")))),
            scopes: Payload(
                ("read", new JsonArray(JsonValue.Create("finance"))),
                ("recommend", new JsonArray(JsonValue.Create("finance"))),
                ("execute", new JsonArray(JsonValue.Create("payments")))),
            thresholds: Payload(("approval", new JsonObject { ["expenseUsd"] = 5000 })),
            escalationRules: Payload(("critical", new JsonArray(JsonValue.Create("over_limit"))), ("escalateTo", JsonValue.Create("founder"))));

        using var client = CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
        {
            toolName = "erp",
            actionType = "execute",
            scope = "payments",
            requestPayload = new { expenseId = "exp-action-scope" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();
        Assert.NotNull(payload);
        Assert.Equal("denied", payload!.Status);
        Assert.Equal("deny", payload.PolicyDecision.Outcome);
        Assert.Contains(PolicyDecisionReasonCodes.ToolActionNotPermitted, payload.PolicyDecision.ReasonCodes);
        Assert.Equal(0, _factory.ToolExecutor.ExecutionCount);

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var attempt = await dbContext.ToolExecutionAttempts.AsNoTracking().SingleAsync(x => x.Id == payload.ExecutionId);
        Assert.Equal(ToolExecutionStatus.Denied, attempt.Status);
        Assert.Equal("tool_action_not_permitted", attempt.PolicyDecision["reasons"]![0]!["code"]!.GetValue<string>());
        Assert.Equal("configured", attempt.PolicyDecision["metadata"]!["actionPolicyState"]!.GetValue<string>());
        Assert.False(attempt.PolicyDecision["metadata"]!["actionAllowed"]!.GetValue<bool>());
        Assert.False(attempt.ResultPayload["success"]!.GetValue<bool>());
        Assert.Equal("policy_denied", attempt.ResultPayload["errorCode"]!.GetValue<string>());
    }

    [Fact]
    public async Task Registered_policy_allowance_still_cannot_execute_unregistered_external_tool()
    {
        var seed = await SeedAgentAsync(
            autonomyLevel: AgentAutonomyLevel.Level2,
            tools: Payload(("allowed", new JsonArray(JsonValue.Create("unregistered_external_system")))),
            scopes: Payload(("execute", new JsonArray(JsonValue.Create("payments")))),
            thresholds: Payload(("approval", new JsonObject { ["expenseUsd"] = 5000 })),
            escalationRules: Payload(("escalateTo", JsonValue.Create("founder"))));

        using var client = CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
        {
            toolName = "unregistered_external_system",
            actionType = "execute",
            scope = "payments",
            requestPayload = new { paymentId = "pay-registry-denied" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();
        Assert.NotNull(payload);
        Assert.Equal("denied", payload!.Status);
        Assert.Equal("allow", payload.PolicyDecision.Outcome);
        Assert.Equal("The requested tool is not registered for trusted execution.", payload.Message);
        Assert.Equal(0, _factory.ToolExecutor.ExecutionCount);
        Assert.NotNull(payload.ExecutionResult);
        Assert.Equal("unregistered_tool", payload.ExecutionResult!["errorCode"].GetString());
        Assert.Equal("policy_enforced_tool_executor", payload.ExecutionResult["metadata"].GetProperty("executionBoundary").GetString());
        Assert.False(payload.ExecutionResult["metadata"].GetProperty("modelOutputTrusted").GetBoolean());

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var attempt = await dbContext.ToolExecutionAttempts.AsNoTracking().SingleAsync(x => x.Id == payload.ExecutionId);
        Assert.Equal(ToolExecutionStatus.Denied, attempt.Status);
        Assert.Null(attempt.ExecutedUtc);
        Assert.Equal("unregistered_external_system", attempt.ToolName);
        Assert.Equal("allow", attempt.PolicyDecision["outcome"]!.GetValue<string>());
        Assert.Equal("unregistered_tool", attempt.ResultPayload["errorCode"]!.GetValue<string>());
        Assert.Equal("deny", attempt.ResultPayload["metadata"]!["registryDecision"]!.GetValue<string>());
    }

    [Fact]
    public async Task Model_supplied_direct_external_execution_payload_is_rejected_before_policy_or_tool_execution()
    {
        var seed = await SeedAgentAsync(
            autonomyLevel: AgentAutonomyLevel.Level2,
            tools: Payload(("allowed", new JsonArray(JsonValue.Create("erp")))),
            scopes: Payload(("execute", new JsonArray(JsonValue.Create("payments")))),
            thresholds: Payload(("approval", new JsonObject { ["expenseUsd"] = 5000 })),
            escalationRules: Payload(("escalateTo", JsonValue.Create("founder"))));

        using var client = CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
        {
            toolName = "erp",
            actionType = "execute",
            scope = "payments",
            requestPayload = new
            {
                paymentId = "pay-direct-external",
                endpointUrl = "https://payments.example.invalid/transfer",
                headers = new { Authorization = "Bearer model-supplied" }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _factory.ToolExecutor.ExecutionCount);

        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains("RequestPayload.endpointUrl", problem!.Errors.Keys);
        Assert.Contains("RequestPayload.headers", problem.Errors.Keys);
    }

    [Fact]
    public async Task Finance_read_tool_executes_through_policy_executor_contract_and_persists_versioned_record()
    {
        _factory.ToolExecutor.Reset();
        var seed = await SeedAgentAsync(
            autonomyLevel: AgentAutonomyLevel.Level2,
            tools: Payload(
                ("allowed", new JsonArray(JsonValue.Create("get_cash_balance"))),
                ("actions", new JsonArray(JsonValue.Create("read")))),
            scopes: Payload(("read", new JsonArray(JsonValue.Create("finance")))),
            thresholds: Payload(("approval", new JsonObject { ["cashReadUsd"] = 100000 })),
            escalationRules: Payload(("escalateTo", JsonValue.Create("founder"))));

        using var client = CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
        {
            toolName = "get_cash_balance",
            actionType = "read",
            scope = "finance",
            requestPayload = new { asOfUtc = "2026-04-16T00:00:00Z" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();
        Assert.NotNull(payload);
        Assert.Equal("executed", payload!.Status);
        Assert.Equal("allow", payload.PolicyDecision.Outcome);
        Assert.Equal(1, _factory.ToolExecutor.ExecutionCount);

        var dispatchedRequest = Assert.Single(_factory.ToolExecutor.Requests);
        Assert.Equal("get_cash_balance", dispatchedRequest.ToolName);
        Assert.Equal("finance", dispatchedRequest.Scope);
        Assert.Equal(ToolActionType.Read, dispatchedRequest.Context.ActionType);
        Assert.Equal("1.0.0", dispatchedRequest.ToolVersion);

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var attempt = await dbContext.ToolExecutionAttempts.AsNoTracking().SingleAsync(x => x.Id == payload.ExecutionId);

        Assert.Equal(ToolExecutionStatus.Executed, attempt.Status);
        Assert.Equal("get_cash_balance", attempt.ToolName);
        Assert.Equal("1.0.0", attempt.ToolVersion);
        Assert.Equal("2026-04-16T00:00:00Z", attempt.RequestPayload["asOfUtc"]!.GetValue<string>());
        Assert.Equal("allow", attempt.PolicyDecision["outcome"]!.GetValue<string>());
        Assert.NotNull(attempt.ResultPayload["data"]!["cashBalance"]);
        Assert.NotNull(attempt.CompletedUtc);
        Assert.NotNull(attempt.ExecutedUtc);
        Assert.Null(attempt.DenialReason);
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

        return new SeededExecutionAgent(companyId, agentId, userId);
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

    private sealed record SeededExecutionAgent(Guid CompanyId, Guid AgentId, Guid UserId);

    private sealed class AgentToolExecutionResponse
    {
        public Guid ExecutionId { get; set; }
        public string Status { get; set; } = string.Empty;
        public Guid? ApprovalRequestId { get; set; }
        public PolicyDecisionResponse PolicyDecision { get; set; } = new();
        public Dictionary<string, JsonElement>? ExecutionResult { get; set; }
        public Dictionary<string, JsonElement>? ApprovalDecisionChain { get; set; }
        public string Message { get; set; } = string.Empty;
        public ToolExecutionDenialResponse? Denial { get; set; }
    }

    private sealed class ToolExecutionDenialResponse
    {
        public string Code { get; set; } = string.Empty;
        public string UserFacingMessage { get; set; } = string.Empty;
        public List<string> ReasonCodes { get; set; } = [];
        public Dictionary<string, JsonElement> Metadata { get; set; } = [];
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
