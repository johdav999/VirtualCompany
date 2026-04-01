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
        var correlationId = CreateCorrelationId();
        var executionId = Guid.NewGuid();

        var attempt = new ToolExecutionAttempt(
            executionId,
            companyId,
            agentId,
            command.ToolName,
            ToolActionTypeValues.Parse(command.ActionType),
            command.Scope,
            command.RequestPayload);

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
            command.ActionType,
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

        var serializedDecision = SerializeDecision(decision);
        if (string.Equals(decision.Outcome, PolicyDecisionOutcomeValues.Deny, StringComparison.OrdinalIgnoreCase))
        {
            attempt.MarkDenied(serializedDecision);

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
                null,
                BuildCallerMessage(decision));
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
                ToolActionTypeValues.Parse(command.ActionType),
                TryGetNonEmptyString(decision.Metadata, "approvalTarget"),
                BuildThresholdContext(command, decision),
                serializedDecision);

            _dbContext.ApprovalRequests.Add(approvalRequest);
            attempt.MarkAwaitingApproval(approvalRequest.Id, serializedDecision);

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
                null,
                BuildCallerMessage(decision));
        }

        var result = await _companyToolExecutor.ExecuteAsync(
            new ToolExecutionRequest(
                companyId,
                agentId,
                command.ToolName,
                command.ActionType,
                command.Scope,
                CloneNodes(command.RequestPayload)),
            cancellationToken);

        attempt.MarkExecuted(serializedDecision, result.Payload);

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
            CloneNodes(result.Payload),
            result.Summary);
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

    private static Dictionary<string, string?> BuildAuditMetadata(
        ExecuteAgentToolCommand command,
        ToolExecutionDecisionDto decision,
        Guid? approvalRequestId = null)
    {
        var metadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["toolName"] = command.ToolName,
            ["actionType"] = command.ActionType,
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
        thresholdContext["actionType"] = JsonValue.Create(command.ActionType);

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

    private static Dictionary<string, JsonNode?> SerializeDecision(ToolExecutionDecisionDto decision) =>
        ToolExecutionPolicyDecisionJsonSerializer.Serialize(decision);

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
            PolicyDecisionReasonCodes.MissingPolicyConfiguration or PolicyDecisionReasonCodes.InvalidPolicyConfiguration or PolicyDecisionReasonCodes.AmbiguousPolicyConfiguration or PolicyDecisionReasonCodes.ThresholdConfigurationMissing or PolicyDecisionReasonCodes.ApprovalRouteMissing
                => "This action was blocked by policy because the required guardrail configuration could not be verified.",
            _ => "This action was blocked by policy."
        };
    }

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

    private string CreateCorrelationId() =>
        string.IsNullOrWhiteSpace(_correlationContextAccessor.CorrelationId)
            ? Activity.Current?.Id ?? Guid.NewGuid().ToString("N")
            : _correlationContextAccessor.CorrelationId!;
}
