using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using static VirtualCompany.Infrastructure.Companies.ApprovalPayloadValues;

namespace VirtualCompany.Infrastructure.Companies;

public sealed partial class ActionApprovalTargetHandler : IApprovalTargetHandler
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IServiceProvider _serviceProvider;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly ICompanyOutboxEnqueuer _outboxEnqueuer;
    private readonly ApprovalReviewMaterialHasher _materialHasher;
    public ActionApprovalTargetHandler(VirtualCompanyDbContext dbContext, IServiceProvider serviceProvider, IAuditEventWriter auditEventWriter, ICompanyOutboxEnqueuer outboxEnqueuer, ApprovalReviewMaterialHasher materialHasher)
    {
        _dbContext = dbContext;
        _serviceProvider = serviceProvider;
        _auditEventWriter = auditEventWriter;
        _outboxEnqueuer = outboxEnqueuer;
        _materialHasher = materialHasher;
    }

    public async Task<bool> ExistsAsync(Guid companyId, Guid targetEntityId, CancellationToken cancellationToken) => await _dbContext.ToolExecutionAttempts.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == targetEntityId, cancellationToken);
    public async Task BindCreatedAsync(ApprovalRequest approval, CancellationToken cancellationToken)
    {
        var attempt = await _dbContext.ToolExecutionAttempts.SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, cancellationToken);
        attempt.MarkAwaitingApproval(approval.Id, approval.PolicyDecision);
    }

    public async Task<object?> GetReviewMaterialAsync(ApprovalRequest approval, CancellationToken ct)
    {
        object? material = null;
        var action = await _dbContext.ToolExecutionAttempts.AsNoTracking().SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, ct);
        material = new
        {
            action.ToolName,
            action.ToolVersion,
            action.AgentId,
            action.ActionType,
            action.Scope,
            action.RequestPayload
        };
        return material;
    }

    public async Task<ApprovalTargetReviewDetails> GetReviewDetailsAsync(ApprovalRequest approval, CancellationToken ct)
    {
        var comparisons = new List<ApprovalComparisonDto>();
        var evidence = new List<ApprovalEvidenceDto>();
        string? executionStatus = null;
        executionStatus = (await _dbContext.ToolExecutionAttempts.Where(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId).Select(x => x.Status).SingleAsync(ct)).ToStorageValue().Replace('_', ' ');
        if (approval.Status == ApprovalRequestStatus.Approved && executionStatus == "awaiting approval" && await _dbContext.CompanyOutboxMessages.AnyAsync(x => x.CompanyId == approval.CompanyId && x.Topic == ReviewedTaskPolicyMessage.Topic && x.IdempotencyKey == $"reviewed-task:{approval.Id:N}:{approval.TargetEntityId:N}", ct))
            executionStatus = "Queued for internal execution";
        return new(comparisons, evidence, executionStatus);
    }

    public async Task<ApprovalTargetStateTransition?> ApplyDecisionAsync(ApprovalRequest approval, CancellationToken cancellationToken)
    {
        if (approval.Status == ApprovalRequestStatus.Pending)
            return null;
        var transition = await ApplyDecisionCoreAsync(approval, cancellationToken);
        await ReconcileReviewedTaskPolicyAsync(approval, cancellationToken);
        return transition;
    }

    private async Task<ApprovalTargetStateTransition?> ApplyDecisionCoreAsync(ApprovalRequest approval, CancellationToken cancellationToken)
    {
        var attempt = await _dbContext.ToolExecutionAttempts.SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, cancellationToken);
        var previousStatus = attempt.Status.ToStorageValue();
        if (!approval.CanExecuteGuardedAction)
        {
            var blockedDecision = BuildBlockedApprovalPolicyDecision(approval);
            var resultPayload = BuildBlockedApprovalResultPayload(approval, attempt);
            if (approval.Status == ApprovalRequestStatus.Rejected)
            {
                attempt.MarkRejected(blockedDecision, resultPayload, denialReason: PolicyDecisionReasonCodes.ApprovalRejected);
                return ApprovalTargetStateTransition.ForAction(attempt.Id, previousStatus, attempt.Status.ToStorageValue());
            }

            attempt.MarkDenied(blockedDecision, resultPayload, denialReason: approval.ExecutionBlockReasonCode);
            return ApprovalTargetStateTransition.ForAction(attempt.Id, previousStatus, attempt.Status.ToStorageValue());
        }

        if (approval.Status == ApprovalRequestStatus.Approved)
        {
            var policyDecision = BuildApprovedApprovalPolicyDecision(approval);
            var retainedTask = attempt.TaskId.HasValue ? await _dbContext.WorkTasks.SingleOrDefaultAsync(x => x.CompanyId == approval.CompanyId && x.Id == attempt.TaskId, cancellationToken) : null;
            if (retainedTask is not null && TaskTypePolicyCatalogue.All.Any(x => !x.Finance && x.Code == retainedTask.Type && x.ToolName == attempt.ToolName) && retainedTask.InputPayload.ContainsKey("taskPolicyVersion"))
            {
                _outboxEnqueuer.Enqueue(approval.CompanyId, ReviewedTaskPolicyMessage.Topic, new ReviewedTaskPolicyMessage(approval.CompanyId, approval.Id, attempt.Id), correlationId: attempt.CorrelationId, idempotencyKey: $"reviewed-task:{approval.Id:N}:{attempt.Id:N}");
                attempt.ResultPayload["approvedInternalWorkQueued"] = JsonValue.Create(true);
                var queuedOutput = CloneNodes(retainedTask.OutputPayload);
                queuedOutput["reviewedActionQueue"] = new JsonObject
                {
                    ["approvalId"] = approval.Id.ToString("D"),
                    ["attemptId"] = attempt.Id.ToString("D")
                };
                retainedTask.UpdateStatus(WorkTaskStatus.New, queuedOutput, "The reviewed internal action is queued. Current policy and execution controls must pass before execution.");
                return ApprovalTargetStateTransition.ForAction(attempt.Id, previousStatus, attempt.Status.ToStorageValue());
            }

            FinanceAgentAuthorizationDecisionDto? actorAuthorization = null;
            if (IsFinanceToolAttempt(attempt))
            {
                var authorityResolver = _serviceProvider.GetRequiredService<IAgentEffectiveAuthorityResolver>();
                var currentAuthority = await authorityResolver.ResolveAsync(approval.CompanyId, attempt.AgentId, cancellationToken);
                var continuationValidation = await RevalidateFinanceContinuationAsync(approval, attempt, currentAuthority, cancellationToken);
                if (!continuationValidation.IsValid)
                {
                    approval.MarkStale(continuationValidation.Explanation);
                    policyDecision["outcome"] = PolicyDecisionOutcomeValues.Deny;
                    policyDecision["approvalStatus"] = ApprovalRequestStatus.Stale.ToStorageValue();
                    policyDecision["reasonCode"] = continuationValidation.ReasonCode;
                    policyDecision["continuationValidation"] = continuationValidation.Evidence;
                    var staleResult = ToolExecutionResult.Failed(attempt.ToolName, attempt.ActionType, ToolExecutionStatus.Denied.ToStorageValue(), continuationValidation.ReasonCode, continuationValidation.Explanation, metadata: new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase) { ["approvalRequestId"] = approval.Id, ["executionId"] = attempt.Id, ["continuationValidation"] = continuationValidation.Evidence.DeepClone() });
                    attempt.MarkDenied(policyDecision, staleResult.ToStructuredPayload(), denialReason: continuationValidation.ReasonCode);
                    return ApprovalTargetStateTransition.ForAction(attempt.Id, previousStatus, attempt.Status.ToStorageValue());
                }

                var approvedAuthorityVersion = TryReadString(approval.ThresholdContext, "effectiveAuthorityVersion");
                var approvedAuthorityHash = TryReadString(approval.ThresholdContext, "effectiveAuthorityHash");
                if (string.IsNullOrWhiteSpace(approvedAuthorityHash) || !string.Equals(approvedAuthorityVersion, currentAuthority.AuthorityVersion, StringComparison.Ordinal) || !string.Equals(approvedAuthorityHash, currentAuthority.AuthorityHash, StringComparison.Ordinal))
                {
                    policyDecision["effectiveAuthorityVersion"] = currentAuthority.AuthorityVersion;
                    policyDecision["effectiveAuthorityHash"] = currentAuthority.AuthorityHash;
                    policyDecision["reasonCode"] = AgentAuthorityReasonCodes.Stale;
                    var staleResult = ToolExecutionResult.Failed(attempt.ToolName, attempt.ActionType, ToolExecutionStatus.Denied.ToStorageValue(), AgentAuthorityReasonCodes.Stale, "Agent permissions changed after approval was requested. Create and review a new request.", metadata: new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase) { ["effectiveAuthorityVersion"] = JsonValue.Create(currentAuthority.AuthorityVersion), ["effectiveAuthorityHash"] = JsonValue.Create(currentAuthority.AuthorityHash), ["approvedAuthorityVersion"] = approvedAuthorityVersion is null ? null : JsonValue.Create(approvedAuthorityVersion), ["approvedAuthorityHash"] = approvedAuthorityHash is null ? null : JsonValue.Create(approvedAuthorityHash) });
                    attempt.MarkDenied(policyDecision, staleResult.ToStructuredPayload(), denialReason: AgentAuthorityReasonCodes.Stale);
                    return ApprovalTargetStateTransition.ForAction(attempt.Id, previousStatus, attempt.Status.ToStorageValue());
                }

                var approvalBinding = approval.ThresholdContext["approvalBinding"] as JsonObject;
                var delegationAuthorityId = approvalBinding is null ? null : FinanceApprovalContinuationBinding.ReadBindingGuid(approvalBinding, "delegationAuthorityId");
                var financeAuthorization = _serviceProvider.GetRequiredService<IFinanceAgentAuthorizationService>();
                actorAuthorization = await financeAuthorization.AuthorizeAsync(new FinanceAgentAuthorizationRequest(approval.CompanyId, attempt.AgentId, attempt.Id, attempt.ToolName, attempt.ActionType, attempt.Scope, attempt.WorkflowInstanceId, attempt.CorrelationId, ActorUserId: delegationAuthorityId.HasValue ? null : approval.RequestedByUserId, DelegationAuthorityId: delegationAuthorityId, IsApprovedContinuation: true), cancellationToken);
                policyDecision["actorAuthorization"] = JsonSerializer.SerializeToNode(actorAuthorization);
                await WriteFinanceAuthorizationAuditAsync(actorAuthorization, attempt.CorrelationId, cancellationToken);
                if (!actorAuthorization.IsAllowed)
                {
                    var deniedResult = ToolExecutionResult.Failed(attempt.ToolName, attempt.ActionType, ToolExecutionStatus.Denied.ToStorageValue(), "finance_actor_unauthorized", "This Finance action is not available for the originating actor.", metadata: new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase) { ["authorizationReasonCode"] = JsonValue.Create(actorAuthorization.ReasonCode), ["authorizationPolicyVersion"] = JsonValue.Create(actorAuthorization.PolicyVersion), ["executionId"] = JsonValue.Create(attempt.Id) });
                    attempt.MarkDenied(policyDecision, deniedResult.ToStructuredPayload(), denialReason: actorAuthorization.ReasonCode);
                    return ApprovalTargetStateTransition.ForAction(attempt.Id, previousStatus, attempt.Status.ToStorageValue());
                }
            }

            var companyToolExecutor = _serviceProvider.GetRequiredService<ICompanyToolExecutor>();
            var result = await companyToolExecutor.ExecuteAsync(new ToolExecutionRequest(approval.CompanyId, attempt.AgentId, attempt.ToolName, attempt.ActionType, attempt.Scope, CloneNodes(attempt.RequestPayload), attempt.TaskId, attempt.WorkflowInstanceId, attempt.CorrelationId, attempt.Id, attempt.ToolVersion, actorAuthorization?.ActorId ?? approval.RequestedByUserId), cancellationToken);
            if (string.Equals(result.Status, ToolExecutionStatus.Denied.ToStorageValue(), StringComparison.OrdinalIgnoreCase))
            {
                attempt.MarkDenied(policyDecision, result.ToStructuredPayload());
                return ApprovalTargetStateTransition.ForAction(attempt.Id, previousStatus, attempt.Status.ToStorageValue());
            }

            if (IsAmbiguousProviderResult(result))
            {
                attempt.MarkReconciliationRequired(policyDecision, result.ToStructuredPayload(), result.ErrorCode ?? "ambiguous_provider_outcome");
                return ApprovalTargetStateTransition.ForAction(attempt.Id, previousStatus, attempt.Status.ToStorageValue());
            }

            if (!result.Success)
            {
                attempt.MarkFailed(policyDecision, result.ToStructuredPayload());
                return ApprovalTargetStateTransition.ForAction(attempt.Id, previousStatus, attempt.Status.ToStorageValue());
            }

            attempt.MarkExecuted(policyDecision, result.ToStructuredPayload());
            return ApprovalTargetStateTransition.ForAction(attempt.Id, previousStatus, attempt.Status.ToStorageValue());
        }

        return null;
    }
}
