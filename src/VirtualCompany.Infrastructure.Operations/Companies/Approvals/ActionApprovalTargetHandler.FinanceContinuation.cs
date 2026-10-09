using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using static VirtualCompany.Infrastructure.Companies.ApprovalPayloadValues;

namespace VirtualCompany.Infrastructure.Companies;

public sealed partial class ActionApprovalTargetHandler
{
    private bool IsFinanceToolAttempt(ToolExecutionAttempt attempt) => _serviceProvider.GetRequiredService<ICompanyToolRegistry>().TryGetTool(attempt.ToolName, out var registration) && registration.Scopes.Contains("finance");
    private static bool IsAmbiguousProviderResult(ToolExecutionResult result) => string.Equals(result.Status, ToolExecutionStatus.ReconciliationRequired.ToStorageValue(), StringComparison.OrdinalIgnoreCase) || result.ErrorCode?.Contains("reconciliation_required", StringComparison.OrdinalIgnoreCase) == true || result.ErrorCode?.Contains("ambiguous", StringComparison.OrdinalIgnoreCase) == true || result.Metadata?.TryGetValue("providerReconciliationRequired", out var node) == true && node is JsonValue value && value.TryGetValue<bool>(out var required) && required;
    private async Task<FinanceContinuationValidation> RevalidateFinanceContinuationAsync(ApprovalRequest approval, ToolExecutionAttempt attempt, AgentEffectiveAuthorityDto currentAuthority, CancellationToken cancellationToken)
    {
        var evidence = new JsonObject
        {
            ["schemaVersion"] = FinanceApprovalContinuationBinding.SchemaVersion,
            ["approvalRequestId"] = approval.Id,
            ["executionId"] = attempt.Id,
            ["validatedUtc"] = DateTime.UtcNow
        };
        FinanceContinuationValidation Invalid(string reasonCode, string state, string explanation)
        {
            evidence["state"] = state;
            evidence["reasonCode"] = reasonCode;
            FinanceAgentAuthorityTelemetry.RecordApproval(attempt.ToolName, "stale", reasonCode);
            return new FinanceContinuationValidation(false, reasonCode, explanation, evidence);
        }

        if (!approval.ThresholdContext.TryGetValue("approvalBinding", out var bindingNode) || bindingNode is not JsonObject binding || !string.Equals(FinanceApprovalContinuationBinding.ReadBindingString(binding, "schemaVersion"), FinanceApprovalContinuationBinding.SchemaVersion, StringComparison.Ordinal))
        {
            return Invalid(FinanceApprovalContinuationReasonCodes.BindingMissing, "binding_missing_or_invalid", "The Finance approval is not bound to a current immutable action. Create and review a new request.");
        }

        FinanceAutonomyApprovalContextDto? autonomyContext = null;
        if (binding["financeAutonomy"] is JsonObject autonomyNode)
        {
            try
            {
                autonomyContext = JsonSerializer.Deserialize<FinanceAutonomyApprovalContextDto>(autonomyNode.ToJsonString(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
                return Invalid(FinanceApprovalContinuationReasonCodes.BindingMismatch, "finance_autonomy_context_invalid", "The autonomous Finance approval context is invalid. Create and review a new request.");
            }

            if (autonomyContext is null)
                return Invalid(FinanceApprovalContinuationReasonCodes.BindingMismatch, "finance_autonomy_context_missing", "The autonomous Finance approval context is missing. Create and review a new request.");
            var autonomyState = await _dbContext.FinanceAutonomyRunSteps.IgnoreQueryFilters().AsNoTracking().Where(step => step.CompanyId == approval.CompanyId && step.Id == autonomyContext.StepId && step.RunId == autonomyContext.RunId).Select(step => new { Step = step, step.Run.AgentId, step.Run.GrantId, step.Run.GrantVersionId, step.Run.GrantVersionNumber, step.Run.CapabilityId, step.Run.Trigger, step.Run.PlanHash, step.Run.PlanVersion, step.Run.EvidenceHash, step.Run.EvidenceObservedUtc, step.Run.BudgetHash, step.Run.PolicyVersion, step.Run.CatalogueVersion, ActionCount = step.Run.Steps.Count, RunStatus = step.Run.Status }).SingleOrDefaultAsync(cancellationToken);
            var exactAutonomyState = autonomyState is not null && autonomyState.AgentId == attempt.AgentId && autonomyState.GrantId == autonomyContext.GrantId && autonomyState.GrantVersionId == autonomyContext.GrantVersionId && autonomyState.GrantVersionNumber == autonomyContext.GrantVersionNumber && string.Equals(autonomyState.CapabilityId, autonomyContext.CapabilityId, StringComparison.Ordinal) && string.Equals(autonomyState.Trigger, autonomyContext.Trigger, StringComparison.Ordinal) && string.Equals(autonomyState.PlanHash, autonomyContext.PlanHash, StringComparison.Ordinal) && string.Equals(autonomyState.PlanVersion, autonomyContext.PlanVersion, StringComparison.Ordinal) && string.Equals(autonomyState.EvidenceHash, autonomyContext.EvidenceHash, StringComparison.Ordinal) && autonomyState.EvidenceObservedUtc == autonomyContext.EvidenceObservedUtc && string.Equals(autonomyState.BudgetHash, autonomyContext.BudgetHash, StringComparison.Ordinal) && string.Equals(autonomyState.PolicyVersion, autonomyContext.AutonomyPolicyVersion, StringComparison.Ordinal) && string.Equals(autonomyState.CatalogueVersion, autonomyContext.CatalogueVersion, StringComparison.Ordinal) && autonomyState.RunStatus == FinanceAutonomyRunStatus.AwaitingApproval && autonomyState.Step.Status == FinanceAutonomyStepStatus.AwaitingApproval && autonomyState.Step.ApprovalRequestId == approval.Id && autonomyState.Step.ToolExecutionAttemptId == attempt.Id && string.Equals(autonomyState.Step.StepKey, autonomyContext.StepKey, StringComparison.Ordinal) && string.Equals(autonomyState.Step.RequestedEffectHash, autonomyContext.RequestedEffectHash, StringComparison.Ordinal) && string.Equals(autonomyState.Step.BusinessIdempotencyKey, autonomyContext.BusinessIdempotencyKey, StringComparison.Ordinal) && autonomyState.Step.AttemptCount == autonomyContext.AttemptNumber && autonomyState.ActionCount == autonomyContext.ActionCount;
            if (!exactAutonomyState)
                return Invalid(FinanceApprovalContinuationReasonCodes.BindingMismatch, "finance_autonomy_run_or_step_changed", "The approved autonomous plan or step changed after review. Create and review a new request.");
            var currentAutonomyDecision = await _serviceProvider.GetRequiredService<IFinanceAutonomyPolicyEvaluator>().EvaluateAsync(new FinanceAutonomyEvaluationRequest(approval.CompanyId, attempt.AgentId, autonomyContext.CapabilityId, autonomyContext.Trigger, autonomyState!.Step.ActionClass, attempt.ToolName, EvidenceObservedUtc: autonomyContext.EvidenceObservedUtc, ActionCount: autonomyContext.ActionCount), cancellationToken);
            if (!currentAutonomyDecision.IsAllowed || currentAutonomyDecision.GrantId != autonomyContext.GrantId || currentAutonomyDecision.GrantVersionId != autonomyContext.GrantVersionId || currentAutonomyDecision.GrantVersionNumber != autonomyContext.GrantVersionNumber || !string.Equals(currentAutonomyDecision.PolicyVersion, autonomyContext.AutonomyPolicyVersion, StringComparison.Ordinal) || !string.Equals(currentAutonomyDecision.CatalogueVersion, autonomyContext.CatalogueVersion, StringComparison.Ordinal) || !string.Equals(currentAutonomyDecision.AuthorityVersion, currentAuthority.AuthorityVersion, StringComparison.Ordinal) || !string.Equals(currentAutonomyDecision.AuthorityHash, currentAuthority.AuthorityHash, StringComparison.Ordinal))
                return Invalid(FinanceApprovalContinuationReasonCodes.EligibilityFailed, currentAutonomyDecision.ReasonCode, "The autonomous Finance grant, eligibility, evidence, or human-only boundary changed. Create and review a new request.");
            var operatingBlocked = await _dbContext.CompanyOperatingConfigurations.IgnoreQueryFilters().AsNoTracking().AnyAsync(item => item.CompanyId == approval.CompanyId && (item.EmergencyStopped || item.IsPaused), cancellationToken);
            var circuitOpen = await _dbContext.FinanceAutonomyCircuitBreakers.IgnoreQueryFilters().AsNoTracking().AnyAsync(item => item.CompanyId == approval.CompanyId && item.AgentId == attempt.AgentId && item.CapabilityId == autonomyContext.CapabilityId && item.Status == FinanceAutonomyCircuitStatus.Open, cancellationToken);
            var budgetReservationValid = await _dbContext.FinanceAutonomyBudgetReservations.IgnoreQueryFilters().AsNoTracking().AnyAsync(item => item.CompanyId == approval.CompanyId && item.RunId == autonomyContext.RunId && item.StepId == autonomyContext.StepId && item.AttemptNumber == autonomyContext.AttemptNumber && item.Status == FinanceAutonomyBudgetReservationStatus.Reconciled, cancellationToken);
            if (operatingBlocked || circuitOpen || !budgetReservationValid)
                return Invalid(FinanceApprovalContinuationReasonCodes.EligibilityFailed, operatingBlocked ? "finance_autonomy_operating_boundary_blocked" : circuitOpen ? FinanceAutonomyBudgetReasonCodes.CircuitOpen : "finance_autonomy_budget_reservation_invalid", "The autonomous Finance operating or budget boundary no longer permits continuation.");
        }

        var expiresUtc = FinanceApprovalContinuationBinding.ReadBindingUtc(binding, "expiresUtc");
        evidence["expiresUtc"] = expiresUtc;
        if (!expiresUtc.HasValue || expiresUtc.Value <= DateTime.UtcNow)
        {
            return Invalid(FinanceApprovalContinuationReasonCodes.Expired, "expired", "The Finance approval expired. Create and review a new request.");
        }

        var registry = _serviceProvider.GetRequiredService<ICompanyToolRegistry>();
        if (!registry.TryGetTool(attempt.ToolName, out var registration) || registration.FinanceRiskClassification is null)
        {
            return Invalid(FinanceApprovalContinuationReasonCodes.PolicyStale, "tool_or_risk_classification_missing", "The Finance tool policy changed after review. Create and review a new request.");
        }

        var exactBindingMatches = FinanceApprovalContinuationBinding.ReadBindingGuid(binding, "companyId") == approval.CompanyId && FinanceApprovalContinuationBinding.ReadBindingGuid(binding, "approvalRequestId") == approval.Id && FinanceApprovalContinuationBinding.ReadBindingGuid(binding, "executionId") == attempt.Id && FinanceApprovalContinuationBinding.ReadBindingGuid(binding, "agentId") == attempt.AgentId && string.Equals(FinanceApprovalContinuationBinding.ReadBindingString(binding, "toolName"), attempt.ToolName, StringComparison.Ordinal) && string.Equals(FinanceApprovalContinuationBinding.ReadBindingString(binding, "toolVersion"), attempt.ToolVersion, StringComparison.Ordinal) && string.Equals(FinanceApprovalContinuationBinding.ReadBindingString(binding, "actionType"), attempt.ActionType.ToStorageValue(), StringComparison.Ordinal) && string.Equals(FinanceApprovalContinuationBinding.ReadBindingString(binding, "scope"), attempt.Scope, StringComparison.Ordinal) && string.Equals(FinanceApprovalContinuationBinding.ReadBindingString(binding, "riskTier"), registration.FinanceRiskClassification.RiskTier, StringComparison.Ordinal) && string.Equals(FinanceApprovalContinuationBinding.ReadBindingString(binding, "requiredActorPermission"), registration.FinanceRiskClassification.RequiredActorPermission, StringComparison.Ordinal) && string.Equals(FinanceApprovalContinuationBinding.ReadBindingString(binding, "approvalBehavior"), registration.FinanceRiskClassification.DefaultApprovalBehavior, StringComparison.Ordinal) && string.Equals(FinanceApprovalContinuationBinding.ReadBindingString(binding, "externalSideEffectClass"), registration.FinanceRiskClassification.ExternalSideEffectClassification, StringComparison.Ordinal) && FinanceApprovalContinuationBinding.ReadBindingBoolean(binding, "sensitiveAction") == registration.SensitiveAction && FinanceApprovalContinuationBinding.ReadBindingBoolean(binding, "segregationRequired") == registration.FinanceRiskClassification.RequiresSegregation && string.Equals(registration.Version, attempt.ToolVersion, StringComparison.Ordinal);
        if (!exactBindingMatches)
        {
            return Invalid(FinanceApprovalContinuationReasonCodes.BindingMismatch, "action_binding_mismatch", "The approved Finance action no longer matches the current attempt. Create and review a new request.");
        }

        var approvedPayloadHash = FinanceApprovalContinuationBinding.ReadBindingString(binding, "normalizedPayloadHash");
        var currentPayloadHash = FinanceApprovalContinuationBinding.ComputePayloadHash(attempt.RequestPayload);
        evidence["approvedPayloadHash"] = approvedPayloadHash;
        evidence["currentPayloadHash"] = currentPayloadHash;
        if (string.IsNullOrWhiteSpace(approvedPayloadHash) || !string.Equals(approvedPayloadHash, currentPayloadHash, StringComparison.Ordinal))
        {
            return Invalid(FinanceApprovalContinuationReasonCodes.BindingMismatch, "payload_hash_mismatch", "The approved Finance payload changed after review. Create and review a new request.");
        }

        var approvedBusinessIdempotencyKey = FinanceApprovalContinuationBinding.ReadBindingString(binding, "businessIdempotencyKey");
        var currentBusinessIdempotencyKey = FinanceApprovalContinuationBinding.ComputeBusinessIdempotencyKey(attempt);
        var approvedContinuationKey = FinanceApprovalContinuationBinding.ReadBindingString(binding, "continuationKey");
        var currentContinuationKey = FinanceApprovalContinuationBinding.ComputeContinuationKey(attempt, currentPayloadHash, registration.FinanceRiskClassification.PolicyVersion);
        if (string.IsNullOrWhiteSpace(approvedBusinessIdempotencyKey) || !string.Equals(approvedBusinessIdempotencyKey, currentBusinessIdempotencyKey, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(approvedContinuationKey) || !string.Equals(approvedContinuationKey, currentContinuationKey, StringComparison.Ordinal))
        {
            return Invalid(FinanceApprovalContinuationReasonCodes.BindingMismatch, "idempotency_or_continuation_binding_mismatch", "The approved Finance continuation identity changed after review. Create and review a new request.");
        }

        var approvedAuthorityVersion = FinanceApprovalContinuationBinding.ReadBindingString(binding, "effectiveAuthorityVersion");
        var approvedAuthorityHash = FinanceApprovalContinuationBinding.ReadBindingString(binding, "effectiveAuthorityHash");
        if (string.IsNullOrWhiteSpace(approvedAuthorityHash) || !string.Equals(approvedAuthorityVersion, currentAuthority.AuthorityVersion, StringComparison.Ordinal) || !string.Equals(approvedAuthorityHash, currentAuthority.AuthorityHash, StringComparison.Ordinal))
        {
            return Invalid(FinanceApprovalContinuationReasonCodes.AuthorityStale, "authority_changed", "Agent authority changed after approval was requested. Create and review a new request.");
        }

        var currentTargets = await FinanceApprovalContinuationBinding.BuildTargetSnapshotAsync(_dbContext, attempt, cancellationToken);
        var approvedTargetHash = FinanceApprovalContinuationBinding.ReadBindingString(binding, "targetSnapshotHash");
        var approvedTargets = binding["targetSnapshot"] as JsonArray;
        var currentTargetHash = FinanceApprovalContinuationBinding.ComputeTargetSnapshotHash(currentTargets);
        evidence["approvedTargetSnapshotHash"] = approvedTargetHash;
        evidence["currentTargetSnapshotHash"] = currentTargetHash;
        if (approvedTargets is null || !string.Equals(approvedTargetHash, FinanceApprovalContinuationBinding.ComputeTargetSnapshotHash(approvedTargets), StringComparison.Ordinal) || currentTargets.Any(item => item is JsonObject target && target["exists"]?.GetValue<bool>() != true) || string.IsNullOrWhiteSpace(approvedTargetHash) || !string.Equals(approvedTargetHash, currentTargetHash, StringComparison.Ordinal))
        {
            return Invalid(FinanceApprovalContinuationReasonCodes.TargetStale, "target_changed_or_missing", "Finance target evidence changed after review. The approval is stale and cannot be edited into validity.");
        }

        var currentIntegrationHash = await FinanceApprovalContinuationBinding.BuildIntegrationStateHashAsync(_dbContext, approval.CompanyId, registration.FinanceRiskClassification, cancellationToken);
        var approvedIntegrationHash = FinanceApprovalContinuationBinding.ReadBindingString(binding, "integrationStateHash");
        evidence["approvedIntegrationStateHash"] = approvedIntegrationHash;
        evidence["currentIntegrationStateHash"] = currentIntegrationHash;
        if (string.IsNullOrWhiteSpace(approvedIntegrationHash) || !string.Equals(approvedIntegrationHash, currentIntegrationHash, StringComparison.Ordinal))
        {
            return Invalid(FinanceApprovalContinuationReasonCodes.IntegrationStale, "integration_state_changed", "Finance integration state changed after review. Create and review a new request.");
        }

        var currentToolAuthority = currentAuthority.Find(attempt.ToolName, attempt.ActionType, attempt.Scope);
        if (currentToolAuthority is null || !currentToolAuthority.IsUsable)
        {
            return Invalid(FinanceApprovalContinuationReasonCodes.EligibilityFailed, "effective_tool_authority_not_usable", "The Finance action is no longer eligible under the effective agent authority.");
        }

        var runtimeProfile = await _serviceProvider.GetRequiredService<IAgentRuntimeProfileResolver>().GetCurrentProfileAsync(approval.CompanyId, attempt.AgentId, cancellationToken);
        var usableTools = currentAuthority.Tools.Where(item => item.IsUsable).ToArray();
        var toolPermissions = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["allowed"] = new JsonArray(usableTools.Select(item => (JsonNode? )JsonValue.Create(item.ToolName)).ToArray()),
            ["actions"] = new JsonArray(usableTools.Select(item => item.ActionType).Distinct(StringComparer.OrdinalIgnoreCase).Select(item => (JsonNode? )JsonValue.Create(item)).ToArray()),
            ["denied"] = new JsonArray(),
            ["deniedActions"] = new JsonArray()
        };
        var dataScopes = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in usableTools.Select(item => item.ActionType).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            dataScopes[action] = new JsonArray(usableTools.Where(item => string.Equals(item.ActionType, action, StringComparison.OrdinalIgnoreCase)).Select(item => item.Scope).Where(scope => !string.IsNullOrWhiteSpace(scope)).Distinct(StringComparer.OrdinalIgnoreCase).Select(scope => (JsonNode? )JsonValue.Create(scope)).ToArray());
        }

        var riskContext = await FinanceApprovalContinuationBinding.BuildRiskContextAsync(_dbContext, attempt, cancellationToken);
        if (!riskContext.BackendVerified)
        {
            return Invalid(FinanceApprovalContinuationReasonCodes.EligibilityFailed, "finance_eligibility_unverified", "Current Finance eligibility evidence could not be verified.");
        }

        var policyDecision = _serviceProvider.GetRequiredService<IPolicyGuardrailEngine>().Evaluate(new PolicyEvaluationRequest(approval.CompanyId, attempt.AgentId, runtimeProfile.CompanyId, runtimeProfile.Status, runtimeProfile.AutonomyLevel, runtimeProfile.CanReceiveAssignments, toolPermissions, dataScopes, CloneNodes(runtimeProfile.ApprovalThresholds), CloneNodes(runtimeProfile.EscalationRules), attempt.ToolName, attempt.ActionType, attempt.Scope, CloneNodes(attempt.RequestPayload), TryReadString(approval.ThresholdContext, "thresholdCategory"), TryReadString(approval.ThresholdContext, "thresholdKey"), TryGetDecimal(approval.ThresholdContext, "thresholdValue"), SensitiveAction: true, ExecutionId: attempt.Id, CorrelationId: attempt.CorrelationId, TrustedToolApprovalRequired: false, TriggerLogic: CloneNodes(runtimeProfile.TriggerLogic), FinanceRiskContext: riskContext));
        var approvedRiskVersion = FinanceApprovalContinuationBinding.ReadBindingString(binding, "riskPolicyVersion");
        var approvedCompanyPolicyVersion = FinanceApprovalContinuationBinding.ReadBindingString(binding, "financeApprovalPolicyVersion");
        var currentRiskVersion = TryReadString(policyDecision.Metadata, "riskPolicyVersion");
        var currentCompanyPolicyVersion = TryReadString(policyDecision.Metadata, "financeApprovalPolicyVersion");
        var approvedThresholdHash = FinanceApprovalContinuationBinding.ReadBindingString(binding, "thresholdEvaluationHash");
        var currentThresholdHash = FinanceApprovalContinuationBinding.ComputeThresholdEvaluationHash(policyDecision);
        evidence["approvedRiskPolicyVersion"] = approvedRiskVersion;
        evidence["currentRiskPolicyVersion"] = currentRiskVersion;
        evidence["approvedFinanceApprovalPolicyVersion"] = approvedCompanyPolicyVersion;
        evidence["currentFinanceApprovalPolicyVersion"] = currentCompanyPolicyVersion;
        evidence["approvedThresholdEvaluationHash"] = approvedThresholdHash;
        evidence["currentThresholdEvaluationHash"] = currentThresholdHash;
        if (!string.Equals(policyDecision.Outcome, PolicyDecisionOutcomeValues.RequireApproval, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(approvedRiskVersion) || !string.Equals(approvedRiskVersion, currentRiskVersion, StringComparison.Ordinal) || !string.Equals(approvedCompanyPolicyVersion, currentCompanyPolicyVersion, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(approvedThresholdHash) || !string.Equals(approvedThresholdHash, currentThresholdHash, StringComparison.Ordinal))
        {
            return Invalid(FinanceApprovalContinuationReasonCodes.PolicyStale, "policy_or_threshold_evidence_changed", "Finance policy or threshold evidence changed after review. Create and review a new request.");
        }

        evidence["state"] = "valid";
        evidence["reasonCode"] = "finance_approval_continuation_valid";
        return new FinanceContinuationValidation(true, "finance_approval_continuation_valid", "The Finance approval remains bound to the current action and evidence.", evidence);
    }

    private Task WriteFinanceAuthorizationAuditAsync(FinanceAgentAuthorizationDecisionDto decision, string? correlationId, CancellationToken cancellationToken) => _auditEventWriter.WriteAsync(new AuditEventWriteRequest(decision.CompanyId, string.Equals(decision.ActorType, FinanceAgentActorTypes.Human, StringComparison.Ordinal) ? AuditActorTypes.User : AuditActorTypes.System, decision.ActorId, AuditEventActions.FinanceAgentToolAuthorizationEvaluated, AuditTargetTypes.AgentToolExecution, decision.ExecutionId.ToString("N"), decision.IsAllowed ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Denied, DataSources: ["approval_continuation", "finance_actor_authorization", "company_membership"], CorrelationId: correlationId, RationaleSummary: decision.Explanation, Metadata: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["authorizationOutcome"] = decision.Outcome, ["authorizationReasonCode"] = decision.ReasonCode, ["authorizationPolicyVersion"] = decision.PolicyVersion, ["authorizationEvidence"] = string.Join(",", decision.Evidence.Select(static item => $"{item.Type}:{item.Reference}:{item.Result}")), ["actorType"] = decision.ActorType, ["membershipState"] = decision.MembershipState, ["toolName"] = decision.ToolName, ["actionType"] = decision.ActionType, ["approvedContinuation"] = "true", ["delegationAuthorityId"] = decision.DelegationAuthorityId?.ToString("N") }), cancellationToken);
    private sealed record FinanceContinuationValidation(bool IsValid, string ReasonCode, string Explanation, JsonObject Evidence);
}
