using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Sales;
using VirtualCompany.Application.Support;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Events;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyApprovalRequestService : IApprovalRequestService, IApprovalAutomationService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyMembershipContextResolver _companyMembershipContextResolver;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly IServiceProvider _serviceProvider;
    private readonly IExecutiveCockpitDashboardCache _dashboardCache;
    private readonly ICompanyOutboxEnqueuer _outboxEnqueuer;
    private static readonly ConcurrentDictionary<Guid, ApprovalDecisionGate> ApprovalDecisionLocks = new();

    public CompanyApprovalRequestService(
        VirtualCompanyDbContext dbContext,
        ICompanyMembershipContextResolver companyMembershipContextResolver,
        IAuditEventWriter auditEventWriter,
        IServiceProvider serviceProvider,
        IExecutiveCockpitDashboardCache dashboardCache,
        ICompanyOutboxEnqueuer outboxEnqueuer)
    {
        _dbContext = dbContext;
        _companyMembershipContextResolver = companyMembershipContextResolver;
        _auditEventWriter = auditEventWriter;
        _serviceProvider = serviceProvider;
        _dashboardCache = dashboardCache;
        _outboxEnqueuer = outboxEnqueuer;
    }

    private const string DefaultRationaleSummary = "This action exceeded a configured approval threshold.";
    private const string DefaultAffectedDataSummary = "Affected data details unavailable.";
    private const string SupplierPaymentProposalApprovalType = "supplier_invoice_payment_proposal";
    private const string SupplierPaymentProposalTaskType = "finance.supplier_invoice_payment_proposal";
    private const int SummaryMaxLength = 220;

    public async Task<ApprovalRequestDto> CreateAsync(
        Guid companyId,
        CreateApprovalRequestCommand command,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);
        Validate(command);

        var targetType = ApprovalTargetEntityTypeValues.Parse(command.TargetEntityType);
        await EnsureTargetExistsAsync(companyId, targetType, command.TargetEntityId, cancellationToken);

        var steps = command.Steps?.Select(step => new ApprovalStepDefinition(
            step.SequenceNo,
            ApprovalStepApproverTypeValues.Parse(step.ApproverType),
            step.ApproverRef)) ?? [];

        var approval = ApprovalRequest.CreateForTarget(
            Guid.NewGuid(),
            companyId,
            targetType,
            command.TargetEntityId,
            command.RequestedByActorType,
            command.RequestedByActorId,
            command.ApprovalType,
            command.ThresholdContext!,
            command.RequiredRole,
            command.RequiredUserId,
            steps);

        _dbContext.ApprovalRequests.Add(approval);
        if (targetType == ApprovalTargetEntityType.Task)
        {
            var task = await _dbContext.WorkTasks.SingleAsync(x => x.CompanyId == companyId && x.Id == command.TargetEntityId, cancellationToken);
            task.UpdateStatus(WorkTaskStatus.AwaitingApproval);
        }
        else if (targetType == ApprovalTargetEntityType.Workflow)
        {
            var workflow = await _dbContext.WorkflowInstances.SingleAsync(x => x.CompanyId == companyId && x.Id == command.TargetEntityId, cancellationToken);
            workflow.UpdateState(WorkflowInstanceStatus.Blocked, workflow.CurrentStep);
        }
        else if (targetType == ApprovalTargetEntityType.Action)
        {
            var attempt = await _dbContext.ToolExecutionAttempts.SingleAsync(x => x.CompanyId == companyId && x.Id == command.TargetEntityId, cancellationToken);
            attempt.MarkAwaitingApproval(approval.Id, approval.PolicyDecision);
        }

        await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                companyId,
                approval.RequestedByActorType,
                approval.RequestedByActorId,
                AuditEventActions.ApprovalCreated,
                AuditTargetTypes.ApprovalRequest,
                approval.Id.ToString("N"),
                AuditEventOutcomes.Requested,
                DataSources: ["approvals", "http_request"],
                RationaleSummary: $"Approval requested for {approval.TargetEntityType} target.",
                Metadata: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["approvalRequestId"] = approval.Id.ToString("N"),
                    ["targetEntityType"] = approval.TargetEntityType,
                    ["targetEntityId"] = approval.TargetEntityId.ToString("N"),
                    ["approvalType"] = approval.ApprovalType
                }),
            cancellationToken);

        EnqueueApprovalNotification(approval);
        EnqueueApprovalUpdatedEvent(approval, "created");
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _dashboardCache.InvalidateAsync(companyId, cancellationToken);

        return await ToDtoAsync(approval, cancellationToken);
    }

    public async Task<ApprovalDecisionResultDto> DecideAsync(
        Guid companyId,
        ApprovalDecisionCommand command,
        CancellationToken cancellationToken)
    {
        var decisionGate = AcquireDecisionGate(command.ApprovalId);
        var lockAcquired = false;
        try
        {
            await decisionGate.Semaphore.WaitAsync(cancellationToken);
            lockAcquired = true;
            return await DecideCoreAsync(companyId, command, cancellationToken);
        }
        finally
        {
            if (lockAcquired)
            {
                decisionGate.Semaphore.Release();
            }
            lock (decisionGate.SyncRoot)
            {
                decisionGate.ReferenceCount--;
                if (decisionGate.ReferenceCount == 0)
                {
                    decisionGate.IsRetired = true;
                    ApprovalDecisionLocks.TryRemove(
                        new KeyValuePair<Guid, ApprovalDecisionGate>(command.ApprovalId, decisionGate));
                }
            }
        }
    }

    private static ApprovalDecisionGate AcquireDecisionGate(Guid approvalId)
    {
        while (true)
        {
            var gate = ApprovalDecisionLocks.GetOrAdd(approvalId, static _ => new ApprovalDecisionGate());
            lock (gate.SyncRoot)
            {
                if (gate.IsRetired)
                {
                    continue;
                }

                gate.ReferenceCount++;
                return gate;
            }
        }
    }

    private sealed class ApprovalDecisionGate
    {
        public object SyncRoot { get; } = new();
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int ReferenceCount { get; set; }
        public bool IsRetired { get; set; }
    }

    private async Task<ApprovalDecisionResultDto> DecideCoreAsync(
        Guid companyId,
        ApprovalDecisionCommand command,
        CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        ValidateDecision(command);

        var approval = await _dbContext.ApprovalRequests
            .Include(x => x.Steps)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == command.ApprovalId, cancellationToken);

        if (approval is null)
        {
            throw new KeyNotFoundException("Approval request not found.");
        }

        var normalizedDecision = command.Decision.Trim().ToLowerInvariant();
        if (approval.Status != ApprovalRequestStatus.Pending)
        {
            if (command.ClientRequestId.HasValue && command.ClientRequestId.Value != Guid.Empty &&
                TryGetGuid(approval.DecisionChain, "lastDecisionClientRequestId") == command.ClientRequestId.Value)
            {
                var replayedStep = approval.Steps
                    .Where(step => step.Status != ApprovalStepStatus.Pending)
                    .OrderByDescending(step => step.SequenceNo)
                    .FirstOrDefault() ?? approval.Steps.OrderBy(step => step.SequenceNo).First();
                return new ApprovalDecisionResultDto(
                    await ToDtoAsync(approval, cancellationToken),
                    ToStepDto(replayedStep),
                    approval.CurrentActionableStep is { } replayNext ? ToStepDto(replayNext) : null,
                    approval.Status != ApprovalRequestStatus.Pending);
            }

            throw new ApprovalValidationException(new Dictionary<string, string[]>
            {
                [nameof(command.Decision)] = [$"Only pending approvals can be decided. Current status: {approval.Status.ToStorageValue()}."]
            });
        }

        if (IsExpiredFinanceActionApproval(approval, DateTime.UtcNow))
        {
            approval.MarkExpired("Finance approval expired before it was decided. Create and review a new request.");
            var expiredTransition = await UpdateLinkedEntityAfterDecisionAsync(approval, cancellationToken);
            await MarkApprovalNotificationsActionedAsync(companyId, approval.Id, membership.UserId, cancellationToken);
            await WriteCompletionAuditAsync(approval, membership.UserId, cancellationToken);
            if (expiredTransition is not null)
            {
                await WriteLinkedEntityStateAuditAsync(approval, expiredTransition, membership.UserId, cancellationToken);
            }
            EnqueueApprovalUpdatedEvent(approval, "expired");
            await _dbContext.SaveChangesAsync(cancellationToken);
            await SynchronizeFinanceAutonomyApprovalAsync(approval, cancellationToken);
            throw new ApprovalValidationException(new Dictionary<string, string[]>
            {
                [nameof(command.Decision)] = ["The Finance approval expired. A new reviewed request is required."]
            });
        }

        var currentStep = approval.CurrentActionableStep ??
            throw new ApprovalValidationException(new Dictionary<string, string[]>
            {
                [nameof(command.StepId)] = ["Approval request has no current actionable step."]
            });

        if (command.StepId.HasValue && command.StepId.Value != currentStep.Id)
        {
            throw new InvalidOperationException("Only the current approval step can be decided.");
        }

        var isApprovalStepDecision = normalizedDecision is "approve" or "approved" or "reject" or "rejected" or
            "request_changes" or "changes_requested";
        var isManager = membership.MembershipRole is CompanyMembershipRole.Owner or CompanyMembershipRole.Admin or CompanyMembershipRole.Manager;
        var canCancel = IsInitiatingUser(approval, membership.UserId) || isManager;
        if (isApprovalStepDecision && !CanDecide(currentStep, membership) ||
            normalizedDecision is "cancel" or "cancelled" && !canCancel ||
            normalizedDecision is "expire" or "expired" or "revoke" or "revoked" or "supersede" or "superseded" && !isManager)
        {
            throw new ApprovalDecisionForbiddenException("The current user is not authorized for this approval transition.");
        }

        var requestedApproval = normalizedDecision is "approve" or "approved";
        var selfApprovalRejected = requestedApproval && RequiresIndependentFinanceReview(approval) &&
                                   IsInitiatingUser(approval, membership.UserId);
        var rejected = selfApprovalRejected || normalizedDecision is "reject" or "rejected";
        var decisionComment = selfApprovalRejected
            ? "Rejected because Finance segregation of duties prohibits requester self-approval."
            : command.Comment;
        ApprovalStep decidedStep;
        if (rejected)
            decidedStep = approval.RejectCurrentStep(currentStep.Id, membership.UserId, decisionComment);
        else if (requestedApproval)
            decidedStep = approval.ApproveCurrentStep(currentStep.Id, membership.UserId, decisionComment);
        else
        {
            decidedStep = currentStep;
            switch (normalizedDecision)
            {
                case "request_changes":
                case "changes_requested":
                    approval.MarkChangesRequested(decisionComment);
                    break;
                case "cancel":
                case "cancelled":
                    approval.MarkCancelled(decisionComment);
                    break;
                case "expire":
                case "expired":
                    approval.MarkExpired(decisionComment);
                    break;
                case "revoke":
                case "revoked":
                    approval.MarkRevoked(decisionComment);
                    break;
                case "supersede":
                case "superseded":
                    approval.MarkSuperseded(decisionComment);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported approval transition.");
            }
        }
        if (command.ClientRequestId.HasValue && command.ClientRequestId.Value != Guid.Empty)
        {
            var decisionChain = CloneNodes(approval.DecisionChain);
            decisionChain["lastDecisionClientRequestId"] = command.ClientRequestId.Value;
            approval.SetDecisionChain(decisionChain);
        }

        EnqueueApprovalUpdatedEvent(approval, approval.Status.ToStorageValue());
        var linkedEntityTransition = await UpdateLinkedEntityAfterDecisionAsync(approval, cancellationToken);
        if (requestedApproval || rejected)
        {
            await WriteDecisionAuditAsync(approval, decidedStep, membership.UserId, rejected, cancellationToken);
        }

        var finalized = approval.Status != ApprovalRequestStatus.Pending;
        if (finalized)
        {
            await MarkApprovalNotificationsActionedAsync(companyId, approval.Id, membership.UserId, cancellationToken);
            await WriteCompletionAuditAsync(approval, membership.UserId, cancellationToken);
            if (linkedEntityTransition is not null)
            {
                await WriteLinkedEntityStateAuditAsync(approval, linkedEntityTransition, membership.UserId, cancellationToken);
            }
        }
        else
        {
            await WriteChainAdvancedAuditAsync(approval, decidedStep, membership.UserId, cancellationToken);
            EnqueueApprovalNotification(approval);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await SynchronizeFinanceAutonomyApprovalAsync(approval, cancellationToken);
        if (finalized &&
            approval.Status == ApprovalRequestStatus.Approved &&
            ApprovalTargetEntityTypeValues.Parse(approval.TargetEntityType) == ApprovalTargetEntityType.FinanceIntegrationWrite)
        {
            await _serviceProvider.GetRequiredService<IFinanceAccountingActionService>().RetryApprovedAsync(companyId, approval.TargetEntityId, cancellationToken);
            await _serviceProvider.GetRequiredService<ISupportRefundFinanceService>()
                .RefreshByWriteRequestAsync(companyId, approval.TargetEntityId, cancellationToken);
        }
        await _dashboardCache.InvalidateAsync(companyId, cancellationToken);

        return new ApprovalDecisionResultDto(
            await ToDtoAsync(approval, cancellationToken),
            ToStepDto(decidedStep),
            approval.CurrentActionableStep is { } nextStep ? ToStepDto(nextStep) : null,
            finalized);
    }

    public async Task<ApprovalDecisionResultDto> ApproveUnderStandingGrantAsync(
        Guid companyId,
        Guid approvalId,
        AutomatedApprovalGrant grant,
        CancellationToken cancellationToken)
    {
        var approval = await _dbContext.ApprovalRequests
            .Include(x => x.Steps)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == approvalId, cancellationToken)
            ?? throw new KeyNotFoundException("Approval request not found.");

        if (approval.Status != ApprovalRequestStatus.Pending)
        {
            return new ApprovalDecisionResultDto(
                await ToDtoAsync(approval, cancellationToken),
                approval.Steps.OrderByDescending(x => x.SequenceNo).Select(ToStepDto).First(),
                approval.CurrentActionableStep is { } existingNext ? ToStepDto(existingNext) : null,
                approval.Status != ApprovalRequestStatus.Pending);
        }

        var currentStep = approval.CurrentActionableStep
            ?? throw new ApprovalValidationException(new Dictionary<string, string[]>
            {
                [nameof(approvalId)] = ["Approval request has no current actionable step."]
            });
        if (RequiresIndependentFinanceReview(approval))
            throw new ApprovalDecisionForbiddenException(
                "Standing automation cannot approve a Finance action that requires independent human review.");
        var comment = $"Automatically approved by {grant.AgentDisplayName} under supplier trust rule {grant.GrantId:N} for {grant.SupplierName} ({grant.Stage}).";
        var decidedStep = approval.ApproveCurrentStep(currentStep.Id, grant.GrantorUserId, comment);
        EnqueueApprovalUpdatedEvent(approval, "automatically_approved");
        var linkedEntityTransition = await UpdateLinkedEntityAfterDecisionAsync(approval, cancellationToken);

        await WriteDecisionAuditAsync(approval, decidedStep, grant.GrantorUserId, rejected: false, cancellationToken);
        await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                companyId,
                "agent",
                grant.AgentId,
                AuditEventActions.ApprovalStepApproved,
                AuditTargetTypes.ApprovalRequest,
                approval.Id.ToString("N"),
                AuditEventOutcomes.Succeeded,
                DataSources: ["supplier_trust_rule", "approvals"],
                RationaleSummary: comment,
                Metadata: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["standingGrantId"] = grant.GrantId.ToString("N"),
                    ["agentId"] = grant.AgentId.ToString("N"),
                    ["agentDisplayName"] = grant.AgentDisplayName,
                    ["supplierName"] = grant.SupplierName,
                    ["stage"] = grant.Stage
                }),
            cancellationToken);

        var finalized = approval.Status != ApprovalRequestStatus.Pending;
        if (finalized)
        {
            await MarkApprovalNotificationsActionedAsync(companyId, approval.Id, grant.GrantorUserId, cancellationToken);
            await WriteCompletionAuditAsync(approval, grant.GrantorUserId, cancellationToken);
            if (linkedEntityTransition is not null)
            {
                await WriteLinkedEntityStateAuditAsync(approval, linkedEntityTransition, grant.GrantorUserId, cancellationToken);
            }
        }
        else
        {
            EnqueueApprovalNotification(approval);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _dashboardCache.InvalidateAsync(companyId, cancellationToken);

        return new ApprovalDecisionResultDto(
            await ToDtoAsync(approval, cancellationToken),
            ToStepDto(decidedStep),
            approval.CurrentActionableStep is { } nextStep ? ToStepDto(nextStep) : null,
            finalized);
    }

    public async Task<IReadOnlyList<ApprovalRequestDto>> ListAsync(
        Guid companyId,
        string? status,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);

        var query = _dbContext.ApprovalRequests
            .AsNoTracking()
            .Include(x => x.Steps)
            .Where(x => x.CompanyId == companyId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var parsedStatus = ApprovalRequestStatusValues.Parse(status);
            query = query.Where(x => x.Status == parsedStatus);
        }

        var approvals = await query
            .OrderByDescending(x => x.CreatedUtc)
            .ToListAsync(cancellationToken);

        var contexts = await BuildSummaryContextsAsync(companyId, approvals, cancellationToken);
        return approvals
            .Select(approval => ToDto(approval, contexts.GetValueOrDefault(approval.Id)))
            .ToList();
    }

    public async Task<ApprovalRequestDto> GetAsync(
        Guid companyId,
        Guid approvalId,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);

        var approval = await _dbContext.ApprovalRequests
            .AsNoTracking()
            .Include(x => x.Steps)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == approvalId, cancellationToken);

        if (approval is null)
        {
            throw new KeyNotFoundException("Approval request not found.");
        }

        return await ToDtoAsync(approval, cancellationToken);
    }

    private async Task<LinkedEntityStateTransition?> UpdateLinkedEntityAfterDecisionAsync(
        ApprovalRequest approval,
        CancellationToken cancellationToken)
    {
        if (approval.Status == ApprovalRequestStatus.Pending)
        {
            return null;
        }

        var targetType = ApprovalTargetEntityTypeValues.Parse(approval.TargetEntityType);
        if (targetType == ApprovalTargetEntityType.Task)
        {
            var task = await _dbContext.WorkTasks.SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, cancellationToken);
            var previousStatus = task.Status.ToStorageValue();
            if (approval.Status == ApprovalRequestStatus.Approved)
            {
                var approvalCompletesTask =
                    string.Equals(approval.ApprovalType, SupplierPaymentProposalApprovalType, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(task.Type, SupplierPaymentProposalTaskType, StringComparison.OrdinalIgnoreCase);
                task.UpdateStatus(approvalCompletesTask ? WorkTaskStatus.Completed : WorkTaskStatus.InProgress);
                await UpdateSupplierPaymentProposalAfterTaskApprovalAsync(approval, task, approved: true, cancellationToken);
                await UpdateSupportRefundAfterTaskApprovalAsync(approval, task, cancellationToken);
                return LinkedEntityStateTransition.ForTask(task.Id, previousStatus, task.Status.ToStorageValue());
            }

            if (approval.Status is ApprovalRequestStatus.Rejected or ApprovalRequestStatus.Expired)
            {
                task.UpdateStatus(WorkTaskStatus.Blocked, rationaleSummary: approval.DecisionSummary);
                await UpdateSupplierPaymentProposalAfterTaskApprovalAsync(approval, task, approved: false, cancellationToken);
                await UpdateSupportRefundAfterTaskApprovalAsync(approval, task, cancellationToken);
                return LinkedEntityStateTransition.ForTask(task.Id, previousStatus, task.Status.ToStorageValue());
            }

            if (approval.Status == ApprovalRequestStatus.Cancelled)
            {
                task.UpdateStatus(WorkTaskStatus.Blocked, rationaleSummary: approval.DecisionSummary);
                await UpdateSupplierPaymentProposalAfterTaskApprovalAsync(approval, task, approved: false, cancellationToken);
                await UpdateSupportRefundAfterTaskApprovalAsync(approval, task, cancellationToken);
                return LinkedEntityStateTransition.ForTask(task.Id, previousStatus, task.Status.ToStorageValue());
            }
        }
        else if (targetType == ApprovalTargetEntityType.Workflow)
        {
            var workflow = await _dbContext.WorkflowInstances.SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, cancellationToken);
            var previousStatus = workflow.State.ToStorageValue();
            if (approval.Status == ApprovalRequestStatus.Approved)
            {
                workflow.UpdateState(WorkflowInstanceStatus.Running, workflow.CurrentStep);
                return LinkedEntityStateTransition.ForWorkflow(workflow.Id, previousStatus, workflow.State.ToStorageValue());
            }

            if (approval.Status is ApprovalRequestStatus.Rejected or ApprovalRequestStatus.Expired)
            {
                workflow.UpdateState(WorkflowInstanceStatus.Failed, workflow.CurrentStep);
                return LinkedEntityStateTransition.ForWorkflow(workflow.Id, previousStatus, workflow.State.ToStorageValue());
            }

            if (approval.Status == ApprovalRequestStatus.Cancelled)
            {
                workflow.UpdateState(WorkflowInstanceStatus.Cancelled, workflow.CurrentStep);
                return LinkedEntityStateTransition.ForWorkflow(workflow.Id, previousStatus, workflow.State.ToStorageValue());
            }
        }
        else if (targetType == ApprovalTargetEntityType.Action)
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
                    return LinkedEntityStateTransition.ForAction(attempt.Id, previousStatus, attempt.Status.ToStorageValue());
                }

                attempt.MarkDenied(blockedDecision, resultPayload, denialReason: approval.ExecutionBlockReasonCode);
                return LinkedEntityStateTransition.ForAction(attempt.Id, previousStatus, attempt.Status.ToStorageValue());
            }

            if (approval.Status == ApprovalRequestStatus.Approved)
            {
                var policyDecision = BuildApprovedApprovalPolicyDecision(approval);
                FinanceAgentAuthorizationDecisionDto? actorAuthorization = null;
                if (IsFinanceToolAttempt(attempt))
                {
                    var authorityResolver = _serviceProvider.GetRequiredService<IAgentEffectiveAuthorityResolver>();
                    var currentAuthority = await authorityResolver.ResolveAsync(
                        approval.CompanyId, attempt.AgentId, cancellationToken);
                    var continuationValidation = await RevalidateFinanceContinuationAsync(
                        approval, attempt, currentAuthority, cancellationToken);
                    if (!continuationValidation.IsValid)
                    {
                        approval.MarkStale(continuationValidation.Explanation);
                        policyDecision["outcome"] = PolicyDecisionOutcomeValues.Deny;
                        policyDecision["approvalStatus"] = ApprovalRequestStatus.Stale.ToStorageValue();
                        policyDecision["reasonCode"] = continuationValidation.ReasonCode;
                        policyDecision["continuationValidation"] = continuationValidation.Evidence;
                        var staleResult = ToolExecutionResult.Failed(
                            attempt.ToolName,
                            attempt.ActionType,
                            ToolExecutionStatus.Denied.ToStorageValue(),
                            continuationValidation.ReasonCode,
                            continuationValidation.Explanation,
                            metadata: new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["approvalRequestId"] = approval.Id,
                                ["executionId"] = attempt.Id,
                                ["continuationValidation"] = continuationValidation.Evidence.DeepClone()
                            });
                        attempt.MarkDenied(policyDecision, staleResult.ToStructuredPayload(),
                            denialReason: continuationValidation.ReasonCode);
                        return LinkedEntityStateTransition.ForAction(
                            attempt.Id, previousStatus, attempt.Status.ToStorageValue());
                    }

                    var approvedAuthorityVersion = TryReadString(approval.ThresholdContext, "effectiveAuthorityVersion");
                    var approvedAuthorityHash = TryReadString(approval.ThresholdContext, "effectiveAuthorityHash");
                    if (string.IsNullOrWhiteSpace(approvedAuthorityHash) ||
                        !string.Equals(approvedAuthorityVersion, currentAuthority.AuthorityVersion, StringComparison.Ordinal) ||
                        !string.Equals(approvedAuthorityHash, currentAuthority.AuthorityHash, StringComparison.Ordinal))
                    {
                        policyDecision["effectiveAuthorityVersion"] = currentAuthority.AuthorityVersion;
                        policyDecision["effectiveAuthorityHash"] = currentAuthority.AuthorityHash;
                        policyDecision["reasonCode"] = AgentAuthorityReasonCodes.Stale;
                        var staleResult = ToolExecutionResult.Failed(
                            attempt.ToolName,
                            attempt.ActionType,
                            ToolExecutionStatus.Denied.ToStorageValue(),
                            AgentAuthorityReasonCodes.Stale,
                            "Agent permissions changed after approval was requested. Create and review a new request.",
                            metadata: new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["effectiveAuthorityVersion"] = JsonValue.Create(currentAuthority.AuthorityVersion),
                                ["effectiveAuthorityHash"] = JsonValue.Create(currentAuthority.AuthorityHash),
                                ["approvedAuthorityVersion"] = approvedAuthorityVersion is null ? null : JsonValue.Create(approvedAuthorityVersion),
                                ["approvedAuthorityHash"] = approvedAuthorityHash is null ? null : JsonValue.Create(approvedAuthorityHash)
                            });
                        attempt.MarkDenied(policyDecision, staleResult.ToStructuredPayload(),
                            denialReason: AgentAuthorityReasonCodes.Stale);
                        return LinkedEntityStateTransition.ForAction(attempt.Id, previousStatus, attempt.Status.ToStorageValue());
                    }

                    var approvalBinding = approval.ThresholdContext["approvalBinding"] as JsonObject;
                    var delegationAuthorityId = approvalBinding is null
                        ? null
                        : FinanceApprovalContinuationBinding.ReadBindingGuid(approvalBinding, "delegationAuthorityId");
                    var financeAuthorization = _serviceProvider.GetRequiredService<IFinanceAgentAuthorizationService>();
                    actorAuthorization = await financeAuthorization.AuthorizeAsync(
                        new FinanceAgentAuthorizationRequest(
                            approval.CompanyId,
                            attempt.AgentId,
                            attempt.Id,
                            attempt.ToolName,
                            attempt.ActionType,
                            attempt.Scope,
                            attempt.WorkflowInstanceId,
                            attempt.CorrelationId,
                            ActorUserId: delegationAuthorityId.HasValue ? null : approval.RequestedByUserId,
                            DelegationAuthorityId: delegationAuthorityId,
                            IsApprovedContinuation: true),
                        cancellationToken);

                    policyDecision["actorAuthorization"] = JsonSerializer.SerializeToNode(actorAuthorization);
                    await WriteFinanceAuthorizationAuditAsync(actorAuthorization, attempt.CorrelationId, cancellationToken);
                    if (!actorAuthorization.IsAllowed)
                    {
                        var deniedResult = ToolExecutionResult.Failed(
                            attempt.ToolName,
                            attempt.ActionType,
                            ToolExecutionStatus.Denied.ToStorageValue(),
                            "finance_actor_unauthorized",
                            "This Finance action is not available for the originating actor.",
                            metadata: new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["authorizationReasonCode"] = JsonValue.Create(actorAuthorization.ReasonCode),
                                ["authorizationPolicyVersion"] = JsonValue.Create(actorAuthorization.PolicyVersion),
                                ["executionId"] = JsonValue.Create(attempt.Id)
                            });
                        attempt.MarkDenied(policyDecision, deniedResult.ToStructuredPayload(), denialReason: actorAuthorization.ReasonCode);
                        return LinkedEntityStateTransition.ForAction(attempt.Id, previousStatus, attempt.Status.ToStorageValue());
                    }
                }

                var companyToolExecutor = _serviceProvider.GetRequiredService<ICompanyToolExecutor>();
                var result = await companyToolExecutor.ExecuteAsync(
                    new ToolExecutionRequest(
                        approval.CompanyId,
                        attempt.AgentId,
                        attempt.ToolName,
                        attempt.ActionType,
                        attempt.Scope,
                        CloneNodes(attempt.RequestPayload),
                        attempt.TaskId,
                        attempt.WorkflowInstanceId,
                        attempt.CorrelationId,
                        attempt.Id,
                        attempt.ToolVersion,
                        actorAuthorization?.ActorId ?? approval.RequestedByUserId),
                    cancellationToken);
                if (string.Equals(result.Status, ToolExecutionStatus.Denied.ToStorageValue(), StringComparison.OrdinalIgnoreCase))
                {
                    attempt.MarkDenied(policyDecision, result.ToStructuredPayload());
                    return LinkedEntityStateTransition.ForAction(attempt.Id, previousStatus, attempt.Status.ToStorageValue());
                }

                if (IsAmbiguousProviderResult(result))
                {
                    attempt.MarkReconciliationRequired(
                        policyDecision,
                        result.ToStructuredPayload(),
                        result.ErrorCode ?? "ambiguous_provider_outcome");
                    return LinkedEntityStateTransition.ForAction(
                        attempt.Id, previousStatus, attempt.Status.ToStorageValue());
                }

                if (!result.Success)
                {
                    attempt.MarkFailed(policyDecision, result.ToStructuredPayload());
                    return LinkedEntityStateTransition.ForAction(attempt.Id, previousStatus, attempt.Status.ToStorageValue());
                }

                attempt.MarkExecuted(policyDecision, result.ToStructuredPayload());
                return LinkedEntityStateTransition.ForAction(attempt.Id, previousStatus, attempt.Status.ToStorageValue());
            }
        }
        else if (targetType == ApprovalTargetEntityType.OperatingPlan)
        {
            var plan = await _dbContext.OperatingPlans.SingleAsync(
                x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId,
                cancellationToken);
            var previousStatus = plan.Status.ToStorageValue();
            if (approval.Status == ApprovalRequestStatus.Approved)
            {
                if (plan.Status == OperatingPlanStatus.AwaitingReview)
                    plan.Approve();
                return LinkedEntityStateTransition.ForOperatingPlan(plan.Id, previousStatus, plan.Status.ToStorageValue());
            }

            if (approval.Status is ApprovalRequestStatus.Rejected or ApprovalRequestStatus.Expired or ApprovalRequestStatus.Cancelled)
            {
                if (plan.Status == OperatingPlanStatus.AwaitingReview)
                    plan.Reject();
                return LinkedEntityStateTransition.ForOperatingPlan(plan.Id, previousStatus, plan.Status.ToStorageValue());
            }
        }
        else if (targetType == ApprovalTargetEntityType.SalesMeetingInvitation)
        {
            var invitation = await _dbContext.SalesMeetingInvitations.SingleAsync(
                x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId,
                cancellationToken);
            var previousStatus = invitation.Status.ToStorageValue();
            if (approval.Status == ApprovalRequestStatus.Approved)
            {
                var approver = approval.Steps.FirstOrDefault(x => x.DecidedByUserId.HasValue)?.DecidedByUserId;
                invitation.MarkApproved(approver, DateTime.UtcNow);
                _outboxEnqueuer.Enqueue(
                    approval.CompanyId,
                    CompanyOutboxTopics.SalesMeetingInvitationDeliveryRequested,
                    new SalesMeetingInvitationDeliveryRequestedMessage(
                        approval.CompanyId,
                        invitation.Id,
                        invitation.IdempotencyKey,
                        approval.Id.ToString("N")),
                    correlationId: approval.Id.ToString("N"),
                    idempotencyKey: $"sales-meeting-delivery:{approval.CompanyId:N}:{invitation.Id:N}:v1",
                    causationId: approval.Id.ToString("N"));
                return LinkedEntityStateTransition.ForSalesMeetingInvitation(
                    invitation.Id,
                    previousStatus,
                    invitation.Status.ToStorageValue());
            }

            if (approval.Status is ApprovalRequestStatus.Rejected or ApprovalRequestStatus.Expired or ApprovalRequestStatus.Cancelled)
            {
                invitation.MarkRejected(DateTime.UtcNow);
                return LinkedEntityStateTransition.ForSalesMeetingInvitation(
                    invitation.Id,
                    previousStatus,
                    invitation.Status.ToStorageValue());
            }
        }
        else if (targetType == ApprovalTargetEntityType.SalesMeetingChangeRequest)
        {
            var change = await _dbContext.SalesMeetingChangeRequests.SingleAsync(
                x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId,
                cancellationToken);
            var previousStatus = change.Status.ToStorageValue();
            if (approval.Status == ApprovalRequestStatus.Approved)
            {
                var approver = approval.Steps.FirstOrDefault(x => x.DecidedByUserId.HasValue)?.DecidedByUserId;
                change.MarkApproved(approver, DateTime.UtcNow);
                _outboxEnqueuer.Enqueue(
                    approval.CompanyId,
                    CompanyOutboxTopics.SalesMeetingChangeDeliveryRequested,
                    new SalesMeetingChangeDeliveryRequestedMessage(
                        approval.CompanyId, change.Id, change.IdempotencyKey, approval.Id.ToString("N")),
                    correlationId: approval.Id.ToString("N"),
                    idempotencyKey: $"sales-meeting-change-delivery:{approval.CompanyId:N}:{change.Id:N}:v1",
                    causationId: approval.Id.ToString("N"));
                return LinkedEntityStateTransition.ForSalesMeetingChangeRequest(
                    change.Id, previousStatus, change.Status.ToStorageValue());
            }

            if (approval.Status is ApprovalRequestStatus.Rejected or ApprovalRequestStatus.Expired or ApprovalRequestStatus.Cancelled)
            {
                change.MarkRejected(DateTime.UtcNow);
                return LinkedEntityStateTransition.ForSalesMeetingChangeRequest(
                    change.Id, previousStatus, change.Status.ToStorageValue());
            }
        }
        else if (targetType == ApprovalTargetEntityType.FinanceIntegrationWrite)
        {
            var command = await _dbContext.FinanceIntegrationWriteCommands
                .Include(x => x.Connection)
                .SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, cancellationToken);
            var previousStatus = command.Status;
            var now = DateTime.UtcNow;

            if (approval.Status == ApprovalRequestStatus.Approved)
            {
                var approver = approval.Steps.FirstOrDefault(x => x.DecidedByUserId.HasValue)?.DecidedByUserId;
                command.MarkApproved(approval.Id, approver, now);
                _dbContext.FinanceIntegrationAuditEvents.Add(new FinanceIntegrationAuditEvent(
                    Guid.NewGuid(),
                    command.CompanyId,
                    command.ConnectionId,
                    command.Connection?.ProviderKey ?? "accounting_system",
                    "write_approval_approved",
                    FinanceIntegrationAuditOutcomes.Succeeded,
                    command.CommandType,
                    command.Id,
                    null,
                    approval.Id.ToString("N"),
                    "Approved accounting-system action is ready for provider execution.",
                    now));
                return LinkedEntityStateTransition.ForFinanceIntegrationWrite(command.Id, previousStatus, command.Status);
            }

            if (approval.Status == ApprovalRequestStatus.Rejected)
            {
                command.MarkRejected(now);
                return LinkedEntityStateTransition.ForFinanceIntegrationWrite(command.Id, previousStatus, command.Status);
            }

            if (approval.Status == ApprovalRequestStatus.Expired)
            {
                command.MarkExpired(now);
                return LinkedEntityStateTransition.ForFinanceIntegrationWrite(command.Id, previousStatus, command.Status);
            }
        }
        else if (targetType == ApprovalTargetEntityType.AccountingProviderSwitchMappingDecision)
        {
            var decision = await _dbContext.AccountingProviderSwitchMappingDecisions.IgnoreQueryFilters()
                .Include(x => x.AffectedRecords)
                .SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId,
                    cancellationToken);
            var recordIds = decision.AffectedRecords.Select(x => x.StagedRecordId).ToArray();
            var currentRecords = await _dbContext.AccountingProviderSwitchStagedRecords.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.CompanyId == approval.CompanyId && x.SwitchId == decision.SwitchId &&
                            recordIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);
            var bindingCurrent = decision.AffectedRecords.Count > 0 && decision.AffectedRecords.All(link =>
                currentRecords.TryGetValue(link.StagedRecordId, out var record) && record.IsCurrent &&
                record.SourceHash == link.StagedSourceHash && record.NormalizedHash == link.StagedNormalizedHash);
            var actorUserId = approval.Steps.Where(x => x.DecidedByUserId.HasValue)
                .OrderByDescending(x => x.DecidedUtc).Select(x => x.DecidedByUserId!.Value)
                .FirstOrDefault();
            string action;
            string summary;
            string outcome;
            if (!bindingCurrent || decision.Status == AccountingProviderSwitchMappingStatuses.Stale)
            {
                if (decision.Status != AccountingProviderSwitchMappingStatuses.Stale)
                    decision.MarkStale(DateTime.UtcNow);
                action = AuditEventActions.AccountingProviderSwitchStaleDecisionRejected;
                summary = "The approval decision was recorded, but the mapping evidence was stale and cannot be used.";
                outcome = AuditEventOutcomes.Blocked;
            }
            else if (approval.Status == ApprovalRequestStatus.Approved)
            {
                decision.RecordApprovalDecision(approval.Id, approved: true, DateTime.UtcNow);
                action = AuditEventActions.AccountingProviderSwitchMappingApproved;
                summary = "The versioned accounting migration mapping was approved with current source evidence.";
                outcome = AuditEventOutcomes.Approved;
            }
            else
            {
                decision.RecordApprovalDecision(approval.Id, approved: false, DateTime.UtcNow);
                action = AuditEventActions.AccountingProviderSwitchMappingRejected;
                summary = "The versioned accounting migration mapping was rejected.";
                outcome = AuditEventOutcomes.Rejected;
            }
            await _auditEventWriter.WriteAsync(new AuditEventWriteRequest(
                approval.CompanyId,
                AuditActorTypes.User,
                actorUserId == Guid.Empty ? approval.RequestedByActorId : actorUserId,
                action,
                AuditTargetTypes.AccountingProviderSwitchMappingDecision,
                decision.Id.ToString("D"),
                outcome,
                summary,
                ["approval", "accounting_provider_switch", "mapping_decision"],
                new Dictionary<string, string?>
                {
                    ["switchId"] = decision.SwitchId.ToString("D"),
                    ["mappingVersion"] = decision.MappingVersion.ToString(),
                    ["bindingHash"] = decision.BindingHash,
                    ["approvalRequestId"] = approval.Id.ToString("D"),
                    ["bindingCurrent"] = bindingCurrent.ToString()
                }), cancellationToken);
        }
        else if (targetType == ApprovalTargetEntityType.AccountingProviderSwitchCutoverPlan)
        {
            var plan = await _dbContext.AccountingProviderSwitchCutoverPlans.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId,
                    cancellationToken);
            var actorUserId = approval.Steps.Where(x => x.DecidedByUserId.HasValue)
                .OrderByDescending(x => x.DecidedUtc).Select(x => x.DecidedByUserId!.Value).FirstOrDefault();
            await _auditEventWriter.WriteAsync(new AuditEventWriteRequest(
                approval.CompanyId, AuditActorTypes.User,
                actorUserId == Guid.Empty ? approval.RequestedByActorId : actorUserId,
                approval.Status == ApprovalRequestStatus.Approved
                    ? "accounting.provider_switch.plan_approved"
                    : "accounting.provider_switch.plan_rejected",
                AuditTargetTypes.AccountingProviderSwitchCutoverPlan, plan.Id.ToString("D"),
                approval.Status == ApprovalRequestStatus.Approved ? AuditEventOutcomes.Approved : AuditEventOutcomes.Rejected,
                approval.Status == ApprovalRequestStatus.Approved
                    ? "The immutable accounting migration cutover plan was approved."
                    : "The immutable accounting migration cutover plan was not approved.",
                ["approval", "accounting_provider_switch", "cutover_plan"],
                new Dictionary<string, string?>
                {
                    ["switchId"] = plan.SwitchId.ToString("D"), ["planVersion"] = plan.PlanVersion.ToString(),
                    ["planHash"] = plan.PlanHash, ["approvalRequestId"] = approval.Id.ToString("D")
                }), cancellationToken);
        }

        return null;
    }

    private async Task UpdateSupplierPaymentProposalAfterTaskApprovalAsync(
        ApprovalRequest approval,
        WorkTask task,
        bool approved,
        CancellationToken cancellationToken)
    {
        if (!task.InputPayload.TryGetValue("paymentProposalId", out var value) ||
            value is null ||
            !Guid.TryParse(value.GetValue<string>(), out var proposalId))
        {
            return;
        }

        var proposal = await _dbContext.SupplierInvoicePaymentProposals
            .SingleOrDefaultAsync(x => x.CompanyId == approval.CompanyId && x.Id == proposalId, cancellationToken);
        if (proposal is null)
        {
            return;
        }

        var decidedBy = approval.Steps.FirstOrDefault(step => step.DecidedByUserId.HasValue)?.DecidedByUserId;
        var decidedUtc = approval.DecidedUtc ?? DateTime.UtcNow;
        if (approved)
        {
            proposal.MarkReadyForPayment(decidedBy, decidedUtc, approval.DecisionSummary);
        }
        else
        {
            proposal.MarkRejected(decidedBy, decidedUtc, approval.DecisionSummary);
        }
    }

    private async Task UpdateSupportRefundAfterTaskApprovalAsync(
        ApprovalRequest approval,
        WorkTask task,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(approval.ApprovalType, "support_refund_credit", StringComparison.OrdinalIgnoreCase) ||
            !task.InputPayload.ContainsKey("refundRequestId"))
        {
            return;
        }

        var handler = _serviceProvider.GetRequiredService<ISupportRefundApprovalOutcomeHandler>();
        var decidedBy = approval.Steps.FirstOrDefault(step => step.DecidedByUserId.HasValue)?.DecidedByUserId;
        await handler.ProcessAsync(
            approval.CompanyId,
            approval.Id,
            approval.Status.ToStorageValue(),
            decidedBy,
            approval.DecisionSummary,
            cancellationToken);
    }

    private async Task MarkApprovalNotificationsActionedAsync(Guid companyId, Guid approvalId, Guid actionedByUserId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        await _dbContext.CompanyNotifications
            .Where(x => x.CompanyId == companyId &&
                        x.RelatedEntityType == AuditTargetTypes.ApprovalRequest &&
                        x.RelatedEntityId == approvalId &&
                        x.Status != CompanyNotificationStatus.Actioned)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.Status, CompanyNotificationStatus.Actioned)
                    .SetProperty(x => x.ActionedUtc, now)
                    .SetProperty(x => x.ActionedByUserId, (Guid?)actionedByUserId)
                    .SetProperty(x => x.ReadUtc, x => x.ReadUtc ?? now),
                cancellationToken);
    }

    private static bool CanDecide(ApprovalStep step, ResolvedCompanyMembershipContext membership)
    {
        if (step.ApproverType == ApprovalStepApproverType.User)
        {
            return Guid.TryParse(step.ApproverRef, out var userId) && userId == membership.UserId;
        }

        if (step.ApproverType == ApprovalStepApproverType.Role)
        {
            if (membership.MembershipRole is CompanyMembershipRole.Owner or CompanyMembershipRole.Admin)
            {
                return true;
            }

            return CompanyMembershipRoles.TryParse(step.ApproverRef, out var role) && role == membership.MembershipRole;
        }

        return false;
    }

    private void EnqueueApprovalNotification(ApprovalRequest approval)
    {
        var current = approval.CurrentActionableStep;
        if (current is null)
        {
            return;
        }

        var recipientUserId = current.ApproverType == ApprovalStepApproverType.User && Guid.TryParse(current.ApproverRef, out var userId)
            ? userId
            : (Guid?)null;
        var recipientRole = current.ApproverType == ApprovalStepApproverType.Role
            ? current.ApproverRef
            : null;

        _outboxEnqueuer.Enqueue(
            approval.CompanyId,
            CompanyOutboxTopics.NotificationDeliveryRequested,
            new NotificationDeliveryRequestedMessage(
                approval.CompanyId,
                CompanyNotificationType.ApprovalRequested.ToStorageValue(),
                CompanyNotificationPriority.High.ToStorageValue(),
                $"{approval.ApprovalType} approval requested",
                $"Review {approval.TargetEntityType} {approval.TargetEntityId:N}.",
                AuditTargetTypes.ApprovalRequest,
                approval.Id,
                $"/inbox?companyId={approval.CompanyId}&approvalId={approval.Id}",
                recipientUserId,
                recipientRole,
                null,
                null,
                $"approval-requested:{approval.Id:N}:step:{current.Id:N}",
                null),
            idempotencyKey: $"notification:approval-requested:{approval.Id:N}:step:{current.Id:N}",
            causationId: approval.Id.ToString("N"));
    }

    private static void ValidateDecision(ApprovalDecisionCommand command)
    {
        if (command.ApprovalId == Guid.Empty)
        {
            throw new ApprovalValidationException(new Dictionary<string, string[]> { [nameof(command.ApprovalId)] = ["Approval id is required."] });
        }

        var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "approve", "approved", "reject", "rejected", "request_changes", "changes_requested",
            "cancel", "cancelled", "expire", "expired", "revoke", "revoked", "supersede", "superseded"
        };
        if (!supported.Contains(command.Decision))
        {
            throw new ApprovalValidationException(new Dictionary<string, string[]>
            {
                [nameof(command.Decision)] = ["Decision must be approve, reject, request_changes, cancel, expire, revoke, or supersede."]
            });
        }

        if (command.Comment?.Trim().Length > 2000)
        {
            throw new ApprovalValidationException(new Dictionary<string, string[]> { [nameof(command.Comment)] = ["Decision comment must be 2000 characters or fewer."] });
        }
    }

    private async Task EnsureTargetExistsAsync(
        Guid companyId,
        ApprovalTargetEntityType targetType,
        Guid targetEntityId,
        CancellationToken cancellationToken)
    {
        var exists = targetType switch
        {
            ApprovalTargetEntityType.Task => await _dbContext.WorkTasks
                .AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.Id == targetEntityId, cancellationToken),
            ApprovalTargetEntityType.Workflow => await _dbContext.WorkflowInstances
                .AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.Id == targetEntityId, cancellationToken),
            ApprovalTargetEntityType.Action => await _dbContext.ToolExecutionAttempts
                .AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.Id == targetEntityId, cancellationToken),
            ApprovalTargetEntityType.FinanceIntegrationWrite => await _dbContext.FinanceIntegrationWriteCommands
                .AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.Id == targetEntityId, cancellationToken),
            ApprovalTargetEntityType.SalesMeetingInvitation => await _dbContext.SalesMeetingInvitations
                .AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.Id == targetEntityId, cancellationToken),
            ApprovalTargetEntityType.SalesMeetingChangeRequest => await _dbContext.SalesMeetingChangeRequests
                .AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.Id == targetEntityId, cancellationToken),
            ApprovalTargetEntityType.OperatingPlan => await _dbContext.OperatingPlans
                .AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.Id == targetEntityId, cancellationToken),
            ApprovalTargetEntityType.OperatingDecision => await _dbContext.OperatingDecisions
                .AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.Id == targetEntityId, cancellationToken),
            ApprovalTargetEntityType.AccountingProviderSwitchMappingDecision => await _dbContext.AccountingProviderSwitchMappingDecisions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.Id == targetEntityId, cancellationToken),
            ApprovalTargetEntityType.AccountingProviderSwitchCutoverPlan => await _dbContext.AccountingProviderSwitchCutoverPlans
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.Id == targetEntityId, cancellationToken),
            ApprovalTargetEntityType.AccountingProviderSwitchActivation => await _dbContext.AccountingProviderSwitchCutoverExecutions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.Id == targetEntityId, cancellationToken),
            ApprovalTargetEntityType.AccountingProviderSwitchClosure => await _dbContext.AccountingProviderSwitchMonitoringRuns
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.Id == targetEntityId, cancellationToken),
            ApprovalTargetEntityType.VatReturn => await _dbContext.VatReturns
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.Id == targetEntityId, cancellationToken),
            ApprovalTargetEntityType.TreasurySource =>
                await _dbContext.TreasuryTransfers.IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(x => x.CompanyId == companyId && x.Id == targetEntityId, cancellationToken) ||
                await _dbContext.BankAdjustments.IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(x => x.CompanyId == companyId && x.Id == targetEntityId, cancellationToken) ||
                await _dbContext.CardSettlements.IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(x => x.CompanyId == companyId && x.Id == targetEntityId, cancellationToken) ||
                await _dbContext.PayoutSettlements.IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(x => x.CompanyId == companyId && x.Id == targetEntityId, cancellationToken),
            _ => false
        };

        if (!exists)
        {
            throw new KeyNotFoundException("Approval target not found.");
        }
    }

    private void EnqueueApprovalUpdatedEvent(ApprovalRequest approval, string reason)
    {
        var eventType = SupportedPlatformEventTypeRegistry.ApprovalUpdated;
        var occurredAtUtc = approval.UpdatedUtc.Kind == DateTimeKind.Utc
            ? approval.UpdatedUtc
            : approval.UpdatedUtc.ToUniversalTime();
        var eventId = $"{eventType}:{approval.Id:N}:{occurredAtUtc:yyyyMMddHHmmssfffffff}:{reason}";

        _outboxEnqueuer.Enqueue(
            approval.CompanyId,
            eventType,
            new PlatformEventEnvelope(
                eventId,
                eventType,
                occurredAtUtc,
                approval.CompanyId,
                eventId,
                "approval_request",
                approval.Id.ToString("N"),
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["approvalRequestId"] = JsonValue.Create(approval.Id.ToString("N")),
                    ["agentId"] = approval.AgentId != Guid.Empty
                        ? JsonValue.Create(approval.AgentId.ToString("N"))
                        : null,
                    ["targetEntityType"] = JsonValue.Create(approval.TargetEntityType),
                    ["targetEntityId"] = JsonValue.Create(approval.TargetEntityId.ToString("N")),
                    ["status"] = JsonValue.Create(approval.Status.ToStorageValue()),
                    ["reason"] = JsonValue.Create(reason)
                }),
            eventId,
            idempotencyKey: $"platform-event:{approval.CompanyId:N}:{eventId}",
            causationId: approval.Id.ToString("N"));
    }

    private static void Validate(CreateApprovalRequestCommand command)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (!ApprovalTargetEntityTypeValues.TryParse(command.TargetEntityType, out _))
        {
            errors[nameof(command.TargetEntityType)] = [$"Target entity type must be one of: {string.Join(", ", ApprovalTargetEntityTypeValues.AllowedValues)}."];
        }

        if (command.TargetEntityId == Guid.Empty)
        {
            errors[nameof(command.TargetEntityId)] = ["Target entity id is required."];
        }

        if (string.IsNullOrWhiteSpace(command.RequestedByActorType))
        {
            errors[nameof(command.RequestedByActorType)] = ["Requested-by actor type is required."];
        }

        if (command.RequestedByActorId == Guid.Empty)
        {
            errors[nameof(command.RequestedByActorId)] = ["Requested-by actor id is required."];
        }

        if (string.IsNullOrWhiteSpace(command.ApprovalType))
        {
            errors[nameof(command.ApprovalType)] = ["Approval type is required."];
        }

        if (command.ThresholdContext is null || command.ThresholdContext.Count == 0)
        {
            errors[nameof(command.ThresholdContext)] = ["Threshold context is required."];
        }

        if (command.RequiredUserId == Guid.Empty)
        {
            errors[nameof(command.RequiredUserId)] = ["Required user id cannot be empty."];
        }

        var hasTopLevelApprover = !string.IsNullOrWhiteSpace(command.RequiredRole) || command.RequiredUserId.HasValue;
        var hasSteps = command.Steps is { Count: > 0 };
        if (!hasTopLevelApprover && !hasSteps)
        {
            errors["Approver"] = ["At least one required role, required user, or ordered approval step is required."];
        }

        if (hasTopLevelApprover && hasSteps)
        {
            errors["Approver"] = ["Use either top-level required approver fields or ordered approval steps, not both."];
        }

        if (hasSteps)
        {
            var steps = command.Steps ?? [];
            var invalidStep = steps.FirstOrDefault(step =>
                step.SequenceNo <= 0 ||
                string.IsNullOrWhiteSpace(step.ApproverType) ||
                string.IsNullOrWhiteSpace(step.ApproverRef));
            if (invalidStep is not null)
            {
                errors[nameof(command.Steps)] = ["Approval steps require a positive sequence number, approver type, and approver reference."];
            }
            else if (steps.Select(step => step.SequenceNo).Distinct().Count() != steps.Count)
            {
                errors[nameof(command.Steps)] = ["Approval step sequence numbers must be unique."];
            }
            else if (steps.Any(step => !ApprovalStepApproverTypeValues.AllowedValues.Contains(step.ApproverType, StringComparer.OrdinalIgnoreCase)))
            {
                errors[nameof(command.Steps)] = [$"Approval step approver type must be one of: {string.Join(", ", ApprovalStepApproverTypeValues.AllowedValues)}."];
            }
        }

        if (errors.Count > 0)
        {
            throw new ApprovalValidationException(errors);
        }
    }

    private async Task<ResolvedCompanyMembershipContext> RequireMembershipAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var membership = await _companyMembershipContextResolver.ResolveAsync(companyId, cancellationToken);
        if (membership is null)
        {
            throw new UnauthorizedAccessException("The current user does not have an active membership in the requested company.");
        }

        return membership;
    }

    private async Task WriteDecisionAuditAsync(
        ApprovalRequest approval,
        ApprovalStep step,
        Guid actorUserId,
        bool rejected,
        CancellationToken cancellationToken)
    {
        await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                approval.CompanyId,
                AuditActorTypes.User,
                actorUserId,
                rejected ? AuditEventActions.ApprovalStepRejected : AuditEventActions.ApprovalStepApproved,
                AuditTargetTypes.ApprovalRequest,
                approval.Id.ToString("N"),
                rejected ? AuditEventOutcomes.Rejected : AuditEventOutcomes.Approved,
                DataSources: ["approvals", "http_request"],
                RationaleSummary: $"Approval step {step.SequenceNo} {(rejected ? "rejected" : "approved")}.",
                Metadata: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["approvalRequestId"] = approval.Id.ToString("N"),
                    ["approvalStepId"] = step.Id.ToString("N"),
                    ["sequenceNo"] = step.SequenceNo.ToString(),
                    ["approverType"] = step.ApproverType.ToStorageValue(),
                    ["approverRef"] = step.ApproverRef,
                    ["targetEntityType"] = approval.TargetEntityType,
                    ["targetEntityId"] = approval.TargetEntityId.ToString("N"),
                    ["comment"] = step.Comment
                }),
            cancellationToken);
    }

    private async Task WriteChainAdvancedAuditAsync(
        ApprovalRequest approval,
        ApprovalStep step,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var nextStep = approval.CurrentActionableStep;
        if (nextStep is null)
        {
            return;
        }

        await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                approval.CompanyId,
                AuditActorTypes.User,
                actorUserId,
                AuditEventActions.ApprovalChainAdvanced,
                AuditTargetTypes.ApprovalRequest,
                approval.Id.ToString("N"),
                AuditEventOutcomes.Pending,
                DataSources: ["approvals", "http_request"],
                RationaleSummary: $"Approval chain advanced from step {step.SequenceNo} to step {nextStep.SequenceNo}.",
                Metadata: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["approvalRequestId"] = approval.Id.ToString("N"),
                    ["completedStepId"] = step.Id.ToString("N"),
                    ["nextStepId"] = nextStep.Id.ToString("N")
                }),
            cancellationToken);
    }

    private async Task WriteCompletionAuditAsync(ApprovalRequest approval, Guid actorUserId, CancellationToken cancellationToken)
    {
        var rejectionComment = GetRejectionComment(approval);
        await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                approval.CompanyId,
                AuditActorTypes.User,
                actorUserId,
                AuditEventActions.ApprovalCompleted,
                AuditTargetTypes.ApprovalRequest,
                approval.Id.ToString("N"),
                approval.Status == ApprovalRequestStatus.Approved ? AuditEventOutcomes.Approved : AuditEventOutcomes.Rejected,
                DataSources: ["approvals", "http_request"],
                RationaleSummary: $"Approval completed with status {approval.Status.ToStorageValue()}",
                Metadata: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["approvalRequestId"] = approval.Id.ToString("N"),
                    ["targetEntityType"] = approval.TargetEntityType,
                    ["targetEntityId"] = approval.TargetEntityId.ToString("N"),
                    ["approvalStatus"] = approval.Status.ToStorageValue(),
                    ["rejectionComment"] = rejectionComment
                }),
            cancellationToken);
    }

    private async Task WriteLinkedEntityStateAuditAsync(
        ApprovalRequest approval,
        LinkedEntityStateTransition transition,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var rejectionComment = GetRejectionComment(approval);
        await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                approval.CompanyId,
                AuditActorTypes.User,
                actorUserId,
                AuditEventActions.ApprovalLinkedEntityStateUpdated,
                transition.AuditTargetType,
                transition.TargetId,
                AuditEventOutcomes.Succeeded,
                DataSources: ["approvals", transition.DataSource],
                RationaleSummary: $"Approval {approval.Status.ToStorageValue()} transitioned {approval.TargetEntityType} from {transition.PreviousState} to {transition.CurrentState}.",
                Metadata: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["approvalRequestId"] = approval.Id.ToString("N"),
                    ["targetEntityType"] = approval.TargetEntityType,
                    ["targetEntityId"] = approval.TargetEntityId.ToString("N"),
                    ["previousState"] = transition.PreviousState,
                    ["currentState"] = transition.CurrentState,
                    ["approvalStatus"] = approval.Status.ToStorageValue(),
                    ["rejectionComment"] = rejectionComment
                }),
            cancellationToken);
    }

    private async Task<ApprovalRequestDto> ToDtoAsync(
        ApprovalRequest approval,
        CancellationToken cancellationToken)
    {
        var contexts = await BuildSummaryContextsAsync(approval.CompanyId, [approval], cancellationToken);
        return ToDto(approval, contexts.GetValueOrDefault(approval.Id));
    }

    private async Task<IReadOnlyDictionary<Guid, ApprovalSummaryContext>> BuildSummaryContextsAsync(
        Guid companyId,
        IReadOnlyCollection<ApprovalRequest> approvals,
        CancellationToken cancellationToken)
    {
        if (approvals.Count == 0)
        {
            return new Dictionary<Guid, ApprovalSummaryContext>();
        }

        var taskIds = approvals
            .Where(x => string.Equals(x.TargetEntityType, ApprovalTargetEntityType.Task.ToStorageValue(), StringComparison.OrdinalIgnoreCase))
            .Select(x => x.TargetEntityId)
            .Distinct()
            .ToList();
        var workflowIds = approvals
            .Where(x => string.Equals(x.TargetEntityType, ApprovalTargetEntityType.Workflow.ToStorageValue(), StringComparison.OrdinalIgnoreCase))
            .Select(x => x.TargetEntityId)
            .Distinct()
            .ToList();
        var actionIds = approvals
            .Where(x => string.Equals(x.TargetEntityType, ApprovalTargetEntityType.Action.ToStorageValue(), StringComparison.OrdinalIgnoreCase))
            .Select(x => x.TargetEntityId)
            .Distinct()
            .ToList();
        var meetingIds = approvals
            .Where(x => string.Equals(x.TargetEntityType, ApprovalTargetEntityType.SalesMeetingInvitation.ToStorageValue(), StringComparison.OrdinalIgnoreCase))
            .Select(x => x.TargetEntityId)
            .Distinct()
            .ToList();
        var meetingChangeIds = approvals
            .Where(x => string.Equals(x.TargetEntityType, ApprovalTargetEntityType.SalesMeetingChangeRequest.ToStorageValue(), StringComparison.OrdinalIgnoreCase))
            .Select(x => x.TargetEntityId)
            .Distinct()
            .ToList();
        var operatingPlanIds = approvals
            .Where(x => string.Equals(x.TargetEntityType, ApprovalTargetEntityType.OperatingPlan.ToStorageValue(), StringComparison.OrdinalIgnoreCase))
            .Select(x => x.TargetEntityId)
            .Distinct()
            .ToList();

        var tasks = taskIds.Count == 0
            ? new Dictionary<Guid, WorkTask>()
            : await _dbContext.WorkTasks
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && taskIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        var workflows = workflowIds.Count == 0
            ? new Dictionary<Guid, WorkflowInstance>()
            : await _dbContext.WorkflowInstances
                .AsNoTracking()
                .Include(x => x.Definition)
                .Where(x => x.CompanyId == companyId && workflowIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        var actions = actionIds.Count == 0
            ? new Dictionary<Guid, ToolExecutionAttempt>()
            : await _dbContext.ToolExecutionAttempts
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && actionIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        var meetings = meetingIds.Count == 0
            ? new Dictionary<Guid, SalesMeetingInvitation>()
            : await _dbContext.SalesMeetingInvitations
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && meetingIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);
        var meetingChanges = meetingChangeIds.Count == 0
            ? new Dictionary<Guid, SalesMeetingChangeRequest>()
            : await _dbContext.SalesMeetingChangeRequests
                .AsNoTracking()
                .Include(x => x.Invitation)
                .Where(x => x.CompanyId == companyId && meetingChangeIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);
        var operatingPlans = operatingPlanIds.Count == 0
            ? new Dictionary<Guid, OperatingPlan>()
            : await _dbContext.OperatingPlans
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && operatingPlanIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        return approvals.ToDictionary(
            approval => approval.Id,
            approval =>
            {
                if (tasks.TryGetValue(approval.TargetEntityId, out var task))
                {
                    return BuildTaskApprovalSummaryContext(task, approval);
                }

                if (workflows.TryGetValue(approval.TargetEntityId, out var workflow))
                {
                    var label = string.IsNullOrWhiteSpace(workflow.CurrentStep)
                        ? workflow.Definition.Name
                        : $"{workflow.Definition.Name} ({workflow.CurrentStep})";
                    return new ApprovalSummaryContext(
                        TryReadString(workflow.ContextJson, "rationaleSummary", "rationale"),
                        $"Workflow: {label}",
                        [new ApprovalAffectedEntityDto(ApprovalTargetEntityType.Workflow.ToStorageValue(), workflow.Id, label)]);
                }

                if (actions.TryGetValue(approval.TargetEntityId, out var action))
                {
                    var actionLabel = string.IsNullOrWhiteSpace(action.Scope)
                        ? $"{action.ToolName} {action.ActionType.ToStorageValue()}"
                        : $"{action.ToolName} {action.ActionType.ToStorageValue()} ({action.Scope})";
                    return new ApprovalSummaryContext(
                        TryReadString(action.PolicyDecision, "explanation", "summary", "message"),
                        $"Action: {actionLabel}",
                        [new ApprovalAffectedEntityDto(ApprovalTargetEntityType.Action.ToStorageValue(), action.Id, actionLabel)]);
                }

                if (operatingPlans.TryGetValue(approval.TargetEntityId, out var operatingPlan))
                {
                    var label = $"Company operating plan v{operatingPlan.Version}: {operatingPlan.Objective}";
                    return new ApprovalSummaryContext(
                        operatingPlan.RationaleSummary,
                        label,
                        [new ApprovalAffectedEntityDto(ApprovalTargetEntityType.OperatingPlan.ToStorageValue(), operatingPlan.Id, label)]);
                }

                if (meetings.TryGetValue(approval.TargetEntityId, out var meeting))
                {
                    var label = $"{meeting.Title} with {meeting.AttendeeEmail}";
                    return new ApprovalSummaryContext(
                        "Review the recipient, time, agenda, and connected calendar before sending this invitation.",
                        $"Meeting invitation: {label}; {meeting.StartsUtc:u} to {meeting.EndsUtc:u}",
                        [new ApprovalAffectedEntityDto(
                            ApprovalTargetEntityType.SalesMeetingInvitation.ToStorageValue(),
                            meeting.Id,
                            label)]);
                }
                if (meetingChanges.TryGetValue(approval.TargetEntityId, out var meetingChange))
                {
                    var operation = meetingChange.Operation == SalesMeetingChangeOperation.Reschedule ? "Reschedule" : "Cancel";
                    var label = $"{operation} {meetingChange.Invitation.Title} with {meetingChange.Invitation.AttendeeEmail}";
                    var timing = meetingChange.Operation == SalesMeetingChangeOperation.Reschedule
                        ? $" from {meetingChange.Invitation.StartsUtc:u} to {meetingChange.StartsUtc:u}"
                        : $" at {meetingChange.Invitation.StartsUtc:u}";
                    return new ApprovalSummaryContext(
                        "Review the recipient, provider event, and requested meeting change before applying it.",
                        $"Meeting change: {label}{timing}",
                        [new ApprovalAffectedEntityDto(
                            ApprovalTargetEntityType.SalesMeetingChangeRequest.ToStorageValue(),
                            meetingChange.Id,
                            label)]);
                }
                return new ApprovalSummaryContext(
                    null,
                    $"{ToDisplayName(approval.TargetEntityType)}: {approval.TargetEntityId:N}",
                    [new ApprovalAffectedEntityDto(approval.TargetEntityType, approval.TargetEntityId, ToDisplayName(approval.TargetEntityType))]);
            });
    }

    private static ApprovalSummaryContext BuildTaskApprovalSummaryContext(WorkTask task, ApprovalRequest approval)
    {
        var invoiceId = TryGetGuid(task.OutputPayload, "invoiceId") ??
            TryGetGuid(task.InputPayload, "invoiceId") ??
            TryGetGuid(approval.ThresholdContext, "invoiceId");
        var invoiceNumber = FirstNonEmptyOrNull(
            TryReadString(task.OutputPayload, "invoiceNumber"),
            TryReadString(task.InputPayload, "invoiceNumber"),
            TryReadString(approval.ThresholdContext, "invoiceNumber"));
        var counterpartyName = FirstNonEmptyOrNull(
            TryReadString(task.OutputPayload, "vendorName", "counterpartyName"),
            TryReadString(task.InputPayload, "vendorName", "counterpartyName"),
            TryReadString(approval.ThresholdContext, "vendorName", "counterpartyName"));
        var invoiceStatus = FirstNonEmptyOrNull(
            TryReadString(task.OutputPayload, "invoiceStatus"),
            TryReadString(task.InputPayload, "status"),
            TryReadString(approval.ThresholdContext, "invoiceStatus"));
        var invoiceAmount = TryGetDecimal(task.OutputPayload, "invoiceAmount") ??
            TryGetDecimal(task.InputPayload, "amount") ??
            TryGetDecimal(approval.ThresholdContext, "invoiceAmount");
        var invoiceCurrency = FirstNonEmptyOrNull(
            TryReadString(task.OutputPayload, "invoiceCurrency"),
            TryReadString(task.InputPayload, "currency"),
            TryReadString(approval.ThresholdContext, "invoiceCurrency"));
        var transactionCount = TryGetNestedInt(task.OutputPayload, "relatedPaymentContext", "transactionCount") ??
            TryGetNestedInt(approval.ThresholdContext, "relatedPaymentContext", "transactionCount");
        var totalPaidAmount = TryGetNestedDecimal(task.OutputPayload, "relatedPaymentContext", "totalPaidAmount") ??
            TryGetNestedDecimal(approval.ThresholdContext, "relatedPaymentContext", "totalPaidAmount");
        var paymentCurrency = FirstNonEmptyOrNull(
            TryGetNestedString(task.OutputPayload, "relatedPaymentContext", "currency"),
            TryGetNestedString(approval.ThresholdContext, "relatedPaymentContext", "currency"),
            invoiceCurrency);

        var affectedSummaryParts = new List<string> { $"Task: {task.Title}" };
        var affectedEntities = new List<ApprovalAffectedEntityDto>
        {
            new(ApprovalTargetEntityType.Task.ToStorageValue(), task.Id, task.Title)
        };

        if (invoiceId.HasValue)
        {
            var invoiceLabel = string.IsNullOrWhiteSpace(invoiceNumber)
                ? $"Invoice {invoiceId.Value:N}"
                : $"Invoice {invoiceNumber}";
            affectedSummaryParts.Add(invoiceLabel);
            affectedEntities.Add(new ApprovalAffectedEntityDto("invoice", invoiceId.Value, invoiceLabel));
        }
        else if (!string.IsNullOrWhiteSpace(invoiceNumber))
        {
            affectedSummaryParts.Add($"Invoice {invoiceNumber}");
        }

        if (!string.IsNullOrWhiteSpace(counterpartyName))
        {
            affectedSummaryParts.Add($"Counterparty: {counterpartyName}");
        }

        if (invoiceAmount.HasValue && !string.IsNullOrWhiteSpace(invoiceCurrency))
        {
            affectedSummaryParts.Add($"Amount: {invoiceAmount.Value:0.##} {invoiceCurrency}");
        }

        if (!string.IsNullOrWhiteSpace(invoiceStatus))
        {
            affectedSummaryParts.Add($"Status: {invoiceStatus}");
        }

        var resolvedTransactionCount = transactionCount.GetValueOrDefault();
        if (resolvedTransactionCount > 0)
        {
            var paymentSummary = totalPaidAmount.HasValue && !string.IsNullOrWhiteSpace(paymentCurrency)
                ? $"Payment activity: {resolvedTransactionCount} transaction(s) totaling {totalPaidAmount.Value:0.##} {paymentCurrency}"
                : $"Payment activity: {resolvedTransactionCount} related transaction(s)";
            affectedSummaryParts.Add(paymentSummary);
        }

        var rationaleSummary = FirstNonEmptyOrNull(
            task.RationaleSummary,
            TryReadString(task.OutputPayload, "rationale"),
            TryReadString(approval.ThresholdContext, "rationaleSummary", "rationale", "explanation"));

        return new ApprovalSummaryContext(
            rationaleSummary,
            string.Join(" | ", affectedSummaryParts),
            affectedEntities);
    }

    private static Dictionary<string, JsonNode?> BuildBlockedApprovalResultPayload(ApprovalRequest approval, ToolExecutionAttempt attempt)
    {
        var reasonCode = approval.ExecutionBlockReasonCode ?? PolicyDecisionReasonCodes.ApprovalCancelled;
        var status = approval.Status == ApprovalRequestStatus.Rejected
            ? ToolExecutionStatus.Rejected.ToStorageValue()
            : ToolExecutionStatus.Denied.ToStorageValue();

        return new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["schemaVersion"] = JsonValue.Create(ToolExecutionResult.SchemaVersion),
            ["success"] = JsonValue.Create(false),
            ["status"] = JsonValue.Create(status),
            ["toolName"] = JsonValue.Create(attempt.ToolName),
            ["actionType"] = JsonValue.Create(attempt.ActionType.ToStorageValue()),
            ["errorCode"] = JsonValue.Create(reasonCode),
            ["errorMessage"] = JsonValue.Create(approval.DecisionSummary ?? "The approval request did not authorize execution."),
            ["approvalRequestId"] = JsonValue.Create(approval.Id),
            ["executionId"] = JsonValue.Create(attempt.Id),
            ["taskId"] = attempt.TaskId.HasValue ? JsonValue.Create(attempt.TaskId.Value) : null,
            ["workflowInstanceId"] = attempt.WorkflowInstanceId.HasValue ? JsonValue.Create(attempt.WorkflowInstanceId.Value) : null
        };
    }

    private static ApprovalRequestDto ToDto(ApprovalRequest approval, ApprovalSummaryContext? summaryContext)
    {
        var thresholdSummary = BuildThresholdSummary(approval.ThresholdContext);
        var rationaleSummary = Truncate(
            FirstNonEmpty(
                summaryContext?.RationaleSummary,
                TryReadString(approval.PolicyDecision, "explanation", "summary", "message"),
                TryReadString(approval.ThresholdContext, "rationaleSummary", "rationale", "explanation"),
                thresholdSummary is null ? null : DefaultRationaleSummary,
                DefaultRationaleSummary),
            SummaryMaxLength);
        var affectedDataSummary = Truncate(summaryContext?.AffectedDataSummary ?? DefaultAffectedDataSummary, SummaryMaxLength);

        return
        new(
            approval.Id,
            approval.CompanyId,
            approval.TargetEntityType,
            approval.TargetEntityId,
            approval.RequestedByActorType,
            approval.RequestedByActorId,
            approval.ApprovalType,
            approval.RequiredRole,
            approval.RequiredUserId,
            approval.Status.ToStorageValue(),
            CloneNodes(approval.ThresholdContext),
            approval.Steps.OrderBy(step => step.SequenceNo).Select(ToStepDto).ToList(),
            approval.CurrentActionableStep is { } currentStep ? ToStepDto(currentStep) : null,
            approval.DecisionSummary,
            GetRejectionComment(approval),
            rationaleSummary,
            affectedDataSummary,
            summaryContext?.AffectedEntities ?? [],
            thresholdSummary,
            approval.CreatedUtc);
    }

    private static string? BuildThresholdSummary(IReadOnlyDictionary<string, JsonNode?> thresholdContext)
    {
        var thresholdKey = TryReadString(thresholdContext, "thresholdKey");
        var thresholdValue = TryReadString(thresholdContext, "thresholdValue");
        var configuredThreshold = TryReadString(thresholdContext, "configuredThreshold");

        if (!string.IsNullOrWhiteSpace(thresholdKey) && !string.IsNullOrWhiteSpace(thresholdValue))
        {
            return string.IsNullOrWhiteSpace(configuredThreshold)
                ? $"Threshold: {thresholdKey} {thresholdValue}"
                : $"Threshold: {thresholdKey} {thresholdValue} (configured {configuredThreshold})";
        }

        var approvalTarget = TryReadString(thresholdContext, "approvalTarget");
        if (!string.IsNullOrWhiteSpace(approvalTarget))
        {
            return $"Approval target: {approvalTarget}";
        }

        return thresholdContext.Count > 0 ? "Configured approval threshold matched." : null;
    }

    private static string? TryReadString(IReadOnlyDictionary<string, JsonNode?> nodes, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!nodes.TryGetValue(key, out var node) || node is null)
            {
                continue;
            }

            if (node is JsonValue value && value.TryGetValue<string>(out var stringValue) && !string.IsNullOrWhiteSpace(stringValue))
            {
                return stringValue.Trim();
            }

            var text = node.ToJsonString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim().Trim('"');
            }
        }

        return null;
    }

    private static Guid? TryGetGuid(IReadOnlyDictionary<string, JsonNode?>? nodes, string key)
    {
        if (nodes is null ||
            !nodes.TryGetValue(key, out var node) ||
            node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<Guid>(out var guid) && guid != Guid.Empty)
        {
            return guid;
        }

        return value.TryGetValue<string>(out var text) && Guid.TryParse(text, out guid) && guid != Guid.Empty
            ? guid
            : null;
    }

    private static decimal? TryGetDecimal(IReadOnlyDictionary<string, JsonNode?>? nodes, string key)
    {
        if (nodes is null ||
            !nodes.TryGetValue(key, out var node) ||
            node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<decimal>(out var number))
        {
            return number;
        }

        return value.TryGetValue<string>(out var text) && decimal.TryParse(text, out number)
            ? number
            : null;
    }

    private static int? TryGetInt(IReadOnlyDictionary<string, JsonNode?>? nodes, string key)
    {
        if (nodes is null ||
            !nodes.TryGetValue(key, out var node) ||
            node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<int>(out var number))
        {
            return number;
        }

        return value.TryGetValue<string>(out var text) && int.TryParse(text, out number)
            ? number
            : null;
    }

    private static string? TryGetNestedString(IReadOnlyDictionary<string, JsonNode?>? nodes, string key, string nestedKey)
    {
        if (nodes is null ||
            !nodes.TryGetValue(key, out var node) ||
            node is not JsonObject obj)
        {
            return null;
        }

        return TryReadString(obj.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase), nestedKey);
    }

    private static int? TryGetNestedInt(IReadOnlyDictionary<string, JsonNode?>? nodes, string key, string nestedKey)
    {
        if (nodes is null ||
            !nodes.TryGetValue(key, out var node) ||
            node is not JsonObject obj)
        {
            return null;
        }

        return TryGetInt(obj.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase), nestedKey);
    }

    private static decimal? TryGetNestedDecimal(IReadOnlyDictionary<string, JsonNode?>? nodes, string key, string nestedKey)
    {
        if (nodes is null ||
            !nodes.TryGetValue(key, out var node) ||
            node is not JsonObject obj)
        {
            return null;
        }

        return TryGetDecimal(obj.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase), nestedKey);
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.First(value => !string.IsNullOrWhiteSpace(value))!.Trim();

    private static string? FirstNonEmptyOrNull(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength].TrimEnd() + "...";

    private static string ToDisplayName(string entityType) =>
        entityType switch
        {
            var value when string.Equals(value, ApprovalTargetEntityType.Task.ToStorageValue(), StringComparison.OrdinalIgnoreCase) => "Task",
            var value when string.Equals(value, ApprovalTargetEntityType.Workflow.ToStorageValue(), StringComparison.OrdinalIgnoreCase) => "Workflow",
            var value when string.Equals(value, ApprovalTargetEntityType.Action.ToStorageValue(), StringComparison.OrdinalIgnoreCase) => "Action",
            var value when string.Equals(value, ApprovalTargetEntityType.FinanceIntegrationWrite.ToStorageValue(), StringComparison.OrdinalIgnoreCase) => "Accounting system action",
            var value when string.Equals(value, ApprovalTargetEntityType.SalesMeetingInvitation.ToStorageValue(), StringComparison.OrdinalIgnoreCase) => "Meeting invitation",
            var value when string.Equals(value, ApprovalTargetEntityType.SalesMeetingChangeRequest.ToStorageValue(), StringComparison.OrdinalIgnoreCase) => "Meeting change",
            var value when string.Equals(value, ApprovalTargetEntityType.OperatingPlan.ToStorageValue(), StringComparison.OrdinalIgnoreCase) => "Company operating plan",
            var value when string.Equals(value, ApprovalTargetEntityType.OperatingDecision.ToStorageValue(), StringComparison.OrdinalIgnoreCase) => "Controlled company action",
            var value when string.Equals(value, ApprovalTargetEntityType.AccountingProviderSwitchMappingDecision.ToStorageValue(), StringComparison.OrdinalIgnoreCase) => "Accounting migration mapping",
            var value when string.Equals(value, ApprovalTargetEntityType.AccountingProviderSwitchCutoverPlan.ToStorageValue(), StringComparison.OrdinalIgnoreCase) => "Accounting migration cutover plan",
            var value when string.Equals(value, ApprovalTargetEntityType.AccountingProviderSwitchActivation.ToStorageValue(), StringComparison.OrdinalIgnoreCase) => "Accounting migration activation",
            var value when string.Equals(value, ApprovalTargetEntityType.AccountingProviderSwitchClosure.ToStorageValue(), StringComparison.OrdinalIgnoreCase) => "Accounting migration closure",
            var value when string.Equals(value, ApprovalTargetEntityType.VatReturn.ToStorageValue(), StringComparison.OrdinalIgnoreCase) => "VAT return",
            var value when string.Equals(value, ApprovalTargetEntityType.TreasurySource.ToStorageValue(), StringComparison.OrdinalIgnoreCase) => "Treasury source",
            var value when string.Equals(value, "fortnox_write", StringComparison.OrdinalIgnoreCase) => "Accounting system action",
            _ => entityType
        };

    private static Dictionary<string, JsonNode?> BuildApprovedApprovalPolicyDecision(ApprovalRequest approval)
    {
        var policyDecision = CloneNodes(approval.PolicyDecision);
        var approvalStatus = approval.Status.ToStorageValue();

        policyDecision["outcome"] = JsonValue.Create(PolicyDecisionOutcomeValues.Allow);
        policyDecision["approvalRequired"] = JsonValue.Create(false);
        policyDecision["approvalStatus"] = JsonValue.Create(approvalStatus);

        JsonObject metadata;
        if (policyDecision.TryGetValue("metadata", out var metadataNode) && metadataNode is JsonObject existingMetadata)
        {
            metadata = existingMetadata;
        }
        else
        {
            metadata = [];
            policyDecision["metadata"] = metadata;
        }

        metadata["approvalRequestId"] = JsonValue.Create(approval.Id);
        metadata["approvalStatus"] = JsonValue.Create(approvalStatus);
        metadata["executionBlocked"] = JsonValue.Create(false);
        metadata["blockedPendingApproval"] = JsonValue.Create(false);
        metadata["executionState"] = JsonValue.Create(ToolExecutionStatus.Executed.ToStorageValue());

        if (!string.IsNullOrWhiteSpace(approval.DecisionSummary))
        {
            metadata["approvalDecisionSummary"] = JsonValue.Create(approval.DecisionSummary);
        }

        return policyDecision;
    }

    private static Dictionary<string, JsonNode?> BuildBlockedApprovalPolicyDecision(ApprovalRequest approval)
    {
        var policyDecision = CloneNodes(approval.PolicyDecision);
        var reasonCode = approval.ExecutionBlockReasonCode ?? "approval_not_executable";
        var approvalStatus = approval.Status.ToStorageValue();

        policyDecision["outcome"] = JsonValue.Create(PolicyDecisionOutcomeValues.Deny);
        policyDecision["approvalStatus"] = JsonValue.Create(approvalStatus);

        JsonObject metadata;
        if (policyDecision.TryGetValue("metadata", out var metadataNode) && metadataNode is JsonObject existingMetadata)
        {
            metadata = existingMetadata;
        }
        else
        {
            metadata = [];
            policyDecision["metadata"] = metadata;
        }

        metadata["approvalRequestId"] = approval.Id;
        metadata["approvalStatus"] = approvalStatus;
        metadata["rejectionComment"] = GetRejectionComment(approval);
        metadata["executionBlockedReason"] = reasonCode;

        JsonArray reasons;
        if (policyDecision.TryGetValue("reasons", out var reasonsNode) && reasonsNode is JsonArray existingReasons)
        {
            reasons = existingReasons;
        }
        else
        {
            reasons = [];
            policyDecision["reasons"] = reasons;
        }

        reasons.Add(new JsonObject
        {
            ["code"] = reasonCode,
            ["category"] = "approval",
            ["message"] = $"Approval is {approvalStatus} and cannot execute the guarded action."
        });

        return policyDecision;
    }

    private static ApprovalStepDto ToStepDto(ApprovalStep step) =>
        new(step.Id, step.SequenceNo, step.ApproverType.ToStorageValue(), step.ApproverRef, step.Status.ToStorageValue(),
            step.DecidedByUserId, step.DecidedUtc, step.Comment);

    private static string? GetRejectionComment(ApprovalRequest approval) =>
        approval.Steps.FirstOrDefault(step => step.Status == ApprovalStepStatus.Rejected)?.Comment;

    private static Dictionary<string, JsonNode?> CloneNodes(IReadOnlyDictionary<string, JsonNode?> nodes) =>
        nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);

    private sealed record ApprovalSummaryContext(
        string? RationaleSummary,
        string AffectedDataSummary,
        IReadOnlyList<ApprovalAffectedEntityDto> AffectedEntities);

    private sealed record LinkedEntityStateTransition(
        string AuditTargetType,
        string TargetId,
        string PreviousState,
        string CurrentState,
        string DataSource)
    {
        public static LinkedEntityStateTransition ForTask(Guid id, string previousState, string currentState) =>
            new(AuditTargetTypes.WorkTask, id.ToString("N"), previousState, currentState, "tasks");
        public static LinkedEntityStateTransition ForWorkflow(Guid id, string previousState, string currentState) =>
            new(AuditTargetTypes.WorkflowInstance, id.ToString("N"), previousState, currentState, "workflow_instances");
        public static LinkedEntityStateTransition ForAction(Guid id, string previousState, string currentState) =>
            new(AuditTargetTypes.AgentToolExecution, id.ToString("N"), previousState, currentState, "agent_tool_executions");
        public static LinkedEntityStateTransition ForFinanceIntegrationWrite(Guid id, string previousState, string currentState) =>
            new(AuditTargetTypes.IntegrationConnection, id.ToString("N"), previousState, currentState, "fortnox_write_commands");
        public static LinkedEntityStateTransition ForSalesMeetingInvitation(Guid id, string previousState, string currentState) =>
            new("sales_meeting_invitation", id.ToString("N"), previousState, currentState, "sales_meeting_invitations");
        public static LinkedEntityStateTransition ForSalesMeetingChangeRequest(Guid id, string previousState, string currentState) =>
            new("sales_meeting_change_request", id.ToString("N"), previousState, currentState, "sales_meeting_change_requests");
        public static LinkedEntityStateTransition ForOperatingPlan(Guid id, string previousState, string currentState) =>
            new("operating_plan", id.ToString("N"), previousState, currentState, "operating_plans");
    }

    private async Task SynchronizeFinanceAutonomyApprovalAsync(
        ApprovalRequest approval, CancellationToken cancellationToken)
    {
        if (!approval.ThresholdContext.ContainsKey("financeAutonomy")) return;
        var coordinator = _serviceProvider.GetService<IFinanceAutonomyApprovalCoordinator>();
        if (coordinator is not null)
            await coordinator.ProcessApprovalAsync(approval.CompanyId, approval.Id, cancellationToken);
    }

    private bool IsFinanceToolAttempt(ToolExecutionAttempt attempt) =>
        _serviceProvider.GetRequiredService<ICompanyToolRegistry>()
            .TryGetTool(attempt.ToolName, out var registration) &&
        registration.Scopes.Contains("finance");

    private static bool IsAmbiguousProviderResult(ToolExecutionResult result) =>
        string.Equals(result.Status, ToolExecutionStatus.ReconciliationRequired.ToStorageValue(), StringComparison.OrdinalIgnoreCase) ||
        result.ErrorCode?.Contains("reconciliation_required", StringComparison.OrdinalIgnoreCase) == true ||
        result.ErrorCode?.Contains("ambiguous", StringComparison.OrdinalIgnoreCase) == true ||
        result.Metadata?.TryGetValue("providerReconciliationRequired", out var node) == true &&
        node is JsonValue value && value.TryGetValue<bool>(out var required) && required;

    private bool RequiresIndependentFinanceReview(ApprovalRequest approval)
    {
        if (ApprovalTargetEntityTypeValues.Parse(approval.TargetEntityType) != ApprovalTargetEntityType.Action)
        {
            return false;
        }

        return _serviceProvider.GetRequiredService<ICompanyToolRegistry>()
                   .TryGetTool(approval.ToolName, out var registration) &&
               registration.FinanceRiskClassification?.RequiresSegregation == true;
    }

    private static bool IsInitiatingUser(ApprovalRequest approval, Guid userId) =>
        approval.RequestedByUserId == userId ||
        (string.Equals(approval.RequestedByActorType, AuditActorTypes.User, StringComparison.OrdinalIgnoreCase) &&
         approval.RequestedByActorId == userId) ||
        approval.ThresholdContext.TryGetValue("approvalBinding", out var bindingNode) &&
        bindingNode is JsonObject binding &&
        FinanceApprovalContinuationBinding.ReadBindingGuid(binding, "initiatingUserId") == userId;

    private static bool IsExpiredFinanceActionApproval(ApprovalRequest approval, DateTime utcNow)
    {
        if (ApprovalTargetEntityTypeValues.Parse(approval.TargetEntityType) != ApprovalTargetEntityType.Action ||
            !approval.ThresholdContext.TryGetValue("approvalBinding", out var bindingNode) ||
            bindingNode is not JsonObject binding)
        {
            return false;
        }

        var expiresUtc = FinanceApprovalContinuationBinding.ReadBindingUtc(binding, "expiresUtc");
        return expiresUtc.HasValue && expiresUtc.Value <= utcNow.ToUniversalTime();
    }

    private async Task<FinanceContinuationValidation> RevalidateFinanceContinuationAsync(
        ApprovalRequest approval,
        ToolExecutionAttempt attempt,
        AgentEffectiveAuthorityDto currentAuthority,
        CancellationToken cancellationToken)
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

        if (!approval.ThresholdContext.TryGetValue("approvalBinding", out var bindingNode) ||
            bindingNode is not JsonObject binding ||
            !string.Equals(FinanceApprovalContinuationBinding.ReadBindingString(binding, "schemaVersion"),
                FinanceApprovalContinuationBinding.SchemaVersion, StringComparison.Ordinal))
        {
            return Invalid(
                FinanceApprovalContinuationReasonCodes.BindingMissing,
                "binding_missing_or_invalid",
                "The Finance approval is not bound to a current immutable action. Create and review a new request.");
        }

        FinanceAutonomyApprovalContextDto? autonomyContext = null;
        if (binding["financeAutonomy"] is JsonObject autonomyNode)
        {
            try
            {
                autonomyContext = JsonSerializer.Deserialize<FinanceAutonomyApprovalContextDto>(
                    autonomyNode.ToJsonString(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
                return Invalid(
                    FinanceApprovalContinuationReasonCodes.BindingMismatch,
                    "finance_autonomy_context_invalid",
                    "The autonomous Finance approval context is invalid. Create and review a new request.");
            }
            if (autonomyContext is null)
                return Invalid(
                    FinanceApprovalContinuationReasonCodes.BindingMismatch,
                    "finance_autonomy_context_missing",
                    "The autonomous Finance approval context is missing. Create and review a new request.");

            var autonomyState = await _dbContext.FinanceAutonomyRunSteps.IgnoreQueryFilters().AsNoTracking()
                .Where(step => step.CompanyId == approval.CompanyId && step.Id == autonomyContext.StepId &&
                               step.RunId == autonomyContext.RunId)
                .Select(step => new
                {
                    Step = step,
                    step.Run.AgentId,
                    step.Run.GrantId,
                    step.Run.GrantVersionId,
                    step.Run.GrantVersionNumber,
                    step.Run.CapabilityId,
                    step.Run.Trigger,
                    step.Run.PlanHash,
                    step.Run.PlanVersion,
                    step.Run.EvidenceHash,
                    step.Run.EvidenceObservedUtc,
                    step.Run.BudgetHash,
                    step.Run.PolicyVersion,
                    step.Run.CatalogueVersion,
                    ActionCount = step.Run.Steps.Count,
                    RunStatus = step.Run.Status
                })
                .SingleOrDefaultAsync(cancellationToken);
            var exactAutonomyState = autonomyState is not null &&
                autonomyState.AgentId == attempt.AgentId &&
                autonomyState.GrantId == autonomyContext.GrantId &&
                autonomyState.GrantVersionId == autonomyContext.GrantVersionId &&
                autonomyState.GrantVersionNumber == autonomyContext.GrantVersionNumber &&
                string.Equals(autonomyState.CapabilityId, autonomyContext.CapabilityId, StringComparison.Ordinal) &&
                string.Equals(autonomyState.Trigger, autonomyContext.Trigger, StringComparison.Ordinal) &&
                string.Equals(autonomyState.PlanHash, autonomyContext.PlanHash, StringComparison.Ordinal) &&
                string.Equals(autonomyState.PlanVersion, autonomyContext.PlanVersion, StringComparison.Ordinal) &&
                string.Equals(autonomyState.EvidenceHash, autonomyContext.EvidenceHash, StringComparison.Ordinal) &&
                autonomyState.EvidenceObservedUtc == autonomyContext.EvidenceObservedUtc &&
                string.Equals(autonomyState.BudgetHash, autonomyContext.BudgetHash, StringComparison.Ordinal) &&
                string.Equals(autonomyState.PolicyVersion, autonomyContext.AutonomyPolicyVersion, StringComparison.Ordinal) &&
                string.Equals(autonomyState.CatalogueVersion, autonomyContext.CatalogueVersion, StringComparison.Ordinal) &&
                autonomyState.RunStatus == FinanceAutonomyRunStatus.AwaitingApproval &&
                autonomyState.Step.Status == FinanceAutonomyStepStatus.AwaitingApproval &&
                autonomyState.Step.ApprovalRequestId == approval.Id &&
                autonomyState.Step.ToolExecutionAttemptId == attempt.Id &&
                string.Equals(autonomyState.Step.StepKey, autonomyContext.StepKey, StringComparison.Ordinal) &&
                string.Equals(autonomyState.Step.RequestedEffectHash, autonomyContext.RequestedEffectHash, StringComparison.Ordinal) &&
                string.Equals(autonomyState.Step.BusinessIdempotencyKey, autonomyContext.BusinessIdempotencyKey, StringComparison.Ordinal) &&
                autonomyState.Step.AttemptCount == autonomyContext.AttemptNumber &&
                autonomyState.ActionCount == autonomyContext.ActionCount;
            if (!exactAutonomyState)
                return Invalid(
                    FinanceApprovalContinuationReasonCodes.BindingMismatch,
                    "finance_autonomy_run_or_step_changed",
                    "The approved autonomous plan or step changed after review. Create and review a new request.");

            var currentAutonomyDecision = await _serviceProvider.GetRequiredService<IFinanceAutonomyPolicyEvaluator>()
                .EvaluateAsync(new FinanceAutonomyEvaluationRequest(
                    approval.CompanyId, attempt.AgentId, autonomyContext.CapabilityId,
                    autonomyContext.Trigger, autonomyState!.Step.ActionClass, attempt.ToolName,
                    EvidenceObservedUtc: autonomyContext.EvidenceObservedUtc,
                    ActionCount: autonomyContext.ActionCount), cancellationToken);
            if (!currentAutonomyDecision.IsAllowed || currentAutonomyDecision.GrantId != autonomyContext.GrantId ||
                currentAutonomyDecision.GrantVersionId != autonomyContext.GrantVersionId ||
                currentAutonomyDecision.GrantVersionNumber != autonomyContext.GrantVersionNumber ||
                !string.Equals(currentAutonomyDecision.PolicyVersion, autonomyContext.AutonomyPolicyVersion, StringComparison.Ordinal) ||
                !string.Equals(currentAutonomyDecision.CatalogueVersion, autonomyContext.CatalogueVersion, StringComparison.Ordinal) ||
                !string.Equals(currentAutonomyDecision.AuthorityVersion, currentAuthority.AuthorityVersion, StringComparison.Ordinal) ||
                !string.Equals(currentAutonomyDecision.AuthorityHash, currentAuthority.AuthorityHash, StringComparison.Ordinal))
                return Invalid(
                    FinanceApprovalContinuationReasonCodes.EligibilityFailed,
                    currentAutonomyDecision.ReasonCode,
                    "The autonomous Finance grant, eligibility, evidence, or human-only boundary changed. Create and review a new request.");

            var operatingBlocked = await _dbContext.CompanyOperatingConfigurations.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(item => item.CompanyId == approval.CompanyId && (item.EmergencyStopped || item.IsPaused), cancellationToken);
            var circuitOpen = await _dbContext.FinanceAutonomyCircuitBreakers.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(item => item.CompanyId == approval.CompanyId && item.AgentId == attempt.AgentId &&
                                  item.CapabilityId == autonomyContext.CapabilityId &&
                                  item.Status == FinanceAutonomyCircuitStatus.Open, cancellationToken);
            var budgetReservationValid = await _dbContext.FinanceAutonomyBudgetReservations.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(item => item.CompanyId == approval.CompanyId && item.RunId == autonomyContext.RunId &&
                                  item.StepId == autonomyContext.StepId && item.AttemptNumber == autonomyContext.AttemptNumber &&
                                  item.Status == FinanceAutonomyBudgetReservationStatus.Reconciled, cancellationToken);
            if (operatingBlocked || circuitOpen || !budgetReservationValid)
                return Invalid(
                    FinanceApprovalContinuationReasonCodes.EligibilityFailed,
                    operatingBlocked ? "finance_autonomy_operating_boundary_blocked" :
                    circuitOpen ? FinanceAutonomyBudgetReasonCodes.CircuitOpen : "finance_autonomy_budget_reservation_invalid",
                    "The autonomous Finance operating or budget boundary no longer permits continuation.");
        }

        var expiresUtc = FinanceApprovalContinuationBinding.ReadBindingUtc(binding, "expiresUtc");
        evidence["expiresUtc"] = expiresUtc;
        if (!expiresUtc.HasValue || expiresUtc.Value <= DateTime.UtcNow)
        {
            return Invalid(
                FinanceApprovalContinuationReasonCodes.Expired,
                "expired",
                "The Finance approval expired. Create and review a new request.");
        }

        var registry = _serviceProvider.GetRequiredService<ICompanyToolRegistry>();
        if (!registry.TryGetTool(attempt.ToolName, out var registration) ||
            registration.FinanceRiskClassification is null)
        {
            return Invalid(
                FinanceApprovalContinuationReasonCodes.PolicyStale,
                "tool_or_risk_classification_missing",
                "The Finance tool policy changed after review. Create and review a new request.");
        }

        var exactBindingMatches =
            FinanceApprovalContinuationBinding.ReadBindingGuid(binding, "companyId") == approval.CompanyId &&
            FinanceApprovalContinuationBinding.ReadBindingGuid(binding, "approvalRequestId") == approval.Id &&
            FinanceApprovalContinuationBinding.ReadBindingGuid(binding, "executionId") == attempt.Id &&
            FinanceApprovalContinuationBinding.ReadBindingGuid(binding, "agentId") == attempt.AgentId &&
            string.Equals(FinanceApprovalContinuationBinding.ReadBindingString(binding, "toolName"), attempt.ToolName, StringComparison.Ordinal) &&
            string.Equals(FinanceApprovalContinuationBinding.ReadBindingString(binding, "toolVersion"), attempt.ToolVersion, StringComparison.Ordinal) &&
            string.Equals(FinanceApprovalContinuationBinding.ReadBindingString(binding, "actionType"), attempt.ActionType.ToStorageValue(), StringComparison.Ordinal) &&
            string.Equals(FinanceApprovalContinuationBinding.ReadBindingString(binding, "scope"), attempt.Scope, StringComparison.Ordinal) &&
            string.Equals(FinanceApprovalContinuationBinding.ReadBindingString(binding, "riskTier"), registration.FinanceRiskClassification.RiskTier, StringComparison.Ordinal) &&
            string.Equals(FinanceApprovalContinuationBinding.ReadBindingString(binding, "requiredActorPermission"), registration.FinanceRiskClassification.RequiredActorPermission, StringComparison.Ordinal) &&
            string.Equals(FinanceApprovalContinuationBinding.ReadBindingString(binding, "approvalBehavior"), registration.FinanceRiskClassification.DefaultApprovalBehavior, StringComparison.Ordinal) &&
            string.Equals(FinanceApprovalContinuationBinding.ReadBindingString(binding, "externalSideEffectClass"), registration.FinanceRiskClassification.ExternalSideEffectClassification, StringComparison.Ordinal) &&
            FinanceApprovalContinuationBinding.ReadBindingBoolean(binding, "sensitiveAction") == registration.SensitiveAction &&
            FinanceApprovalContinuationBinding.ReadBindingBoolean(binding, "segregationRequired") == registration.FinanceRiskClassification.RequiresSegregation &&
            string.Equals(registration.Version, attempt.ToolVersion, StringComparison.Ordinal);
        if (!exactBindingMatches)
        {
            return Invalid(
                FinanceApprovalContinuationReasonCodes.BindingMismatch,
                "action_binding_mismatch",
                "The approved Finance action no longer matches the current attempt. Create and review a new request.");
        }

        var approvedPayloadHash = FinanceApprovalContinuationBinding.ReadBindingString(binding, "normalizedPayloadHash");
        var currentPayloadHash = FinanceApprovalContinuationBinding.ComputePayloadHash(attempt.RequestPayload);
        evidence["approvedPayloadHash"] = approvedPayloadHash;
        evidence["currentPayloadHash"] = currentPayloadHash;
        if (string.IsNullOrWhiteSpace(approvedPayloadHash) ||
            !string.Equals(approvedPayloadHash, currentPayloadHash, StringComparison.Ordinal))
        {
            return Invalid(
                FinanceApprovalContinuationReasonCodes.BindingMismatch,
                "payload_hash_mismatch",
                "The approved Finance payload changed after review. Create and review a new request.");
        }

        var approvedBusinessIdempotencyKey = FinanceApprovalContinuationBinding.ReadBindingString(binding, "businessIdempotencyKey");
        var currentBusinessIdempotencyKey = FinanceApprovalContinuationBinding.ComputeBusinessIdempotencyKey(attempt);
        var approvedContinuationKey = FinanceApprovalContinuationBinding.ReadBindingString(binding, "continuationKey");
        var currentContinuationKey = FinanceApprovalContinuationBinding.ComputeContinuationKey(
            attempt, currentPayloadHash, registration.FinanceRiskClassification.PolicyVersion);
        if (string.IsNullOrWhiteSpace(approvedBusinessIdempotencyKey) ||
            !string.Equals(approvedBusinessIdempotencyKey, currentBusinessIdempotencyKey, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(approvedContinuationKey) ||
            !string.Equals(approvedContinuationKey, currentContinuationKey, StringComparison.Ordinal))
        {
            return Invalid(
                FinanceApprovalContinuationReasonCodes.BindingMismatch,
                "idempotency_or_continuation_binding_mismatch",
                "The approved Finance continuation identity changed after review. Create and review a new request.");
        }

        var approvedAuthorityVersion = FinanceApprovalContinuationBinding.ReadBindingString(binding, "effectiveAuthorityVersion");
        var approvedAuthorityHash = FinanceApprovalContinuationBinding.ReadBindingString(binding, "effectiveAuthorityHash");
        if (string.IsNullOrWhiteSpace(approvedAuthorityHash) ||
            !string.Equals(approvedAuthorityVersion, currentAuthority.AuthorityVersion, StringComparison.Ordinal) ||
            !string.Equals(approvedAuthorityHash, currentAuthority.AuthorityHash, StringComparison.Ordinal))
        {
            return Invalid(
                FinanceApprovalContinuationReasonCodes.AuthorityStale,
                "authority_changed",
                "Agent authority changed after approval was requested. Create and review a new request.");
        }

        var currentTargets = await FinanceApprovalContinuationBinding.BuildTargetSnapshotAsync(
            _dbContext, attempt, cancellationToken);
        var approvedTargetHash = FinanceApprovalContinuationBinding.ReadBindingString(binding, "targetSnapshotHash");
        var approvedTargets = binding["targetSnapshot"] as JsonArray;
        var currentTargetHash = FinanceApprovalContinuationBinding.ComputeTargetSnapshotHash(currentTargets);
        evidence["approvedTargetSnapshotHash"] = approvedTargetHash;
        evidence["currentTargetSnapshotHash"] = currentTargetHash;
        if (approvedTargets is null ||
            !string.Equals(approvedTargetHash,
                FinanceApprovalContinuationBinding.ComputeTargetSnapshotHash(approvedTargets), StringComparison.Ordinal) ||
            currentTargets.Any(item => item is JsonObject target && target["exists"]?.GetValue<bool>() != true) ||
            string.IsNullOrWhiteSpace(approvedTargetHash) ||
            !string.Equals(approvedTargetHash, currentTargetHash, StringComparison.Ordinal))
        {
            return Invalid(
                FinanceApprovalContinuationReasonCodes.TargetStale,
                "target_changed_or_missing",
                "Finance target evidence changed after review. The approval is stale and cannot be edited into validity.");
        }

        var currentIntegrationHash = await FinanceApprovalContinuationBinding.BuildIntegrationStateHashAsync(
            _dbContext, approval.CompanyId, registration.FinanceRiskClassification, cancellationToken);
        var approvedIntegrationHash = FinanceApprovalContinuationBinding.ReadBindingString(binding, "integrationStateHash");
        evidence["approvedIntegrationStateHash"] = approvedIntegrationHash;
        evidence["currentIntegrationStateHash"] = currentIntegrationHash;
        if (string.IsNullOrWhiteSpace(approvedIntegrationHash) ||
            !string.Equals(approvedIntegrationHash, currentIntegrationHash, StringComparison.Ordinal))
        {
            return Invalid(
                FinanceApprovalContinuationReasonCodes.IntegrationStale,
                "integration_state_changed",
                "Finance integration state changed after review. Create and review a new request.");
        }

        var currentToolAuthority = currentAuthority.Find(attempt.ToolName, attempt.ActionType, attempt.Scope);
        if (currentToolAuthority is null || !currentToolAuthority.IsUsable)
        {
            return Invalid(
                FinanceApprovalContinuationReasonCodes.EligibilityFailed,
                "effective_tool_authority_not_usable",
                "The Finance action is no longer eligible under the effective agent authority.");
        }

        var runtimeProfile = await _serviceProvider.GetRequiredService<IAgentRuntimeProfileResolver>()
            .GetCurrentProfileAsync(approval.CompanyId, attempt.AgentId, cancellationToken);
        var usableTools = currentAuthority.Tools.Where(item => item.IsUsable).ToArray();
        var toolPermissions = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["allowed"] = new JsonArray(usableTools.Select(item => (JsonNode?)JsonValue.Create(item.ToolName)).ToArray()),
            ["actions"] = new JsonArray(usableTools.Select(item => item.ActionType)
                .Distinct(StringComparer.OrdinalIgnoreCase).Select(item => (JsonNode?)JsonValue.Create(item)).ToArray()),
            ["denied"] = new JsonArray(),
            ["deniedActions"] = new JsonArray()
        };
        var dataScopes = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in usableTools.Select(item => item.ActionType).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            dataScopes[action] = new JsonArray(usableTools
                .Where(item => string.Equals(item.ActionType, action, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Scope)
                .Where(scope => !string.IsNullOrWhiteSpace(scope))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(scope => (JsonNode?)JsonValue.Create(scope))
                .ToArray());
        }

        var riskContext = await FinanceApprovalContinuationBinding.BuildRiskContextAsync(
            _dbContext, attempt, cancellationToken);
        if (!riskContext.BackendVerified)
        {
            return Invalid(
                FinanceApprovalContinuationReasonCodes.EligibilityFailed,
                "finance_eligibility_unverified",
                "Current Finance eligibility evidence could not be verified.");
        }

        var policyDecision = _serviceProvider.GetRequiredService<IPolicyGuardrailEngine>().Evaluate(
            new PolicyEvaluationRequest(
                approval.CompanyId,
                attempt.AgentId,
                runtimeProfile.CompanyId,
                runtimeProfile.Status,
                runtimeProfile.AutonomyLevel,
                runtimeProfile.CanReceiveAssignments,
                toolPermissions,
                dataScopes,
                CloneNodes(runtimeProfile.ApprovalThresholds),
                CloneNodes(runtimeProfile.EscalationRules),
                attempt.ToolName,
                attempt.ActionType,
                attempt.Scope,
                CloneNodes(attempt.RequestPayload),
                TryReadString(approval.ThresholdContext, "thresholdCategory"),
                TryReadString(approval.ThresholdContext, "thresholdKey"),
                TryGetDecimal(approval.ThresholdContext, "thresholdValue"),
                SensitiveAction: true,
                ExecutionId: attempt.Id,
                CorrelationId: attempt.CorrelationId,
                TrustedToolApprovalRequired: false,
                TriggerLogic: CloneNodes(runtimeProfile.TriggerLogic),
                FinanceRiskContext: riskContext));

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
        if (!string.Equals(policyDecision.Outcome, PolicyDecisionOutcomeValues.RequireApproval, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(approvedRiskVersion) ||
            !string.Equals(approvedRiskVersion, currentRiskVersion, StringComparison.Ordinal) ||
            !string.Equals(approvedCompanyPolicyVersion, currentCompanyPolicyVersion, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(approvedThresholdHash) ||
            !string.Equals(approvedThresholdHash, currentThresholdHash, StringComparison.Ordinal))
        {
            return Invalid(
                FinanceApprovalContinuationReasonCodes.PolicyStale,
                "policy_or_threshold_evidence_changed",
                "Finance policy or threshold evidence changed after review. Create and review a new request.");
        }

        evidence["state"] = "valid";
        evidence["reasonCode"] = "finance_approval_continuation_valid";
        return new FinanceContinuationValidation(
            true,
            "finance_approval_continuation_valid",
            "The Finance approval remains bound to the current action and evidence.",
            evidence);
    }

    private Task WriteFinanceAuthorizationAuditAsync(
        FinanceAgentAuthorizationDecisionDto decision,
        string? correlationId,
        CancellationToken cancellationToken) =>
        _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                decision.CompanyId,
                string.Equals(decision.ActorType, FinanceAgentActorTypes.Human, StringComparison.Ordinal)
                    ? AuditActorTypes.User
                    : AuditActorTypes.System,
                decision.ActorId,
                AuditEventActions.FinanceAgentToolAuthorizationEvaluated,
                AuditTargetTypes.AgentToolExecution,
                decision.ExecutionId.ToString("N"),
                decision.IsAllowed ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Denied,
                DataSources: ["approval_continuation", "finance_actor_authorization", "company_membership"],
                CorrelationId: correlationId,
                RationaleSummary: decision.Explanation,
                Metadata: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["authorizationOutcome"] = decision.Outcome,
                    ["authorizationReasonCode"] = decision.ReasonCode,
                    ["authorizationPolicyVersion"] = decision.PolicyVersion,
                    ["authorizationEvidence"] = string.Join(",", decision.Evidence.Select(static item =>
                        $"{item.Type}:{item.Reference}:{item.Result}")),
                    ["actorType"] = decision.ActorType,
                    ["membershipState"] = decision.MembershipState,
                    ["toolName"] = decision.ToolName,
                    ["actionType"] = decision.ActionType,
                    ["approvedContinuation"] = "true",
                    ["delegationAuthorityId"] = decision.DelegationAuthorityId?.ToString("N")
                }),
            cancellationToken);

    private sealed record FinanceContinuationValidation(
        bool IsValid,
        string ReasonCode,
        string Explanation,
        JsonObject Evidence);
}
