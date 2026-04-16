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
using VirtualCompany.Infrastructure.Observability;
using Xunit;
using VirtualCompany.Infrastructure.Companies;

namespace VirtualCompany.Api.Tests;

public sealed class SingleAgentOrchestrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public SingleAgentOrchestrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.ToolExecutor.Reset();
    }

    [Fact]
    public async Task Task_execute_endpoint_uses_shared_orchestration_pipeline_and_persists_metadata()
    {
        var seed = await SeedTaskAsync();
        using var client = CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/tasks/{seed.TaskId}/execute", new
        {
            agentId = seed.AgentId,
            initiatingActorId = seed.UserId,
            initiatingActorType = "user",
            correlationId = "orch-test-correlation",
            intent = "execute_task",
            toolInvocations = new[]
            {
                new
                {
                    toolName = "erp",
                    actionType = "execute",
                    scope = "payments",
                    requestPayload = new { invoiceId = "inv-100" },
                    thresholdCategory = "approval",
                    thresholdKey = "expenseUsd",
                    thresholdValue = 100,
                    sensitiveAction = false
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<OrchestrationResponse>();
        Assert.NotNull(payload);
        Assert.Equal(seed.CompanyId, payload!.CompanyId);
        Assert.Equal(seed.TaskId, payload.TaskId);
        Assert.Equal(seed.AgentId, payload.AgentId);
        Assert.Equal("completed", payload.Status);
        Assert.Equal("orch-test-correlation", payload.CorrelationId);
        Assert.Contains("Nora Ledger completed task", payload.UserFacingOutput);
        Assert.NotNull(payload.FinalResult);
        Assert.Equal(payload.UserFacingOutput, payload.FinalResult!.UserOutput.DisplayMessage);
        Assert.Equal("orch-test-correlation", payload.FinalResult.CorrelationId);
        Assert.NotNull(payload.TaskArtifact);
        Assert.Equal(seed.TaskId, payload.TaskArtifact!.TaskId);
        Assert.Equal("completed", payload.TaskArtifact.Status);
        Assert.Equal("orch-test-correlation", payload.TaskArtifact.CorrelationId);
        Assert.NotNull(payload.TaskArtifact.OutputPayload);
        Assert.Equal(payload.UserFacingOutput, payload.TaskArtifact.OutputPayload!.Value.GetProperty("userFacingOutput").GetString());
        Assert.NotNull(payload.RationaleSummary);
        Assert.DoesNotContain("chain-of-thought", payload.RationaleSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Single(payload.AuditArtifacts);
        Assert.Equal("single_agent_task.orchestration.executed", payload.AuditArtifacts[0].Action);
        Assert.Equal("orch-test-correlation", payload.AuditArtifacts[0].CorrelationId);
        Assert.Single(payload.ToolExecutions);
        Assert.Single(payload.ToolExecutionReferences);
        Assert.Equal("executed", payload.ToolExecutions[0].Status);
        Assert.Equal("orch-test-correlation", payload.ToolExecutions[0].CorrelationId);
        Assert.NotNull(payload.ToolExecutions[0].ResultPayload);
        Assert.True(payload.ToolExecutions[0].ResultPayload!.Value.GetProperty("success").GetBoolean());
        Assert.Equal("executed", payload.ToolExecutions[0].ResultPayload!.Value.GetProperty("status").GetString());
        Assert.Equal("erp", payload.ToolExecutions[0].ResultPayload!.Value.GetProperty("toolName").GetString());
        Assert.Equal(1, _factory.ToolExecutor.ExecutionCount);

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var task = await dbContext.WorkTasks.AsNoTracking().SingleAsync(x => x.Id == seed.TaskId);
        Assert.Equal(WorkTaskStatus.Completed, task.Status);
        Assert.Equal("orch-test-correlation", task.CorrelationId);
        Assert.Equal("orch-test-correlation", task.OutputPayload["correlationId"]!.GetValue<string>());
        Assert.Equal(seed.AgentId.ToString("N"), task.OutputPayload["agentId"]!.GetValue<string>());
        Assert.NotNull(task.RationaleSummary);
        Assert.True(task.OutputPayload.ContainsKey("toolExecutionReferences"));
        Assert.True(task.OutputPayload.ContainsKey("sourceReferences"));
        Assert.True(task.OutputPayload.ContainsKey("rationaleSummary"));
        Assert.True(task.ConfidenceScore > 0);

        var auditEvent = await dbContext.AuditEvents.AsNoTracking().SingleAsync(x =>
            x.CompanyId == seed.CompanyId &&
            x.Action == AuditEventActions.SingleAgentTaskOrchestrationExecuted &&
            x.TargetId == seed.TaskId.ToString("N"));

        Assert.Equal("orch-test-correlation", auditEvent.CorrelationId);
        Assert.Equal(payload.OrchestrationId.ToString("N"), auditEvent.Metadata["orchestrationId"]);
        Assert.Equal(seed.AgentId, auditEvent.ActorId);
        Assert.Equal("Nora Ledger", auditEvent.AgentName);
        Assert.Equal("Finance Manager", auditEvent.AgentRole);
        Assert.Equal("Finance", auditEvent.ResponsibilityDomain);
        Assert.NotNull(auditEvent.PromptProfileVersion);
        Assert.Equal(AuditBoundaryDecisionOutcomes.InScope, auditEvent.BoundaryDecisionOutcome);
        Assert.Null(auditEvent.IdentityReasonCode);
        Assert.False(await dbContext.AuditEvents.AsNoTracking().AnyAsync(x =>
            x.CompanyId == seed.CompanyId &&
            x.Action == AuditEventActions.AgentGeneration &&
            x.ActorId == seed.AgentId &&
            x.TargetId == payload.OrchestrationId.ToString("N") &&
            x.AgentName == "Nora Ledger" &&
            x.AgentRole == "Finance Manager" &&
            x.ResponsibilityDomain == "Finance" &&
            x.PromptProfileVersion != null &&
            x.BoundaryDecisionOutcome == AuditBoundaryDecisionOutcomes.InScope &&
            x.IdentityReasonCode != null));
        Assert.True(await dbContext.AuditEvents.AsNoTracking().AnyAsync(x =>
            x.CompanyId == seed.CompanyId &&
            x.Action == AuditEventActions.BoundaryEnforcement &&
            x.ActorId == seed.AgentId &&
            x.TargetId == payload.ToolExecutions[0].ExecutionId.ToString("N") &&
            x.BoundaryDecisionOutcome == AuditBoundaryDecisionOutcomes.InScope));
        Assert.False(await dbContext.AuditEvents.AsNoTracking().AnyAsync(x =>
            x.CompanyId == seed.CompanyId &&
            x.Action == AuditEventActions.AgentResponsibilityOutOfScopeHandled &&
            x.ActorId == seed.AgentId));

        var attempt = await dbContext.ToolExecutionAttempts.AsNoTracking().SingleAsync(x => x.Id == payload.ToolExecutions[0].ExecutionId);
        Assert.Equal(seed.CompanyId, attempt.CompanyId);
        Assert.Equal(seed.AgentId, attempt.AgentId);
        Assert.Equal(seed.TaskId, attempt.TaskId);
        Assert.Equal("orch-test-correlation", attempt.CorrelationId);
        Assert.Equal(ToolExecutionStatus.Executed, attempt.Status);
        Assert.True(attempt.StartedUtc <= attempt.CompletedUtc);
        Assert.NotNull(attempt.CompletedUtc);
        Assert.Equal("inv-100", attempt.RequestPayload["invoiceId"]!.GetValue<string>());
        Assert.True(attempt.ResultPayload["success"]!.GetValue<bool>());
        Assert.Equal("allow", attempt.PolicyDecision["outcome"]!.GetValue<string>());
    }

    [Fact]
    public async Task Task_execute_endpoint_surfaces_safe_policy_denial_output_and_persists_audit()
    {
        var seed = await SeedTaskAsync();
        using var client = CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/tasks/{seed.TaskId}/execute", new
        {
            agentId = seed.AgentId,
            initiatingActorId = seed.UserId,
            initiatingActorType = "user",
            correlationId = "orch-denial-correlation",
            intent = "execute_task",
            toolInvocations = new[]
            {
                new
                {
                    toolName = "wire_transfer",
                    actionType = "execute",
                    scope = "payments",
                    requestPayload = new { paymentId = "pay-denied" },
                    sensitiveAction = false
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<OrchestrationResponse>();
        Assert.NotNull(payload);
        Assert.Equal("completed", payload!.Status);
        Assert.Contains("blocked by policy", payload.UserFacingOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("outside the agent's allowed tool scope", payload.UserFacingOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Single(payload.ToolExecutions);
        Assert.Equal("denied", payload.ToolExecutions[0].Status);
        Assert.Equal(0, _factory.ToolExecutor.ExecutionCount);

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var attempt = await dbContext.ToolExecutionAttempts.AsNoTracking().SingleAsync(x => x.Id == payload.ToolExecutions[0].ExecutionId);
        Assert.Equal(seed.CompanyId, attempt.CompanyId);
        Assert.Equal(seed.AgentId, attempt.AgentId);
        Assert.Equal(seed.TaskId, attempt.TaskId);
        Assert.Equal(ToolExecutionStatus.Denied, attempt.Status);
        Assert.Equal("deny", attempt.PolicyDecision["outcome"]!.GetValue<string>());
        Assert.Equal("This action was blocked by policy.", attempt.PolicyDecision["explanation"]!.GetValue<string>());
        Assert.Equal("This action was blocked by policy.", attempt.PolicyDecision["metadata"]!["safeUserFacingExplanation"]!.GetValue<string>());
        Assert.Equal("tool_not_permitted", attempt.ResultPayload["metadata"]!["primaryReasonCode"]!.GetValue<string>());

        var auditEvent = await dbContext.AuditEvents.AsNoTracking().SingleAsync(x =>
            x.CompanyId == seed.CompanyId &&
            x.Action == AuditEventActions.AgentToolExecutionDenied &&
            x.TargetId == payload.ToolExecutions[0].ExecutionId.ToString("N"));

        Assert.Equal(AuditEventOutcomes.Denied, auditEvent.Outcome);
        Assert.Equal(seed.AgentId, auditEvent.ActorId);
        Assert.Equal("tool_not_permitted", auditEvent.Metadata["primaryReasonCode"]);
        Assert.Equal("orch-denial-correlation", auditEvent.CorrelationId);

        var boundaryAudit = await dbContext.AuditEvents.AsNoTracking().SingleAsync(x =>
            x.CompanyId == seed.CompanyId &&
            x.Action == AuditEventActions.BoundaryEnforcement &&
            x.TargetId == payload.ToolExecutions[0].ExecutionId.ToString("N"));

        Assert.Equal(AuditBoundaryDecisionOutcomes.DeniedByPolicy, boundaryAudit.BoundaryDecisionOutcome);
        Assert.Equal(AuditReasonCodes.BoundaryDeniedByPolicy, boundaryAudit.BoundaryReasonCode);
        Assert.Equal("tool_not_permitted", boundaryAudit.Metadata["primaryReasonCode"]);
    }

    [Theory]
    [InlineData("Finance Manager", "Nora Ledger", "finance.payments", "legal.contracts", "Legal Lead")]
    [InlineData("Support Lead", "Ivy Resolve", "support.tickets", "finance.payments", "Finance Manager")]
    public async Task Responsibility_boundary_returns_normal_or_delegation_result_per_agent_role(
        string roleName,
        string displayName,
        string inScopeDomain,
        string outOfScopeDomain,
        string delegationTarget)
    {
        var inScopeSeed = await SeedTaskAsync(inScopeDomain, roleName, displayName, $"{inScopeDomain.Split('.')[0]}.*", delegationTarget);
        var outOfScopeSeed = await SeedTaskAsync(outOfScopeDomain, roleName, displayName, $"{inScopeDomain.Split('.')[0]}.*", delegationTarget);
        using var client = CreateAuthenticatedClient();

        var inScopeResponse = await client.PostAsJsonAsync($"/api/companies/{inScopeSeed.CompanyId}/tasks/{inScopeSeed.TaskId}/execute", new
        {
            agentId = inScopeSeed.AgentId,
            initiatingActorId = inScopeSeed.UserId,
            initiatingActorType = "user",
            correlationId = $"orch-in-scope-{roleName.Replace(" ", "-", StringComparison.OrdinalIgnoreCase)}",
            intent = "execute_task"
        });

        Assert.Equal(HttpStatusCode.OK, inScopeResponse.StatusCode);
        var inScopePayload = await inScopeResponse.Content.ReadFromJsonAsync<OrchestrationResponse>();
        Assert.NotNull(inScopePayload);
        Assert.Contains($"{displayName} completed task", inScopePayload!.UserFacingOutput);
        Assert.Null(inScopePayload.Action);

        var outOfScopeResponse = await client.PostAsJsonAsync($"/api/companies/{outOfScopeSeed.CompanyId}/tasks/{outOfScopeSeed.TaskId}/execute", new
        {
            agentId = outOfScopeSeed.AgentId,
            initiatingActorId = outOfScopeSeed.UserId,
            initiatingActorType = "user",
            correlationId = $"orch-out-of-scope-{roleName.Replace(" ", "-", StringComparison.OrdinalIgnoreCase)}",
            intent = "execute_task"
        });

        Assert.Equal(HttpStatusCode.OK, outOfScopeResponse.StatusCode);
        var outOfScopePayload = await outOfScopeResponse.Content.ReadFromJsonAsync<OrchestrationResponse>();
        Assert.NotNull(outOfScopePayload);
        Assert.Contains($"outside {displayName}'s responsibility boundary", outOfScopePayload!.UserFacingOutput);
        Assert.DoesNotContain("completed task", outOfScopePayload.UserFacingOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ResponsibilityPolicyDecisionTypes.Delegation, outOfScopePayload.Action!.ActionType);
        Assert.Equal(delegationTarget, outOfScopePayload.Action.TargetAgentRole);
        Assert.Equal(outOfScopeDomain, outOfScopePayload.Action.RequestedDomain);
        Assert.Equal(ResponsibilityPolicyRuleKinds.DefaultDeny, outOfScopePayload.Action.MatchedRule);

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(outOfScopeSeed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        Assert.False(await dbContext.AuditEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(x =>
                x.CompanyId == inScopeSeed.CompanyId &&
                x.Action == AuditEventActions.AgentResponsibilityOutOfScopeHandled &&
                x.ActorId == inScopeSeed.AgentId));

        var outOfScopeAudit = await dbContext.AuditEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(x =>
                x.CompanyId == outOfScopeSeed.CompanyId &&
                x.Action == AuditEventActions.AgentResponsibilityOutOfScopeHandled &&
                x.ActorId == outOfScopeSeed.AgentId);

        Assert.Equal(outOfScopeSeed.AgentId, outOfScopeAudit.ActorId);
        Assert.Equal(displayName, outOfScopeAudit.AgentName);
        Assert.Equal(roleName, outOfScopeAudit.AgentRole);
        Assert.Equal(AuditReasonCodes.BoundaryDelegateOutOfScope, outOfScopeAudit.BoundaryReasonCode);
        Assert.Equal(outOfScopeDomain, outOfScopeAudit.Metadata["requestedDomain"]);
        Assert.Equal(ResponsibilityPolicyRuleKinds.DefaultDeny, outOfScopeAudit.Metadata["matchedRule"]);
        Assert.Equal(delegationTarget, outOfScopeAudit.Metadata["delegationTarget"]);
        Assert.Equal(ResponsibilityPolicyDecisionTypes.Delegation, outOfScopeAudit.Metadata["delegationAction"]);
        Assert.NotNull(outOfScopeAudit.PayloadDiffJson);

        using var payloadDiff = JsonDocument.Parse(outOfScopeAudit.PayloadDiffJson!);
        var payloadRoot = payloadDiff.RootElement;
        Assert.Equal(outOfScopeSeed.AgentId.ToString(), payloadRoot.GetProperty("agentId").GetString());
        Assert.Equal(outOfScopeDomain, payloadRoot.GetProperty("requestedDomain").GetString());
        Assert.Equal(ResponsibilityPolicyRuleKinds.DefaultDeny, payloadRoot.GetProperty("matchedRule").GetString());
        Assert.Equal(delegationTarget, payloadRoot.GetProperty("delegationTarget").GetString());

        var boundaryAudit = await dbContext.AuditEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(x =>
                x.CompanyId == outOfScopeSeed.CompanyId &&
                x.Action == AuditEventActions.BoundaryEnforcement &&
                x.ActorId == outOfScopeSeed.AgentId);

        Assert.Equal(AuditBoundaryDecisionOutcomes.DelegatedOutOfScope, boundaryAudit.BoundaryDecisionOutcome);
        Assert.Equal(AuditReasonCodes.BoundaryDelegateOutOfScope, boundaryAudit.BoundaryReasonCode);
    }

    [Fact]
    public void Single_agent_entry_points_resolve_one_shared_orchestration_engine()
    {
        using var scope = _factory.Services.CreateScope();

        var services = scope.ServiceProvider.GetServices<ISingleAgentOrchestrationService>().ToList();

        var service = Assert.Single(services);
        Assert.IsType<SingleAgentOrchestrationService>(service);
        Assert.Same(service, scope.ServiceProvider.GetRequiredService<ISingleAgentOrchestrationService>());

        var constructor = Assert.Single(typeof(SingleAgentOrchestrationService).GetConstructors());
        Assert.DoesNotContain(constructor.GetParameters(), parameter => parameter.ParameterType == typeof(ICorrelationContextAccessor));
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, "founder");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, "founder@example.com");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, "Founder");
        return client;
    }

    private async Task<SeededTask> SeedTaskAsync(
        string requestedDomain = "finance.payments",
        string roleName = "Finance Manager",
        string displayName = "Nora Ledger",
        string allowedDomainPattern = "finance.*",
        string defaultDelegationTarget = "Legal Lead")
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

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
                displayName,
                roleName,
                "Finance",
                null,
                AgentSeniority.Senior,
                AgentStatus.Active,
                autonomyLevel: AgentAutonomyLevel.Level2,
                objectives: Payload(("primary", new JsonArray(JsonValue.Create("Protect cash flow")))),
                kpis: Payload(("targets", new JsonArray(JsonValue.Create("forecast_accuracy")))),
                tools: Payload(("allowed", new JsonArray(JsonValue.Create("erp")))),
                scopes: Payload(
                    ("execute", new JsonArray(JsonValue.Create("payments"))),
                    ("responsibilityPolicy", new JsonObject
                    {
                        ["allowedDomains"] = new JsonArray(JsonValue.Create(allowedDomainPattern)),
                        ["deniedDomains"] = new JsonArray(JsonValue.Create($"{allowedDomainPattern.TrimEnd('*')}high_risk")),
                        ["delegationTargets"] = new JsonObject
                        {
                            [$"{allowedDomainPattern.TrimEnd('*')}high_risk"] = new JsonObject
                            {
                                ["target"] = JsonValue.Create("Chief Financial Officer"),
                                ["actionType"] = JsonValue.Create("escalation")
                            },
                            ["default"] = JsonValue.Create(defaultDelegationTarget)
                        }
                    })),
                thresholds: Payload(("approval", new JsonObject { ["expenseUsd"] = 1000 })),
                escalationRules: Payload(("escalateTo", JsonValue.Create("founder"))),
                roleBrief: "Execute finance operations through approved tools."));
            dbContext.WorkTasks.Add(new WorkTask(
                taskId,
                companyId,
                "finance_execution",
                "Pay approved invoice",
                "Run the approved payment action with tenant-scoped context.",
                WorkTaskPriority.Normal,
                agentId,
                null,
                "user",
                userId,
                Payload(
                    ("invoiceId", JsonValue.Create("inv-100")),
                    ("requestedDomain", JsonValue.Create(requestedDomain)))));
            return Task.CompletedTask;
        });

        return new SeededTask(companyId, userId, agentId, taskId);
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

    private sealed record SeededTask(Guid CompanyId, Guid UserId, Guid AgentId, Guid TaskId);

    private sealed class OrchestrationResponse
    {
        public Guid OrchestrationId { get; set; }
        public Guid CompanyId { get; set; }
        public Guid TaskId { get; set; }
        public Guid AgentId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string UserFacingOutput { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public string? RationaleSummary { get; set; }
        public OrchestrationUserOutputResponse UserOutput { get; set; } = new();
        public OrchestrationTaskArtifactResponse? TaskArtifact { get; set; }
        public List<OrchestrationAuditArtifactResponse> AuditArtifacts { get; set; } = [];
        public List<OrchestrationToolExecutionReferenceResponse> ToolExecutionReferences { get; set; } = [];
        public OrchestrationFinalResultResponse? FinalResult { get; set; }
        public List<ToolInvocationResponse> ToolExecutions { get; set; } = [];
        public OrchestrationActionResponse? Action { get; set; }
    }

    private sealed class OrchestrationUserOutputResponse
    {
        public string DisplayMessage { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
    }

    private sealed class OrchestrationTaskArtifactResponse
    {
        public Guid TaskId { get; set; }
        public string Status { get; set; } = string.Empty;
        public JsonElement? OutputPayload { get; set; }
        public string CorrelationId { get; set; } = string.Empty;
    }

    private sealed class OrchestrationAuditArtifactResponse
    {
        public string Action { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
    }

    private sealed class OrchestrationToolExecutionReferenceResponse
    {
        public Guid ExecutionId { get; set; }
        public string ToolName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
    }

    private sealed class OrchestrationActionResponse
    {
        public string ActionType { get; set; } = string.Empty;
        public string? TargetAgentRole { get; set; }
        public Guid? TargetAgentId { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string RequestedDomain { get; set; } = string.Empty;
        public string MatchedRule { get; set; } = string.Empty;
    }

    private sealed class OrchestrationFinalResultResponse
    {
        public OrchestrationUserOutputResponse UserOutput { get; set; } = new();
        public OrchestrationTaskArtifactResponse? TaskArtifact { get; set; }
        public List<OrchestrationAuditArtifactResponse> AuditArtifacts { get; set; } = [];
        public string? RationaleSummary { get; set; }
        public string CorrelationId { get; set; } = string.Empty;
    }

    private sealed class ToolInvocationResponse
    {
        public Guid ExecutionId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public JsonElement? ResultPayload { get; set; }
    }
}
