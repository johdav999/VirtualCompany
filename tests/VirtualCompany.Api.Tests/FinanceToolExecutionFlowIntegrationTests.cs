using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Shared;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceToolExecutionFlowIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Capability_api_and_execution_share_authority_and_stale_hash_blocks_dispatch()
    {
        using var financeFactory = CreateFinanceContractFactory();
        var seed = await SeedAgentAsync(
            financeFactory.Services,
            tools: Payload(
                ("allowed", new JsonArray("get_cash_balance")),
                ("actions", new JsonArray("read"))),
            scopes: Payload(("read", new JsonArray("finance"))));
        using var client = CreateAuthenticatedClient(financeFactory, seed);

        var catalog = await client.GetFromJsonAsync<AgentCapabilityCatalogDto>(
            $"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/capabilities");
        Assert.NotNull(catalog);
        var displayed = catalog!.EffectiveTools.Single(item => item.ToolName == "get_cash_balance");
        Assert.True(displayed.IsUsable);
        var profile = await client.GetFromJsonAsync<AgentProfileViewDto>(
            $"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}");
        Assert.NotNull(profile);
        Assert.Equal(catalog.AuthorityVersion, profile!.EffectiveAuthority.AuthorityVersion);
        Assert.Equal(catalog.AuthorityHash, profile.EffectiveAuthority.AuthorityHash);

        var allowedResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions",
            new
            {
                toolName = "get_cash_balance",
                actionType = "read",
                scope = "finance",
                expectedAuthorityVersion = catalog.AuthorityVersion,
                expectedAuthorityHash = catalog.AuthorityHash,
                requestPayload = new { asOfUtc = "2026-04-16T00:00:00Z" }
            });
        var allowed = await allowedResponse.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();
        Assert.Equal("executed", allowed!.Status);
        Assert.Equal(catalog.AuthorityVersion, allowed.EffectiveAuthorityVersion);
        Assert.Equal(catalog.AuthorityHash, allowed.EffectiveAuthorityHash);

        using (var mutationScope = financeFactory.Services.CreateScope())
        {
            var db = mutationScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            var agent = await db.Agents.IgnoreQueryFilters().SingleAsync(x => x.Id == seed.AgentId);
            var changedTools = agent.Tools.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
            changedTools["integrationAvailability"] = new JsonObject { ["get_cash_balance"] = false };
            agent.UpdateOperatingProfile(agent.RoleBrief, agent.Status, agent.AutonomyLevel, agent.Objectives, agent.Kpis,
                changedTools, agent.Scopes, agent.Thresholds, agent.EscalationRules, agent.TriggerLogic,
                agent.WorkingHours, agent.CommunicationProfile);
            await db.SaveChangesAsync();
        }

        var staleResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions",
            new
            {
                toolName = "get_cash_balance",
                actionType = "read",
                scope = "finance",
                expectedAuthorityVersion = catalog.AuthorityVersion,
                expectedAuthorityHash = catalog.AuthorityHash,
                requestPayload = new { asOfUtc = "2026-04-16T00:00:00Z" }
            });
        var stale = await staleResponse.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();
        Assert.Equal("denied", stale!.Status);
        Assert.Contains(AgentAuthorityReasonCodes.Stale, stale.PolicyDecision.ReasonCodes);
        Assert.NotEqual(catalog.AuthorityHash, stale.EffectiveAuthorityHash);
        Assert.Equal(1, financeFactory.Services.GetRequiredService<TrackingFinanceToolProvider>().TotalCallCount);
    }

    [Theory]
    [MemberData(nameof(SuccessfulFinanceToolRequests))]
    public async Task Finance_read_tools_execute_through_policy_executor_provider_and_persist_execution_record(
        string toolName,
        object requestPayload,
        string expectedDataProperty,
        string expectedProviderCall)
    {
        using var financeFactory = CreateFinanceContractFactory();
        var seed = await SeedAgentAsync(
            financeFactory.Services,
            tools: Payload(
                ("allowed", new JsonArray(JsonValue.Create(toolName))),
                ("actions", new JsonArray(JsonValue.Create("read")))),
            scopes: Payload(("read", new JsonArray(JsonValue.Create("finance")))));

        using var client = CreateAuthenticatedClient(financeFactory, seed);
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
        {
            toolName,
            actionType = "read",
            scope = "finance",
            requestPayload
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();
        Assert.NotNull(payload);
        Assert.Equal("executed", payload!.Status);
        Assert.Equal("allow", payload.PolicyDecision.Outcome);
        Assert.NotNull(payload.ExecutionResult);
        Assert.Equal("executed", payload.ExecutionResult!["status"]!.GetValue<string>());
        Assert.Equal(toolName, payload.ExecutionResult["toolName"]!.GetValue<string>());
        Assert.Equal("read", payload.ExecutionResult["actionType"]!.GetValue<string>());
        Assert.True(payload.ExecutionResult["success"]!.GetValue<bool>());
        Assert.True(payload.ExecutionResult["data"]!.AsObject().ContainsKey(expectedDataProperty));

        var metadata = payload.ExecutionResult["metadata"];
        Assert.Equal("finance_tool_provider", metadata!["contractName"]!.GetValue<string>());
        Assert.Equal("1.0.0", metadata["toolVersion"]!.GetValue<string>());
        Assert.Equal(InternalToolExecutionResponse.SchemaVersion, metadata["contractSchemaVersion"]!.GetValue<string>());

        var trackingFinance = financeFactory.Services.GetRequiredService<TrackingFinanceToolProvider>();
        Assert.Equal(1, trackingFinance.TotalCallCount);
        Assert.Equal(expectedProviderCall, Assert.Single(trackingFinance.CallNames));

        using var scope = financeFactory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var attempt = await dbContext.ToolExecutionAttempts.AsNoTracking().SingleAsync(x => x.Id == payload.ExecutionId);
        Assert.Equal(seed.CompanyId, attempt.CompanyId);
        Assert.Equal(seed.AgentId, attempt.AgentId);
        Assert.Equal(toolName, attempt.ToolName);
        Assert.Equal("1.0.0", attempt.ToolVersion);
        Assert.Equal(ToolActionType.Read, attempt.ActionType);
        Assert.Equal("finance", attempt.Scope);
        Assert.Equal(ToolExecutionStatus.Executed, attempt.Status);
        Assert.Equal("allow", attempt.PolicyDecision["outcome"]!.GetValue<string>());
        Assert.Equal("executed", attempt.ResultPayload["status"]!.GetValue<string>());
        Assert.Equal(toolName, attempt.ResultPayload["toolName"]!.GetValue<string>());
        Assert.Equal("read", attempt.ResultPayload["actionType"]!.GetValue<string>());
        Assert.True(attempt.ResultPayload["success"]!.GetValue<bool>());
        Assert.NotNull(attempt.ResultPayload["data"]![expectedDataProperty]);
        Assert.Equal("finance_tool_provider", attempt.ResultPayload["metadata"]!["contractName"]!.GetValue<string>());
        Assert.Equal("1.0.0", attempt.ResultPayload["metadata"]!["toolVersion"]!.GetValue<string>());
        Assert.Equal(InternalToolExecutionResponse.SchemaVersion, attempt.ResultPayload["metadata"]!["contractSchemaVersion"]!.GetValue<string>());
        Assert.NotEqual(default, attempt.StartedUtc);
        Assert.NotEqual(default, attempt.CreatedUtc);
        Assert.NotEqual(default, attempt.UpdatedUtc);
        Assert.NotNull(attempt.CompletedUtc);
        Assert.NotNull(attempt.ExecutedUtc);
        Assert.True(attempt.CompletedUtc >= attempt.StartedUtc);
        Assert.Null(attempt.DenialReason);
    }

    [Fact]
    public async Task Finance_tool_policy_denial_is_persisted_and_blocks_provider_dispatch()
    {
        using var financeFactory = CreateFinanceContractFactory();
        var seed = await SeedAgentAsync(
            financeFactory.Services,
            tools: Payload(
                ("allowed", new JsonArray(JsonValue.Create("list_transactions"))),
                ("actions", new JsonArray(JsonValue.Create("read")))),
            scopes: Payload(("read", new JsonArray(JsonValue.Create("finance")))));

        using var client = CreateAuthenticatedClient(financeFactory, seed);
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
        Assert.Equal("denied", payload!.Status);
        Assert.Equal("deny", payload.PolicyDecision.Outcome);
        Assert.Contains(PolicyDecisionReasonCodes.ToolNotPermitted, payload.PolicyDecision.ReasonCodes);
        Assert.NotNull(payload.Denial);
        Assert.Equal("policy_denied", payload.Denial!.Code);
        Assert.Equal(payload.Message, payload.Denial.UserFacingMessage);

        var trackingFinance = financeFactory.Services.GetRequiredService<TrackingFinanceToolProvider>();
        Assert.Equal(0, trackingFinance.TotalCallCount);

        using var scope = financeFactory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var attempt = await dbContext.ToolExecutionAttempts.AsNoTracking().SingleAsync(x => x.Id == payload.ExecutionId);
        Assert.Equal("get_cash_balance", attempt.ToolName);
        Assert.Equal("1.0.0", attempt.ToolVersion);
        Assert.Equal(ToolActionType.Read, attempt.ActionType);
        Assert.Equal("finance", attempt.Scope);
        Assert.Equal(ToolExecutionStatus.Denied, attempt.Status);
        Assert.Equal("deny", attempt.PolicyDecision["outcome"]!.GetValue<string>());
        Assert.Equal(PolicyDecisionReasonCodes.ToolNotPermitted, attempt.PolicyDecision["reasons"]![0]!["code"]!.GetValue<string>());
        Assert.Equal(payload.Message, attempt.DenialReason);
        Assert.Equal("denied", attempt.ResultPayload["status"]!.GetValue<string>());
        Assert.Equal("policy_denied", attempt.ResultPayload["errorCode"]!.GetValue<string>());
        Assert.Equal(payload.Message, attempt.ResultPayload["errorMessage"]!.GetValue<string>());
        Assert.NotEqual(default, attempt.StartedUtc);
        Assert.NotEqual(default, attempt.CreatedUtc);
        Assert.NotEqual(default, attempt.UpdatedUtc);
        Assert.NotNull(attempt.CompletedUtc);
        Assert.Null(attempt.ExecutedUtc);
    }

    [Theory]
    [InlineData(CompanyMembershipRole.Owner, "get_cash_balance", "read", true)]
    [InlineData(CompanyMembershipRole.Admin, "get_cash_balance", "read", true)]
    [InlineData(CompanyMembershipRole.Manager, "categorize_transaction", "execute", true)]
    [InlineData(CompanyMembershipRole.FinanceApprover, "recommend_transaction_category", "recommend", true)]
    [InlineData(CompanyMembershipRole.FinanceApprover, "categorize_transaction", "execute", false)]
    [InlineData(CompanyMembershipRole.Employee, "get_cash_balance", "read", false)]
    public async Task Finance_actor_permission_is_enforced_before_guardrail_and_provider_dispatch(
        CompanyMembershipRole role,
        string toolName,
        string actionType,
        bool expectedAllowed)
    {
        using var financeFactory = CreateFinanceContractFactory();
        var seed = await SeedAgentAsync(
            financeFactory.Services,
            tools: Payload(
                ("allowed", new JsonArray(JsonValue.Create(toolName))),
                ("actions", new JsonArray(JsonValue.Create(actionType)))),
            scopes: Payload((actionType, new JsonArray(JsonValue.Create("finance")))),
            thresholds: toolName == "categorize_transaction" && expectedAllowed
                ? CategorizationExceptionThresholds("actor-policy-v1", 250m, 1)
                : null,
            membershipRole: role);
        var financeSeed = await SeedFinanceRecordAsync(financeFactory.Services, seed.CompanyId);
        using var client = CreateAuthenticatedClient(financeFactory, seed);
        object requestPayload = toolName switch
        {
            "get_cash_balance" => new { asOfUtc = "2026-04-16T00:00:00Z" },
            "recommend_transaction_category" => new { transactionId = financeSeed.TransactionId },
            _ => new { transactionId = financeSeed.TransactionId, category = "software" }
        };

        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
        {
            toolName,
            actionType,
            scope = "finance",
            requestPayload
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();
        Assert.NotNull(payload?.ActorAuthorization);
        Assert.Equal(expectedAllowed, payload!.ActorAuthorization!.IsAllowed);
        Assert.Equal(expectedAllowed ? "executed" : "denied", payload.Status);
        if (!expectedAllowed)
        {
            Assert.Equal("This Finance action is not available for the current actor.", payload.Message);
            Assert.Equal("finance_actor_unauthorized", payload.ExecutionResult!["errorCode"]!.GetValue<string>());
        }

        Assert.Equal(expectedAllowed ? 1 : 0,
            financeFactory.Services.GetRequiredService<TrackingFinanceToolProvider>().TotalCallCount);

        using var auditScope = financeFactory.Services.CreateScope();
        var companyContext = auditScope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContext.SetCompanyId(seed.CompanyId);
        var db = auditScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var authorizationAudit = await db.AuditEvents.AsNoTracking().SingleAsync(x =>
            x.CompanyId == seed.CompanyId &&
            x.Action == AuditEventActions.FinanceAgentToolAuthorizationEvaluated &&
            x.TargetId == payload.ExecutionId.ToString("N"));
        Assert.Equal(expectedAllowed ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Denied,
            authorizationAudit.Outcome);
        Assert.Equal(payload.ActorAuthorization.ReasonCode,
            authorizationAudit.Metadata["authorizationReasonCode"]);
    }

    [Fact]
    public async Task Finance_execute_action_with_disallowed_action_type_is_denied_before_provider_dispatch()
    {
        using var financeFactory = CreateFinanceContractFactory();
        var seed = await SeedAgentAsync(
            financeFactory.Services,
            tools: Payload(
                ("allowed", new JsonArray(JsonValue.Create("categorize_transaction"))),
                ("actions", new JsonArray(JsonValue.Create("read")))),
            scopes: Payload(
                ("read", new JsonArray(JsonValue.Create("finance"))),
                ("execute", new JsonArray(JsonValue.Create("finance")))));

        var financeSeed = await SeedFinanceRecordAsync(financeFactory.Services, seed.CompanyId);
        using var client = CreateAuthenticatedClient(financeFactory, seed);
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
        {
            toolName = "categorize_transaction",
            actionType = "execute",
            scope = "finance",
            requestPayload = new { transactionId = financeSeed.TransactionId, category = "software" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();
        Assert.NotNull(payload);
        Assert.Equal("denied", payload!.Status);
        Assert.Equal("deny", payload.PolicyDecision.Outcome);
        Assert.Contains(PolicyDecisionReasonCodes.ToolActionNotPermitted, payload.PolicyDecision.ReasonCodes);

        using var scope = financeFactory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var transaction = await dbContext.FinanceTransactions.AsNoTracking().SingleAsync(x => x.Id == financeSeed.TransactionId);
        var attempt = await dbContext.ToolExecutionAttempts.AsNoTracking().SingleAsync(x => x.Id == payload.ExecutionId);

        Assert.Equal("uncategorized", transaction.TransactionType);
        Assert.Equal(ToolExecutionStatus.Denied, attempt.Status);
        Assert.Equal(0, financeFactory.Services.GetRequiredService<TrackingFinanceToolProvider>().TotalCallCount);
    }

    [Fact]
    public async Task Reversible_categorization_exception_executes_at_boundary_and_audits_exact_policy_version()
    {
        using var financeFactory = CreateFinanceContractFactory();
        var seed = await SeedAgentAsync(
            financeFactory.Services,
            tools: Payload(
                ("allowed", new JsonArray("categorize_transaction")),
                ("actions", new JsonArray("execute"))),
            scopes: Payload(("execute", new JsonArray("finance"))),
            thresholds: CategorizationExceptionThresholds("category-policy-v3", 250m, 1),
            membershipRole: CompanyMembershipRole.Manager);
        var financeSeed = await SeedFinanceRecordAsync(financeFactory.Services, seed.CompanyId);
        using var client = CreateAuthenticatedClient(financeFactory, seed);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions",
            new
            {
                toolName = "categorize_transaction",
                actionType = "execute",
                scope = "finance",
                sensitiveAction = false,
                requestPayload = new { transactionId = financeSeed.TransactionId, category = "software" }
            });
        var payload = await response.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("executed", payload!.Status);
        Assert.Equal("allow", payload.PolicyDecision.Outcome);
        Assert.Equal(FinanceToolRiskPolicyVersions.V1,
            payload.PolicyDecision.Metadata["riskPolicyVersion"]!.GetValue<string>());
        Assert.Equal("category-policy-v3",
            payload.PolicyDecision.Metadata["financeApprovalPolicyVersion"]!.GetValue<string>());
        Assert.Equal(2, payload.PolicyDecision.ThresholdEvaluations!.Count);

        using var scope = financeFactory.Services.CreateScope();
        var companyContext = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContext.SetCompanyId(seed.CompanyId);
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var transaction = await db.FinanceTransactions.AsNoTracking().SingleAsync(x => x.Id == financeSeed.TransactionId);
        var attempt = await db.ToolExecutionAttempts.AsNoTracking().SingleAsync(x => x.Id == payload.ExecutionId);
        var boundaryAudit = await db.AuditEvents.AsNoTracking().SingleAsync(x =>
            x.CompanyId == seed.CompanyId && x.Action == AuditEventActions.BoundaryEnforcement &&
            x.TargetId == payload.ExecutionId.ToString("N"));
        Assert.Equal("software", transaction.TransactionType);
        Assert.Equal(FinanceToolRiskPolicyVersions.V1,
            attempt.PolicyDecision["metadata"]!["riskPolicyVersion"]!.GetValue<string>());
        Assert.Equal("category-policy-v3", boundaryAudit.Metadata["financeApprovalPolicyVersion"]);
        Assert.Equal(1, financeFactory.Services.GetRequiredService<TrackingFinanceToolProvider>().TotalCallCount);
    }

    [Fact]
    public async Task Finance_execute_action_above_threshold_creates_approval_request_without_state_change()
    {
        using var financeFactory = CreateFinanceContractFactory();
        var seed = await SeedAgentAsync(
            financeFactory.Services,
            tools: Payload(
                ("allowed", new JsonArray(JsonValue.Create("categorize_transaction"))),
                ("actions", new JsonArray(JsonValue.Create("execute")))),
            scopes: Payload(("execute", new JsonArray(JsonValue.Create("finance")))),
            thresholds: Payload(("approval", new JsonObject { ["financeMutationUsd"] = 100 })));

        var financeSeed = await SeedFinanceRecordAsync(financeFactory.Services, seed.CompanyId);
        using var client = CreateAuthenticatedClient(financeFactory, seed);
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
        {
            toolName = "categorize_transaction",
            actionType = "execute",
            scope = "finance",
            thresholdCategory = "approval",
            thresholdKey = "financeMutationUsd",
            thresholdValue = 250,
            sensitiveAction = true,
            taskId = financeSeed.TaskId,
            requestPayload = new { transactionId = financeSeed.TransactionId, category = "software" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();
        Assert.NotNull(payload);
        Assert.Equal("awaiting_approval", payload!.Status);
        Assert.Equal("require_approval", payload.PolicyDecision.Outcome);
        Assert.NotNull(payload.ApprovalRequestId);

        using var scope = financeFactory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var transaction = await dbContext.FinanceTransactions.AsNoTracking().SingleAsync(x => x.Id == financeSeed.TransactionId);
        var attempt = await dbContext.ToolExecutionAttempts.AsNoTracking().SingleAsync(x => x.Id == payload.ExecutionId);
        var approval = await dbContext.ApprovalRequests.AsNoTracking().SingleAsync(x => x.Id == payload.ApprovalRequestId);

        Assert.Equal("uncategorized", transaction.TransactionType);
        Assert.Equal(ToolExecutionStatus.AwaitingApproval, attempt.Status);
        Assert.Equal(payload.ApprovalRequestId, attempt.ApprovalRequestId);
        Assert.Equal(attempt.Id, approval.ToolExecutionAttemptId);
        Assert.Equal(attempt.Id, approval.TargetEntityId);
        Assert.Equal("action", approval.TargetEntityType);
        Assert.Equal(financeSeed.TaskId, approval.ThresholdContext["taskId"]!.GetValue<Guid>());
        Assert.Equal(financeSeed.TaskId, approval.ThresholdContext["originatingTaskId"]!.GetValue<Guid>());
        Assert.Equal(attempt.Id, approval.ThresholdContext["toolExecutionId"]!.GetValue<Guid>());
        Assert.Equal(attempt.Id, approval.ThresholdContext["toolExecutionAttemptId"]!.GetValue<Guid>());
        Assert.Equal(
            FinanceToolRiskPolicyVersions.V1,
            attempt.PolicyDecision["metadata"]!["riskPolicyVersion"]!.GetValue<string>());
        Assert.Equal(
            FinanceToolRiskPolicyVersions.V1,
            approval.ThresholdContext["riskPolicyVersion"]!.GetValue<string>());
        Assert.Equal(2, approval.ThresholdContext["thresholdEvaluations"]!.AsArray().Count);
        var binding = Assert.IsType<JsonObject>(approval.ThresholdContext["approvalBinding"]);
        Assert.Equal("finance-approval-binding-v1", binding["schemaVersion"]!.GetValue<string>());
        Assert.Equal(seed.CompanyId, binding["companyId"]!.GetValue<Guid>());
        Assert.Equal(seed.UserId, binding["initiatingUserId"]!.GetValue<Guid>());
        Assert.Equal(seed.AgentId, binding["agentId"]!.GetValue<Guid>());
        Assert.Equal("categorize_transaction", binding["toolName"]!.GetValue<string>());
        Assert.Equal("1.0.0", binding["toolVersion"]!.GetValue<string>());
        Assert.Equal("execute", binding["actionType"]!.GetValue<string>());
        Assert.Equal("finance", binding["scope"]!.GetValue<string>());
        Assert.Equal(64, binding["normalizedPayloadHash"]!.GetValue<string>().Length);
        Assert.Equal(64, binding["targetSnapshotHash"]!.GetValue<string>().Length);
        Assert.Equal(64, binding["thresholdEvaluationHash"]!.GetValue<string>().Length);
        Assert.Equal(FinanceToolRiskPolicyVersions.V1, binding["riskPolicyVersion"]!.GetValue<string>());
        Assert.True(binding["expiresUtc"]!.GetValue<DateTime>() > binding["issuedUtc"]!.GetValue<DateTime>());
        Assert.DoesNotContain("credential", binding.ToJsonString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("providerData", binding.ToJsonString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(financeSeed.TaskId, approval.DecisionChain["originatingTaskId"]!.GetValue<Guid>());
        Assert.Equal(attempt.Id, approval.DecisionChain["toolExecutionAttemptId"]!.GetValue<Guid>());
        Assert.Equal(0, financeFactory.Services.GetRequiredService<TrackingFinanceToolProvider>().TotalCallCount);
    }

    [Fact]
    public async Task Approved_finance_execute_approval_runs_state_change_and_marks_execution_executed()
    {
        using var financeFactory = CreateFinanceContractFactory();
        var seed = await SeedAgentAsync(
            financeFactory.Services,
            tools: Payload(
                ("allowed", new JsonArray(JsonValue.Create("categorize_transaction"))),
                ("actions", new JsonArray(JsonValue.Create("execute")))),
            scopes: Payload(("execute", new JsonArray(JsonValue.Create("finance")))),
            thresholds: Payload(("approval", new JsonObject { ["financeMutationUsd"] = 100 })));

        var financeSeed = await SeedFinanceRecordAsync(financeFactory.Services, seed.CompanyId);
        using var client = CreateAuthenticatedClient(financeFactory, seed);
        var executeResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
        {
            toolName = "categorize_transaction",
            actionType = "execute",
            scope = "finance",
            thresholdCategory = "approval",
            thresholdKey = "financeMutationUsd",
            thresholdValue = 250,
            sensitiveAction = true,
            taskId = financeSeed.TaskId,
            requestPayload = new { transactionId = financeSeed.TransactionId, category = "software" }
        });
        var executePayload = await executeResponse.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();
        Assert.NotNull(executePayload?.ApprovalRequestId);

        var approval = await client.GetFromJsonAsync<ApprovalRequestDto>(
            $"/api/companies/{seed.CompanyId}/approvals/{executePayload!.ApprovalRequestId!.Value}");
        Assert.NotNull(approval);
        var decisionResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/approvals/{approval.Id}/decisions", new
        {
            decision = "approve",
            stepId = approval!.CurrentStep!.Id,
            comment = "Approved for categorization."
        });

        Assert.Equal(HttpStatusCode.OK, decisionResponse.StatusCode);

        using var scope = financeFactory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var transaction = await dbContext.FinanceTransactions.AsNoTracking().SingleAsync(x => x.Id == financeSeed.TransactionId);
        var attempt = await dbContext.ToolExecutionAttempts.AsNoTracking().SingleAsync(x => x.Id == executePayload.ExecutionId);

        Assert.Equal("software", transaction.TransactionType);
        Assert.Equal(ToolExecutionStatus.Executed, attempt.Status);
        Assert.NotNull(attempt.ExecutedUtc);
        Assert.Equal("executed", attempt.ResultPayload["status"]!.GetValue<string>());
        Assert.Equal(1, financeFactory.Services.GetRequiredService<TrackingFinanceToolProvider>().TotalCallCount);
    }

    [Fact]
    public async Task Changed_finance_target_marks_approval_stale_without_mutation()
    {
        using var financeFactory = CreateFinanceContractFactory();
        var requester = await SeedAgentAsync(
            financeFactory.Services,
            tools: Payload(
                ("allowed", new JsonArray("categorize_transaction")),
                ("actions", new JsonArray("execute"))),
            scopes: Payload(("execute", new JsonArray("finance"))),
            thresholds: Payload(("approval", new JsonObject { ["financeMutationUsd"] = 100 })),
            membershipRole: CompanyMembershipRole.Manager);
        var approver = await SeedAdditionalMemberAsync(
            financeFactory.Services, requester.CompanyId, requester.AgentId, CompanyMembershipRole.Owner);
        var financeSeed = await SeedFinanceRecordAsync(financeFactory.Services, requester.CompanyId);
        using var requesterClient = CreateAuthenticatedClient(financeFactory, requester);
        var executionResponse = await requesterClient.PostAsJsonAsync(
            $"/api/companies/{requester.CompanyId}/agents/{requester.AgentId}/executions", new
            {
                toolName = "categorize_transaction",
                actionType = "execute",
                scope = "finance",
                sensitiveAction = true,
                requestPayload = new { transactionId = financeSeed.TransactionId, category = "software" }
            });
        var execution = await executionResponse.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();
        Assert.NotNull(execution?.ApprovalRequestId);

        using (var mutationScope = financeFactory.Services.CreateScope())
        {
            var companyContext = mutationScope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
            companyContext.SetCompanyId(requester.CompanyId);
            var db = mutationScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            var transaction = await db.FinanceTransactions.SingleAsync(item => item.Id == financeSeed.TransactionId);
            transaction.ChangeCategory("travel");
            await db.SaveChangesAsync();
        }

        using var approverClient = CreateAuthenticatedClient(financeFactory, approver);
        var approvalDto = await approverClient.GetFromJsonAsync<ApprovalRequestDto>(
            $"/api/companies/{requester.CompanyId}/approvals/{execution!.ApprovalRequestId!.Value}");
        var decisionResponse = await approverClient.PostAsJsonAsync(
            $"/api/companies/{requester.CompanyId}/approvals/{approvalDto!.Id}/decisions", new
            {
                decision = "approve",
                stepId = approvalDto.CurrentStep!.Id,
                clientRequestId = Guid.NewGuid(),
                comment = "Approved against the earlier evidence."
            });

        Assert.Equal(HttpStatusCode.OK, decisionResponse.StatusCode);
        using var verificationScope = financeFactory.Services.CreateScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        verificationContext.SetCompanyId(requester.CompanyId);
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var transactionAfter = await verificationDb.FinanceTransactions.AsNoTracking()
            .SingleAsync(item => item.Id == financeSeed.TransactionId);
        var attempt = await verificationDb.ToolExecutionAttempts.AsNoTracking()
            .SingleAsync(item => item.Id == execution.ExecutionId);
        var approval = await verificationDb.ApprovalRequests.AsNoTracking()
            .SingleAsync(item => item.Id == execution.ApprovalRequestId);
        Assert.Equal("travel", transactionAfter.TransactionType);
        Assert.Equal(ApprovalRequestStatus.Stale, approval.Status);
        Assert.Equal(ToolExecutionStatus.Denied, attempt.Status);
        Assert.Equal(FinanceApprovalContinuationReasonCodes.TargetStale, attempt.DenialReason);
        Assert.Equal("stale", approval.DecisionChain["status"]!.GetValue<string>());
        Assert.Equal(execution.ExecutionId, approval.DecisionChain["toolExecutionAttemptId"]!.GetValue<Guid>());
        Assert.Equal(0, financeFactory.Services.GetRequiredService<TrackingFinanceToolProvider>().TotalCallCount);
    }

    [Theory]
    [InlineData("company_id")]
    [InlineData("target_id")]
    [InlineData("role")]
    [InlineData("action_class")]
    [InlineData("scope")]
    [InlineData("sensitivity_flag")]
    [InlineData("risk_tier")]
    [InlineData("approval_id")]
    [InlineData("payload_hash")]
    [InlineData("authority_version")]
    [InlineData("delegation_token")]
    [InlineData("idempotency_key")]
    [InlineData("threshold_hash")]
    public async Task Tampered_finance_approval_context_fails_closed_without_mutation(string tamperClass)
    {
        using var financeFactory = CreateFinanceContractFactory();
        var seed = await SeedAgentAsync(
            financeFactory.Services,
            tools: Payload(
                ("allowed", new JsonArray("categorize_transaction")),
                ("actions", new JsonArray("execute"))),
            scopes: Payload(("execute", new JsonArray("finance"))),
            thresholds: Payload(("approval", new JsonObject { ["financeMutationUsd"] = 100 })));
        var financeSeed = await SeedFinanceRecordAsync(financeFactory.Services, seed.CompanyId);
        using var client = CreateAuthenticatedClient(financeFactory, seed);
        var executionResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
            {
                toolName = "categorize_transaction",
                actionType = "execute",
                scope = "finance",
                sensitiveAction = true,
                requestPayload = new
                {
                    transactionId = financeSeed.TransactionId,
                    category = "software",
                    idempotencyKey = $"categorize:{financeSeed.TransactionId:N}"
                }
            });
        var execution = await executionResponse.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();
        Assert.NotNull(execution?.ApprovalRequestId);

        using (var tamperScope = financeFactory.Services.CreateScope())
        {
            var companyContext = tamperScope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
            companyContext.SetCompanyId(seed.CompanyId);
            var db = tamperScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            var approval = await db.ApprovalRequests.SingleAsync(item => item.Id == execution!.ApprovalRequestId);
            var binding = Assert.IsType<JsonObject>(approval.ThresholdContext["approvalBinding"]);
            switch (tamperClass)
            {
                case "company_id": binding["companyId"] = Guid.NewGuid(); break;
                case "target_id":
                    Assert.IsType<JsonObject>(Assert.IsType<JsonArray>(binding["targetSnapshot"])[0])["entityId"] = Guid.NewGuid();
                    break;
                case "role": binding["requiredActorPermission"] = FinancePermissions.View; break;
                case "action_class": binding["actionType"] = "read"; break;
                case "scope": binding["scope"] = "restricted"; break;
                case "sensitivity_flag": binding["sensitiveAction"] = false; break;
                case "risk_tier": binding["riskTier"] = FinanceToolRiskTiers.Critical; break;
                case "approval_id": binding["approvalRequestId"] = Guid.NewGuid(); break;
                case "payload_hash": binding["normalizedPayloadHash"] = new string('0', 64); break;
                case "authority_version": binding["effectiveAuthorityVersion"] = "forged-authority-version"; break;
                case "delegation_token": binding["delegationAuthorityId"] = Guid.NewGuid(); break;
                case "idempotency_key": binding["businessIdempotencyKey"] = "forged-idempotency-key"; break;
                case "threshold_hash": binding["thresholdEvaluationHash"] = new string('f', 64); break;
                default: throw new InvalidOperationException($"Unknown tamper class: {tamperClass}");
            }

            db.Entry(approval).Property(item => item.ThresholdContext).IsModified = true;
            await db.SaveChangesAsync();
        }

        var approvalDto = await client.GetFromJsonAsync<ApprovalRequestDto>(
            $"/api/companies/{seed.CompanyId}/approvals/{execution!.ApprovalRequestId!.Value}");
        var decisionResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/approvals/{approvalDto!.Id}/decisions", new
            {
                decision = "approve",
                stepId = approvalDto.CurrentStep!.Id,
                clientRequestId = Guid.NewGuid(),
                comment = "Adversarial continuation proof."
            });

        Assert.Equal(HttpStatusCode.OK, decisionResponse.StatusCode);
        using var verificationScope = financeFactory.Services.CreateScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        verificationContext.SetCompanyId(seed.CompanyId);
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var transaction = await verificationDb.FinanceTransactions.AsNoTracking()
            .SingleAsync(item => item.Id == financeSeed.TransactionId);
        var attempt = await verificationDb.ToolExecutionAttempts.AsNoTracking()
            .SingleAsync(item => item.Id == execution.ExecutionId);
        var auditCount = await verificationDb.AuditEvents.AsNoTracking()
            .CountAsync(item => item.CompanyId == seed.CompanyId &&
                                (item.TargetId == approvalDto.Id.ToString("N") ||
                                 item.TargetId == execution.ExecutionId.ToString("N")));
        Assert.Equal("uncategorized", transaction.TransactionType);
        Assert.Equal(ToolExecutionStatus.Denied, attempt.Status);
        Assert.NotEmpty(attempt.ResultPayload);
        Assert.True(auditCount > 0);
        Assert.Equal(0, financeFactory.Services.GetRequiredService<TrackingFinanceToolProvider>().TotalCallCount);
    }

    [Fact]
    public async Task Audit_failure_stops_finance_dispatch_before_any_mutation()
    {
        using var financeFactory = CreateFinanceContractFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAuditEventWriter>();
                services.AddSingleton<IAuditEventWriter, ThrowingAuditEventWriter>();
            }));
        var seed = await SeedAgentAsync(
            financeFactory.Services,
            tools: Payload(("allowed", new JsonArray("categorize_transaction")), ("actions", new JsonArray("execute"))),
            scopes: Payload(("execute", new JsonArray("finance"))),
            thresholds: CategorizationExceptionThresholds("audit-fault-policy", 250m, 2));
        var financeSeed = await SeedFinanceRecordAsync(financeFactory.Services, seed.CompanyId);
        using var client = CreateAuthenticatedClient(financeFactory, seed);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
            {
                toolName = "categorize_transaction",
                actionType = "execute",
                scope = "finance",
                requestPayload = new { transactionId = financeSeed.TransactionId, category = "software" }
            });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var scope = financeFactory.Services.CreateScope();
        var companyContext = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContext.SetCompanyId(seed.CompanyId);
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        Assert.Equal("uncategorized", (await db.FinanceTransactions.AsNoTracking()
            .SingleAsync(item => item.Id == financeSeed.TransactionId)).TransactionType);
        Assert.Empty(await db.ToolExecutionAttempts.AsNoTracking().Where(item => item.CompanyId == seed.CompanyId).ToArrayAsync());
        Assert.Equal(0, financeFactory.Services.GetRequiredService<TrackingFinanceToolProvider>().TotalCallCount);
    }

    [Fact]
    public async Task Transaction_failure_rolls_back_finance_mutation_and_persists_failed_attempt()
    {
        var interceptor = new FailFinanceMutationSaveInterceptor();
        using var rootFactory = new TestWebApplicationFactory(TimeProvider.System, null, false, [interceptor]);
        using var financeFactory = CreateFinanceContractFactory(rootFactory);
        var seed = await SeedAgentAsync(
            financeFactory.Services,
            tools: Payload(("allowed", new JsonArray("categorize_transaction")), ("actions", new JsonArray("execute"))),
            scopes: Payload(("execute", new JsonArray("finance"))),
            thresholds: CategorizationExceptionThresholds("transaction-fault-policy", 250m, 2),
            membershipRole: CompanyMembershipRole.Manager);
        var financeSeed = await SeedFinanceRecordAsync(financeFactory.Services, seed.CompanyId);
        using var client = CreateAuthenticatedClient(financeFactory, seed);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
            {
                toolName = "categorize_transaction",
                actionType = "execute",
                scope = "finance",
                requestPayload = new { transactionId = financeSeed.TransactionId, category = "software" }
            });
        var execution = await response.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("denied", execution!.Status);
        using var scope = financeFactory.Services.CreateScope();
        var companyContext = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContext.SetCompanyId(seed.CompanyId);
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        Assert.Equal("uncategorized", (await db.FinanceTransactions.AsNoTracking()
            .SingleAsync(item => item.Id == financeSeed.TransactionId)).TransactionType);
        Assert.Equal(ToolExecutionStatus.Denied, (await db.ToolExecutionAttempts.AsNoTracking()
            .SingleAsync(item => item.Id == execution.ExecutionId)).Status);
        Assert.Equal(1, financeFactory.Services.GetRequiredService<TrackingFinanceToolProvider>().TotalCallCount);
    }

    [Fact]
    public async Task Outbox_failure_prevents_approved_continuation_and_leaves_approval_pending()
    {
        using var financeFactory = CreateFinanceContractFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICompanyOutboxEnqueuer>();
                services.AddSingleton<ICompanyOutboxEnqueuer, ThrowingCompanyOutboxEnqueuer>();
            }));
        var seed = await SeedAgentAsync(
            financeFactory.Services,
            tools: Payload(("allowed", new JsonArray("categorize_transaction")), ("actions", new JsonArray("execute"))),
            scopes: Payload(("execute", new JsonArray("finance"))),
            thresholds: Payload(("approval", new JsonObject { ["financeMutationUsd"] = 100 })));
        var financeSeed = await SeedFinanceRecordAsync(financeFactory.Services, seed.CompanyId);
        using var client = CreateAuthenticatedClient(financeFactory, seed);
        var executionResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
            {
                toolName = "categorize_transaction",
                actionType = "execute",
                scope = "finance",
                sensitiveAction = true,
                requestPayload = new { transactionId = financeSeed.TransactionId, category = "software" }
            });
        var execution = await executionResponse.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();
        var approval = await client.GetFromJsonAsync<ApprovalRequestDto>(
            $"/api/companies/{seed.CompanyId}/approvals/{execution!.ApprovalRequestId!.Value}");

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/approvals/{approval!.Id}/decisions", new
            {
                decision = "approve",
                stepId = approval.CurrentStep!.Id,
                clientRequestId = Guid.NewGuid()
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var scope = financeFactory.Services.CreateScope();
        var companyContext = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContext.SetCompanyId(seed.CompanyId);
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        Assert.Equal("uncategorized", (await db.FinanceTransactions.AsNoTracking()
            .SingleAsync(item => item.Id == financeSeed.TransactionId)).TransactionType);
        Assert.Equal(ApprovalRequestStatus.Pending, (await db.ApprovalRequests.AsNoTracking()
            .SingleAsync(item => item.Id == approval.Id)).Status);
        Assert.Equal(ToolExecutionStatus.AwaitingApproval, (await db.ToolExecutionAttempts.AsNoTracking()
            .SingleAsync(item => item.Id == execution.ExecutionId)).Status);
        Assert.Equal(0, financeFactory.Services.GetRequiredService<TrackingFinanceToolProvider>().TotalCallCount);
    }

    [Fact]
    public Task Duplicate_approval_delivery_replays_one_durable_continuation() =>
        RunDuplicateApprovalDeliveryAsync(_factory);

    [ApiSqlServerFact]
    [Trait("Category", "SqlServer")]
    public async Task Sql_server_approval_continuation_is_atomic_and_ambiguous_results_require_reconciliation()
    {
        using var sqlFactory = TestWebApplicationFactory.CreateSqlServer(TimeProvider.System);
        await RunDuplicateApprovalDeliveryAsync(sqlFactory);
        await RunAmbiguousFinanceContinuationAsync(sqlFactory);
    }

    private async Task RunDuplicateApprovalDeliveryAsync(WebApplicationFactory<Program> rootFactory)
    {
        using var financeFactory = CreateFinanceContractFactory(rootFactory);
        var seed = await SeedAgentAsync(
            financeFactory.Services,
            tools: Payload(
                ("allowed", new JsonArray("categorize_transaction")),
                ("actions", new JsonArray("execute"))),
            scopes: Payload(("execute", new JsonArray("finance"))),
            thresholds: Payload(("approval", new JsonObject { ["financeMutationUsd"] = 100 })));
        var financeSeed = await SeedFinanceRecordAsync(financeFactory.Services, seed.CompanyId);
        using var client = CreateAuthenticatedClient(financeFactory, seed);
        var executionResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
            {
                toolName = "categorize_transaction",
                actionType = "execute",
                scope = "finance",
                sensitiveAction = true,
                requestPayload = new { transactionId = financeSeed.TransactionId, category = "software" }
            });
        var execution = await executionResponse.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();
        var approval = await client.GetFromJsonAsync<ApprovalRequestDto>(
            $"/api/companies/{seed.CompanyId}/approvals/{execution!.ApprovalRequestId!.Value}");
        var clientRequestId = Guid.NewGuid();
        var route = $"/api/companies/{seed.CompanyId}/approvals/{approval!.Id}/decisions";
        var decision = new
        {
            decision = "approve",
            stepId = approval.CurrentStep!.Id,
            clientRequestId,
            comment = "Approved once."
        };

        var concurrentDeliveries = await Task.WhenAll(
            client.PostAsJsonAsync(route, decision),
            client.PostAsJsonAsync(route, decision));
        var restartReplay = await client.PostAsJsonAsync(route, decision);

        Assert.All(concurrentDeliveries, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(HttpStatusCode.OK, restartReplay.StatusCode);
        using var scope = financeFactory.Services.CreateScope();
        var companyContext = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContext.SetCompanyId(seed.CompanyId);
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var attempts = await db.ToolExecutionAttempts.AsNoTracking()
            .Where(item => item.Id == execution.ExecutionId).ToArrayAsync();
        var storedApproval = await db.ApprovalRequests.AsNoTracking().SingleAsync(item => item.Id == approval.Id);
        Assert.Single(attempts);
        Assert.Equal(ToolExecutionStatus.Executed, attempts[0].Status);
        Assert.Equal(clientRequestId, storedApproval.DecisionChain["lastDecisionClientRequestId"]!.GetValue<Guid>());
        Assert.Equal(1, financeFactory.Services.GetRequiredService<TrackingFinanceToolProvider>().TotalCallCount);
    }

    [Fact]
    public async Task Segregated_finance_action_rejects_requester_self_approval()
    {
        using var financeFactory = CreateFinanceContractFactory();
        var seed = await SeedAgentAsync(
            financeFactory.Services,
            tools: Payload(
                ("allowed", new JsonArray("approve_invoice")),
                ("actions", new JsonArray("execute"))),
            scopes: Payload(("execute", new JsonArray("finance"))),
            thresholds: Payload(("approval", new JsonObject { ["invoiceApprovalUsd"] = 100 })));
        var invoiceId = await SeedInvoiceAsync(financeFactory.Services, seed.CompanyId);
        using var client = CreateAuthenticatedClient(financeFactory, seed);
        var executionResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
            {
                toolName = "approve_invoice",
                actionType = "execute",
                scope = "finance",
                sensitiveAction = false,
                requestPayload = new { invoiceId, status = "approved" }
            });
        var execution = await executionResponse.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();
        Assert.NotNull(execution?.ApprovalRequestId);
        var approvalDto = await client.GetFromJsonAsync<ApprovalRequestDto>(
            $"/api/companies/{seed.CompanyId}/approvals/{execution!.ApprovalRequestId!.Value}");

        var decisionResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/approvals/{approvalDto!.Id}/decisions", new
            {
                decision = "approve",
                stepId = approvalDto.CurrentStep!.Id,
                clientRequestId = Guid.NewGuid(),
                comment = "Requester attempting own approval."
            });

        Assert.Equal(HttpStatusCode.OK, decisionResponse.StatusCode);
        using var scope = financeFactory.Services.CreateScope();
        var companyContext = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContext.SetCompanyId(seed.CompanyId);
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var approval = await db.ApprovalRequests.AsNoTracking().SingleAsync(item => item.Id == approvalDto.Id);
        var attempt = await db.ToolExecutionAttempts.AsNoTracking().SingleAsync(item => item.Id == execution.ExecutionId);
        var invoice = await db.FinanceInvoices.AsNoTracking().SingleAsync(item => item.Id == invoiceId);
        Assert.Equal(ApprovalRequestStatus.Rejected, approval.Status);
        Assert.Contains("segregation of duties", approval.DecisionSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ToolExecutionStatus.Rejected, attempt.Status);
        Assert.Equal("awaiting_approval", invoice.Status);
        Assert.Equal(0, financeFactory.Services.GetRequiredService<TrackingFinanceToolProvider>().TotalCallCount);
    }

    [Fact]
    public async Task Expired_finance_approval_is_explicitly_closed_and_never_executes()
    {
        using var financeFactory = CreateFinanceContractFactory();
        var seed = await SeedAgentAsync(
            financeFactory.Services,
            tools: Payload(
                ("allowed", new JsonArray("categorize_transaction")),
                ("actions", new JsonArray("execute"))),
            scopes: Payload(("execute", new JsonArray("finance"))),
            thresholds: Payload(("approval", new JsonObject { ["financeMutationUsd"] = 100 })));
        var financeSeed = await SeedFinanceRecordAsync(financeFactory.Services, seed.CompanyId);
        using var client = CreateAuthenticatedClient(financeFactory, seed);
        var executionResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
            {
                toolName = "categorize_transaction",
                actionType = "execute",
                scope = "finance",
                sensitiveAction = true,
                requestPayload = new { transactionId = financeSeed.TransactionId, category = "software" }
            });
        var execution = await executionResponse.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();

        using (var expiryScope = financeFactory.Services.CreateScope())
        {
            var expiryCompanyContext = expiryScope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
            expiryCompanyContext.SetCompanyId(seed.CompanyId);
            var db = expiryScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            var approval = await db.ApprovalRequests.SingleAsync(item => item.Id == execution!.ApprovalRequestId);
            var binding = Assert.IsType<JsonObject>(approval.ThresholdContext["approvalBinding"]);
            binding["expiresUtc"] = DateTime.UtcNow.AddMinutes(-1);
            db.Entry(approval).Property(item => item.ThresholdContext).IsModified = true;
            await db.SaveChangesAsync();
        }

        var approvalDto = await client.GetFromJsonAsync<ApprovalRequestDto>(
            $"/api/companies/{seed.CompanyId}/approvals/{execution!.ApprovalRequestId!.Value}");
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/approvals/{approvalDto!.Id}/decisions", new
            {
                decision = "approve",
                stepId = approvalDto.CurrentStep!.Id,
                clientRequestId = Guid.NewGuid()
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var verificationScope = financeFactory.Services.CreateScope();
        var companyContext = verificationScope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContext.SetCompanyId(seed.CompanyId);
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var approvalAfter = await verificationDb.ApprovalRequests.AsNoTracking().SingleAsync(item => item.Id == approvalDto.Id);
        var attempt = await verificationDb.ToolExecutionAttempts.AsNoTracking().SingleAsync(item => item.Id == execution.ExecutionId);
        var transaction = await verificationDb.FinanceTransactions.AsNoTracking().SingleAsync(item => item.Id == financeSeed.TransactionId);
        Assert.Equal(ApprovalRequestStatus.Expired, approvalAfter.Status);
        Assert.Equal(ToolExecutionStatus.Denied, attempt.Status);
        Assert.Equal("uncategorized", transaction.TransactionType);
        Assert.Equal(0, financeFactory.Services.GetRequiredService<TrackingFinanceToolProvider>().TotalCallCount);
    }

    [Fact]
    public Task Ambiguous_approved_finance_result_enters_reconciliation_without_blind_retry() =>
        RunAmbiguousFinanceContinuationAsync(_factory);

    private async Task RunAmbiguousFinanceContinuationAsync(WebApplicationFactory<Program> rootFactory)
    {
        using var financeFactory = CreateAmbiguousFinanceContractFactory(rootFactory);
        var seed = await SeedAgentAsync(
            financeFactory.Services,
            tools: Payload(
                ("allowed", new JsonArray("categorize_transaction")),
                ("actions", new JsonArray("execute"))),
            scopes: Payload(("execute", new JsonArray("finance"))),
            thresholds: Payload(("approval", new JsonObject { ["financeMutationUsd"] = 100 })));
        var financeSeed = await SeedFinanceRecordAsync(financeFactory.Services, seed.CompanyId);
        using var client = CreateAuthenticatedClient(financeFactory, seed);
        var executionResponse = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
            {
                toolName = "categorize_transaction",
                actionType = "execute",
                scope = "finance",
                sensitiveAction = true,
                requestPayload = new { transactionId = financeSeed.TransactionId, category = "software" }
            });
        var execution = await executionResponse.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();
        var approval = await client.GetFromJsonAsync<ApprovalRequestDto>(
            $"/api/companies/{seed.CompanyId}/approvals/{execution!.ApprovalRequestId!.Value}");
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/approvals/{approval!.Id}/decisions", new
            {
                decision = "approve",
                stepId = approval.CurrentStep!.Id,
                clientRequestId = Guid.NewGuid()
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = financeFactory.Services.CreateScope();
        var companyContext = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContext.SetCompanyId(seed.CompanyId);
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var attempt = await db.ToolExecutionAttempts.AsNoTracking().SingleAsync(item => item.Id == execution.ExecutionId);
        var transaction = await db.FinanceTransactions.AsNoTracking().SingleAsync(item => item.Id == financeSeed.TransactionId);
        Assert.Equal(ToolExecutionStatus.ReconciliationRequired, attempt.Status);
        Assert.Equal("ambiguous_provider_outcome", attempt.DenialReason);
        Assert.Equal("uncategorized", transaction.TransactionType);
        Assert.Equal(1, financeFactory.Services.GetRequiredService<AmbiguousFinanceToolExecutor>().CallCount);
    }

    [Fact]
    public async Task Rejected_finance_execute_approval_leaves_state_unchanged_and_marks_execution_rejected()
    {
        using var financeFactory = CreateFinanceContractFactory();
        var seed = await SeedAgentAsync(
            financeFactory.Services,
            tools: Payload(
                ("allowed", new JsonArray(JsonValue.Create("categorize_transaction"))),
                ("actions", new JsonArray(JsonValue.Create("execute")))),
            scopes: Payload(("execute", new JsonArray(JsonValue.Create("finance")))),
            thresholds: Payload(("approval", new JsonObject { ["financeMutationUsd"] = 100 })));

        var financeSeed = await SeedFinanceRecordAsync(financeFactory.Services, seed.CompanyId);
        using var client = CreateAuthenticatedClient(financeFactory, seed);
        var executeResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
        {
            toolName = "categorize_transaction",
            actionType = "execute",
            scope = "finance",
            thresholdCategory = "approval",
            thresholdKey = "financeMutationUsd",
            thresholdValue = 250,
            sensitiveAction = true,
            taskId = financeSeed.TaskId,
            requestPayload = new { transactionId = financeSeed.TransactionId, category = "software" }
        });
        var executePayload = await executeResponse.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();
        Assert.NotNull(executePayload?.ApprovalRequestId);

        var approval = await client.GetFromJsonAsync<ApprovalRequestDto>(
            $"/api/companies/{seed.CompanyId}/approvals/{executePayload!.ApprovalRequestId!.Value}");
        Assert.NotNull(approval);
        var decisionResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/approvals/{approval.Id}/decisions", new
        {
            decision = "reject",
            stepId = approval!.CurrentStep!.Id,
            comment = "Needs more evidence."
        });

        Assert.Equal(HttpStatusCode.OK, decisionResponse.StatusCode);

        using var scope = financeFactory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var transaction = await dbContext.FinanceTransactions.AsNoTracking().SingleAsync(x => x.Id == financeSeed.TransactionId);
        var attempt = await dbContext.ToolExecutionAttempts.AsNoTracking().SingleAsync(x => x.Id == executePayload.ExecutionId);

        Assert.Equal("uncategorized", transaction.TransactionType);
        Assert.Equal(ToolExecutionStatus.Rejected, attempt.Status);
        Assert.Null(attempt.ExecutedUtc);
        Assert.Equal(executePayload.ApprovalRequestId, attempt.ApprovalRequestId);
        Assert.Equal(PolicyDecisionReasonCodes.ApprovalRejected, attempt.DenialReason);
        Assert.Equal("rejected", attempt.ResultPayload["status"]!.GetValue<string>());
        Assert.Equal(0, financeFactory.Services.GetRequiredService<TrackingFinanceToolProvider>().TotalCallCount);
    }

    [Fact]
    public async Task Approved_continuation_rechecks_originating_actor_permission_before_finance_mutation()
    {
        using var financeFactory = CreateFinanceContractFactory();
        var requester = await SeedAgentAsync(
            financeFactory.Services,
            tools: Payload(
                ("allowed", new JsonArray(JsonValue.Create("categorize_transaction"))),
                ("actions", new JsonArray(JsonValue.Create("execute")))),
            scopes: Payload(("execute", new JsonArray(JsonValue.Create("finance")))),
            thresholds: Payload(("approval", new JsonObject { ["financeMutationUsd"] = 100 })),
            membershipRole: CompanyMembershipRole.Manager);
        var approver = await SeedAdditionalMemberAsync(
            financeFactory.Services, requester.CompanyId, requester.AgentId, CompanyMembershipRole.Owner);
        var financeSeed = await SeedFinanceRecordAsync(financeFactory.Services, requester.CompanyId);

        using var requesterClient = CreateAuthenticatedClient(financeFactory, requester);
        var executeResponse = await requesterClient.PostAsJsonAsync(
            $"/api/companies/{requester.CompanyId}/agents/{requester.AgentId}/executions", new
            {
                toolName = "categorize_transaction",
                actionType = "execute",
                scope = "finance",
                thresholdCategory = "approval",
                thresholdKey = "financeMutationUsd",
                thresholdValue = 250,
                sensitiveAction = true,
                requestPayload = new { transactionId = financeSeed.TransactionId, category = "software" }
            });
        var execution = await executeResponse.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();
        Assert.NotNull(execution?.ApprovalRequestId);

        using (var mutationScope = financeFactory.Services.CreateScope())
        {
            var db = mutationScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            var membership = await db.CompanyMemberships.SingleAsync(x =>
                x.CompanyId == requester.CompanyId && x.UserId == requester.UserId);
            membership.UpdateRole(CompanyMembershipRole.FinanceApprover);
            await db.SaveChangesAsync();
        }

        using var approverClient = CreateAuthenticatedClient(financeFactory, approver);
        var approval = await approverClient.GetFromJsonAsync<ApprovalRequestDto>(
            $"/api/companies/{requester.CompanyId}/approvals/{execution!.ApprovalRequestId!.Value}");
        var decisionResponse = await approverClient.PostAsJsonAsync(
            $"/api/companies/{requester.CompanyId}/approvals/{approval!.Id}/decisions", new
            {
                decision = "approve",
                stepId = approval.CurrentStep!.Id,
                comment = "Approved after review."
            });

        Assert.Equal(HttpStatusCode.OK, decisionResponse.StatusCode);
        using var verificationScope = financeFactory.Services.CreateScope();
        var companyContext = verificationScope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContext.SetCompanyId(requester.CompanyId);
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var transaction = await verificationDb.FinanceTransactions.AsNoTracking()
            .SingleAsync(x => x.Id == financeSeed.TransactionId);
        var attempt = await verificationDb.ToolExecutionAttempts.AsNoTracking()
            .SingleAsync(x => x.Id == execution.ExecutionId);
        Assert.Equal("uncategorized", transaction.TransactionType);
        Assert.Equal(ToolExecutionStatus.Denied, attempt.Status);
        Assert.Equal(FinanceAgentAuthorizationReasonCodes.PermissionMissing, attempt.DenialReason);
        Assert.Equal(0, financeFactory.Services.GetRequiredService<TrackingFinanceToolProvider>().TotalCallCount);
    }

    [Theory]
    [InlineData("valid", true, FinanceAgentAuthorizationReasonCodes.Authorized)]
    [InlineData("expired", false, FinanceAgentAuthorizationReasonCodes.DelegationExpired)]
    [InlineData("workflow_mismatch", false, FinanceAgentAuthorizationReasonCodes.DelegationWorkflowMismatch)]
    public async Task Background_execution_requires_matching_persisted_delegation_without_agent_fallback(
        string variation,
        bool expectedAllowed,
        string expectedReason)
    {
        using var financeFactory = CreateFinanceContractFactory();
        var seed = await SeedAgentAsync(
            financeFactory.Services,
            tools: Payload(
                ("allowed", new JsonArray(JsonValue.Create("get_cash_balance"))),
                ("actions", new JsonArray(JsonValue.Create("read")))),
            scopes: Payload(("read", new JsonArray(JsonValue.Create("finance")))));
        var workflowId = Guid.NewGuid();
        var delegationId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        using var scope = financeFactory.Services.CreateScope();
        var companyContext = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContext.SetCompanyId(seed.CompanyId);
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        db.FinanceAgentDelegationAuthorities.Add(new FinanceAgentDelegationAuthority(
            delegationId,
            seed.CompanyId,
            seed.AgentId,
            seed.UserId,
            seed.UserId,
            workflowId,
            "finance",
            [ToolActionType.Read],
            ["finance"],
            now.AddHours(-1),
            variation == "expired" ? now.AddMinutes(-1) : now.AddHours(1)));
        await db.SaveChangesAsync();

        var service = scope.ServiceProvider.GetRequiredService<IAgentToolExecutionService>();
        var result = await service.ExecuteAsync(
            seed.CompanyId,
            seed.AgentId,
            new ExecuteAgentToolCommand(
                "get_cash_balance",
                "read",
                "finance",
                Payload(("asOfUtc", JsonValue.Create("2026-04-16T00:00:00Z"))),
                null,
                null,
                null,
                WorkflowInstanceId: variation == "workflow_mismatch" ? Guid.NewGuid() : workflowId,
                CorrelationId: $"background-{variation}",
                DelegationAuthorityId: delegationId),
            CancellationToken.None);

        Assert.NotNull(result.ActorAuthorization);
        Assert.Equal(expectedAllowed, result.ActorAuthorization!.IsAllowed);
        Assert.Equal(expectedReason, result.ActorAuthorization.ReasonCode);
        Assert.Equal(expectedAllowed ? "executed" : "denied", result.Status);
        Assert.Equal(expectedAllowed ? 1 : 0,
            financeFactory.Services.GetRequiredService<TrackingFinanceToolProvider>().TotalCallCount);
    }

    public static IEnumerable<object[]> SuccessfulFinanceToolRequests()
    {
        yield return
        [
            "get_cash_balance",
            new { asOfUtc = "2026-04-16T00:00:00Z" },
            "cashBalance",
            nameof(TrackingFinanceToolProvider.GetCashBalanceAsync)
        ];

        yield return
        [
            "resolve_finance_agent_query",
            new { queryText = "what should i pay this week" },
            "result",
            nameof(TrackingFinanceToolProvider.ResolveAgentQueryAsync)
        ];

        yield return
        [
            "list_transactions",
            new { startUtc = "2026-04-01T00:00:00Z", endUtc = "2026-04-16T00:00:00Z", limit = 25 },
            "transactions",
            nameof(TrackingFinanceToolProvider.GetTransactionsAsync)
        ];

        yield return
        [
            "list_uncategorized_transactions",
            new { limit = 10 },
            "transactions",
            nameof(TrackingFinanceToolProvider.GetTransactionsAsync)
        ];

        yield return
        [
            "list_invoices_awaiting_approval",
            new { limit = 10 },
            "invoices",
            nameof(TrackingFinanceToolProvider.GetInvoicesAsync)
        ];

        yield return
        [
            "get_profit_and_loss_summary",
            new { year = 2026, month = 4 },
            "profitAndLossSummary",
            nameof(TrackingFinanceToolProvider.GetMonthlyProfitAndLossAsync)
        ];
    }

    private WebApplicationFactory<Program> CreateFinanceContractFactory() =>
        CreateFinanceContractFactory(_factory);

    private static WebApplicationFactory<Program> CreateFinanceContractFactory(
        WebApplicationFactory<Program> rootFactory) =>
        rootFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IFinanceToolProvider>();
                services.RemoveAll<IInternalCompanyToolContract>();
                services.AddSingleton<TrackingFinanceToolProvider>();
                services.AddScoped<IFinanceToolProvider>(provider => provider.GetRequiredService<TrackingFinanceToolProvider>());
                services.AddScoped<IInternalCompanyToolContract, InternalCompanyToolContract>();
            });
        });

    private WebApplicationFactory<Program> CreateAmbiguousFinanceContractFactory() =>
        CreateAmbiguousFinanceContractFactory(_factory);

    private static WebApplicationFactory<Program> CreateAmbiguousFinanceContractFactory(
        WebApplicationFactory<Program> rootFactory) =>
        rootFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICompanyToolExecutor>();
                services.AddSingleton<AmbiguousFinanceToolExecutor>();
                services.AddScoped<ICompanyToolExecutor>(provider =>
                    provider.GetRequiredService<AmbiguousFinanceToolExecutor>());
            });
        });

    private static async Task<SeededExecutionAgent> SeedAgentAsync(
        IServiceProvider services,
        Dictionary<string, JsonNode?> tools,
        Dictionary<string, JsonNode?> scopes,
        Dictionary<string, JsonNode?>? thresholds = null,
        CompanyMembershipRole membershipRole = CompanyMembershipRole.Owner)
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var subject = $"finance-tool-{Guid.NewGuid():N}";
        var email = $"{subject}@example.com";
        const string displayName = "Finance Tool Tester";

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Users.Add(new User(userId, email, displayName, "dev-header", subject));
        dbContext.Companies.Add(new Company(companyId, "Finance Tool Company"));
        dbContext.CompanyMemberships.Add(new CompanyMembership(
            Guid.NewGuid(),
            companyId,
            userId,
            membershipRole,
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
            autonomyLevel: AgentAutonomyLevel.Level2,
            objectives: Payload(("primary", new JsonArray(JsonValue.Create("Protect cash flow")))),
            kpis: Payload(("targets", new JsonArray(JsonValue.Create("forecast_accuracy")))),
            tools: tools,
            scopes: scopes,
            thresholds: thresholds ?? Payload(("approval", new JsonObject { ["cashReadUsd"] = 100000 })),
            escalationRules: Payload(("escalateTo", JsonValue.Create("founder"))),
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

        await dbContext.SaveChangesAsync();

        return new SeededExecutionAgent(companyId, agentId, userId, subject, email, displayName);
    }

    private static async Task<SeededFinanceRecord> SeedFinanceRecordAsync(IServiceProvider services, Guid companyId)
    {
        var accountId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var counterpartyId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        using var scope = services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(companyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        dbContext.FinanceAccounts.Add(new FinanceAccount(
            accountId,
            companyId,
            "1000",
            "Operating Cash",
            "asset",
            "USD",
            1000m,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        dbContext.FinanceCounterparties.Add(new FinanceCounterparty(counterpartyId, companyId, "Vendor", "vendor"));
        dbContext.FinanceTransactions.Add(new FinanceTransaction(
            transactionId,
            companyId,
            accountId,
            counterpartyId,
            null,
            null,
            new DateTime(2026, 4, 16, 0, 0, 0, DateTimeKind.Utc),
            "uncategorized",
            -250m,
            "USD",
            "Cloud tools",
            $"txn-{transactionId:N}"));
        dbContext.WorkTasks.Add(new WorkTask(
            taskId,
            companyId,
            "finance",
            "Review transaction category",
            "Requires approval before mutation.",
            WorkTaskPriority.Normal,
            null,
            null,
            "agent",
            Guid.NewGuid(),
            new Dictionary<string, JsonNode?> { ["transactionId"] = JsonValue.Create(transactionId) }));

        await dbContext.SaveChangesAsync();
        return new SeededFinanceRecord(transactionId, taskId);
    }

    private static async Task<Guid> SeedInvoiceAsync(IServiceProvider services, Guid companyId)
    {
        var counterpartyId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var now = new DateTime(2026, 4, 16, 0, 0, 0, DateTimeKind.Utc);
        using var scope = services.CreateScope();
        var companyContext = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContext.SetCompanyId(companyId);
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        db.FinanceCounterparties.Add(new FinanceCounterparty(
            counterpartyId, companyId, "Approval Customer", "customer"));
        db.FinanceInvoices.Add(new FinanceInvoice(
            invoiceId,
            companyId,
            counterpartyId,
            "INV-APPROVAL-001",
            now,
            now.AddDays(30),
            500m,
            "USD",
            "awaiting_approval"));
        await db.SaveChangesAsync();
        return invoiceId;
    }

    private static async Task<SeededExecutionAgent> SeedAdditionalMemberAsync(
        IServiceProvider services,
        Guid companyId,
        Guid agentId,
        CompanyMembershipRole role)
    {
        var userId = Guid.NewGuid();
        var subject = $"finance-tool-{Guid.NewGuid():N}";
        var email = $"{subject}@example.com";
        const string displayName = "Finance Approval Tester";
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        db.Users.Add(new User(userId, email, displayName, "dev-header", subject));
        db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, role, CompanyMembershipStatus.Active));
        await db.SaveChangesAsync();
        return new SeededExecutionAgent(companyId, agentId, userId, subject, email, displayName);
    }

    private static HttpClient CreateAuthenticatedClient(WebApplicationFactory<Program> factory, SeededExecutionAgent seed)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, seed.Subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, seed.Email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, seed.DisplayName);
        return client;
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

    private static Dictionary<string, JsonNode?> CategorizationExceptionThresholds(
        string policyVersion,
        decimal maximumAmount,
        int maximumBatchCount)
    {
        return Payload(
            ("financePolicy", new JsonObject
            {
                ["policyVersion"] = policyVersion,
                ["categorizationException"] = new JsonObject
                {
                    ["policyVersion"] = policyVersion,
                    ["enabled"] = true,
                    ["maxAmount"] = maximumAmount,
                    ["maxBatchCount"] = maximumBatchCount,
                    ["requiredCurrentState"] = "uncategorized",
                    ["allowedCategories"] = new JsonArray("software", "office_supplies")
                }
            }));
    }

    private sealed record SeededExecutionAgent(
        Guid CompanyId,
        Guid AgentId,
        Guid UserId,
        string Subject,
        string Email,
        string DisplayName);

    private sealed record SeededFinanceRecord(
        Guid TransactionId,
        Guid TaskId);

    public sealed class TrackingFinanceToolProvider : IFinanceToolProvider
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ConcurrentQueue<string> _callNames = new();

        public TrackingFinanceToolProvider(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public IReadOnlyList<string> CallNames => _callNames.ToArray();
        public int TotalCallCount => _callNames.Count;

        public Task<FinanceCashBalanceDto> GetCashBalanceAsync(
            GetFinanceCashBalanceQuery query,
            CancellationToken cancellationToken)
        {
            _callNames.Enqueue(nameof(GetCashBalanceAsync));
            return Task.FromResult(new FinanceCashBalanceDto(
                query.CompanyId,
                query.AsOfUtc ?? new DateTime(2026, 4, 16, 0, 0, 0, DateTimeKind.Utc),
                9876.54m,
                "USD",
                [
                    new FinanceAccountBalanceDto(
                        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                        "1000",
                        "Tracked Cash",
                        "asset",
                        9876.54m,
                        "USD",
                        query.AsOfUtc ?? new DateTime(2026, 4, 16, 0, 0, 0, DateTimeKind.Utc))
                ]));
        }

        public Task<FinanceAgentQueryResultDto> ResolveAgentQueryAsync(
            GetFinanceAgentQueryQuery query,
            CancellationToken cancellationToken)
        {
            _callNames.Enqueue(nameof(ResolveAgentQueryAsync));
            return Task.FromResult(new FinanceAgentQueryResultDto(
                query.CompanyId,
                FinanceAgentQueryIntents.WhatShouldIPayThisWeek,
                FinanceAgentQueryRouting.NormalizeQueryText(query.QueryText),
                "Selected 1 payable item.",
                "USD",
                query.AsOfUtc ?? new DateTime(2026, 4, 16, 0, 0, 0, DateTimeKind.Utc),
                new FinanceAgentQueryPeriodDto(query.AsOfUtc ?? new DateTime(2026, 4, 16, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 4, 13, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc), null, null, "UTC"),
                [new FinanceAgentQueryItemDto(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), "bill", Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), "Tracked Vendor", "BILL-001", new DateTime(2026, 4, 17, 0, 0, 0, DateTimeKind.Utc), 250m, "USD", "Due within the current company week.", 1, null, null, [Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd")], [new FinanceAgentMetricComponentDto("remaining_balance", "Remaining balance", 250m, null, 250m, "USD", [Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd")])])], [new FinanceAgentMetricComponentDto("recommended_payables_total", "Recommended payables total", 250m, null, 250m, "USD", [Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd")])], [Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd")]));
        }

        public Task<FinanceMonthlyProfitAndLossDto> GetMonthlyProfitAndLossAsync(
            GetFinanceMonthlyProfitAndLossQuery query,
            CancellationToken cancellationToken)
        {
            _callNames.Enqueue(nameof(GetMonthlyProfitAndLossAsync));
            return Task.FromResult(new FinanceMonthlyProfitAndLossDto(
                query.CompanyId,
                query.Year,
                query.Month,
                new DateTime(query.Year, query.Month, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(query.Year, query.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1),
                5000m,
                1750m,
                3250m,
                "USD"));
        }

        public Task<FinanceExpenseBreakdownDto> GetExpenseBreakdownAsync(
            GetFinanceExpenseBreakdownQuery query,
            CancellationToken cancellationToken)
        {
            _callNames.Enqueue(nameof(GetExpenseBreakdownAsync));
            return Task.FromResult(new FinanceExpenseBreakdownDto(
                query.CompanyId,
                query.StartUtc,
                query.EndUtc,
                100m,
                "USD",
                [new FinanceExpenseCategoryDto("software", 100m, "USD")]));
        }

        public Task<IReadOnlyList<FinanceTransactionDto>> GetTransactionsAsync(
            GetFinanceTransactionsQuery query,
            CancellationToken cancellationToken)
        {
            _callNames.Enqueue(nameof(GetTransactionsAsync));
            IReadOnlyList<FinanceTransactionDto> transactions =
            [
                new FinanceTransactionDto(
                    Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                    Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    "Tracked Cash",
                    null,
                    null,
                    null,
                    null,
                    new DateTime(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc),
                    "uncategorized",
                    -42m,
                    "USD",
                    "Tracked uncategorized transaction",
                    "tracked-uncategorized",
                    null),
                new FinanceTransactionDto(
                    Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                    Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    "Tracked Cash",
                    null,
                    null,
                    null,
                    null,
                    new DateTime(2026, 4, 14, 12, 0, 0, DateTimeKind.Utc),
                    "revenue",
                    250m,
                    "USD",
                    "Tracked categorized transaction",
                    "tracked-revenue",
                    null)
            ];
            return Task.FromResult(transactions);
        }

        public Task<IReadOnlyList<FinanceInvoiceDto>> GetInvoicesAsync(
            GetFinanceInvoicesQuery query,
            CancellationToken cancellationToken)
        {
            _callNames.Enqueue(nameof(GetInvoicesAsync));
            IReadOnlyList<FinanceInvoiceDto> invoices =
            [
                new FinanceInvoiceDto(
                    Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                    Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                    "Tracked Customer",
                    "INV-TRACKED-001",
                    new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc),
                    1200m,
                    "USD",
                    "awaiting_approval",
                    null),
                new FinanceInvoiceDto(
                    Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                    Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                    "Tracked Customer",
                    "INV-TRACKED-002",
                    new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc),
                    800m,
                    "USD",
                    "paid",
                    null)
            ];
            return Task.FromResult(invoices);
        }

        public Task<IReadOnlyList<FinanceBillDto>> GetBillsAsync(
            GetFinanceBillsQuery query,
            CancellationToken cancellationToken)
        {
            _callNames.Enqueue(nameof(GetBillsAsync));
            IReadOnlyList<FinanceBillDto> bills = [];
            return Task.FromResult(bills);
        }

        public Task<IReadOnlyList<FinanceAccountBalanceDto>> GetBalancesAsync(
            GetFinanceBalancesQuery query,
            CancellationToken cancellationToken)
        {
            _callNames.Enqueue(nameof(GetBalancesAsync));
            IReadOnlyList<FinanceAccountBalanceDto> balances =
            [
                new FinanceAccountBalanceDto(
                    Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    "1000",
                    "Tracked Cash",
                    "asset",
                    9876.54m,
                    "USD",
                    query.AsOfUtc ?? new DateTime(2026, 4, 16, 0, 0, 0, DateTimeKind.Utc))
            ];
            return Task.FromResult(balances);
        }

        public Task<FinanceTransactionCategoryRecommendationDto> RecommendTransactionCategoryAsync(
            InternalToolExecutionRequest request,
            CancellationToken cancellationToken)
        {
            _callNames.Enqueue(nameof(RecommendTransactionCategoryAsync));
            var transactionId = request.Payload.TryGetValue("transactionId", out var node) && node is JsonValue value && value.TryGetValue<Guid>(out var guid)
                ? guid
                : Guid.Empty;
            return Task.FromResult(new FinanceTransactionCategoryRecommendationDto(transactionId, "software", 0.8m));
        }

        public Task<FinanceInvoiceApprovalRecommendationDto> RecommendInvoiceApprovalDecisionAsync(
            InternalToolExecutionRequest request,
            CancellationToken cancellationToken)
        {
            _callNames.Enqueue(nameof(RecommendInvoiceApprovalDecisionAsync));
            var invoiceId = request.Payload.TryGetValue("invoiceId", out var node) && node is JsonValue value && value.TryGetValue<Guid>(out var guid)
                ? guid
                : Guid.Empty;
            return Task.FromResult(new FinanceInvoiceApprovalRecommendationDto(invoiceId, "approved", 0.8m));
        }

        public async Task<FinanceTransactionDto> UpdateTransactionCategoryAsync(
            UpdateFinanceTransactionCategoryCommand command,
            CancellationToken cancellationToken)
        {
            _callNames.Enqueue(nameof(UpdateTransactionCategoryAsync));
            using var scope = _scopeFactory.CreateScope();
            var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
            companyContextAccessor.SetCompanyId(command.CompanyId);
            var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            var transaction = await dbContext.FinanceTransactions
                .Include(x => x.Account)
                .Include(x => x.Counterparty)
                .SingleAsync(x => x.CompanyId == command.CompanyId && x.Id == command.TransactionId, cancellationToken);

            transaction.ChangeCategory(command.Category);
            await dbContext.SaveChangesAsync(cancellationToken);
            return new FinanceTransactionDto(transaction.Id, transaction.AccountId, transaction.Account.Name, transaction.CounterpartyId, transaction.Counterparty?.Name, transaction.InvoiceId, transaction.BillId, transaction.TransactionUtc, transaction.TransactionType, transaction.Amount, transaction.Currency, transaction.Description, transaction.ExternalReference, null);
        }

        public async Task<FinanceInvoiceDto> UpdateInvoiceApprovalStatusAsync(
            UpdateFinanceInvoiceApprovalStatusCommand command,
            CancellationToken cancellationToken)
        {
            _callNames.Enqueue(nameof(UpdateInvoiceApprovalStatusAsync));
            using var scope = _scopeFactory.CreateScope();
            var service = ActivatorUtilities.CreateInstance<VirtualCompany.Infrastructure.Finance.CompanyFinanceCommandService>(
                scope.ServiceProvider,
                scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>());
            return await service.UpdateInvoiceApprovalStatusAsync(command, cancellationToken);
        }

        public Task<PaidSupplierBillExpensePostingDto> PostPaidSupplierBillExpenseAsync(
            PostPaidSupplierBillExpenseCommand command,
            CancellationToken cancellationToken)
        {
            _callNames.Enqueue(nameof(PostPaidSupplierBillExpenseAsync));
            var now = new DateTime(2026, 4, 16, 0, 0, 0, DateTimeKind.Utc);
            var draftAction = new SupplierInvoiceDraftActionDto(
                Guid.Parse("abababab-abab-abab-abab-abababababab"),
                command.BillId,
                "booked",
                FinanceIntegrationProviderKeys.Fortnox,
                Guid.Parse("cdcdcdcd-cdcd-cdcd-cdcd-cdcdcdcdcdcd"),
                command.ActorUserId,
                now,
                null,
                now,
                "Tracked Fortnox booked supplier invoice.",
                now,
                now);
            return Task.FromResult(new PaidSupplierBillExpensePostingDto(
                command.BillId,
                draftAction.Id,
                draftAction.Status,
                Posted: true,
                FinanceIntegrationProviderKeys.Fortnox,
                draftAction.ConnectionId,
                "Tracked Fortnox booked supplier invoice.",
                draftAction.RequestedUtc,
                draftAction.BookedUtc,
                draftAction));
        }
    }

    public sealed class AmbiguousFinanceToolExecutor : ICompanyToolExecutor
    {
        private int _callCount;
        public int CallCount => _callCount;

        public Task<ToolExecutionResult> ExecuteAsync(
            ToolExecutionRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(ToolExecutionResult.Failed(
                request.ToolName,
                request.ActionType,
                ToolExecutionStatus.ReconciliationRequired.ToStorageValue(),
                "ambiguous_provider_outcome",
                "The provider outcome is ambiguous and requires reconciliation."));
        }
    }

    private sealed class ThrowingAuditEventWriter : IAuditEventWriter
    {
        public Task WriteAsync(AuditEventWriteRequest auditEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Injected audit persistence failure.");
    }

    private sealed class ThrowingCompanyOutboxEnqueuer : ICompanyOutboxEnqueuer
    {
        public void Enqueue(
            Guid companyId,
            string topic,
            object payload,
            string? correlationId = null,
            DateTime? availableAtUtc = null,
            string? idempotencyKey = null,
            string? messageType = null,
            string? causationId = null,
            IReadOnlyDictionary<string, string?>? headers = null) =>
            throw new InvalidOperationException("Injected outbox persistence failure.");
    }

    private sealed class FailFinanceMutationSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<FinanceTransaction>()
                    .Any(entry => entry.State == EntityState.Modified) == true)
            {
                throw new InvalidOperationException("Injected Finance transaction persistence failure.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
