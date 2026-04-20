using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Events;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyApprovalRequestService : IApprovalRequestService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyMembershipContextResolver _companyMembershipContextResolver;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly IServiceProvider _serviceProvider;
    private readonly IExecutiveCockpitDashboardCache _dashboardCache;
    private readonly ICompanyOutboxEnqueuer _outboxEnqueuer;

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
        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        ValidateDecision(command);

        var approval = await _dbContext.ApprovalRequests
            .Include(x => x.Steps)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == command.ApprovalId, cancellationToken);

        if (approval is null)
        {
            throw new KeyNotFoundException("Approval request not found.");
        }

        if (approval.Status != ApprovalRequestStatus.Pending)
        {
            throw new ApprovalValidationException(new Dictionary<string, string[]>
            {
                [nameof(command.Decision)] = [$"Only pending approvals can be decided. Current status: {approval.Status.ToStorageValue()}."]
            });
        }

        var currentStep = approval.CurrentActionableStep ?? throw new ApprovalValidationException(new Dictionary<string, string[]>
        {
            [nameof(command.StepId)] = ["Approval request has no current actionable step."]
        });

        if (command.StepId.HasValue && command.StepId.Value != currentStep.Id)
        {
            throw new InvalidOperationException("Only the current approval step can be decided.");
        }

        if (!CanDecide(currentStep, membership))
        {
            throw new ApprovalDecisionForbiddenException("The current user is not an approver for the current step.");
        }

        var rejected = string.Equals(command.Decision, "reject", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command.Decision, "rejected", StringComparison.OrdinalIgnoreCase);
        var decidedStep = rejected
            ? approval.RejectCurrentStep(currentStep.Id, membership.UserId, command.Comment)
            : approval.ApproveCurrentStep(currentStep.Id, membership.UserId, command.Comment);

        var linkedEntityTransition = await UpdateLinkedEntityAfterDecisionAsync(approval, cancellationToken);
        await WriteDecisionAuditAsync(approval, decidedStep, membership.UserId, rejected, cancellationToken);

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

        EnqueueApprovalUpdatedEvent(approval, rejected ? "rejected" : "approved");
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
                task.UpdateStatus(WorkTaskStatus.InProgress);
                return LinkedEntityStateTransition.ForTask(task.Id, previousStatus, task.Status.ToStorageValue());
            }

            if (approval.Status is ApprovalRequestStatus.Rejected or ApprovalRequestStatus.Expired)
            {
                task.UpdateStatus(WorkTaskStatus.Blocked, rationaleSummary: approval.DecisionSummary);
                return LinkedEntityStateTransition.ForTask(task.Id, previousStatus, task.Status.ToStorageValue());
            }

            if (approval.Status == ApprovalRequestStatus.Cancelled)
            {
                task.UpdateStatus(WorkTaskStatus.Blocked, rationaleSummary: approval.DecisionSummary);
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
                        attempt.Id),
                    cancellationToken);
                if (string.Equals(result.Status, ToolExecutionStatus.Denied.ToStorageValue(), StringComparison.OrdinalIgnoreCase))
                {
                    attempt.MarkDenied(policyDecision, result.ToStructuredPayload());
                    return LinkedEntityStateTransition.ForAction(attempt.Id, previousStatus, attempt.Status.ToStorageValue());
                }

                attempt.MarkExecuted(policyDecision, result.ToStructuredPayload());
                return LinkedEntityStateTransition.ForAction(attempt.Id, previousStatus, attempt.Status.ToStorageValue());
            }
        }

        return null;
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

        if (!string.Equals(command.Decision, "approve", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(command.Decision, "approved", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(command.Decision, "reject", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(command.Decision, "rejected", StringComparison.OrdinalIgnoreCase))
        {
            throw new ApprovalValidationException(new Dictionary<string, string[]> { [nameof(command.Decision)] = ["Decision must be approve or reject."] });
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

        return approvals.ToDictionary(
            approval => approval.Id,
            approval =>
            {
                if (tasks.TryGetValue(approval.TargetEntityId, out var task))
                {
                    return new ApprovalSummaryContext(
                        task.RationaleSummary,
                        $"Task: {task.Title}",
                        [new ApprovalAffectedEntityDto(ApprovalTargetEntityType.Task.ToStorageValue(), task.Id, task.Title)]);
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

                return new ApprovalSummaryContext(
                    null,
                    $"{ToDisplayName(approval.TargetEntityType)}: {approval.TargetEntityId:N}",
                    [new ApprovalAffectedEntityDto(approval.TargetEntityType, approval.TargetEntityId, ToDisplayName(approval.TargetEntityType))]);
            });
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

    private static string FirstNonEmpty(params string?[] values) =>
        values.First(value => !string.IsNullOrWhiteSpace(value))!.Trim();

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength].TrimEnd() + "...";

    private static string ToDisplayName(string entityType) =>
        entityType switch
        {
            var value when string.Equals(value, ApprovalTargetEntityType.Task.ToStorageValue(), StringComparison.OrdinalIgnoreCase) => "Task",
            var value when string.Equals(value, ApprovalTargetEntityType.Workflow.ToStorageValue(), StringComparison.OrdinalIgnoreCase) => "Workflow",
            var value when string.Equals(value, ApprovalTargetEntityType.Action.ToStorageValue(), StringComparison.OrdinalIgnoreCase) => "Action",
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
    }
}
