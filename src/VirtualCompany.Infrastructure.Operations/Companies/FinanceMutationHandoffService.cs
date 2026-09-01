using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class FinanceMutationHandoffOptions
{
    public const string SectionName = "FinanceMutationHandoff";
    public int ConfirmationLifetimeSeconds { get; set; } = 300;
}

public sealed class FinanceMutationConfirmationRegistry
{
    private readonly ConcurrentDictionary<string, Entry> _confirmations = new(StringComparer.Ordinal);

    public async Task<FinanceMutationConfirmationResult> RunOnceAsync(
        string tokenHash,
        Func<Task<FinanceMutationConfirmationResult>> factory)
    {
        var candidate = new Entry(new Lazy<Task<FinanceMutationConfirmationResult>>(
            factory, LazyThreadSafetyMode.ExecutionAndPublication));
        var entry = _confirmations.GetOrAdd(tokenHash, candidate);
        var result = await entry.Result.Value.ConfigureAwait(false);
        return result with { IsDuplicate = !ReferenceEquals(candidate, entry) };
    }

    private sealed record Entry(Lazy<Task<FinanceMutationConfirmationResult>> Result);
}

public sealed class FinanceMutationHandoffService : IFinanceMutationHandoffService
{
    private static readonly IReadOnlySet<string> DirectConversationExcludedSideEffects =
        new HashSet<string>(StringComparer.Ordinal)
        {
            FinanceToolExternalSideEffects.PaymentAction,
            FinanceToolExternalSideEffects.ComplianceSubmission,
            FinanceToolExternalSideEffects.PeriodCloseOrLock,
            FinanceToolExternalSideEffects.YearEnd
        };

    private readonly IFinanceToolPlanner _planner;
    private readonly IAgentToolExecutionService _executor;
    private readonly IAgentEffectiveAuthorityResolver _authorityResolver;
    private readonly IAgentRuntimeProfileResolver _runtimeProfileResolver;
    private readonly ICompanyToolRegistry _toolRegistry;
    private readonly IPolicyGuardrailEngine _guardrail;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAuditEventWriter _audit;
    private readonly FinanceMutationConfirmationRegistry _registry;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _timeProvider;
    private readonly FinanceMutationHandoffOptions _options;

    public FinanceMutationHandoffService(
        IFinanceToolPlanner planner,
        IAgentToolExecutionService executor,
        IAgentEffectiveAuthorityResolver authorityResolver,
        IAgentRuntimeProfileResolver runtimeProfileResolver,
        ICompanyToolRegistry toolRegistry,
        IPolicyGuardrailEngine guardrail,
        ICurrentUserAccessor currentUser,
        VirtualCompanyDbContext dbContext,
        IAuditEventWriter audit,
        FinanceMutationConfirmationRegistry registry,
        IDataProtectionProvider dataProtectionProvider,
        TimeProvider timeProvider,
        IOptions<FinanceMutationHandoffOptions> options)
    {
        _planner = planner;
        _executor = executor;
        _authorityResolver = authorityResolver;
        _runtimeProfileResolver = runtimeProfileResolver;
        _toolRegistry = toolRegistry;
        _guardrail = guardrail;
        _currentUser = currentUser;
        _dbContext = dbContext;
        _audit = audit;
        _registry = registry;
        _protector = dataProtectionProvider.CreateProtector(
            $"VirtualCompany.Finance.MutationConfirmation.{FinanceMutationHandoffVersions.ConfirmationV1}");
        _timeProvider = timeProvider;
        _options = options.Value;
    }

    public async Task<FinanceMutationPreviewResult> PreviewAsync(
        PreviewFinanceMutationRequest request,
        CancellationToken cancellationToken)
    {
        ValidatePreview(request);
        var actorId = RequireActor();
        var plan = await _planner.PlanAsync(new FinanceToolPlanRequest(
            request.CompanyId, request.AgentId, request.UserRequest, request.Context, request.TaskId,
            request.ConversationId, request.CorrelationId, request.References), cancellationToken);
        var previewId = Guid.NewGuid();
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        if (plan.State is not (FinanceToolPlanStates.ConfirmationRequired or FinanceToolPlanStates.ApprovalRequired) ||
            plan.Steps.Count == 0 || plan.Steps.Any(step => !string.Equals(step.ActionType, "execute", StringComparison.Ordinal)))
        {
            return new FinanceMutationPreviewResult(previewId, FinanceMutationHandoffVersions.ContractV1,
                plan.PlanId, plan.Revision, FinanceMutationPreviewStates.Unsupported, plan.ReasonCode,
                "This request is not an executable Finance mutation. Read and recommendation requests use the non-mutating conversation path.",
                [], plan.EffectiveAuthorityVersion, plan.EffectiveAuthorityHash, plan.PlanningContextHash, now);
        }

        var profile = await _runtimeProfileResolver.GetCurrentProfileAsync(
            request.CompanyId, request.AgentId, cancellationToken, correlationId: plan.CorrelationId);
        var policyHash = ComputePolicyHash(profile);
        var previews = new List<FinanceMutationStepPreview>();

        foreach (var step in plan.Steps.OrderBy(item => item.Order))
        {
            if (!_toolRegistry.TryGetTool(step.ToolName, out var registration) ||
                registration.FinanceRiskClassification is null ||
                !string.Equals(registration.Version, step.ToolVersion, StringComparison.Ordinal))
            {
                return Unsupported(plan, previewId, now, "The execute tool no longer has a current Finance risk contract.");
            }

            var risk = registration.FinanceRiskClassification;
            if (DirectConversationExcludedSideEffects.Contains(risk.ExternalSideEffectClassification))
            {
                return Unsupported(plan, previewId, now,
                    "Payments, statutory submissions, final locks, and year-end actions cannot be executed directly from conversation.");
            }

            var payload = Clone(step.NormalizedArguments);
            var attempt = CreateAttempt(request.CompanyId, request.AgentId, step, payload, plan.CorrelationId);
            var target = await ReadTargetAsync(attempt, risk, cancellationToken);
            if (!target.State.Exists)
                return Unsupported(plan, previewId, now, "The authoritative mutation target is no longer available.");

            var boundaries = ResolvePolicyBoundaries(profile, await _authorityResolver.ResolveAsync(
                request.CompanyId, request.AgentId, cancellationToken));
            var riskContext = await FinanceApprovalContinuationBinding.BuildRiskContextAsync(
                _dbContext, attempt, cancellationToken);
            var policyDecision = _guardrail.Evaluate(new PolicyEvaluationRequest(
                request.CompanyId, request.AgentId, profile.CompanyId, profile.Status, profile.AutonomyLevel,
                profile.CanReceiveAssignments, boundaries.ToolPermissions, boundaries.DataScopes,
                Clone(profile.ApprovalThresholds), Clone(profile.EscalationRules), step.ToolName,
                ToolActionType.Execute, step.Scope, payload, null, null, null, registration.SensitiveAction,
                previewId, plan.CorrelationId, false, Clone(profile.TriggerLogic), riskContext));
            if (string.Equals(policyDecision.Outcome, PolicyDecisionOutcomeValues.Deny, StringComparison.Ordinal))
                return Unsupported(plan, previewId, now,
                    "Current Finance policy denies this proposed mutation. No confirmation token was issued.");

            var expiresUtc = now.AddSeconds(Math.Clamp(_options.ConfirmationLifetimeSeconds, 30, 900));
            var confirmationId = Guid.NewGuid();
            var claims = new ConfirmationClaims(
                FinanceMutationHandoffVersions.ConfirmationV1, confirmationId, previewId, plan.PlanId,
                plan.Revision, step.StepId, request.CompanyId, request.AgentId, actorId, step.ToolName,
                step.ToolVersion, step.ActionType, step.Scope, payload,
                FinanceApprovalContinuationBinding.ComputePayloadHash(payload), plan.EffectiveAuthorityVersion,
                plan.EffectiveAuthorityHash, plan.PlanningContextHash, policyHash, risk.PolicyVersion,
                target.SnapshotHash, target.IntegrationStateHash, request.TaskId, plan.CorrelationId, now, expiresUtc);
            var token = _protector.Protect(JsonSerializer.Serialize(claims));
            var approvalRequired = string.Equals(policyDecision.Outcome,
                PolicyDecisionOutcomeValues.RequireApproval, StringComparison.Ordinal);
            previews.Add(new FinanceMutationStepPreview(
                step.StepId, step.Order, step.ToolName, step.ToolVersion, step.ActionType, step.Scope,
                target.State, payload, step.ExpectedEffect, risk.Reversibility, risk.RiskTier,
                policyDecision.Outcome,
                risk.RequiredActorPermission,
                approvalRequired ? "existing_p0_approval_workflow" : "confirming_actor_then_p0_revalidation",
                EvidenceAgeSeconds(target.State.UpdatedUtc, now), token, expiresUtc));
        }

        var previewState = previews.Any(step => step.PolicyOutcome == PolicyDecisionOutcomeValues.RequireApproval)
            ? FinanceMutationPreviewStates.ApprovalRequired
            : FinanceMutationPreviewStates.Ready;
        await WriteAuditAsync(request.CompanyId, actorId, AuditEventActions.FinanceMutationPreviewCreated,
            previewId, AuditEventOutcomes.Succeeded, plan.CorrelationId,
            "A deterministic Finance mutation preview was created. No mutation was performed.",
            new Dictionary<string, string?>
            {
                ["planId"] = plan.PlanId.ToString("N"),
                ["planRevision"] = plan.Revision.ToString(),
                ["stepCount"] = previews.Count.ToString(),
                ["lifecycleStage"] = "proposal_preview"
            }, cancellationToken);
        return new FinanceMutationPreviewResult(previewId, FinanceMutationHandoffVersions.ContractV1,
            plan.PlanId, plan.Revision, previewState,
            previewState == FinanceMutationPreviewStates.ApprovalRequired
                ? "finance_mutation_approval_handoff_previewed"
                : "finance_mutation_confirmation_required",
            previewState == FinanceMutationPreviewStates.ApprovalRequired
                ? "This is a proposal preview. Confirmation will request approval through the existing workflow; it will not execute the mutation."
                : "This is a proposal preview. Confirm the exact step before the token expires to request P0-revalidated execution.",
            previews, plan.EffectiveAuthorityVersion, plan.EffectiveAuthorityHash, plan.PlanningContextHash, now);
    }

    public async Task<FinanceMutationConfirmationResult> ConfirmAsync(
        ConfirmFinanceMutationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CompanyId == Guid.Empty || request.AgentId == Guid.Empty || string.IsNullOrWhiteSpace(request.ConfirmationToken))
            throw new ArgumentException("CompanyId, AgentId, and ConfirmationToken are required.", nameof(request));
        var actorId = RequireActor();
        if (!TryReadClaims(request.ConfirmationToken, out var claims) ||
            claims.CompanyId != request.CompanyId || claims.AgentId != request.AgentId || claims.ActorId != actorId)
        {
            return InvalidResult(FinanceMutationConfirmationStates.Invalid, "finance_confirmation_invalid_or_actor_mismatch",
                "This confirmation token is invalid or belongs to a different actor.");
        }

        if (claims.ExpiresUtc <= _timeProvider.GetUtcNow().UtcDateTime)
            return Result(claims, FinanceMutationConfirmationStates.Expired, "finance_confirmation_expired",
                "The confirmation expired. Refresh the preview and review the current state again.");

        var tokenHash = Hash(request.ConfirmationToken);
        return await _registry.RunOnceAsync(tokenHash,
            () => ConfirmOnceAsync(claims, request.CorrelationId, cancellationToken));
    }

    public async Task<FinanceMutationConfirmationResult> ReconcileAsync(
        ReconcileFinanceMutationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actorId = RequireActor();
        if (!TryReadClaims(request.ConfirmationToken, out var claims) || claims.CompanyId != request.CompanyId ||
            claims.AgentId != request.AgentId || claims.ActorId != actorId)
            return InvalidResult(FinanceMutationConfirmationStates.Invalid, "finance_confirmation_invalid_or_actor_mismatch",
                "This reconciliation token is invalid or belongs to a different actor.");

        if (!_toolRegistry.TryGetTool(claims.ToolName, out var registration) ||
            registration.FinanceRiskClassification is null)
            return Result(claims, FinanceMutationConfirmationStates.Stale, "finance_confirmation_tool_stale",
                "The Finance tool contract changed. Refresh the preview.");

        var attempt = CreateAttempt(claims);
        var current = await ReadTargetAsync(attempt, registration.FinanceRiskClassification, cancellationToken);
        var state = ClassifyAuthoritativeState(current.State, current.SnapshotHash, claims.TargetSnapshotHash);
        var result = Result(claims, state,
            state == FinanceMutationConfirmationStates.Executed
                ? "finance_mutation_authoritatively_reconciled"
                : "finance_mutation_still_reconciling",
            state == FinanceMutationConfirmationStates.Executed
                ? "The authoritative Finance state now reflects a completed change."
                : "The authoritative Finance state does not yet prove completion; the action remains pending or ambiguous.",
            authoritativeState: current.State);
        await WriteAuditAsync(claims.CompanyId, actorId, AuditEventActions.FinanceMutationReconciled,
            claims.ConfirmationId, state == FinanceMutationConfirmationStates.Executed
                ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Pending, claims.CorrelationId,
            result.SafeExplanation, new Dictionary<string, string?>
            {
                ["planId"] = claims.PlanId.ToString("N"), ["stepId"] = claims.StepId,
                ["lifecycleStage"] = state
            }, cancellationToken);
        return result;
    }

    private async Task<FinanceMutationConfirmationResult> ConfirmOnceAsync(
        ConfirmationClaims claims,
        string? requestedCorrelationId,
        CancellationToken cancellationToken)
    {
        if (!_toolRegistry.TryGetTool(claims.ToolName, out var registration) ||
            registration.FinanceRiskClassification is null ||
            !string.Equals(registration.Version, claims.ToolVersion, StringComparison.Ordinal) ||
            !string.Equals(registration.FinanceRiskClassification.PolicyVersion, claims.RiskPolicyVersion, StringComparison.Ordinal))
            return Result(claims, FinanceMutationConfirmationStates.Stale, "finance_confirmation_policy_stale",
                "The Finance tool or risk policy changed. Refresh the preview before confirming.");

        var authority = await _authorityResolver.ResolveAsync(claims.CompanyId, claims.AgentId, cancellationToken);
        if (!string.Equals(authority.AuthorityVersion, claims.AuthorityVersion, StringComparison.Ordinal) ||
            !string.Equals(authority.AuthorityHash, claims.AuthorityHash, StringComparison.Ordinal))
            return Result(claims, FinanceMutationConfirmationStates.Stale, "finance_confirmation_authority_stale",
                "Agent permissions changed. Refresh the preview before confirming.");

        var profile = await _runtimeProfileResolver.GetCurrentProfileAsync(
            claims.CompanyId, claims.AgentId, cancellationToken, correlationId: claims.CorrelationId);
        if (!string.Equals(ComputePolicyHash(profile), claims.PolicyHash, StringComparison.Ordinal))
            return Result(claims, FinanceMutationConfirmationStates.Stale, "finance_confirmation_policy_stale",
                "The applicable Finance policy changed. Refresh the preview before confirming.");
        if (!string.Equals(FinanceApprovalContinuationBinding.ComputePayloadHash(claims.Payload),
                claims.PayloadHash, StringComparison.Ordinal))
            return Result(claims, FinanceMutationConfirmationStates.Invalid, "finance_confirmation_payload_invalid",
                "The protected confirmation payload could not be verified.");

        var attempt = CreateAttempt(claims);
        var before = await ReadTargetAsync(attempt, registration.FinanceRiskClassification, cancellationToken);
        if (!string.Equals(before.SnapshotHash, claims.TargetSnapshotHash, StringComparison.Ordinal) ||
            !string.Equals(before.IntegrationStateHash, claims.IntegrationStateHash, StringComparison.Ordinal))
            return Result(claims, FinanceMutationConfirmationStates.Stale, "finance_confirmation_target_stale",
                "The target or integration state changed after preview. Refresh and confirm the new preview.",
                authoritativeState: before.State);

        var correlationId = string.IsNullOrWhiteSpace(requestedCorrelationId)
            ? $"finance-confirmation:{claims.ConfirmationId:N}"
            : requestedCorrelationId.Trim();
        var response = await _executor.ExecuteAsync(claims.CompanyId, claims.AgentId,
            new ExecuteAgentToolCommand(claims.ToolName, claims.ActionType, claims.Scope, Clone(claims.Payload),
                ThresholdCategory: null, ThresholdKey: null, ThresholdValue: null,
                SensitiveAction: registration.SensitiveAction, TaskId: claims.TaskId, CorrelationId: correlationId,
                ExpectedAuthorityVersion: claims.AuthorityVersion, ExpectedAuthorityHash: claims.AuthorityHash),
            cancellationToken);
        var after = await ReadTargetAsync(attempt, registration.FinanceRiskClassification, cancellationToken);

        string state;
        string reason;
        string explanation;
        if (response.ApprovalRequestId.HasValue ||
            string.Equals(response.Status, ToolExecutionStatus.AwaitingApproval.ToStorageValue(), StringComparison.OrdinalIgnoreCase))
        {
            state = FinanceMutationConfirmationStates.ApprovalRequired;
            reason = "finance_mutation_approval_requested";
            explanation = "Confirmation created an approval request. No mutation was performed; only the existing P0 continuation may resume it after approval.";
        }
        else if (string.Equals(response.Status, ToolExecutionStatus.Denied.ToStorageValue(), StringComparison.OrdinalIgnoreCase))
        {
            state = FinanceMutationConfirmationStates.Denied;
            reason = response.Denial?.Code ?? "finance_mutation_denied";
            explanation = response.Message;
        }
        else if (!string.Equals(response.Status, ToolExecutionStatus.Executed.ToStorageValue(), StringComparison.OrdinalIgnoreCase))
        {
            state = string.Equals(response.Status, ToolExecutionStatus.ReconciliationRequired.ToStorageValue(), StringComparison.OrdinalIgnoreCase)
                ? FinanceMutationConfirmationStates.Reconciling
                : FinanceMutationConfirmationStates.Failed;
            reason = state == FinanceMutationConfirmationStates.Reconciling
                ? "finance_mutation_reconciliation_required" : "finance_mutation_failed";
            explanation = state == FinanceMutationConfirmationStates.Reconciling
                ? "The outcome is ambiguous and requires reconciliation against authoritative state."
                : "The Finance mutation failed. No successful outcome is reported.";
        }
        else if (ContainsPendingSemantics(response.ExecutionResult))
        {
            state = FinanceMutationConfirmationStates.Queued;
            reason = "finance_mutation_queued";
            explanation = "The action was accepted for processing, but authoritative completion is not yet proven. Reconcile it before treating it as executed.";
        }
        else
        {
            state = ClassifyAuthoritativeState(after.State, after.SnapshotHash, before.SnapshotHash);
            reason = state == FinanceMutationConfirmationStates.Executed
                ? "finance_mutation_authoritatively_executed" : "finance_mutation_outcome_ambiguous";
            explanation = state == FinanceMutationConfirmationStates.Executed
                ? "The mutation completed and the authoritative Finance target was re-read successfully."
                : "The executor returned, but authoritative state does not prove the requested change. The outcome remains ambiguous.";
        }

        var result = Result(claims, state, reason, explanation, response.ExecutionId,
            response.ApprovalRequestId, response.PolicyDecision.Outcome, after.State);
        await WriteAuditAsync(claims.CompanyId, claims.ActorId,
            AuditEventActions.FinanceMutationConfirmationSubmitted, claims.ConfirmationId,
            state == FinanceMutationConfirmationStates.Executed ? AuditEventOutcomes.Succeeded :
            state == FinanceMutationConfirmationStates.ApprovalRequired || state == FinanceMutationConfirmationStates.Queued ||
            state == FinanceMutationConfirmationStates.Reconciling ? AuditEventOutcomes.Pending : AuditEventOutcomes.Failed,
            correlationId, explanation, new Dictionary<string, string?>
            {
                ["planId"] = claims.PlanId.ToString("N"), ["stepId"] = claims.StepId,
                ["executionId"] = response.ExecutionId.ToString("N"),
                ["approvalRequestId"] = response.ApprovalRequestId?.ToString("N"),
                ["lifecycleStage"] = state, ["policyOutcome"] = response.PolicyDecision.Outcome
            }, cancellationToken);
        return result;
    }

    private async Task<TargetRead> ReadTargetAsync(
        ToolExecutionAttempt attempt,
        FinanceToolRiskClassification risk,
        CancellationToken cancellationToken)
    {
        var snapshot = await FinanceApprovalContinuationBinding.BuildTargetSnapshotAsync(
            _dbContext, attempt, cancellationToken);
        var snapshotHash = FinanceApprovalContinuationBinding.ComputeTargetSnapshotHash(snapshot);
        var integrationHash = await FinanceApprovalContinuationBinding.BuildIntegrationStateHashAsync(
            _dbContext, attempt.CompanyId, risk, cancellationToken);
        var first = snapshot.FirstOrDefault() as JsonObject;
        var entityType = ReadString(first, "entityType") ?? "finance_target";
        var entityId = ReadGuid(first, "entityId");
        var exists = ReadBoolean(first, "exists");
        var version = ReadString(first, "version") ?? "missing";
        var state = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        DateTime? updatedUtc = null;

        if (attempt.ToolName.Equals("categorize_transaction", StringComparison.OrdinalIgnoreCase) && entityId.HasValue)
        {
            var target = await _dbContext.FinanceTransactions.IgnoreQueryFilters().AsNoTracking()
                .Where(item => item.CompanyId == attempt.CompanyId && item.Id == entityId.Value)
                .Select(item => new { item.TransactionType, item.Amount, item.Currency, item.TransactionUtc, item.CreatedUtc })
                .SingleOrDefaultAsync(cancellationToken);
            if (target is not null)
            {
                state["category"] = target.TransactionType;
                state["amount"] = target.Amount;
                state["currency"] = target.Currency;
                state["transactionUtc"] = target.TransactionUtc;
                updatedUtc = target.CreatedUtc;
            }
        }
        else if (attempt.ToolName.Equals("approve_invoice", StringComparison.OrdinalIgnoreCase) && entityId.HasValue)
        {
            var target = await _dbContext.FinanceInvoices.IgnoreQueryFilters().AsNoTracking()
                .Where(item => item.CompanyId == attempt.CompanyId && item.Id == entityId.Value)
                .Select(item => new { item.Status, item.SettlementStatus, item.PostingStatus, item.ProcessingStatus, item.UpdatedUtc })
                .SingleOrDefaultAsync(cancellationToken);
            if (target is not null)
            {
                state["status"] = target.Status;
                state["settlementStatus"] = target.SettlementStatus;
                state["postingStatus"] = target.PostingStatus;
                state["processingStatus"] = target.ProcessingStatus;
                updatedUtc = target.UpdatedUtc;
            }
        }
        else if (attempt.ToolName.Equals("post_paid_supplier_bill_expense", StringComparison.OrdinalIgnoreCase) && entityId.HasValue)
        {
            var target = await _dbContext.FinanceBills.IgnoreQueryFilters().AsNoTracking()
                .Where(item => item.CompanyId == attempt.CompanyId && item.Id == entityId.Value)
                .Select(item => new { item.Status, item.SettlementStatus, item.PostingStatus, item.ProcessingStatus, item.UpdatedUtc })
                .SingleOrDefaultAsync(cancellationToken);
            if (target is not null)
            {
                state["status"] = target.Status;
                state["settlementStatus"] = target.SettlementStatus;
                state["postingStatus"] = target.PostingStatus;
                state["processingStatus"] = target.ProcessingStatus;
                updatedUtc = target.UpdatedUtc;
            }
        }
        else if (AccountingProviderSwitchAgentToolIds.ExecuteTools.Contains(attempt.ToolName, StringComparer.OrdinalIgnoreCase) && entityId.HasValue)
        {
            var target = await _dbContext.AccountingProviderSwitches.IgnoreQueryFilters().AsNoTracking()
                .Where(item => item.CompanyId == attempt.CompanyId && item.Id == entityId.Value)
                .Select(item => new { item.Status, item.Version, item.UpdatedUtc, item.SourceKind, item.TargetKind })
                .SingleOrDefaultAsync(cancellationToken);
            if (target is not null)
            {
                state["status"] = target.Status;
                state["recordVersion"] = target.Version;
                state["sourceKind"] = target.SourceKind;
                state["targetKind"] = target.TargetKind;
                updatedUtc = target.UpdatedUtc;
            }
        }

        return new TargetRead(new FinanceMutationTargetState(entityType, entityId, exists, version, state, updatedUtc),
            snapshotHash, integrationHash);
    }

    private static string ClassifyAuthoritativeState(
        FinanceMutationTargetState state,
        string currentSnapshotHash,
        string previousSnapshotHash)
    {
        if (!state.Exists || string.Equals(currentSnapshotHash, previousSnapshotHash, StringComparison.Ordinal))
            return FinanceMutationConfirmationStates.Reconciling;
        return ContainsPendingSemantics(state.State)
            ? FinanceMutationConfirmationStates.Reconciling
            : FinanceMutationConfirmationStates.Executed;
    }

    private static bool ContainsPendingSemantics(IReadOnlyDictionary<string, JsonNode?>? values)
    {
        if (values is null) return false;
        foreach (var node in values.Values)
        {
            if (node is JsonValue value && value.TryGetValue<string>(out var text) &&
                (text.Contains("pending", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("queued", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("processing", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("reconcil", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("in_progress", StringComparison.OrdinalIgnoreCase)))
                return true;
            if (node is JsonObject child && ContainsPendingSemantics(child.ToDictionary())) return true;
            if (node is JsonArray array && array.OfType<JsonObject>().Any(child => ContainsPendingSemantics(child.ToDictionary())))
                return true;
        }
        return false;
    }

    private bool TryReadClaims(string token, out ConfirmationClaims claims)
    {
        try
        {
            claims = JsonSerializer.Deserialize<ConfirmationClaims>(_protector.Unprotect(token))!;
            return claims is not null && claims.SchemaVersion == FinanceMutationHandoffVersions.ConfirmationV1 &&
                   claims.ConfirmationId != Guid.Empty && claims.Payload is not null;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or ArgumentException)
        {
            claims = null!;
            return false;
        }
    }

    private static ToolExecutionAttempt CreateAttempt(
        Guid companyId,
        Guid agentId,
        FinanceToolPlanStep step,
        Dictionary<string, JsonNode?> payload,
        string correlationId) =>
        new(Guid.NewGuid(), companyId, agentId, step.ToolName, ToolActionType.Execute, step.Scope, payload,
            correlationId: correlationId, toolVersion: step.ToolVersion);

    private static ToolExecutionAttempt CreateAttempt(ConfirmationClaims claims) =>
        new(Guid.NewGuid(), claims.CompanyId, claims.AgentId, claims.ToolName, ToolActionType.Execute, claims.Scope,
            Clone(claims.Payload), claims.TaskId, correlationId: claims.CorrelationId, toolVersion: claims.ToolVersion);

    private static string ComputePolicyHash(AgentRuntimeProfileDto profile) => Hash(Canonicalize(
        JsonSerializer.SerializeToNode(new
        {
            profile.AutonomyLevel,
            profile.Status,
            profile.ApprovalThresholds,
            profile.EscalationRules,
            profile.TriggerLogic
        })));

    private (Dictionary<string, JsonNode?> ToolPermissions, Dictionary<string, JsonNode?> DataScopes)
        ResolvePolicyBoundaries(AgentRuntimeProfileDto profile, AgentEffectiveAuthorityDto authority)
    {
        if (!string.Equals(profile.TemplateId, "laura-finance", StringComparison.OrdinalIgnoreCase) &&
            !(string.Equals(profile.DisplayName, "Laura", StringComparison.OrdinalIgnoreCase) &&
              string.Equals(profile.Department, "Finance", StringComparison.OrdinalIgnoreCase)))
            return (Clone(profile.ToolPermissions), Clone(profile.DataScopes));

        var granted = authority.Tools.Where(item => item.IsUsable).ToArray();
        var allowed = granted.Select(item => item.ToolName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var actions = granted.Select(item => item.ActionType).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return (
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["allowed"] = ToJsonArray(allowed),
                ["actions"] = ToJsonArray(actions),
                ["denied"] = ToJsonArray(_toolRegistry.ListTools().Select(item => item.ToolName)
                    .Except(allowed, StringComparer.OrdinalIgnoreCase)),
                ["deniedActions"] = new JsonArray()
            },
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["read"] = new JsonArray("finance"),
                ["recommend"] = new JsonArray("finance"),
                ["execute"] = new JsonArray("finance"),
                ["write"] = new JsonArray()
            });
    }

    private static JsonArray ToJsonArray(IEnumerable<string> values) =>
        new(values.Order(StringComparer.OrdinalIgnoreCase).Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());

    private static string Canonicalize(JsonNode? node) => node switch
    {
        null => "null",
        JsonObject value => "{" + string.Join(",", value.OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => JsonSerializer.Serialize(item.Key) + ":" + Canonicalize(item.Value))) + "}",
        JsonArray value => "[" + string.Join(",", value.Select(Canonicalize)) + "]",
        _ => node.ToJsonString(new JsonSerializerOptions { WriteIndented = false })
    };

    private Task WriteAuditAsync(Guid companyId, Guid actorId, string action, Guid targetId, string outcome,
        string? correlationId, string rationale, IReadOnlyDictionary<string, string?> metadata,
        CancellationToken cancellationToken) => _audit.WriteAsync(new AuditEventWriteRequest(
            companyId, AuditActorTypes.User, actorId, action, AuditTargetTypes.AgentToolExecution,
            targetId.ToString("N"), outcome, rationale, ["finance_mutation_handoff", "authoritative_finance_state"],
            metadata, correlationId), cancellationToken);

    private static FinanceMutationPreviewResult Unsupported(FinanceToolPlan plan, Guid previewId, DateTime now, string explanation) =>
        new(previewId, FinanceMutationHandoffVersions.ContractV1, plan.PlanId, plan.Revision,
            FinanceMutationPreviewStates.Unsupported, "finance_mutation_direct_conversation_unsupported", explanation,
            [], plan.EffectiveAuthorityVersion, plan.EffectiveAuthorityHash, plan.PlanningContextHash, now);

    private static FinanceMutationConfirmationResult InvalidResult(string state, string reason, string explanation) =>
        new(Guid.Empty, FinanceMutationHandoffVersions.ContractV1, Guid.Empty, string.Empty, string.Empty,
            state, reason, explanation, null, null, PolicyDecisionOutcomeValues.Deny, null, false, DateTime.UtcNow);

    private static FinanceMutationConfirmationResult Result(ConfirmationClaims claims, string state, string reason,
        string explanation, Guid? executionId = null, Guid? approvalRequestId = null,
        string policyOutcome = "not_evaluated", FinanceMutationTargetState? authoritativeState = null) =>
        new(claims.ConfirmationId, FinanceMutationHandoffVersions.ContractV1, claims.PlanId, claims.StepId,
            claims.ToolName, state, reason, explanation, executionId, approvalRequestId, policyOutcome,
            authoritativeState, false, DateTime.UtcNow);

    private static int EvidenceAgeSeconds(DateTime? updatedUtc, DateTime now) => updatedUtc.HasValue
        ? (int)Math.Clamp((now - updatedUtc.Value.ToUniversalTime()).TotalSeconds, 0, int.MaxValue)
        : 0;

    private Guid RequireActor() => _currentUser.UserId is { } actor && actor != Guid.Empty
        ? actor
        : throw new UnauthorizedAccessException("An authenticated human actor is required.");

    private static Dictionary<string, JsonNode?> Clone(IReadOnlyDictionary<string, JsonNode?> values) =>
        values.ToDictionary(item => item.Key, item => item.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);

    private static string? ReadString(JsonObject? source, string name) =>
        source is not null && source.TryGetPropertyValue(name, out var node) && node is JsonValue value &&
        value.TryGetValue<string>(out var text) ? text : null;

    private static Guid? ReadGuid(JsonObject? source, string name) =>
        source is not null && source.TryGetPropertyValue(name, out var node) && node is JsonValue value &&
        ((value.TryGetValue<Guid>(out var result) && result != Guid.Empty) ||
         (value.TryGetValue<string>(out var text) && Guid.TryParse(text, out result) && result != Guid.Empty))
            ? result : null;

    private static bool ReadBoolean(JsonObject? source, string name) =>
        source is not null && source.TryGetPropertyValue(name, out var node) && node is JsonValue value &&
        value.TryGetValue<bool>(out var result) && result;

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void ValidatePreview(PreviewFinanceMutationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CompanyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(request));
        if (request.AgentId == Guid.Empty) throw new ArgumentException("AgentId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.UserRequest) || request.UserRequest.Trim().Length > 8_000)
            throw new ArgumentException("A bounded UserRequest is required.", nameof(request));
    }

    private sealed record TargetRead(
        FinanceMutationTargetState State,
        string SnapshotHash,
        string IntegrationStateHash);

    private sealed record ConfirmationClaims(
        string SchemaVersion,
        Guid ConfirmationId,
        Guid PreviewId,
        Guid PlanId,
        int PlanRevision,
        string StepId,
        Guid CompanyId,
        Guid AgentId,
        Guid ActorId,
        string ToolName,
        string ToolVersion,
        string ActionType,
        string Scope,
        Dictionary<string, JsonNode?> Payload,
        string PayloadHash,
        string AuthorityVersion,
        string AuthorityHash,
        string PlanningContextHash,
        string PolicyHash,
        string RiskPolicyVersion,
        string TargetSnapshotHash,
        string IntegrationStateHash,
        Guid? TaskId,
        string CorrelationId,
        DateTime IssuedUtc,
        DateTime ExpiresUtc);
}
