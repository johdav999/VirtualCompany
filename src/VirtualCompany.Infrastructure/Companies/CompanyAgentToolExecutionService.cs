using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.Json;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyAgentToolExecutionService : IAgentToolExecutionService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyMembershipContextResolver _companyMembershipContextResolver;
    private readonly IAgentRuntimeProfileResolver _agentRuntimeProfileResolver;
    private readonly IPolicyGuardrailEngine _policyGuardrailEngine;
    private readonly ICompanyToolExecutor _companyToolExecutor;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly ICorrelationContextAccessor _correlationContextAccessor;

    public CompanyAgentToolExecutionService(
        VirtualCompanyDbContext dbContext,
        ICompanyMembershipContextResolver companyMembershipContextResolver,
        IAgentRuntimeProfileResolver agentRuntimeProfileResolver,
        IPolicyGuardrailEngine policyGuardrailEngine,
        ICompanyToolExecutor companyToolExecutor,
        IAuditEventWriter auditEventWriter,
        ICorrelationContextAccessor correlationContextAccessor)
    {
        _dbContext = dbContext;
        _companyMembershipContextResolver = companyMembershipContextResolver;
        _agentRuntimeProfileResolver = agentRuntimeProfileResolver;
        _policyGuardrailEngine = policyGuardrailEngine;
        _companyToolExecutor = companyToolExecutor;
        _auditEventWriter = auditEventWriter;
        _correlationContextAccessor = correlationContextAccessor;
    }

    public async Task<ExecuteAgentToolResultDto> ExecuteAsync(
        Guid companyId,
        Guid agentId,
        ExecuteAgentToolCommand command,
        CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        ExecuteAgentToolCommandValidator.ValidateAndThrow(command);
        var correlationId = CreateCorrelationId(command.CorrelationId);
        var startedAtUtc = DateTime.UtcNow;
        var executionId = Guid.NewGuid();
        var actionType = ToolActionTypeValues.Parse(command.ActionType);
        var actionTypeValue = actionType.ToStorageValue();

        var attempt = new ToolExecutionAttempt(
            executionId,
            companyId,
            agentId,
            command.ToolName,
            actionType,
            command.Scope,
            command.RequestPayload,
            command.TaskId,
            command.WorkflowInstanceId,
            correlationId,
            startedAtUtc);

        var runtimeProfile = await _agentRuntimeProfileResolver.GetCurrentProfileAsync(companyId, agentId, cancellationToken);
        var policyRequest = new PolicyEvaluationRequest(
            companyId,
            agentId,
            runtimeProfile.CompanyId,
            runtimeProfile.Status,
            runtimeProfile.AutonomyLevel,
            runtimeProfile.CanReceiveAssignments,
            CloneNodes(runtimeProfile.ToolPermissions),
            CloneNodes(runtimeProfile.DataScopes),
            CloneNodes(runtimeProfile.ApprovalThresholds),
            CloneNodes(runtimeProfile.EscalationRules),
            command.ToolName,
            actionType,
            command.Scope,
            CloneNodes(command.RequestPayload),
            command.ThresholdCategory,
            command.ThresholdKey,
            command.ThresholdValue,
            command.SensitiveAction,
            executionId,
            correlationId);

        var decision = _policyGuardrailEngine.Evaluate(policyRequest);
        _dbContext.ToolExecutionAttempts.Add(attempt);

        await WriteBoundaryEnforcementAuditAsync(
            companyId,
            agentId,
            command,
            decision,
            runtimeProfile,
            attempt.Id,
            correlationId,
            cancellationToken);

        var serializedDecision = SerializeDecision(decision);
        if (string.Equals(decision.Outcome, PolicyDecisionOutcomeValues.Deny, StringComparison.OrdinalIgnoreCase))
        {
            var callerMessage = BuildCallerMessage(decision);
            var denial = ToolExecutionDenialDto.FromDecision(decision, callerMessage);
            var structuredResult = ToolExecutionResult.Failed(
                command.ToolName,
                actionType,
                ToolExecutionStatus.Denied.ToStorageValue(),
                "policy_denied",
                callerMessage,
                metadata: BuildExecutionResultMetadata(command, decision, correlationId, executionId, userFacingMessage: callerMessage));
            attempt.MarkDenied(serializedDecision, structuredResult.ToStructuredPayload(), DateTime.UtcNow);

            await _auditEventWriter.WriteAsync(
                new AuditEventWriteRequest(
                    companyId,
                    AuditActorTypes.Agent,
                    agentId,
                    AuditEventActions.AgentToolExecutionDenied,
                    AuditTargetTypes.AgentToolExecution,
                    attempt.Id.ToString("N"),
                    AuditEventOutcomes.Denied,
                    DataSources: ["agent_execution", "policy_guardrail", "http_request"],
                    CorrelationId: correlationId,
                    RationaleSummary: BuildAuditRationaleSummary(decision),
                    Metadata: BuildAuditMetadata(command, decision)),
                cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return new ExecuteAgentToolResultDto(
                attempt.Id,
                attempt.Status.ToStorageValue(),
                null,
                decision,
                structuredResult.ToStructuredPayload(),
                callerMessage,
                Denial: denial);
        }

        if (string.Equals(decision.Outcome, PolicyDecisionOutcomeValues.RequireApproval, StringComparison.OrdinalIgnoreCase))
        {
            var approvalRequest = new ApprovalRequest(
                Guid.NewGuid(),
                companyId,
                agentId,
                attempt.Id,
                membership.UserId,
                command.ToolName,
                actionType,
                TryGetNonEmptyString(decision.Metadata, "approvalTarget"),
                BuildThresholdContext(command, decision),
                serializedDecision,
                null);
            var decisionChain = BuildApprovalDecisionChain(command, decision, approvalRequest.Id, attempt.Id, membership.UserId);
            approvalRequest.SetDecisionChain(decisionChain);
            var structuredResult = ToolExecutionResult.Failed(
                command.ToolName,
                actionType,
                ToolExecutionStatus.AwaitingApproval.ToStorageValue(),
                "approval_required",
                BuildCallerMessage(decision),
                CloneNodes(decisionChain),
                BuildExecutionResultMetadata(command, decision, correlationId, executionId, approvalRequest.Id));

            _dbContext.ApprovalRequests.Add(approvalRequest);
            attempt.MarkAwaitingApproval(approvalRequest.Id, serializedDecision, structuredResult.ToStructuredPayload(), DateTime.UtcNow);

            await _auditEventWriter.WriteAsync(
                new AuditEventWriteRequest(
                    companyId,
                    AuditActorTypes.User,
                    membership.UserId,
                    AuditEventActions.AgentToolExecutionApprovalRequested,
                    AuditTargetTypes.ApprovalRequest,
                    approvalRequest.Id.ToString("N"),
                    AuditEventOutcomes.Pending,
                    DataSources: ["agent_execution", "policy_guardrail", "http_request"],
                    CorrelationId: correlationId,
                    RationaleSummary: decision.Explanation,
                    Metadata: BuildAuditMetadata(command, decision, approvalRequest.Id)),
                cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return new ExecuteAgentToolResultDto(
                attempt.Id,
                attempt.Status.ToStorageValue(),
                approvalRequest.Id,
                decision,
                structuredResult.ToStructuredPayload(),
                structuredResult.Summary,
                CloneNodes(decisionChain));
        }

        try
        {
            var result = await _companyToolExecutor.ExecuteAsync(
            new ToolExecutionRequest(
                companyId,
                agentId,
                command.ToolName,
                actionType,
                command.Scope,
                CloneNodes(command.RequestPayload),
                command.TaskId,
                command.WorkflowInstanceId,
                correlationId,
                executionId),
            cancellationToken);
            result = NormalizeStructuredResult(result, command);

            if (string.Equals(result.Status, ToolExecutionStatus.Denied.ToStorageValue(), StringComparison.OrdinalIgnoreCase))
            {
                attempt.MarkDenied(serializedDecision, result.ToStructuredPayload(), DateTime.UtcNow);

                await _auditEventWriter.WriteAsync(
                new AuditEventWriteRequest(
                    companyId,
                    AuditActorTypes.Agent,
                    agentId,
                    AuditEventActions.AgentToolExecutionDenied,
                    AuditTargetTypes.AgentToolExecution,
                    attempt.Id.ToString("N"),
                    AuditEventOutcomes.Denied,
                    DataSources: ["agent_execution", "policy_guardrail", "tool_registry"],
                    CorrelationId: correlationId,
                    RationaleSummary: result.Summary,
                    Metadata: BuildAuditMetadata(command, decision)),
                cancellationToken);

                await _dbContext.SaveChangesAsync(cancellationToken);

                return new ExecuteAgentToolResultDto(
                attempt.Id,
                attempt.Status.ToStorageValue(),
                null,
                decision,
                result.ToStructuredPayload(),
                result.Summary);
            }

            attempt.MarkExecuted(serializedDecision, result.ToStructuredPayload(), DateTime.UtcNow);

            await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                companyId,
                AuditActorTypes.User,
                membership.UserId,
                AuditEventActions.AgentToolExecutionExecuted,
                AuditTargetTypes.AgentToolExecution,
                attempt.Id.ToString("N"),
                AuditEventOutcomes.Succeeded,
                DataSources: ["agent_execution", "policy_guardrail", "http_request"],
                CorrelationId: correlationId,
                RationaleSummary: result.Summary,
                Metadata: BuildAuditMetadata(command, decision)),
            cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return new ExecuteAgentToolResultDto(
            attempt.Id,
            attempt.Status.ToStorageValue(),
            null,
            decision,
            result.ToStructuredPayload(),
            result.Summary);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var failedResult = ToolExecutionResult.Failed(
                command.ToolName,
                actionType,
                ToolExecutionStatus.Failed.ToStorageValue(),
                "tool_execution_failed",
                "The tool execution failed before a structured successful response was produced.",
                metadata: BuildExecutionResultMetadata(command, decision, correlationId, executionId, exceptionType: ex.GetType().Name));
            attempt.MarkFailed(serializedDecision, failedResult.ToStructuredPayload(), DateTime.UtcNow);

            await _auditEventWriter.WriteAsync(
                new AuditEventWriteRequest(
                    companyId,
                    AuditActorTypes.User,
                    membership.UserId,
                    AuditEventActions.AgentToolExecutionExecuted,
                    AuditTargetTypes.AgentToolExecution,
                    attempt.Id.ToString("N"),
                    AuditEventOutcomes.Failed,
                    DataSources: ["agent_execution", "policy_guardrail", "http_request"],
                    CorrelationId: correlationId,
                    RationaleSummary: failedResult.Summary,
                    Metadata: BuildAuditMetadata(command, decision)),
                cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return new ExecuteAgentToolResultDto(
                attempt.Id,
                attempt.Status.ToStorageValue(),
                null,
                decision,
                failedResult.ToStructuredPayload(),
                failedResult.Summary);
        }
    }

    private async Task<VirtualCompany.Application.Auth.ResolvedCompanyMembershipContext> RequireMembershipAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var membership = await _companyMembershipContextResolver.ResolveAsync(companyId, cancellationToken);
        if (membership is null)
        {
            throw new UnauthorizedAccessException("The current user does not have an active membership in the requested company.");
        }

        return membership;
    }

    private Task WriteBoundaryEnforcementAuditAsync(
        Guid companyId,
        Guid agentId,
        ExecuteAgentToolCommand command,
        ToolExecutionDecisionDto decision,
        AgentRuntimeProfileDto runtimeProfile,
        Guid executionId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var boundaryDecisionOutcome = ResolveBoundaryDecisionOutcome(decision);

        return _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                companyId,
                AuditActorTypes.Agent,
                agentId,
                AuditEventActions.BoundaryEnforcement,
                AuditTargetTypes.AgentToolExecution,
                executionId.ToString("N"),
                ResolveBoundaryAuditOutcome(decision),
                DataSources: ["agent_execution", "policy_guardrail"],
                CorrelationId: correlationId,
                RationaleSummary: BuildAuditRationaleSummary(decision),
                Metadata: BuildAuditMetadata(command, decision),
                AgentName: runtimeProfile.DisplayName,
                AgentRole: runtimeProfile.RoleName,
                ResponsibilityDomain: runtimeProfile.Department,
                PromptProfileVersion: BuildPromptProfileVersion(runtimeProfile),
                BoundaryDecisionOutcome: boundaryDecisionOutcome,
                BoundaryReasonCode: string.Equals(boundaryDecisionOutcome, AuditBoundaryDecisionOutcomes.DeniedByPolicy, StringComparison.OrdinalIgnoreCase)
                    ? AuditReasonCodes.BoundaryDeniedByPolicy
                    : null),
            cancellationToken);
    }

    private static string BuildPromptProfileVersion(AgentRuntimeProfileDto agent) =>
        agent.UpdatedUtc.ToUniversalTime().ToString("yyyyMMddHHmmss");

    private static Dictionary<string, string?> BuildAuditMetadata(
        ExecuteAgentToolCommand command,
        ToolExecutionDecisionDto decision,
        Guid? approvalRequestId = null)
    {
        var metadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["toolName"] = command.ToolName,
            ["actionType"] = ToolActionTypeValues.Parse(command.ActionType).ToStorageValue(),
            ["scope"] = command.Scope,
            ["policyOutcome"] = decision.Outcome,
            ["approvalRequired"] = decision.ApprovalRequired ? "true" : "false",
            ["autonomyLevel"] = decision.EvaluatedAutonomyLevel,
            ["reasonCodes"] = string.Join(",", decision.ReasonCodes),
            ["policyDecisionSchemaVersion"] = decision.SchemaVersion,
            ["policyEvaluationVersion"] = decision.Audit?.PolicyVersion,
            ["policyCorrelationId"] = decision.Audit?.CorrelationId,
            ["executionId"] = decision.Audit is null ? null : decision.Audit.ExecutionId.ToString("N"),
            ["approvalRequirementType"] = decision.ApprovalRequirement?.RequirementType,
            ["executionState"] = TryGetNonEmptyString(decision.Metadata, "executionState"),
            ["primaryReasonCode"] = GetPrimaryReasonCode(decision)
        };

        if (approvalRequestId.HasValue)
        {
            metadata["approvalRequestId"] = approvalRequestId.Value.ToString("N");
        }

        if (decision.ThresholdEvaluations is not null)
        {
            metadata["thresholdEvaluationCount"] = decision.ThresholdEvaluations.Count.ToString(CultureInfo.InvariantCulture);
        }

        return metadata;
    }

    private static Dictionary<string, JsonNode?> BuildThresholdContext(
        ExecuteAgentToolCommand command,
        ToolExecutionDecisionDto decision)
    {
        var thresholdContext = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(command.ThresholdCategory))
        {
            thresholdContext["thresholdCategory"] = JsonValue.Create(command.ThresholdCategory.Trim());
        }

        if (!string.IsNullOrWhiteSpace(command.ThresholdKey))
        {
            thresholdContext["thresholdKey"] = JsonValue.Create(command.ThresholdKey.Trim());
        }

        if (command.ThresholdValue.HasValue)
        {
            thresholdContext["thresholdValue"] = JsonValue.Create(command.ThresholdValue.Value);
        }

        if (decision.Metadata.TryGetValue("configuredThreshold", out var configuredThreshold))
        {
            thresholdContext["configuredThreshold"] = configuredThreshold?.DeepClone();
        }

        if (decision.Metadata.TryGetValue("approvalTarget", out var approvalTarget))
        {
            thresholdContext["approvalTarget"] = approvalTarget?.DeepClone();
        }

        thresholdContext["toolName"] = JsonValue.Create(command.ToolName);
        thresholdContext["actionType"] = JsonValue.Create(ToolActionTypeValues.Parse(command.ActionType).ToStorageValue());

        if (!string.IsNullOrWhiteSpace(command.Scope))
        {
            thresholdContext["scope"] = JsonValue.Create(command.Scope.Trim());
        }

        thresholdContext["sensitiveAction"] = JsonValue.Create(command.SensitiveAction);

        if (decision.Metadata.TryGetValue("thresholdEvaluation", out var thresholdEvaluation))
        {
            thresholdContext["thresholdEvaluation"] = thresholdEvaluation?.DeepClone();
        }

        if (decision.Metadata.TryGetValue("executionState", out var executionState))
        {
            thresholdContext["executionState"] = executionState?.DeepClone();
        }

        thresholdContext["schemaVersion"] = JsonValue.Create(decision.SchemaVersion);

        if (decision.ApprovalRequirement is not null)
        {
            thresholdContext["approvalRequirement"] = JsonSerializer.SerializeToNode(decision.ApprovalRequirement);
        }

        if (decision.ThresholdEvaluations is not null && decision.ThresholdEvaluations.Count > 0)
        {
            thresholdContext["thresholdEvaluations"] = JsonSerializer.SerializeToNode(decision.ThresholdEvaluations);
        }

        return thresholdContext;
    }

    private static Dictionary<string, JsonNode?> BuildApprovalDecisionChain(
        ExecuteAgentToolCommand command,
        ToolExecutionDecisionDto decision,
        Guid approvalRequestId,
        Guid executionId,
        Guid requestedByUserId)
    {
        var steps = new JsonArray
        {
            new JsonObject
            {
                ["step"] = "policy_evaluation",
                ["outcome"] = decision.Outcome,
                ["reasonCodes"] = JsonSerializer.SerializeToNode(decision.ReasonCodes),
                ["evaluatedAtUtc"] = decision.Audit is null
                    ? JsonValue.Create(DateTime.UtcNow)
                    : JsonValue.Create(decision.Audit.EvaluatedAtUtc),
                ["policyVersion"] = decision.Audit?.PolicyVersion,
                ["correlationId"] = decision.Audit?.CorrelationId
            },
            new JsonObject
            {
                ["step"] = "approval_request_created",
                ["outcome"] = "pending",
                ["approvalRequestId"] = approvalRequestId,
                ["requestedByUserId"] = requestedByUserId,
                ["toolName"] = command.ToolName,
                ["actionType"] = ToolActionTypeValues.Parse(command.ActionType).ToStorageValue(),
                ["scope"] = command.Scope,
                ["approvalTarget"] = TryGetNonEmptyString(decision.Metadata, "approvalTarget")
            }
        };

        var chain = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["schemaVersion"] = JsonValue.Create("2026-04-12"),
            ["approvalRequestId"] = JsonValue.Create(approvalRequestId),
            ["executionId"] = JsonValue.Create(executionId),
            ["status"] = JsonValue.Create("pending"),
            ["currentStep"] = JsonValue.Create("approval_request_created"),
            ["steps"] = steps
        };

        if (decision.ApprovalRequirement is not null)
        {
            chain["approvalRequirement"] = JsonSerializer.SerializeToNode(decision.ApprovalRequirement);
        }

        if (decision.ThresholdEvaluations is not null && decision.ThresholdEvaluations.Count > 0)
        {
            chain["thresholdEvaluations"] = JsonSerializer.SerializeToNode(decision.ThresholdEvaluations);
        }

        if (decision.Metadata.TryGetValue("approvalRequirementPolicy", out var approvalRequirementPolicy))
        {
            chain["approvalRequirementPolicy"] = approvalRequirementPolicy?.DeepClone();
        }

        return chain;
    }

    private static Dictionary<string, JsonNode?> SerializeDecision(ToolExecutionDecisionDto decision) =>
        ToolExecutionPolicyDecisionJsonSerializer.Serialize(decision);

    private static ToolExecutionResult NormalizeStructuredResult(ToolExecutionResult result, ExecuteAgentToolCommand command)
    {
        var actionType = ToolActionTypeValues.Parse(command.ActionType);

        if (result.Payload is null)
        {
            return ToolExecutionResult.Failed(
                command.ToolName,
                actionType,
                ToolExecutionStatus.Failed.ToStorageValue(),
                "unstructured_tool_response",
                "The tool executor returned no structured payload.");
        }

        ToolActionTypeValues.EnsureSupported(result.ActionType, nameof(result.ActionType));
        return result with
        {
            ToolName = string.IsNullOrWhiteSpace(result.ToolName) ? command.ToolName : result.ToolName.Trim(),
            Status = string.IsNullOrWhiteSpace(result.Status) ? ToolExecutionStatus.Failed.ToStorageValue() : result.Status.Trim(),
            Payload = CloneNodes(result.Payload),
            Metadata = CloneNodes(result.Metadata)
        };
    }

    private static Dictionary<string, JsonNode?> BuildExecutionResultMetadata(
        ExecuteAgentToolCommand command,
        ToolExecutionDecisionDto decision,
        string correlationId,
        Guid executionId,
        Guid? approvalRequestId = null,
        string? userFacingMessage = null,
        string? exceptionType = null)
    {
        var metadata = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["correlationId"] = JsonValue.Create(correlationId),
            ["executionId"] = JsonValue.Create(executionId),
            ["primaryReasonCode"] = string.IsNullOrWhiteSpace(GetPrimaryReasonCode(decision)) ? null : JsonValue.Create(GetPrimaryReasonCode(decision)),
            ["taskId"] = command.TaskId.HasValue ? JsonValue.Create(command.TaskId.Value) : null,
            ["workflowInstanceId"] = command.WorkflowInstanceId.HasValue ? JsonValue.Create(command.WorkflowInstanceId.Value) : null,
            ["policyOutcome"] = JsonValue.Create(decision.Outcome),
            ["policyDecisionSchemaVersion"] = JsonValue.Create(decision.SchemaVersion),
            ["approvalRequestId"] = approvalRequestId.HasValue ? JsonValue.Create(approvalRequestId.Value) : null,
            ["exceptionType"] = string.IsNullOrWhiteSpace(exceptionType) ? null : JsonValue.Create(exceptionType)
        };
        metadata["userFacingMessage"] = string.IsNullOrWhiteSpace(userFacingMessage) ? null : JsonValue.Create(userFacingMessage.Trim());

        return metadata;
    }

    private static JsonObject ToJsonObject(IReadOnlyDictionary<string, JsonNode?> nodes)
    {
        var jsonObject = new JsonObject();
        foreach (var (key, value) in nodes)
        {
            jsonObject[key] = value?.DeepClone();
        }

        return jsonObject;
    }

    private static string BuildAuditRationaleSummary(ToolExecutionDecisionDto decision)
    {
        var primaryReasonCode = GetPrimaryReasonCode(decision);
        return string.IsNullOrWhiteSpace(primaryReasonCode)
            ? "Action blocked by policy before execution."
            : $"Action blocked by policy before execution ({primaryReasonCode}).";
    }

    private static string BuildCallerMessage(ToolExecutionDecisionDto decision)
    {
        if (!string.Equals(decision.Outcome, PolicyDecisionOutcomeValues.Deny, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(decision.Outcome, PolicyDecisionOutcomeValues.RequireApproval, StringComparison.OrdinalIgnoreCase))
            {
                return GetPrimaryReasonCode(decision) switch
                {
                    PolicyDecisionReasonCodes.SensitiveActionRequiresApproval or PolicyDecisionReasonCodes.ThresholdExceededRequiresApproval
                        => "This sensitive action is pending approval and was not executed.",
                    _ => "This action is pending approval and was not executed."
                };
            }

            return decision.Explanation;
        }

        return GetPrimaryReasonCode(decision) switch
        {
            PolicyDecisionReasonCodes.ToolNotConfigured
                => "This action was blocked by policy because the requested tool could not be identified.",
            PolicyDecisionReasonCodes.ToolExplicitlyDenied or PolicyDecisionReasonCodes.ToolNotPermitted
                => "This action was blocked by policy because the agent is not permitted to use that tool.",
            PolicyDecisionReasonCodes.ScopeNotPermitted or PolicyDecisionReasonCodes.ScopeContextMissing or PolicyDecisionReasonCodes.InvalidCompanyContext or PolicyDecisionReasonCodes.TenantScopeViolation or PolicyDecisionReasonCodes.DataScopeViolation
                => "This action was blocked by policy because the request is outside the agent's allowed company scope.",
            PolicyDecisionReasonCodes.AgentStatusDisallowsExecution
                => "This action was blocked by policy because the agent is not currently allowed to execute tools.",
            PolicyDecisionReasonCodes.AutonomyLevelBlocksAction
                => "This action was blocked by policy because the agent autonomy level does not permit it.",
            PolicyDecisionReasonCodes.InvalidActionType
                => "This action was blocked by policy because the requested action type is not supported.",
            PolicyDecisionReasonCodes.MissingPolicyConfiguration or
            PolicyDecisionReasonCodes.InvalidPolicyConfiguration or
            PolicyDecisionReasonCodes.AmbiguousPolicyConfiguration or
            PolicyDecisionReasonCodes.ThresholdConfigurationMissing or
            PolicyDecisionReasonCodes.ApprovalRouteMissing
                => "This action was blocked by policy because the required guardrail configuration could not be verified.",
            _ => "This action was blocked by policy."
        };
    }

    private static string ResolveBoundaryAuditOutcome(ToolExecutionDecisionDto decision) =>
        decision.Outcome switch
        {
            PolicyDecisionOutcomeValues.Deny => AuditEventOutcomes.Denied,
            PolicyDecisionOutcomeValues.RequireApproval => AuditEventOutcomes.Pending,
            _ => AuditEventOutcomes.Succeeded
        };

    private static string ResolveBoundaryDecisionOutcome(ToolExecutionDecisionDto decision) =>
        string.Equals(decision.Outcome, PolicyDecisionOutcomeValues.Deny, StringComparison.OrdinalIgnoreCase)
            ? AuditBoundaryDecisionOutcomes.DeniedByPolicy
            : AuditBoundaryDecisionOutcomes.InScope;

    private static string? GetPrimaryReasonCode(ToolExecutionDecisionDto decision) =>
        decision.ReasonCodes.FirstOrDefault(static code => !string.IsNullOrWhiteSpace(code));

    private static string? TryGetNonEmptyString(IReadOnlyDictionary<string, JsonNode?> values, string key)
    {
        if (!values.TryGetValue(key, out var node) || node is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var text))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static Dictionary<string, JsonNode?> CloneNodes(IReadOnlyDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);

    private string CreateCorrelationId(string? requestedCorrelationId)
    {
        if (!string.IsNullOrWhiteSpace(requestedCorrelationId))
        {
            return requestedCorrelationId.Trim();
        }

        return string.IsNullOrWhiteSpace(_correlationContextAccessor.CorrelationId)
            ? System.Diagnostics.Activity.Current?.Id ?? Guid.NewGuid().ToString("N")
            : _correlationContextAccessor.CorrelationId!;
    }
}
