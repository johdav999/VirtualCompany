using System.Text.Json.Nodes;
using System.Text.Json;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Shared;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class PolicyGuardrailEngine : IPolicyGuardrailEngine
{
    private const string SafeDeniedExplanation = "This action was blocked by policy.";
    private readonly ICompanyToolRegistry _toolRegistry;

    public PolicyGuardrailEngine(ICompanyToolRegistry toolRegistry)
    {
        _toolRegistry = toolRegistry;
    }

    public ToolExecutionDecisionDto Evaluate(PolicyEvaluationRequest request)
    {
        var metadata = CreateBaseMetadata(request);
        var thresholdEvaluations = new List<PolicyDecisionThresholdEvaluationDto>();
        PolicyDecisionApprovalRequirementDto? approvalRequirement = null;

        if (request.CompanyId == Guid.Empty ||
            request.AgentId == Guid.Empty ||
            request.AgentCompanyId == Guid.Empty ||
            request.AgentCompanyId != request.CompanyId)
        {
            return Deny(
                request,
                [PolicyDecisionReasonCodes.InvalidCompanyContext, PolicyDecisionReasonCodes.TenantScopeViolation],
                "Tenant context is missing or inconsistent for this execution request.",
                metadata,
                thresholdEvaluations);
        }

        metadata["agentStatus"] = JsonValue.Create(request.AgentStatus);
        metadata["canReceiveAssignments"] = JsonValue.Create(request.CanReceiveAssignments);
        if (!AgentStatusValues.TryParse(request.AgentStatus, out var status) ||
            status is AgentStatus.Paused or AgentStatus.Restricted or AgentStatus.Archived ||
            !request.CanReceiveAssignments)
        {
            return Deny(
                request,
                [PolicyDecisionReasonCodes.AgentStatusDisallowsExecution],
                "The agent status does not permit tool execution.",
                metadata,
                thresholdEvaluations);
        }

        if (!TryNormalizeActionType(request.ActionType, out var actionType, out var normalizedAction))
        {
            return Deny(
                request,
                normalizedAction,
                [PolicyDecisionReasonCodes.InvalidActionType],
                "The requested action type is missing or invalid.",
                metadata,
                thresholdEvaluations);
        }

        if (!AgentAutonomyLevelValues.TryParse(request.EvaluatedAutonomyLevel, out var autonomyLevel))
        {
            return Deny(
                request,
                normalizedAction,
                [PolicyDecisionReasonCodes.InvalidAutonomyLevel],
                "The agent autonomy configuration is missing or invalid.",
                metadata,
                thresholdEvaluations);
        }

        // Guardrail evaluation stays fail-closed when any required policy section is absent.
        if (request.ToolPermissions is null ||
            request.DataScopes is null ||
            request.ApprovalThresholds is null ||
            request.EscalationRules is null)
        {
            metadata["policyConfigurationState"] = JsonValue.Create("missing");
            return Deny(
                request,
                normalizedAction,
                [PolicyDecisionReasonCodes.MissingPolicyConfiguration],
                "Required policy configuration is missing, so tool execution is denied by default.",
                metadata,
                thresholdEvaluations);
        }

        if (string.IsNullOrWhiteSpace(request.ToolName))
        {
            metadata["toolPolicyState"] = JsonValue.Create("invalid");
            return Deny(
                request,
                normalizedAction,
                [PolicyDecisionReasonCodes.ToolNotConfigured],
                "Tool identity is missing or invalid, so policy cannot authorize execution.",
                metadata,
                thresholdEvaluations);
        }

        var toolName = request.ToolName.Trim();
        var normalizedScope = string.IsNullOrWhiteSpace(request.Scope) ? null : request.Scope.Trim();
        // The policy layer evaluates the agent's configured authority. The executor remains the
        // fail-closed enforcement boundary for trusted registry membership, action, scope, and schema.
        TrustedToolRegistration? registeredTool = null;
        if (_toolRegistry.TryGetTool(toolName, out var resolvedRegisteredTool))
        {
            registeredTool = resolvedRegisteredTool;
            metadata["registryPolicyState"] = JsonValue.Create("registered");
            metadata["registeredToolVersion"] = JsonValue.Create(registeredTool.Version);
            metadata["registeredToolActions"] = ToJsonArray(registeredTool.SupportedActions.Select(static action => action.ToStorageValue()));
            metadata["registryActionAllowed"] = JsonValue.Create(registeredTool.Supports(actionType, normalizedScope));
        }
        else
        {
            metadata["registryPolicyState"] = JsonValue.Create("unregistered");
            metadata["registryActionAllowed"] = JsonValue.Create(false);
        }
        if (!TryGetIdentifierSet(request.ToolPermissions, "allowed", out var allowedTools, out var allowedToolsExists) ||
            !TryGetIdentifierSet(request.ToolPermissions, "denied", out var deniedTools, out _) ||
            !TryGetIdentifierSet(request.ToolPermissions, "actions", out var allowedActions, out var allowedActionsExists) ||
            !TryGetIdentifierSet(request.ToolPermissions, "deniedActions", out var deniedActions, out _))
        {
            metadata["toolPolicyState"] = JsonValue.Create("invalid");
            return Deny(
                request,
                normalizedAction,
                [PolicyDecisionReasonCodes.InvalidPolicyConfiguration],
                "Tool permissions are invalid, so execution is denied by default.",
                metadata,
                thresholdEvaluations);
        }

        if (!allowedToolsExists || allowedTools.Count == 0)
        {
            metadata["toolPolicyState"] = JsonValue.Create("missing");
            return Deny(
                request,
                normalizedAction,
                [PolicyDecisionReasonCodes.MissingPolicyConfiguration],
                "Tool permissions do not define an allowed set for this agent.",
                metadata,
                thresholdEvaluations);
        }

        if (allowedTools.Overlaps(deniedTools))
        {
            metadata["toolPolicyState"] = JsonValue.Create("ambiguous");
            return Deny(
                request,
                normalizedAction,
                [PolicyDecisionReasonCodes.AmbiguousPolicyConfiguration],
                "Tool permissions are ambiguous because the same tool is both allowed and denied.",
                metadata,
                thresholdEvaluations);
        }

        if (allowedActions.Overlaps(deniedActions))
        {
            metadata["actionPolicyState"] = JsonValue.Create("ambiguous");
            return Deny(
                request,
                normalizedAction,
                [PolicyDecisionReasonCodes.AmbiguousPolicyConfiguration],
                "Tool action permissions are ambiguous because the same action is both allowed and denied.",
                metadata,
                thresholdEvaluations);
        }

        if (deniedTools.Contains(toolName))
        {
            metadata["toolPolicyState"] = JsonValue.Create("configured");
            metadata["toolAllowed"] = JsonValue.Create(false);
            return Deny(
                request,
                normalizedAction,
                [PolicyDecisionReasonCodes.ToolExplicitlyDenied],
                "The requested tool is explicitly denied for this agent.",
                metadata,
                thresholdEvaluations);
        }

        if (!allowedTools.Contains(toolName))
        {
            metadata["toolPolicyState"] = JsonValue.Create("configured");
            metadata["toolAllowed"] = JsonValue.Create(false);
            return Deny(
                request,
                normalizedAction,
                [PolicyDecisionReasonCodes.ToolNotPermitted],
                "The requested tool is outside the agent's allowed tool scope.",
                metadata,
                thresholdEvaluations);
        }

        metadata["toolPolicyState"] = JsonValue.Create("configured");
        metadata["toolAllowed"] = JsonValue.Create(true);
        metadata["actionPolicyState"] = JsonValue.Create(allowedActionsExists ? "configured" : "not_configured");

        if (deniedActions.Contains(normalizedAction))
        {
            metadata["actionAllowed"] = JsonValue.Create(false);
            return Deny(
                request,
                normalizedAction,
                [PolicyDecisionReasonCodes.ToolActionNotPermitted],
                "The requested action type is explicitly denied for this agent.",
                metadata,
                thresholdEvaluations);
        }

        if (allowedActionsExists && !allowedActions.Contains(normalizedAction))
        {
            metadata["actionAllowed"] = JsonValue.Create(false);
            return Deny(
                request,
                normalizedAction,
                [PolicyDecisionReasonCodes.ToolActionNotPermitted],
                "The requested action type is outside the agent's allowed tool action scope.",
                metadata,
                thresholdEvaluations);
        }

        metadata["actionAllowed"] = JsonValue.Create(true);

        if (!TryResolveScopePolicyBucket(request.DataScopes, actionType, out var requiredScopeBucket, out var scopes, out var scopeConfigState))
        {
            metadata["scopePolicyBucket"] = JsonValue.Create(requiredScopeBucket);
            metadata["scopeConfigState"] = JsonValue.Create(scopeConfigState);
            return Deny(
                request, normalizedAction,
                [scopeConfigState == "invalid" ? PolicyDecisionReasonCodes.InvalidPolicyConfiguration : PolicyDecisionReasonCodes.MissingPolicyConfiguration],
                "Data scope policy is missing or invalid for the requested action.",
                metadata,
                thresholdEvaluations);
        }
        metadata["scopePolicyBucket"] = JsonValue.Create(requiredScopeBucket);
        metadata["scopePolicyFallbackApplied"] = JsonValue.Create(false);
        metadata["scopeConfigState"] = JsonValue.Create("configured");
        metadata["permittedScopes"] = ToJsonArray(scopes);

        if (actionType == ToolActionType.Execute && normalizedScope is null)
        {
            metadata["scopeMatch"] = JsonValue.Create(false);
            return Deny(
                request,
                normalizedAction,
                [PolicyDecisionReasonCodes.ScopeContextMissing],
                "Execute actions require an explicit scope so guardrails can authorize them before execution.",
                metadata,
                thresholdEvaluations);
        }

        if (normalizedScope is not null)
        {
            if (!scopes.Contains(normalizedScope))
            {
                metadata["scopeMatch"] = JsonValue.Create(false);
                return Deny(
                    request,
                    normalizedAction,
                    [PolicyDecisionReasonCodes.ScopeNotPermitted, PolicyDecisionReasonCodes.DataScopeViolation],
                    "The requested scope is outside the agent's configured data access boundaries.",
                    metadata,
                    thresholdEvaluations);
            }
        }
        metadata["scopeMatch"] = JsonValue.Create(true);

        if (actionType == ToolActionType.Execute &&
            registeredTool?.Scopes.Contains("finance") == true)
        {
            if (registeredTool.FinanceRiskClassification is null)
            {
                metadata["riskPolicyVersion"] = JsonValue.Create(FinanceToolRiskPolicyVersions.V1);
                metadata["riskPolicyState"] = JsonValue.Create("missing_classification");
                return Deny(
                    request,
                    normalizedAction,
                    [PolicyDecisionReasonCodes.FinanceRiskPolicyMissing],
                    "The Finance execute tool has no explicit backend risk classification.",
                    metadata,
                    thresholdEvaluations);
            }

            var financeRisk = EvaluateFinanceRiskPolicy(request, registeredTool.FinanceRiskClassification);
            thresholdEvaluations.AddRange(financeRisk.ThresholdEvaluations);
            metadata["riskPolicyVersion"] = JsonValue.Create(registeredTool.FinanceRiskClassification.PolicyVersion);
            metadata["financeApprovalPolicyVersion"] = JsonValue.Create(financeRisk.CompanyPolicyVersion);
            metadata["riskPolicyState"] = JsonValue.Create(financeRisk.State);
            metadata["authoritativeSensitiveAction"] = JsonValue.Create(true);
            metadata["riskClassification"] = CreateRiskClassificationMetadata(registeredTool.FinanceRiskClassification);
            metadata["financeRiskEvaluation"] = financeRisk.Metadata;

            if (financeRisk.RequiresApproval)
            {
                approvalRequirement = new PolicyDecisionApprovalRequirementDto(
                    "finance_risk_policy",
                    null,
                    financeRisk.PolicySource,
                    registeredTool.FinanceRiskClassification.ThresholdCategory,
                    null,
                    request.FinanceRiskContext?.Amount,
                    financeRisk.ConfiguredAmountLimit,
                    [normalizedAction],
                    [toolName],
                    normalizedScope is null ? [] : [normalizedScope]);
                return RequireApproval(
                    request,
                    normalizedAction,
                    metadata,
                    [PolicyDecisionReasonCodes.SensitiveActionRequiresApproval,
                        PolicyDecisionReasonCodes.FinanceApprovalPolicyRequiresReview],
                    financeRisk.Explanation,
                    thresholdEvaluations,
                    approvalRequirement);
            }

            metadata["boundedFinanceExceptionApplied"] = JsonValue.Create(true);
        }

        if (autonomyLevel == AgentAutonomyLevel.Level0 && actionType != ToolActionType.Read)
        {
            if (actionType == ToolActionType.Recommend)
            {
                return Allow(
                    request,
                    normalizedAction,
                    metadata,
                    "The requested action is in scope and allowed by policy.",
                    thresholdEvaluations);
            }

            return Deny(
                request, normalizedAction,
                [PolicyDecisionReasonCodes.AutonomyLevelBlocksAction],
                "Autonomy level 0 is limited to read and recommendation activity and cannot execute tools directly.",
                metadata,
                thresholdEvaluations);
        }

        if (autonomyLevel == AgentAutonomyLevel.Level1 && actionType == ToolActionType.Execute)
        {
            approvalRequirement = new PolicyDecisionApprovalRequirementDto(
                "autonomy_level",
                null,
                "autonomy_level.level_1_execute",
                null,
                null,
                null,
                null,
                [normalizedAction],
                [toolName],
                normalizedScope is null ? [] : [normalizedScope]);
            return RequireApproval(
                request,
                normalizedAction,
                metadata,
                [PolicyDecisionReasonCodes.AutonomyLevelRequiresApproval],
                "Autonomy level 1 requires approval before execute actions can run.",
                thresholdEvaluations,
                approvalRequirement);
        }

        if (actionType == ToolActionType.Execute && request.TrustedToolApprovalRequired)
        {
            metadata["trustedToolApprovalRequired"] = JsonValue.Create(true);
            approvalRequirement = new PolicyDecisionApprovalRequirementDto(
                "trusted_tool",
                null,
                "trusted_tool_registry",
                null,
                null,
                null,
                null,
                [normalizedAction],
                [toolName],
                normalizedScope is null ? [] : [normalizedScope]);
            return RequireApproval(
                request,
                normalizedAction,
                metadata,
                [PolicyDecisionReasonCodes.SensitiveActionRequiresApproval],
                "This trusted sensitive tool requires human approval before it can run.",
                thresholdEvaluations,
                approvalRequirement);
        }

        if (actionType == ToolActionType.Execute && request.SensitiveAction)
        {
            metadata["sensitivityEvaluation"] = new JsonObject
            {
                ["requiresThresholdReview"] = JsonValue.Create(true),
                ["sensitiveExecuteAction"] = JsonValue.Create(true)
            };

            if (!HasThresholdContext(request))
            {
                metadata["thresholdEvaluationState"] = JsonValue.Create("missing_request_context");
                thresholdEvaluations.Add(CreateThresholdEvaluation(request, null, false, false, "missing_request_context"));
                return Deny(
                    request,
                    normalizedAction,
                    [PolicyDecisionReasonCodes.ThresholdContextMissing],
                    "Sensitive execute actions require threshold context before policy can determine whether approval is needed.",
                    metadata,
                    thresholdEvaluations);
            }

            AddRequestedThresholdMetadata(request, metadata);

            if (!TryGetThresholdValue(
                request.ApprovalThresholds,
                request.ThresholdCategory!,
                request.ThresholdKey!,
                out var configuredThreshold,
                out var thresholdConfigurationState))
            {
                var thresholdEvaluationState = thresholdConfigurationState == "invalid" ? "invalid_configuration" : "missing_configuration";
                metadata["thresholdConfigurationState"] = JsonValue.Create(thresholdConfigurationState);
                metadata["thresholdEvaluationState"] = JsonValue.Create(thresholdEvaluationState);
                thresholdEvaluations.Add(CreateThresholdEvaluation(request, null, false, false, thresholdEvaluationState));
                return Deny(
                    request,
                    normalizedAction,
                    thresholdConfigurationState == "invalid"
                        ? [PolicyDecisionReasonCodes.InvalidPolicyConfiguration, PolicyDecisionReasonCodes.ThresholdConfigurationMissing]
                        : [PolicyDecisionReasonCodes.ThresholdConfigurationMissing],
                    thresholdConfigurationState == "invalid"
                        ? "Threshold policy is invalid for this sensitive execute action."
                        : "Threshold policy is missing for this sensitive execute action.",
                    metadata,
                    thresholdEvaluations);
            }

            metadata["thresholdEvaluationState"] = JsonValue.Create("evaluated");
            AddThresholdEvaluationMetadata(request, metadata, configuredThreshold);
            var exceeded = request.ThresholdValue!.Value > configuredThreshold;
            thresholdEvaluations.Add(CreateThresholdEvaluation(request, configuredThreshold, exceeded, exceeded, "evaluated"));

            if (exceeded)
            {
                approvalRequirement = new PolicyDecisionApprovalRequirementDto(
                    "threshold",
                    null,
                    "approval_thresholds",
                    request.ThresholdCategory,
                    request.ThresholdKey,
                    request.ThresholdValue,
                    configuredThreshold);
                return RequireApproval(
                    request,
                    actionType.ToStorageValue(),
                    metadata,
                    [
                        PolicyDecisionReasonCodes.SensitiveActionRequiresApproval,
                        PolicyDecisionReasonCodes.ThresholdExceededRequiresApproval
                    ],
                    "This sensitive execute action exceeds the configured approval threshold and must remain pending approval before it can run.",
                    thresholdEvaluations,
                    approvalRequirement);
            }
        }

        if (!TryGetApprovalRequirementPolicy(request.EscalationRules, out var approvalRequirementPolicy, out var approvalRequirementConfigured, out var approvalRequirementPolicyState))
        {
            metadata["approvalRequirementPolicyState"] = JsonValue.Create(approvalRequirementPolicyState);
            return Deny(
                request,
                normalizedAction,
                [approvalRequirementPolicyState == "invalid"
                    ? PolicyDecisionReasonCodes.InvalidPolicyConfiguration
                    : PolicyDecisionReasonCodes.AmbiguousPolicyConfiguration],
                approvalRequirementPolicyState == "invalid"
                    ? "Approval requirement policy is present but invalid."
                    : "Approval requirement policy is present but ambiguous.",
                metadata,
                thresholdEvaluations);
        }

        metadata["approvalRequirementPolicyState"] = JsonValue.Create(approvalRequirementConfigured ? "configured" : "not_configured");
        if (approvalRequirementConfigured && approvalRequirementPolicy is not null)
        {
            metadata["approvalRequirementPolicy"] = CreateApprovalRequirementMetadata(approvalRequirementPolicy);

            if (MatchesApprovalRequirement(approvalRequirementPolicy, normalizedAction, toolName, normalizedScope))
            {
                approvalRequirement = new PolicyDecisionApprovalRequirementDto(
                    "policy_rule",
                    null,
                    "escalation_rules.requireApproval",
                    null,
                    null,
                    null,
                    null,
                    approvalRequirementPolicy.Actions.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                    approvalRequirementPolicy.Tools.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                    approvalRequirementPolicy.Scopes.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray());
                return RequireApproval(
                    request,
                    normalizedAction,
                    metadata,
                    [PolicyDecisionReasonCodes.ApprovalRequiredByPolicy, PolicyDecisionReasonCodes.ApprovalRequired],
                    "The requested action requires human approval based on the configured policy.",
                    thresholdEvaluations,
                    approvalRequirement);
            }
        }

        if (request.ThresholdValue.HasValue)
        {
            if (!HasThresholdContext(request))
            {
                metadata["thresholdEvaluationState"] = JsonValue.Create("missing_request_context");
                thresholdEvaluations.Add(CreateThresholdEvaluation(request, null, false, false, "missing_request_context"));
                return Deny(
                    request,
                    normalizedAction,
                    [PolicyDecisionReasonCodes.ThresholdContextMissing],
                    "Threshold context is incomplete for the requested action.",
                    metadata,
                    thresholdEvaluations);
            }

            AddRequestedThresholdMetadata(request, metadata);

            if (!TryGetThresholdValue(
                    request.ApprovalThresholds,
                    request.ThresholdCategory!,
                    request.ThresholdKey!,
                    out var configuredThreshold,
                    out var thresholdConfigurationState))
            {
                var thresholdEvaluationState = thresholdConfigurationState == "invalid" ? "invalid_configuration" : "missing_configuration";
                metadata["thresholdConfigurationState"] = JsonValue.Create(thresholdConfigurationState);
                metadata["thresholdEvaluationState"] = JsonValue.Create(thresholdEvaluationState);
                thresholdEvaluations.Add(CreateThresholdEvaluation(request, null, false, false, thresholdEvaluationState));
                return Deny(
                    request,
                    normalizedAction,
                    thresholdConfigurationState == "invalid"
                        ? [PolicyDecisionReasonCodes.InvalidPolicyConfiguration, PolicyDecisionReasonCodes.ThresholdConfigurationMissing]
                        : [PolicyDecisionReasonCodes.ThresholdConfigurationMissing],
                    thresholdConfigurationState == "invalid"
                        ? "Threshold policy is invalid for the requested action."
                        : "Threshold policy is missing for the requested action.",
                    metadata,
                    thresholdEvaluations);
            }

            metadata["thresholdEvaluationState"] = JsonValue.Create("evaluated");
            AddThresholdEvaluationMetadata(request, metadata, configuredThreshold);
            var exceeded = request.ThresholdValue.Value > configuredThreshold;
            thresholdEvaluations.Add(CreateThresholdEvaluation(request, configuredThreshold, exceeded, exceeded, "evaluated"));

            if (exceeded)
            {
                approvalRequirement = new PolicyDecisionApprovalRequirementDto(
                    "threshold",
                    null,
                    "approval_thresholds",
                    request.ThresholdCategory,
                    request.ThresholdKey,
                    request.ThresholdValue,
                    configuredThreshold);
                return RequireApproval(
                    request,
                    actionType.ToStorageValue(),
                    metadata,
                    [PolicyDecisionReasonCodes.ThresholdExceededRequiresApproval],
                    "The requested action exceeds the configured approval threshold.",
                    thresholdEvaluations,
                    approvalRequirement);
            }
        }

        return Allow(request, normalizedAction, metadata, "The requested action is within scope, within threshold, and allowed to execute.", thresholdEvaluations);
    }

    private static ToolExecutionDecisionDto Allow(
        PolicyEvaluationRequest request,
        string evaluatedActionType,
        Dictionary<string, JsonNode?> metadata,
        string explanation,
        IReadOnlyList<PolicyDecisionThresholdEvaluationDto> thresholdEvaluations)
    {
        metadata["decisionOutcome"] = JsonValue.Create(PolicyDecisionOutcomeValues.Allow);
        metadata["executionBlocked"] = JsonValue.Create(false);
        metadata["blockedPendingApproval"] = JsonValue.Create(false);
        metadata["executionState"] = JsonValue.Create(ToolExecutionStatus.Executed.ToStorageValue());
        return new(
            PolicyDecisionOutcomeValues.Allow,
            [PolicyDecisionReasonCodes.PolicyChecksPassed],
            explanation,
            request.EvaluatedAutonomyLevel,
            evaluatedActionType,
            string.IsNullOrWhiteSpace(request.Scope) ? null : request.Scope.Trim(),
            false,
            metadata,
            PolicyDecisionSchemaVersions.V1,
            CreateReasons([PolicyDecisionReasonCodes.PolicyChecksPassed]),
            CreateTenantContext(request),
            CreateActorContext(request),
            CreateToolContext(request, evaluatedActionType),
            thresholdEvaluations.ToArray(),
            null,
            CreateAuditContext(request));
    }

    private static ToolExecutionDecisionDto Deny(
        PolicyEvaluationRequest request,
        string evaluatedActionType,
        IReadOnlyList<string> reasonCodes,
        string explanation,
        Dictionary<string, JsonNode?> metadata,
        IReadOnlyList<PolicyDecisionThresholdEvaluationDto> thresholdEvaluations,
        PolicyDecisionApprovalRequirementDto? approvalRequirement = null)
    {
        metadata["decisionOutcome"] = JsonValue.Create(PolicyDecisionOutcomeValues.Deny);
        metadata["executionBlocked"] = JsonValue.Create(true);
        metadata["internalRationaleSummary"] = JsonValue.Create(explanation);
        metadata["safeUserFacingExplanation"] = JsonValue.Create(SafeDeniedExplanation);
        metadata["blockedPendingApproval"] = JsonValue.Create(false);
        metadata["executionState"] = JsonValue.Create(ToolExecutionStatus.Denied.ToStorageValue());
        return new(
            PolicyDecisionOutcomeValues.Deny,
            reasonCodes,
            SafeDeniedExplanation,
            request.EvaluatedAutonomyLevel,
            evaluatedActionType,
            string.IsNullOrWhiteSpace(request.Scope) ? null : request.Scope.Trim(),
            false,
            metadata,
            PolicyDecisionSchemaVersions.V1,
            CreateReasons(reasonCodes),
            CreateTenantContext(request),
            CreateActorContext(request),
            CreateToolContext(request, evaluatedActionType),
            thresholdEvaluations.ToArray(),
            approvalRequirement,
            CreateAuditContext(request));
    }

    private static ToolExecutionDecisionDto RequireApproval(
        PolicyEvaluationRequest request,
        string evaluatedActionType,
        Dictionary<string, JsonNode?> metadata,
        IReadOnlyList<string> reasonCodes,
        string explanation,
        IReadOnlyList<PolicyDecisionThresholdEvaluationDto> thresholdEvaluations,
        PolicyDecisionApprovalRequirementDto? approvalRequirement)
    {
        if (!TryGetEscalationTarget(request.EscalationRules, out var approvalTarget))
        {
            metadata["approvalRequirementState"] = JsonValue.Create("missing_route");
            metadata["decisionOutcome"] = JsonValue.Create(PolicyDecisionOutcomeValues.Deny);
            return Deny(
                request,
                evaluatedActionType,
                [PolicyDecisionReasonCodes.ApprovalRouteMissing],
                "Approval is required, but the escalation route is missing or invalid.",
                metadata,
                thresholdEvaluations,
                approvalRequirement);
        }

        metadata["approvalTarget"] = JsonValue.Create(approvalTarget);
        metadata["approvalRequirementState"] = JsonValue.Create("configured");
        metadata["decisionOutcome"] = JsonValue.Create(PolicyDecisionOutcomeValues.RequireApproval);
        metadata["executionBlocked"] = JsonValue.Create(true);
        metadata["blockedPendingApproval"] = JsonValue.Create(true);
        metadata["executionState"] = JsonValue.Create(ToolExecutionStatus.AwaitingApproval.ToStorageValue());
        var effectiveApprovalRequirement = approvalRequirement is null
            ? new PolicyDecisionApprovalRequirementDto("manual", approvalTarget, "escalation_rules.escalateTo", null, null, null, null)
            : approvalRequirement with { ApprovalTarget = approvalTarget };
        return new ToolExecutionDecisionDto(
            PolicyDecisionOutcomeValues.RequireApproval,
            reasonCodes,
            explanation,
            request.EvaluatedAutonomyLevel,
            evaluatedActionType,
            string.IsNullOrWhiteSpace(request.Scope) ? null : request.Scope.Trim(),
            true,
            metadata,
            PolicyDecisionSchemaVersions.V1,
            CreateReasons(reasonCodes),
            CreateTenantContext(request),
            CreateActorContext(request),
            CreateToolContext(request, evaluatedActionType),
            thresholdEvaluations.ToArray(),
            effectiveApprovalRequirement,
            CreateAuditContext(request));
    }

    private static Dictionary<string, JsonNode?> CreateBaseMetadata(PolicyEvaluationRequest request) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["companyId"] = JsonValue.Create(request.CompanyId),
            ["agentId"] = JsonValue.Create(request.AgentId),
            ["toolName"] = string.IsNullOrWhiteSpace(request.ToolName) ? null : JsonValue.Create(request.ToolName.Trim()),
            ["actionType"] = TryNormalizeActionType(request.ActionType, out _, out var normalizedActionType)
                ? JsonValue.Create(normalizedActionType)
                : null,
            ["scope"] = string.IsNullOrWhiteSpace(request.Scope) ? null : JsonValue.Create(request.Scope.Trim()),
            ["agentCompanyId"] = JsonValue.Create(request.AgentCompanyId),
            ["companyScopeMatched"] = JsonValue.Create(
                request.CompanyId != Guid.Empty &&
                request.AgentCompanyId != Guid.Empty &&
                request.AgentCompanyId == request.CompanyId),
            ["evaluatedAutonomyLevel"] = JsonValue.Create(request.EvaluatedAutonomyLevel),
            ["evaluationVersion"] = JsonValue.Create(PolicyDecisionEvaluationVersions.Current),
            ["policyDecisionSchemaVersion"] = JsonValue.Create(PolicyDecisionSchemaVersions.V1),
            ["policyMode"] = JsonValue.Create("default_deny"),
            ["sensitiveAction"] = JsonValue.Create(request.SensitiveAction),
            ["requestedSensitiveAction"] = JsonValue.Create(request.SensitiveAction),
            ["executionId"] = JsonValue.Create(request.ExecutionId),
            ["correlationId"] = string.IsNullOrWhiteSpace(request.CorrelationId) ? null : JsonValue.Create(request.CorrelationId)
        };

    private static ToolExecutionDecisionDto Deny(
        PolicyEvaluationRequest request,
        IReadOnlyList<string> reasonCodes,
        string explanation,
        Dictionary<string, JsonNode?> metadata,
        IReadOnlyList<PolicyDecisionThresholdEvaluationDto> thresholdEvaluations,
        PolicyDecisionApprovalRequirementDto? approvalRequirement = null) =>
        Deny(request, NormalizeEvaluatedActionType(request.ActionType), reasonCodes, explanation, metadata, thresholdEvaluations, approvalRequirement);

    private static PolicyDecisionTenantContextDto CreateTenantContext(PolicyEvaluationRequest request) =>
        new(request.CompanyId, request.AgentCompanyId, request.CompanyId != Guid.Empty && request.AgentCompanyId != Guid.Empty && request.AgentCompanyId == request.CompanyId);

    private static PolicyDecisionActorContextDto CreateActorContext(PolicyEvaluationRequest request) =>
        new(request.AgentId, request.AgentStatus, request.CanReceiveAssignments);

    private static PolicyDecisionToolContextDto CreateToolContext(PolicyEvaluationRequest request, string evaluatedActionType) =>
        new(
            string.IsNullOrWhiteSpace(request.ToolName) ? string.Empty : request.ToolName.Trim(),
            evaluatedActionType,
            string.IsNullOrWhiteSpace(request.Scope) ? null : request.Scope.Trim());

    private static PolicyDecisionAuditContextDto CreateAuditContext(PolicyEvaluationRequest request) =>
        new(request.ExecutionId, string.IsNullOrWhiteSpace(request.CorrelationId) ? null : request.CorrelationId.Trim(), DateTime.UtcNow, "default_deny", PolicyDecisionEvaluationVersions.Current);

    private static bool HasThresholdContext(PolicyEvaluationRequest request) =>
        !string.IsNullOrWhiteSpace(request.ThresholdCategory) &&
        !string.IsNullOrWhiteSpace(request.ThresholdKey) &&
        request.ThresholdValue.HasValue;

    private static bool TryGetIdentifierSet(
        IReadOnlyDictionary<string, JsonNode?> values,
        string key,
        out HashSet<string> results,
        out bool exists)
    {
        results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        exists = values.TryGetValue(key, out var node) && node is not null;

        if (!exists)
        {
            return true;
        }

        if (node is not JsonArray items)
        {
            return false;
        }

        foreach (var item in items)
        {
            if (item is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            results.Add(text.Trim());
        }

        return true;
    }

    private static bool TryResolveScopePolicyBucket(
        IReadOnlyDictionary<string, JsonNode?>? dataScopes,
        ToolActionType actionType,
        out string requiredScopeBucket,
        out HashSet<string> scopes,
        out string scopeConfigurationState)
    {
        requiredScopeBucket = actionType.ToStorageValue();
        scopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        scopeConfigurationState = "missing";

        if (dataScopes is null)
        {
            return false;
        }

        if (!TryGetIdentifierSet(dataScopes, requiredScopeBucket, out scopes, out var scopeConfigExists))
        {
            scopeConfigurationState = "invalid";
            return false;
        }

        scopeConfigurationState = !scopeConfigExists || scopes.Count == 0 ? "missing" : "configured";
        return scopeConfigExists && scopes.Count > 0;
    }

    private static void AddRequestedThresholdMetadata(
        PolicyEvaluationRequest request,
        Dictionary<string, JsonNode?> metadata)
    {
        if (!string.IsNullOrWhiteSpace(request.ThresholdCategory))
        {
            metadata["thresholdCategory"] = JsonValue.Create(request.ThresholdCategory.Trim());
        }

        if (!string.IsNullOrWhiteSpace(request.ThresholdKey))
        {
            metadata["thresholdKey"] = JsonValue.Create(request.ThresholdKey.Trim());
        }

        if (request.ThresholdValue.HasValue)
        {
            metadata["thresholdValue"] = JsonValue.Create(request.ThresholdValue.Value);
        }
    }

    private static void AddThresholdEvaluationMetadata(
        PolicyEvaluationRequest request,
        Dictionary<string, JsonNode?> metadata,
        decimal configuredThreshold)
    {
        metadata["configuredThreshold"] = JsonValue.Create(configuredThreshold);
        var exceeded = request.ThresholdValue!.Value > configuredThreshold;
        metadata["thresholdEvaluation"] = new JsonObject
        {
            ["category"] = JsonValue.Create(request.ThresholdCategory),
            ["key"] = JsonValue.Create(request.ThresholdKey),
            ["requestValue"] = JsonValue.Create(request.ThresholdValue!.Value),
            ["configuredThreshold"] = JsonValue.Create(configuredThreshold),
            ["exceeded"] = JsonValue.Create(exceeded),
            ["approvalRequired"] = JsonValue.Create(exceeded),
            ["sensitiveAction"] = JsonValue.Create(request.SensitiveAction)
        };
    }

    private static bool TryGetThresholdValue(
        IReadOnlyDictionary<string, JsonNode?>? thresholds,
        string category,
        string key,
        out decimal thresholdValue,
        out string thresholdConfigurationState)
    {
        thresholdValue = 0;
        thresholdConfigurationState = "missing";

        if (thresholds is null)
        {
            return false;
        }

        if (!thresholds.TryGetValue(category, out var node) || node is null)
        {
            return false;
        }

        if (node is not JsonObject categoryObject)
        {
            thresholdConfigurationState = "invalid";
            return false;
        }

        if (!categoryObject.TryGetPropertyValue(key, out var thresholdNode) || thresholdNode is null)
        {
            return false;
        }

        if (thresholdNode is not JsonValue thresholdJson)
        {
            thresholdConfigurationState = "invalid";
            return false;
        }

        if (!TryGetNumericThresholdValue(thresholdJson, out thresholdValue) || thresholdValue < 0)
        {
            thresholdConfigurationState = "invalid";
            thresholdValue = 0;
            return false;
        }

        thresholdConfigurationState = "configured";
        return true;
    }

    private static bool TryGetNumericThresholdValue(JsonValue thresholdJson, out decimal thresholdValue)
    {
        thresholdValue = 0;

        if (thresholdJson.TryGetValue<decimal>(out thresholdValue))
        {
            return true;
        }

        if (thresholdJson.TryGetValue<double>(out var doubleValue))
        {
            thresholdValue = (decimal)doubleValue;
            return true;
        }

        if (thresholdJson.TryGetValue<int>(out var intValue))
        {
            thresholdValue = intValue;
            return true;
        }

        return false;
    }

    private static bool TryGetApprovalRequirementPolicy(
        IReadOnlyDictionary<string, JsonNode?>? escalationRules,
        out ApprovalRequirementPolicy? policy,
        out bool exists,
        out string policyState)
    {
        policy = null;
        exists = false;
        policyState = "missing";

        if (escalationRules is null)
        {
            return false;
        }

        exists = escalationRules.TryGetValue("requireApproval", out var node) && node is not null;

        if (!exists)
        {
            return true;
        }

        if (node is not JsonObject jsonObject)
        {
            policyState = "invalid";
            return false;
        }

        if (!TryGetIdentifierSet(jsonObject, "actions", out var actions, out var actionsExists) ||
            !TryGetIdentifierSet(jsonObject, "tools", out var tools, out var toolsExists) ||
            !TryGetIdentifierSet(jsonObject, "scopes", out var scopes, out var scopesExists))
        {
            policyState = "invalid";
            return false;
        }

        if ((!actionsExists && !toolsExists && !scopesExists) ||
            (actionsExists && actions.Count == 0) ||
            (toolsExists && tools.Count == 0) ||
            (scopesExists && scopes.Count == 0))
        {
            policyState = "ambiguous";
            return false;
        }

        policyState = "configured";
        policy = new ApprovalRequirementPolicy(actions, tools, scopes);
        return true;
    }

    private static bool MatchesApprovalRequirement(
        ApprovalRequirementPolicy policy,
        string actionType,
        string toolName,
        string? scope)
    {
        var actionMatches = policy.Actions.Count == 0 || policy.Actions.Contains(actionType);
        var toolMatches = policy.Tools.Count == 0 || policy.Tools.Contains(toolName);
        var scopeMatches = policy.Scopes.Count == 0 ||
            (!string.IsNullOrWhiteSpace(scope) && policy.Scopes.Contains(scope));

        return actionMatches && toolMatches && scopeMatches;
    }

    private static JsonObject CreateApprovalRequirementMetadata(ApprovalRequirementPolicy policy)
    {
        return new JsonObject
        {
            ["actions"] = ToJsonArray(policy.Actions),
            ["tools"] = ToJsonArray(policy.Tools),
            ["scopes"] = ToJsonArray(policy.Scopes)
        };
    }

    private static JsonArray ToJsonArray(IEnumerable<string> values) =>
        new(values
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .Select(static value => (JsonNode?)JsonValue.Create(value))
            .ToArray());

    private static PolicyDecisionThresholdEvaluationDto CreateThresholdEvaluation(
        PolicyEvaluationRequest request,
        decimal? configuredThreshold,
        bool exceeded,
        bool approvalRequired,
        string evaluationState) =>
        new(
            request.ThresholdCategory?.Trim() ?? string.Empty,
            request.ThresholdKey?.Trim() ?? string.Empty,
            request.ThresholdValue,
            configuredThreshold,
            exceeded,
            approvalRequired,
            request.SensitiveAction,
            evaluationState);

    private static IReadOnlyList<PolicyDecisionReasonDto> CreateReasons(IReadOnlyList<string> reasonCodes) =>
        reasonCodes
            .Where(static code => !string.IsNullOrWhiteSpace(code))
            .Select(code => new PolicyDecisionReasonDto(code, ResolveReasonCategory(code), ResolveReasonSummary(code)))
            .ToArray();

    private static string ResolveReasonCategory(string reasonCode) => reasonCode switch
    {
        PolicyDecisionReasonCodes.InvalidCompanyContext or PolicyDecisionReasonCodes.TenantScopeViolation => "tenant",
        PolicyDecisionReasonCodes.AgentStatusDisallowsExecution => "actor",
        PolicyDecisionReasonCodes.InvalidActionType => "action",
        PolicyDecisionReasonCodes.InvalidAutonomyLevel or PolicyDecisionReasonCodes.AutonomyLevelBlocksAction or PolicyDecisionReasonCodes.AutonomyLevelRequiresApproval => "autonomy",
        PolicyDecisionReasonCodes.MissingPolicyConfiguration or PolicyDecisionReasonCodes.InvalidPolicyConfiguration or PolicyDecisionReasonCodes.AmbiguousPolicyConfiguration => "policy_configuration",
        PolicyDecisionReasonCodes.ToolNotConfigured or PolicyDecisionReasonCodes.ToolExplicitlyDenied or PolicyDecisionReasonCodes.ToolNotPermitted or PolicyDecisionReasonCodes.ToolActionTypeMismatch or PolicyDecisionReasonCodes.ToolActionNotPermitted => "tool",
        PolicyDecisionReasonCodes.ScopeNotPermitted or PolicyDecisionReasonCodes.ScopeContextMissing or PolicyDecisionReasonCodes.DataScopeViolation => "scope",
        PolicyDecisionReasonCodes.ApprovalRequiredByPolicy or PolicyDecisionReasonCodes.ApprovalRouteMissing or PolicyDecisionReasonCodes.ApprovalRequired => "approval",
        PolicyDecisionReasonCodes.ThresholdContextMissing or PolicyDecisionReasonCodes.ThresholdConfigurationMissing or PolicyDecisionReasonCodes.ThresholdExceededRequiresApproval => "threshold",
        PolicyDecisionReasonCodes.SensitiveActionRequiresApproval or PolicyDecisionReasonCodes.FinanceRiskPolicyMissing or PolicyDecisionReasonCodes.FinanceApprovalPolicyRequiresReview => "sensitivity",
        PolicyDecisionReasonCodes.FinanceCategorizationExceptionApplied => "allowance",
        PolicyDecisionReasonCodes.PolicyChecksPassed => "allowance",
        _ => "policy"
    };

    private static string ResolveReasonSummary(string reasonCode) => reasonCode switch
    {
        PolicyDecisionReasonCodes.PolicyChecksPassed => "All configured policy checks passed.",
        PolicyDecisionReasonCodes.InvalidCompanyContext => "Tenant context was missing or inconsistent.",
        PolicyDecisionReasonCodes.TenantScopeViolation => "The request crossed tenant boundaries.",
        PolicyDecisionReasonCodes.ApprovalRequiredByPolicy => "Configured policy requires a human approval step.",
        PolicyDecisionReasonCodes.InvalidActionType => "The requested action type is not supported.",
        PolicyDecisionReasonCodes.InvalidAutonomyLevel => "The agent autonomy level is missing or invalid.",
        PolicyDecisionReasonCodes.AgentStatusDisallowsExecution => "The agent status does not permit execution.",
        PolicyDecisionReasonCodes.MissingPolicyConfiguration => "Required guardrail configuration is missing.",
        PolicyDecisionReasonCodes.InvalidPolicyConfiguration => "Required guardrail configuration is invalid.",
        PolicyDecisionReasonCodes.AmbiguousPolicyConfiguration => "Guardrail configuration is ambiguous.",
        PolicyDecisionReasonCodes.ToolNotConfigured => "The requested tool identity is missing or invalid.",
        PolicyDecisionReasonCodes.ToolExplicitlyDenied => "The tool is explicitly denied.",
        PolicyDecisionReasonCodes.ToolNotPermitted => "The tool is outside the allowed tool set.",
        PolicyDecisionReasonCodes.ToolActionTypeMismatch => "The requested action type does not match the registered tool metadata.",
        PolicyDecisionReasonCodes.ToolActionNotPermitted => "The requested action type is outside the allowed tool action set.",
        PolicyDecisionReasonCodes.ScopeNotPermitted => "The requested scope is not permitted.",
        PolicyDecisionReasonCodes.DataScopeViolation => "The requested scope violates the configured data boundary.",
        PolicyDecisionReasonCodes.AutonomyLevelBlocksAction => "The autonomy level blocks this action.",
        PolicyDecisionReasonCodes.ScopeContextMissing => "The request is missing required scope context.",
        PolicyDecisionReasonCodes.AutonomyLevelRequiresApproval => "The autonomy level requires approval for this action.",
        PolicyDecisionReasonCodes.ApprovalRouteMissing => "Approval is required but no escalation route is configured.",
        PolicyDecisionReasonCodes.ApprovalRequired => "A human approval step is required.",
        PolicyDecisionReasonCodes.ThresholdContextMissing => "Threshold context is incomplete.",
        PolicyDecisionReasonCodes.ThresholdConfigurationMissing => "Threshold policy is missing or invalid.",
        PolicyDecisionReasonCodes.ThresholdExceededRequiresApproval => "The request exceeded a configured threshold.",
        PolicyDecisionReasonCodes.SensitiveActionRequiresApproval => "The action is marked as sensitive and requires approval.",
        PolicyDecisionReasonCodes.FinanceRiskPolicyMissing => "The Finance tool has no explicit backend risk classification.",
        PolicyDecisionReasonCodes.FinanceApprovalPolicyRequiresReview => "The authoritative Finance policy requires human review.",
        PolicyDecisionReasonCodes.FinanceCategorizationExceptionApplied => "A versioned bounded categorization exception allowed the action.",
        _ => "The action was evaluated by a default-deny guardrail rule."
    };

    private static FinanceRiskEvaluation EvaluateFinanceRiskPolicy(
        PolicyEvaluationRequest request,
        FinanceToolRiskClassification classification)
    {
        var financePolicy = request.ApprovalThresholds.TryGetValue("financePolicy", out var policyNode)
            ? policyNode as JsonObject
            : null;
        var companyPolicyVersion = ReadString(financePolicy, "policyVersion") ?? "company-finance-policy-unversioned";
        var requireAllExecute = ReadBooleanFailSafe(financePolicy, "requireApprovalForExecute", out var executeRuleState);
        var explicitToolRule = ContainsIdentifierFailSafe(financePolicy, "requireApprovalForTools", classification.ToolName, out var toolRuleState);
        var explicitActionRule = ContainsIdentifierFailSafe(financePolicy, "requireApprovalForActions", ToolActionType.Execute.ToStorageValue(), out var actionRuleState);
        var workflowRule = RequiresApprovalFromWorkflow(request.TriggerLogic, classification.ToolName, out var workflowRuleState);

        var metadata = new JsonObject
        {
            ["toolName"] = classification.ToolName,
            ["riskPolicyVersion"] = classification.PolicyVersion,
            ["companyPolicyVersion"] = companyPolicyVersion,
            ["requireApprovalForExecute"] = requireAllExecute,
            ["requireApprovalForExecuteState"] = executeRuleState,
            ["explicitToolRuleMatched"] = explicitToolRule,
            ["explicitToolRuleState"] = toolRuleState,
            ["explicitActionRuleMatched"] = explicitActionRule,
            ["explicitActionRuleState"] = actionRuleState,
            ["workflowRuleMatched"] = workflowRule,
            ["workflowRuleState"] = workflowRuleState,
            ["backendContextVerified"] = request.FinanceRiskContext?.BackendVerified ?? false,
            ["evidenceSource"] = request.FinanceRiskContext?.EvidenceSource
        };
        var evaluations = new List<PolicyDecisionThresholdEvaluationDto>();

        if (requireAllExecute || explicitToolRule || explicitActionRule || workflowRule)
        {
            evaluations.Add(new PolicyDecisionThresholdEvaluationDto(
                "finance_policy", "approval_rule", 1m, 1m, false, true, true, "policy_requires_review"));
            metadata["decision"] = "approval_required";
            return new FinanceRiskEvaluation(true, "configured_approval_rule",
                "The effective Finance approval policy requires review for this execute action.",
                requireAllExecute ? "approval_thresholds.financePolicy.requireApprovalForExecute"
                    : workflowRule ? "trigger_logic.workflowCapabilities.requiresApproval"
                    : "approval_thresholds.financePolicy.explicit_rules",
                companyPolicyVersion, null, evaluations, metadata);
        }

        if (string.Equals(classification.DefaultApprovalBehavior,
                FinanceToolApprovalBehaviors.ReviewUnlessBoundedCategorizationException,
                StringComparison.OrdinalIgnoreCase))
        {
            return EvaluateCategorizationException(request, classification, financePolicy, companyPolicyVersion,
                evaluations, metadata);
        }

        evaluations.Add(new PolicyDecisionThresholdEvaluationDto(
            classification.ThresholdCategory, "default_approval", null, null, false, true, true,
            "risk_classification_requires_review"));
        metadata["decision"] = "approval_required";
        return new FinanceRiskEvaluation(true, "risk_classification_requires_review",
            "The versioned Finance risk classification requires human review for this action.",
            "finance_tool_risk_policy.default_approval_behavior", companyPolicyVersion, null,
            evaluations, metadata);
    }

    private static FinanceRiskEvaluation EvaluateCategorizationException(
        PolicyEvaluationRequest request,
        FinanceToolRiskClassification classification,
        JsonObject? financePolicy,
        string companyPolicyVersion,
        List<PolicyDecisionThresholdEvaluationDto> evaluations,
        JsonObject metadata)
    {
        var exception = financePolicy?["categorizationException"] as JsonObject;
        var exceptionVersion = ReadString(exception, "policyVersion");
        var enabled = ReadBoolean(exception, "enabled") == true;
        var maxAmount = ReadDecimal(exception, "maxAmount");
        var maxBatchCount = ReadInt(exception, "maxBatchCount");
        var requiredState = ReadString(exception, "requiredCurrentState");
        var allowedCategories = ReadIdentifiers(exception, "allowedCategories", out var categoriesValid);
        var context = request.FinanceRiskContext;

        var configurationValid = enabled && !string.IsNullOrWhiteSpace(exceptionVersion) &&
                                 maxAmount is >= 0 && maxBatchCount is > 0 and <= 100 &&
                                 string.Equals(requiredState, "uncategorized", StringComparison.OrdinalIgnoreCase) &&
                                 categoriesValid && allowedCategories.Count > 0 &&
                                 allowedCategories.All(FinanceTransactionCategories.IsSupported);
        var amountExceeded = !context?.Amount.HasValue ?? true;
        amountExceeded = amountExceeded || (maxAmount.HasValue && Math.Abs(context!.Amount!.Value) > maxAmount.Value);
        var countExceeded = context is null || context.ItemCount <= 0 || !maxBatchCount.HasValue || context.ItemCount > maxBatchCount.Value;
        var stateAllowed = context is not null && string.Equals(context.CurrentState, requiredState, StringComparison.OrdinalIgnoreCase);
        var categoryAllowed = context?.RequestedCategory is not null && allowedCategories.Contains(context.RequestedCategory);
        var contextValid = context?.BackendVerified == true && context.Amount.HasValue && context.ItemCount > 0;
        var exceptionApplied = configurationValid && contextValid && !amountExceeded && !countExceeded &&
                               stateAllowed && categoryAllowed &&
                               string.Equals(classification.Reversibility, FinanceToolReversibility.Reversible, StringComparison.Ordinal);

        var evaluationState = !configurationValid ? "missing_or_invalid_exception_configuration"
            : !contextValid ? "unverified_backend_context"
            : exceptionApplied ? "within_bounded_exception" : "outside_bounded_exception";
        evaluations.Add(new PolicyDecisionThresholdEvaluationDto(
            classification.ThresholdCategory, "maxAmount", context?.Amount, maxAmount,
            amountExceeded, !exceptionApplied, true, evaluationState));
        evaluations.Add(new PolicyDecisionThresholdEvaluationDto(
            classification.ThresholdCategory, "maxBatchCount", context?.ItemCount, maxBatchCount,
            countExceeded, !exceptionApplied, true, evaluationState));

        metadata["categorizationException"] = new JsonObject
        {
            ["policyVersion"] = exceptionVersion,
            ["configurationValid"] = configurationValid,
            ["applied"] = exceptionApplied,
            ["maxAmount"] = maxAmount,
            ["actualAmount"] = context?.Amount,
            ["maxBatchCount"] = maxBatchCount,
            ["actualBatchCount"] = context?.ItemCount,
            ["requiredCurrentState"] = requiredState,
            ["actualCurrentState"] = context?.CurrentState,
            ["requestedCategory"] = context?.RequestedCategory,
            ["categoryAllowed"] = categoryAllowed,
            ["evaluationState"] = evaluationState
        };
        metadata["decision"] = exceptionApplied ? "bounded_exception_allowed" : "approval_required";

        return exceptionApplied
            ? new FinanceRiskEvaluation(false, "bounded_categorization_exception_applied",
                "The action is within the versioned, reversible categorization exception.",
                "approval_thresholds.financePolicy.categorizationException", exceptionVersion!, maxAmount,
                evaluations, metadata)
            : new FinanceRiskEvaluation(true, evaluationState,
                "Transaction categorization requires review because no valid bounded exception covers this request.",
                "finance_tool_risk_policy.default_approval_behavior", exceptionVersion ?? companyPolicyVersion,
                maxAmount, evaluations, metadata);
    }

    private static JsonObject CreateRiskClassificationMetadata(FinanceToolRiskClassification classification) => new()
    {
        ["toolName"] = classification.ToolName,
        ["policyVersion"] = classification.PolicyVersion,
        ["riskTier"] = classification.RiskTier,
        ["reversibility"] = classification.Reversibility,
        ["requiredActorPermission"] = classification.RequiredActorPermission,
        ["defaultApprovalBehavior"] = classification.DefaultApprovalBehavior,
        ["thresholdCategory"] = classification.ThresholdCategory,
        ["requiresSegregation"] = classification.RequiresSegregation,
        ["externalSideEffectClassification"] = classification.ExternalSideEffectClassification
    };

    private static string? ReadString(JsonObject? source, string key) =>
        source?[key] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;

    private static bool? ReadBoolean(JsonObject? source, string key) =>
        source?[key] is JsonValue value && value.TryGetValue<bool>(out var result) ? result : null;

    private static bool ReadBooleanFailSafe(JsonObject? source, string key, out string state)
    {
        if (source is null || !source.TryGetPropertyValue(key, out var node) || node is null)
        {
            state = "not_configured";
            return false;
        }
        if (node is JsonValue value && value.TryGetValue<bool>(out var result))
        {
            state = "configured";
            return result;
        }
        state = "invalid_fail_safe_review";
        return true;
    }

    private static decimal? ReadDecimal(JsonObject? source, string key)
    {
        if (source?[key] is not JsonValue value) return null;
        if (value.TryGetValue<decimal>(out var decimalValue)) return decimalValue;
        if (value.TryGetValue<double>(out var doubleValue)) return (decimal)doubleValue;
        if (value.TryGetValue<int>(out var intValue)) return intValue;
        return null;
    }

    private static int? ReadInt(JsonObject? source, string key) =>
        source?[key] is JsonValue value && value.TryGetValue<int>(out var result) ? result : null;

    private static HashSet<string> ReadIdentifiers(JsonObject? source, string key, out bool valid)
    {
        valid = false;
        if (source?[key] is not JsonArray array) return new(StringComparer.OrdinalIgnoreCase);
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in array)
        {
            if (node is not JsonValue value || !value.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text))
                return new(StringComparer.OrdinalIgnoreCase);
            values.Add(FinanceTransactionCategories.Normalize(text));
        }
        valid = true;
        return values;
    }

    private static bool ContainsIdentifierFailSafe(JsonObject? source, string key, string expected, out string state)
    {
        if (source is null || !source.TryGetPropertyValue(key, out var node) || node is null)
        {
            state = "not_configured";
            return false;
        }
        if (node is not JsonArray array)
        {
            state = "invalid_fail_safe_review";
            return true;
        }
        var matched = false;
        foreach (var item in array)
        {
            if (item is not JsonValue value ||
                !value.TryGetValue<string>(out var text) ||
                string.IsNullOrWhiteSpace(text))
            {
                state = "invalid_fail_safe_review";
                return true;
            }

            matched |= string.Equals(text, expected, StringComparison.OrdinalIgnoreCase);
        }

        state = "configured";
        return matched;
    }

    private static bool RequiresApprovalFromWorkflow(
        IReadOnlyDictionary<string, JsonNode?>? triggerLogic,
        string toolName,
        out string state)
    {
        state = "not_configured";
        if (triggerLogic is null || !triggerLogic.TryGetValue("workflowCapabilities", out var node) || node is null)
            return false;
        if (node is not JsonObject workflow || !workflow.TryGetPropertyValue("requiresApproval", out var required) || required is null)
            return false;
        if (required is not JsonArray array)
        {
            state = "invalid_fail_safe_review";
            return true;
        }
        var matched = false;
        foreach (var item in array)
        {
            if (item is not JsonValue value ||
                !value.TryGetValue<string>(out var text) ||
                string.IsNullOrWhiteSpace(text))
            {
                state = "invalid_fail_safe_review";
                return true;
            }

            matched |= string.Equals(text, toolName, StringComparison.OrdinalIgnoreCase);
        }

        state = "configured";
        return matched;
    }

    private sealed record FinanceRiskEvaluation(
        bool RequiresApproval,
        string State,
        string Explanation,
        string PolicySource,
        string CompanyPolicyVersion,
        decimal? ConfiguredAmountLimit,
        IReadOnlyList<PolicyDecisionThresholdEvaluationDto> ThresholdEvaluations,
        JsonObject Metadata);

    private static bool TryGetIdentifierSet(
        JsonObject values,
        string key,
        out HashSet<string> results,
        out bool exists)
    {
        results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        exists = values.TryGetPropertyValue(key, out var node) && node is not null;

        if (!exists)
        {
            return true;
        }

        if (node is not JsonArray items)
        {
            return false;
        }

        foreach (var item in items)
        {
            if (item is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            results.Add(text.Trim());
        }

        return true;
    }

    private static string NormalizeEvaluatedActionType(ToolActionType? actionType) =>
        TryNormalizeActionType(actionType, out _, out var normalizedActionType)
            ? normalizedActionType
            : string.Empty;

    private static bool TryNormalizeActionType(
        ToolActionType? actionType,
        out ToolActionType normalizedActionType,
        out string normalizedStorageValue)
    {
        normalizedActionType = default;
        normalizedStorageValue = string.Empty;
        if (!actionType.HasValue)
        {
            return false;
        }

        normalizedActionType = actionType.Value;
        return ToolActionTypeValues.TryParse(actionType.Value.ToString(), out normalizedActionType) &&
            !string.IsNullOrWhiteSpace(normalizedStorageValue = normalizedActionType.ToStorageValue());
    }

    private static bool TryGetEscalationTarget(
        IReadOnlyDictionary<string, JsonNode?> escalationRules,
        out string? escalationTarget)
    {
        escalationTarget = null;
        if (!escalationRules.TryGetValue("escalateTo", out var node) ||
            node is not JsonValue jsonValue ||
            !jsonValue.TryGetValue<string>(out var value) ||
            string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        escalationTarget = value.Trim();
        return true;
    }

    private sealed record ApprovalRequirementPolicy(
        HashSet<string> Actions,
        HashSet<string> Tools,
        HashSet<string> Scopes);
}
