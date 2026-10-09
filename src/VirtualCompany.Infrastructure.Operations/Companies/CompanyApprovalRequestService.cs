using System.Collections.Concurrent;
using static VirtualCompany.Infrastructure.Companies.ApprovalPayloadValues;
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

public sealed partial class CompanyApprovalRequestService : IApprovalRequestService, IApprovalAutomationService
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
        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        Validate(command);

        var targetType = ApprovalTargetEntityTypeValues.Parse(command.TargetEntityType);
        await EnsureTargetExistsAsync(companyId, targetType, command.TargetEntityId, cancellationToken);

        var targetHandler = _serviceProvider.GetRequiredKeyedService<IApprovalTargetHandler>(targetType);
        await targetHandler.ValidateCreationAsync(companyId, command, membership.UserId, cancellationToken);

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

        if (!await CanReadReviewAsync(approval, membership, cancellationToken))
            throw new UnauthorizedAccessException("This proposal is outside the current user's review scope.");
        _dbContext.ApprovalRequests.Add(approval);
        await targetHandler.BindCreatedAsync(approval, cancellationToken);

        await BindReviewAsync(approval, cancellationToken);
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
            return await _dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                _dbContext.ChangeTracker.Clear();
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
                try
                {
                    var result = await DecideCoreAsync(companyId, command, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return result;
                }
                catch (ApprovalProposalChangedException) { await transaction.CommitAsync(cancellationToken); throw; }
                catch (ApprovalValidationException) { await transaction.CommitAsync(cancellationToken); throw; }
                catch (DbUpdateConcurrencyException)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw new InvalidOperationException("Another reviewer decided this proposal. Refresh its current status.");
                }
            });
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
        if (!await CanReadReviewAsync(approval, membership, cancellationToken))
            throw new ApprovalDecisionForbiddenException("This proposal is outside the current user's review scope.");
        if (command.ClientRequestId.HasValue && command.ClientRequestId.Value != Guid.Empty &&
            TryGetGuid(approval.DecisionChain, "lastDecisionClientRequestId") == command.ClientRequestId.Value)
        {
            if (TryGetGuid(approval.DecisionChain, "lastDecisionActorId") != membership.UserId ||
                approval.DecisionChain.GetValueOrDefault("lastDecision")?.ToString() != normalizedDecision ||
                approval.DecisionChain.GetValueOrDefault("lastDecisionComment")?.ToString() != (command.Comment?.Trim() ?? "") ||
                command.StepId.HasValue && TryGetGuid(approval.DecisionChain, "lastDecisionStepId") != command.StepId)
                throw new InvalidOperationException("This request identifier was already used for a different decision.");
            var step = approval.Steps.Single(x => x.Id == TryGetGuid(approval.DecisionChain, "lastDecisionStepId"));
            return new(await ToDtoAsync(approval, cancellationToken), ToStepDto(step),
                approval.CurrentActionableStep is { } next ? ToStepDto(next) : null, approval.IsTerminal);
        }
        if (approval.Status != ApprovalRequestStatus.Pending)
        {
            throw new ApprovalValidationException(new Dictionary<string, string[]>
            {
                [nameof(command.Decision)] = [$"Only pending approvals can be decided. Current status: {approval.Status.ToStorageValue()}."]
            });
        }

        if (ReviewExpiry(approval) <= DateTime.UtcNow)
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

        if (normalizedDecision is "request_changes" or "changes_requested" && string.IsNullOrWhiteSpace(command.Comment))
            throw new ApprovalValidationException(new Dictionary<string, string[]> { [nameof(command.Comment)] = ["Explain the requested changes."] });
        await EnsureReviewedVersionAsync(approval, command, membership.UserId, cancellationToken);
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
                    decidedStep = approval.RequestChangesCurrentStep(currentStep.Id, membership.UserId, decisionComment!);
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
        var reviewChain = CloneNodes(approval.DecisionChain);
        reviewChain["lastReviewedMaterialHash"] = await MaterialHashAsync(approval, cancellationToken);
        approval.SetDecisionChain(reviewChain);
        if (command.ClientRequestId.HasValue && command.ClientRequestId.Value != Guid.Empty)
        {
            var decisionChain = CloneNodes(approval.DecisionChain);
            decisionChain["lastDecisionClientRequestId"] = command.ClientRequestId.Value;
            decisionChain["lastDecisionActorId"] = membership.UserId;
            decisionChain["lastDecision"] = normalizedDecision;
            decisionChain["lastDecisionStepId"] = currentStep.Id;
            decisionChain["lastDecisionComment"] = command.Comment?.Trim() ?? "";
            approval.SetDecisionChain(decisionChain);
        }

        // Claim the concurrency token before owner execution. The transaction keeps the claim,
        // linked transitions and outbox atomic; a racing process loses before it can execute.
        await _dbContext.SaveChangesAsync(cancellationToken);
        await WriteReviewAuditAsync(approval, membership.UserId, normalizedDecision, cancellationToken);
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
        if (TargetHandler(approval) is { } targetHandler)
            await targetHandler.AfterDecisionPersistedAsync(approval, cancellationToken);
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
        await EnsureReviewedVersionAsync(approval, new(approval.Id, "approve", currentStep.Id), grant.GrantorUserId, cancellationToken);
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
        var membership = await RequireMembershipAsync(companyId, cancellationToken);

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

        var visible = new List<ApprovalRequest>();
        foreach (var item in approvals)
            if (await CanReadReviewAsync(item, membership, cancellationToken)) visible.Add(item);
        approvals = visible;
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
        var membership = await RequireMembershipAsync(companyId, cancellationToken);

        var approval = await _dbContext.ApprovalRequests
            .AsNoTracking()
            .Include(x => x.Steps)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == approvalId, cancellationToken);

        if (approval is null)
        {
            throw new KeyNotFoundException("Approval request not found.");
        }

        if (!await CanReadReviewAsync(approval, membership, cancellationToken)) throw new KeyNotFoundException("Approval request not found.");
        return await ToDtoAsync(approval, cancellationToken);
    }

    private async Task<ApprovalTargetStateTransition?> UpdateLinkedEntityAfterDecisionAsync(ApprovalRequest approval, CancellationToken cancellationToken)
    {
        if (approval.Status == ApprovalRequestStatus.Pending) return null;
        var handler = TargetHandler(approval);
        return handler is null ? null : await handler.ApplyDecisionAsync(approval, cancellationToken);
    }

    private IApprovalTargetHandler? TargetHandler(ApprovalRequest approval) =>
        _serviceProvider.GetKeyedService<IApprovalTargetHandler>(ApprovalTargetEntityTypeValues.Parse(approval.TargetEntityType));

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
                $"/work?companyId={approval.CompanyId}&tab=approvals&itemId={approval.Id}",
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

    private async Task EnsureTargetExistsAsync(Guid companyId, ApprovalTargetEntityType targetType, Guid targetEntityId, CancellationToken cancellationToken)
    {
        var handler = _serviceProvider.GetKeyedService<IApprovalTargetHandler>(targetType);
        if (handler is null || !await handler.ExistsAsync(companyId, targetEntityId, cancellationToken))
            throw new KeyNotFoundException("Approval target not found.");
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
                approval.Status.ToStorageValue(),
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
        ApprovalTargetStateTransition transition,
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
        var dto = ToDto(approval, contexts.GetValueOrDefault(approval.Id));
        var reviewerIds = approval.Steps.Where(x => x.DecidedByUserId.HasValue).Select(x => x.DecidedByUserId!.Value).ToArray();
        var names = await _dbContext.Users.AsNoTracking().Where(x => reviewerIds.Contains(x.Id) &&
            _dbContext.CompanyMemberships.Any(m => m.CompanyId == approval.CompanyId && m.UserId == x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken);
        return dto with { Review = await ReviewAsync(approval, cancellationToken),
            Steps = dto.Steps.Select(x => x with { ReviewerName = x.DecidedByUserId is Guid user ? names.GetValueOrDefault(user) : null }).ToArray() };
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
        var marketingActionIds = approvals.Where(x => x.TargetEntityType == ApprovalTargetEntityType.MarketingChannelAction.ToStorageValue())
            .Select(x => x.TargetEntityId).Distinct().ToList();
        var marketingActions = marketingActionIds.Count == 0 ? new Dictionary<Guid, MarketingChannelAction>()
            : await _dbContext.MarketingChannelActions.AsNoTracking().Where(x => x.CompanyId == companyId && marketingActionIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);
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
                if (marketingActions.TryGetValue(approval.TargetEntityId, out var marketingAction))
                {
                    var label = $"{marketingAction.ActionType} to {marketingAction.DestinationReference}; content brief version {marketingAction.ContentBriefVersion?.ToString() ?? "not linked"}";
                    return new ApprovalSummaryContext("Review the exact content version and destination. Approval does not confirm provider delivery or increase the campaign budget.",
                        label, [new ApprovalAffectedEntityDto(ApprovalTargetEntityType.MarketingChannelAction.ToStorageValue(), marketingAction.Id, label)]);
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

    private static ApprovalRequestDto ToDto(ApprovalRequest approval, ApprovalSummaryContext? summaryContext)
    {
        var thresholdSummary = approval.ApprovalType == "manual_review" && TryReadString(approval.ThresholdContext, "thresholdKey", "approvalTarget") is null
            ? null : BuildThresholdSummary(approval.ThresholdContext);
        var rationaleSummary = Truncate(
            FirstNonEmpty(
                summaryContext?.RationaleSummary,
                TryReadString(approval.PolicyDecision, "explanation", "summary", "message"),
                TryReadString(approval.ThresholdContext, "rationaleSummary", "rationale", "explanation", "reason"),
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
            var value when string.Equals(value, ApprovalTargetEntityType.MarketingChannelAction.ToStorageValue(), StringComparison.OrdinalIgnoreCase) => "Marketing delivery",
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

    private static ApprovalStepDto ToStepDto(ApprovalStep step) =>
        new(step.Id, step.SequenceNo, step.ApproverType.ToStorageValue(), step.ApproverRef, step.Status.ToStorageValue(),
            step.DecidedByUserId, step.DecidedUtc, step.Comment);

    private sealed record ApprovalSummaryContext(
        string? RationaleSummary,
        string AffectedDataSummary,
        IReadOnlyList<ApprovalAffectedEntityDto> AffectedEntities);

    private async Task SynchronizeFinanceAutonomyApprovalAsync(
        ApprovalRequest approval, CancellationToken cancellationToken)
    {
        if (!approval.ThresholdContext.ContainsKey("financeAutonomy")) return;
        var coordinator = _serviceProvider.GetService<IFinanceAutonomyApprovalCoordinator>();
        if (coordinator is not null)
            await coordinator.ProcessApprovalAsync(approval.CompanyId, approval.Id, cancellationToken);
    }

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
}
