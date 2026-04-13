using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Context;
using VirtualCompany.Application.Documents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Documents;

namespace VirtualCompany.Infrastructure.Auditing;

public sealed class CompanyAuditQueryService : IAuditQueryService
{
    private const int DefaultTake = 50;
    private const int MaxTake = 200;
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyContextAccessor _companyContextAccessor;
    private readonly IKnowledgeAccessPolicyEvaluator _knowledgeAccessPolicyEvaluator;

    public CompanyAuditQueryService(
        VirtualCompanyDbContext dbContext,
        ICompanyContextAccessor companyContextAccessor)
        : this(dbContext, companyContextAccessor, new KnowledgeAccessPolicyEvaluator())
    {
    }

    public CompanyAuditQueryService(
        VirtualCompanyDbContext dbContext,
        ICompanyContextAccessor companyContextAccessor,
        IKnowledgeAccessPolicyEvaluator knowledgeAccessPolicyEvaluator)
    {
        _dbContext = dbContext;
        _companyContextAccessor = companyContextAccessor;
        _knowledgeAccessPolicyEvaluator = knowledgeAccessPolicyEvaluator;
    }

    public async Task<AuditHistoryResult> ListAsync(
        Guid companyId,
        AuditHistoryFilter filter,
        CancellationToken cancellationToken)
    {
        EnsureAuditReviewAccess(companyId);

        filter ??= new AuditHistoryFilter();
        if (filter.FromUtc is DateTime requestedFromUtc && filter.ToUtc is DateTime requestedToUtc && requestedFromUtc > requestedToUtc)
        {
            throw new ArgumentException("Audit history from date must be on or before the to date.", nameof(filter));
        }

        var skip = Math.Max(filter.Skip ?? 0, 0);
        var take = Math.Clamp(filter.Take ?? DefaultTake, 1, MaxTake);

        var query = _dbContext.AuditEvents
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId);

        if (filter.FromUtc is { } fromUtc)
        {
            query = query.Where(x => x.OccurredUtc >= fromUtc);
        }

        if (filter.ToUtc is { } toUtc)
        {
            query = query.Where(x => x.OccurredUtc <= toUtc);
        }

        if (filter.AgentId is Guid agentId)
        {
            var agentTargetId = agentId.ToString();
            query = query.Where(x =>
                x.RelatedAgentId == agentId ||
                (x.ActorId == agentId && x.ActorType == AuditActorTypes.Agent) ||
                (x.TargetType == AuditTargetTypes.Agent && x.TargetId == agentTargetId));
        }

        if (filter.TaskId is Guid taskId)
        {
            var taskTargetId = taskId.ToString();
            query = query.Where(x =>
                x.RelatedTaskId == taskId ||
                (x.TargetType == AuditTargetTypes.WorkTask && x.TargetId == taskTargetId));
        }

        if (filter.WorkflowInstanceId is Guid workflowInstanceId)
        {
            var workflowTargetId = workflowInstanceId.ToString();
            query = query.Where(x =>
                x.RelatedWorkflowInstanceId == workflowInstanceId ||
                (x.TargetType == AuditTargetTypes.WorkflowInstance && x.TargetId == workflowTargetId));
        }

        var candidateEvents = await query
            .OrderByDescending(x => x.OccurredUtc)
            .ToListAsync(cancellationToken);

        var filteredEvents = candidateEvents
            .Where(x => MatchesAgentFilter(x, filter.AgentId))
            .Where(x => MatchesTaskFilter(x, filter.TaskId))
            .Where(x => MatchesWorkflowFilter(x, filter.WorkflowInstanceId))
            .ToList();

        var page = filteredEvents.Skip(skip).Take(take).ToList();
        var related = await LoadRelatedReferencesAsync(companyId, page, cancellationToken);

        return new AuditHistoryResult(
            page.Select(x => MapListItem(x, related)).ToList(),
            filteredEvents.Count,
            skip,
            take);
    }

    public async Task<AuditDetailDto> GetAsync(
        Guid companyId,
        Guid auditEventId,
        CancellationToken cancellationToken)
    {
        EnsureAuditReviewAccess(companyId);

        var auditEvent = await _dbContext.AuditEvents
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == auditEventId, cancellationToken)
            ?? throw new KeyNotFoundException("Audit event was not found.");

        var related = await LoadRelatedReferencesAsync(companyId, [auditEvent], cancellationToken);
        var linkedApprovals = await LoadLinkedApprovalsAsync(companyId, auditEvent, cancellationToken);
        var linkedToolExecutions = await LoadLinkedToolExecutionsAsync(companyId, auditEvent, cancellationToken);
        var sourceReferences = BuildSourceReferences(auditEvent, related);

        return new AuditDetailDto(
            auditEvent.Id,
            auditEvent.CompanyId,
            auditEvent.ActorType,
            auditEvent.ActorId,
            ResolveActorLabel(auditEvent, related),
            auditEvent.Action,
            auditEvent.TargetType,
            auditEvent.TargetId,
            ResolveTargetLabel(auditEvent, related),
            auditEvent.Outcome,
            auditEvent.RationaleSummary,
            auditEvent.DataSources.Select(SafeAuditExplanationMapper.NormalizeSummary).ToList(),
            BuildSafeExplanation(auditEvent, sourceReferences),
            sourceReferences,
            auditEvent.OccurredUtc,
            auditEvent.CorrelationId,
            SafeAuditExplanationMapper.SanitizeMetadata(auditEvent.Metadata),
            linkedApprovals,
            linkedToolExecutions,
            BuildAffectedEntities(auditEvent, related, linkedApprovals, linkedToolExecutions));
    }

    private void EnsureTenant(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Company context is required.");
        }

        if (!_companyContextAccessor.IsResolved ||
            _companyContextAccessor.CompanyId is not Guid currentCompanyId ||
            currentCompanyId != companyId)
        {
            throw new UnauthorizedAccessException("Audit history is scoped to the active company context.");
        }
    }

    private void EnsureAuditReviewAccess(Guid companyId)
    {
        EnsureTenant(companyId);

        var membership = _companyContextAccessor.Membership;
        if (membership is null ||
            membership.CompanyId != companyId ||
            membership.Status != CompanyMembershipStatus.Active ||
            !CanReviewAudit(membership.MembershipRole))
        {
            throw new UnauthorizedAccessException("Audit history requires company audit review access.");
        }
    }

    private static bool CanReviewAudit(CompanyMembershipRole role) =>
        role is CompanyMembershipRole.Owner or CompanyMembershipRole.Admin or CompanyMembershipRole.Manager;

    private async Task<RelatedReferenceSet> LoadRelatedReferencesAsync(
        Guid companyId,
        IReadOnlyCollection<AuditEvent> auditEvents,
        CancellationToken cancellationToken)
    {
        var agentIds = new HashSet<Guid>();
        var taskIds = new HashSet<Guid>();
        var workflowIds = new HashSet<Guid>();
        var documentIds = new HashSet<Guid>();
        var knowledgeChunkIds = new HashSet<Guid>();
        var memoryIds = new HashSet<Guid>();
        var approvalIds = new HashSet<Guid>();
        var toolExecutionIds = new HashSet<Guid>();
        var conversationIds = new HashSet<Guid>();
        var messageIds = new HashSet<Guid>();

        foreach (var auditEvent in auditEvents)
        {
            if (auditEvent.ActorId is Guid actorId && IsAgentActor(auditEvent.ActorType))
            {
                agentIds.Add(actorId);
            }

            if (auditEvent.RelatedAgentId is Guid relatedAgentId)
            {
                agentIds.Add(relatedAgentId);
            }

            if (auditEvent.RelatedTaskId is Guid relatedTaskId)
            {
                taskIds.Add(relatedTaskId);
            }

            if (auditEvent.RelatedWorkflowInstanceId is Guid relatedWorkflowInstanceId)
            {
                workflowIds.Add(relatedWorkflowInstanceId);
            }

            AddIfTargetGuid(auditEvent, AuditTargetTypes.Agent, agentIds);
            AddIfTargetGuid(auditEvent, AuditTargetTypes.WorkTask, taskIds);
            AddIfTargetGuid(auditEvent, AuditTargetTypes.WorkflowInstance, workflowIds);

            AddMetadataGuid(auditEvent, "agentId", agentIds);
            AddMetadataGuid(auditEvent, "taskId", taskIds);
            AddMetadataGuid(auditEvent, "workTaskId", taskIds);
            AddMetadataGuid(auditEvent, "workflowInstanceId", workflowIds);
            AddMetadataGuid(auditEvent, "documentId", documentIds);
            AddMetadataGuid(auditEvent, "knowledgeDocumentId", documentIds);
            AddMetadataGuid(auditEvent, "knowledgeChunkId", knowledgeChunkIds);
            AddMetadataGuid(auditEvent, "memoryId", memoryIds);
            AddMetadataGuid(auditEvent, "approvalRequestId", approvalIds);
            AddMetadataGuid(auditEvent, "toolExecutionId", toolExecutionIds);
            AddMetadataGuid(auditEvent, "toolExecutionAttemptId", toolExecutionIds);
            AddMetadataGuid(auditEvent, "conversationId", conversationIds);
            AddMetadataGuid(auditEvent, "messageId", messageIds);

            foreach (var source in auditEvent.DataSourcesUsed)
            {
                AddSourceGuid(source.SourceType, source.SourceId, agentIds, taskIds, workflowIds, documentIds, knowledgeChunkIds, memoryIds, approvalIds, toolExecutionIds, conversationIds, messageIds);
                AddSourceGuid(source.SourceType, source.Reference, agentIds, taskIds, workflowIds, documentIds, knowledgeChunkIds, memoryIds, approvalIds, toolExecutionIds, conversationIds, messageIds);

                if (source.Reference is not null && source.Reference.Contains("documentId=", StringComparison.OrdinalIgnoreCase))
                {
                    AddReferenceQueryStringGuid(source.Reference, "documentId", documentIds);
                }
            }
        }

        var agents = agentIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Agents
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && agentIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken);

        var tasks = taskIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.WorkTasks
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && taskIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Title, cancellationToken);

        var workflows = workflowIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.WorkflowInstances
                .AsNoTracking()
                .Include(x => x.Definition)
                .Where(x => x.CompanyId == companyId && workflowIds.Contains(x.Id))
                .ToDictionaryAsync(
                    x => x.Id,
                    x => $"{x.Definition.Name} ({x.State.ToStorageValue()})",
                    cancellationToken);

        var documentRows = documentIds.Count == 0
            ? []
            : await _dbContext.CompanyKnowledgeDocuments
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && documentIds.Contains(x.Id))
                .ToListAsync(cancellationToken);

        var documents = documentRows
            .Where(x => CanAccessKnowledgeDocument(companyId, x))
            .ToDictionary(x => x.Id, x => x.Title);

        var knowledgeChunkRows = knowledgeChunkIds.Count == 0
            ? []
            : await _dbContext.CompanyKnowledgeChunks
                .AsNoTracking()
                .Include(x => x.Document)
                .Where(x => x.CompanyId == companyId && knowledgeChunkIds.Contains(x.Id))
                .ToListAsync(cancellationToken);

        var knowledgeChunks = knowledgeChunkRows
            .Where(x => CanAccessKnowledgeDocument(companyId, x.Document))
            .ToDictionary(x => x.Id, x => $"{x.Document.Title} - chunk {x.ChunkIndex + 1}");

        var memories = memoryIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.MemoryItems
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && memoryIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => $"Memory: {x.MemoryType.ToStorageValue()}", cancellationToken);

        var approvals = approvalIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.ApprovalRequests
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && approvalIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => $"{x.ApprovalType} approval ({x.Status.ToStorageValue()})", cancellationToken);

        var toolExecutions = toolExecutionIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.ToolExecutionAttempts
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && toolExecutionIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => $"{x.ToolName} {x.ActionType.ToStorageValue()} ({x.Status.ToStorageValue()})", cancellationToken);

        var conversations = conversationIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Conversations
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && conversationIds.Contains(x.Id))
                .ToDictionaryAsync(
                    x => x.Id,
                    x => string.IsNullOrWhiteSpace(x.Subject) ? $"{x.ChannelType} conversation" : x.Subject,
                    cancellationToken);

        var messages = messageIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Messages
                .AsNoTracking()
                .Include(x => x.Conversation)
                .Where(x => x.CompanyId == companyId && messageIds.Contains(x.Id))
                .ToDictionaryAsync(
                    x => x.Id,
                    x => string.IsNullOrWhiteSpace(x.Conversation.Subject)
                        ? "Conversation message"
                        : $"Message in {x.Conversation.Subject}",
                    cancellationToken);

        return new RelatedReferenceSet(
            agents,
            tasks,
            workflows,
            documents,
            knowledgeChunks,
            memories,
            approvals,
            toolExecutions,
            conversations,
            messages);
    }

    private async Task<IReadOnlyList<AuditApprovalReferenceDto>> LoadLinkedApprovalsAsync(
        Guid companyId,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        var approvalIds = new HashSet<Guid>();
        var targetIds = new HashSet<Guid>();
        var toolExecutionIds = new HashSet<Guid>();

        if (auditEvent.RelatedApprovalRequestId is Guid relatedApprovalRequestId)
        {
            approvalIds.Add(relatedApprovalRequestId);
        }

        if (auditEvent.RelatedToolExecutionAttemptId is Guid relatedToolExecutionAttemptId)
        {
            toolExecutionIds.Add(relatedToolExecutionAttemptId);
        }

        if (Guid.TryParse(auditEvent.TargetId, out var targetGuid))
        {
            targetIds.Add(targetGuid);
            if (string.Equals(auditEvent.TargetType, AuditTargetTypes.ApprovalRequest, StringComparison.OrdinalIgnoreCase))
            {
                approvalIds.Add(targetGuid);
            }

            if (string.Equals(auditEvent.TargetType, AuditTargetTypes.AgentToolExecution, StringComparison.OrdinalIgnoreCase))
            {
                toolExecutionIds.Add(targetGuid);
            }
        }

        AddMetadataGuid(auditEvent, "approvalRequestId", approvalIds);
        AddMetadataGuid(auditEvent, "toolExecutionId", toolExecutionIds);
        AddMetadataGuid(auditEvent, "toolExecutionAttemptId", toolExecutionIds);

        var query = _dbContext.ApprovalRequests
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId);

        var approvals = await query
            .Where(x =>
                approvalIds.Contains(x.Id) ||
                toolExecutionIds.Contains(x.ToolExecutionAttemptId ?? Guid.Empty) ||
                targetIds.Contains(x.TargetEntityId))
            .OrderByDescending(x => x.CreatedUtc)
            .Take(20)
            .ToListAsync(cancellationToken);

        return approvals
            .Select(x => new AuditApprovalReferenceDto(
                x.Id,
                x.ApprovalType,
                x.Status.ToStorageValue(),
                x.TargetEntityType,
                x.TargetEntityId,
                x.DecisionSummary,
                x.CreatedUtc,
                x.DecidedUtc))
            .ToList();
    }

    private async Task<IReadOnlyList<AuditToolExecutionReferenceDto>> LoadLinkedToolExecutionsAsync(
        Guid companyId,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        var toolExecutionIds = new HashSet<Guid>();
        var taskIds = new HashSet<Guid>();
        var workflowIds = new HashSet<Guid>();
        var approvalIds = new HashSet<Guid>();

        if (auditEvent.RelatedToolExecutionAttemptId is Guid relatedToolExecutionAttemptId)
        {
            toolExecutionIds.Add(relatedToolExecutionAttemptId);
        }

        if (auditEvent.RelatedTaskId is Guid relatedTaskId)
        {
            taskIds.Add(relatedTaskId);
        }

        if (auditEvent.RelatedWorkflowInstanceId is Guid relatedWorkflowInstanceId)
        {
            workflowIds.Add(relatedWorkflowInstanceId);
        }

        if (auditEvent.RelatedApprovalRequestId is Guid relatedApprovalRequestId)
        {
            approvalIds.Add(relatedApprovalRequestId);
        }

        if (Guid.TryParse(auditEvent.TargetId, out var targetGuid))
        {
            if (string.Equals(auditEvent.TargetType, AuditTargetTypes.AgentToolExecution, StringComparison.OrdinalIgnoreCase))
            {
                toolExecutionIds.Add(targetGuid);
            }

            if (string.Equals(auditEvent.TargetType, AuditTargetTypes.WorkTask, StringComparison.OrdinalIgnoreCase))
            {
                taskIds.Add(targetGuid);
            }

            if (string.Equals(auditEvent.TargetType, AuditTargetTypes.WorkflowInstance, StringComparison.OrdinalIgnoreCase))
            {
                workflowIds.Add(targetGuid);
            }

            if (string.Equals(auditEvent.TargetType, AuditTargetTypes.ApprovalRequest, StringComparison.OrdinalIgnoreCase))
            {
                approvalIds.Add(targetGuid);
            }
        }

        AddMetadataGuid(auditEvent, "toolExecutionId", toolExecutionIds);
        AddMetadataGuid(auditEvent, "toolExecutionAttemptId", toolExecutionIds);
        AddMetadataGuid(auditEvent, "taskId", taskIds);
        AddMetadataGuid(auditEvent, "workTaskId", taskIds);
        AddMetadataGuid(auditEvent, "workflowInstanceId", workflowIds);
        AddMetadataGuid(auditEvent, "approvalRequestId", approvalIds);

        var executions = await _dbContext.ToolExecutionAttempts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Where(x =>
                toolExecutionIds.Contains(x.Id) ||
                taskIds.Contains(x.TaskId ?? Guid.Empty) ||
                workflowIds.Contains(x.WorkflowInstanceId ?? Guid.Empty) ||
                approvalIds.Contains(x.ApprovalRequestId ?? Guid.Empty) ||
                (!string.IsNullOrWhiteSpace(auditEvent.CorrelationId) && x.CorrelationId == auditEvent.CorrelationId))
            .OrderByDescending(x => x.StartedUtc)
            .Take(20)
            .ToListAsync(cancellationToken);

        var agentIds = executions.Select(x => x.AgentId).Distinct().ToList();
        var agentNames = agentIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Agents
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && agentIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken);

        return executions
            .Select(x => new AuditToolExecutionReferenceDto(
                x.Id,
                x.AgentId,
                agentNames.GetValueOrDefault(x.AgentId),
                x.ToolName,
                x.ActionType.ToStorageValue(),
                x.Status.ToStorageValue(),
                x.TaskId,
                x.WorkflowInstanceId,
                x.ApprovalRequestId,
                x.StartedUtc,
                x.CompletedUtc))
            .ToList();
    }

    private CompanyKnowledgeAccessContext BuildKnowledgeAccessContext(Guid companyId)
    {
        var membership = _companyContextAccessor.Membership;
        return new CompanyKnowledgeAccessContext(
            companyId,
            membership?.MembershipId,
            membership?.UserId,
            membership?.MembershipRole.ToStorageValue());
    }

    private bool CanAccessKnowledgeDocument(Guid companyId, CompanyKnowledgeDocument document) =>
        _knowledgeAccessPolicyEvaluator.CanAccess(BuildKnowledgeAccessContext(companyId), document);

    private static AuditHistoryListItem MapListItem(AuditEvent auditEvent, RelatedReferenceSet related) =>
        new(
            auditEvent.Id,
            auditEvent.CompanyId,
            auditEvent.ActorType,
            auditEvent.ActorId,
            ResolveActorLabel(auditEvent, related),
            auditEvent.Action,
            auditEvent.TargetType,
            auditEvent.TargetId,
            ResolveTargetLabel(auditEvent, related),
            auditEvent.Outcome,
            auditEvent.RationaleSummary,
            auditEvent.OccurredUtc,
            BuildSafeExplanation(auditEvent),
            auditEvent.CorrelationId,
            BuildAffectedEntities(auditEvent, related));

    private static AuditSafeExplanationDto BuildSafeExplanation(
        AuditEvent auditEvent,
        IReadOnlyList<AuditSourceReferenceDto>? sourceReferences = null) =>
        SafeAuditExplanationMapper.Build(
            auditEvent.Action,
            auditEvent.Outcome,
            auditEvent.RationaleSummary,
            sourceReferences?.Select(source => source.Label)
                ?? auditEvent.DataSources.Concat(auditEvent.DataSourcesUsed.Select(source => ResolveSourceLabel(source, RelatedReferenceSet.Empty))));

    private static IReadOnlyList<AuditSourceReferenceDto> BuildSourceReferences(AuditEvent auditEvent, RelatedReferenceSet related)
    {
        var references = auditEvent.DataSources
            .Select(AuditSourceReferenceDisplayFormatter.FormatLegacyDataSource)
            .ToList();

        references.AddRange(auditEvent.DataSourcesUsed.Select(source =>
            AuditSourceReferenceDisplayFormatter.Format(
                source,
                ResolveSourceLabel(source, related),
                ResolveSourceContext(source, related))));

        foreach (var key in new[] { "sourceReference", "sourceRef", "documentTitle", "policyVersion" })
        {
            if (auditEvent.Metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                references.Add(AuditSourceReferenceDisplayFormatter.FormatMetadata(ToDisplayName(key), value));
            }
        }

        return references
            .GroupBy(x => $"{x.Type}:{x.Label}:{x.EntityId}:{x.SecondaryText}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();
    }

    private static string ResolveSourceLabel(AuditDataSourceUsed source, RelatedReferenceSet related) =>
        !string.IsNullOrWhiteSpace(source.DisplayName)
            ? source.DisplayName
            : ResolveSourceLabel(source.SourceType, source.SourceId, related)
                ?? ResolveSourceLabel(source.SourceType, source.Reference, related)
                ?? AuditSourceReferenceDisplayFormatter.BuildExplanationLabel(source);

    private static IReadOnlyList<AuditEntityReferenceDto> BuildAffectedEntities(
        AuditEvent auditEvent,
        RelatedReferenceSet related,
        IReadOnlyCollection<AuditApprovalReferenceDto>? linkedApprovals = null,
        IReadOnlyCollection<AuditToolExecutionReferenceDto>? linkedToolExecutions = null)
    {
        var entities = new List<AuditEntityReferenceDto>
        {
            new(auditEvent.TargetType, auditEvent.TargetId, ResolveTargetLabel(auditEvent, related))
        };

        AddKnownEntity(entities, related, AuditTargetTypes.Agent, auditEvent.RelatedAgentId);
        AddKnownEntity(entities, related, AuditTargetTypes.WorkTask, auditEvent.RelatedTaskId);
        AddKnownEntity(entities, related, AuditTargetTypes.WorkflowInstance, auditEvent.RelatedWorkflowInstanceId);
        AddKnownEntity(entities, related, AuditTargetTypes.ApprovalRequest, auditEvent.RelatedApprovalRequestId);
        AddKnownEntity(entities, related, AuditTargetTypes.AgentToolExecution, auditEvent.RelatedToolExecutionAttemptId);
        AddMetadataEntity(auditEvent, related, entities, "agentId", AuditTargetTypes.Agent);
        AddMetadataEntity(auditEvent, related, entities, "taskId", AuditTargetTypes.WorkTask);
        AddMetadataEntity(auditEvent, related, entities, "workTaskId", AuditTargetTypes.WorkTask);
        AddMetadataEntity(auditEvent, related, entities, "workflowInstanceId", AuditTargetTypes.WorkflowInstance);
        AddMetadataEntity(auditEvent, related, entities, "approvalRequestId", AuditTargetTypes.ApprovalRequest);
        AddMetadataEntity(auditEvent, related, entities, "toolExecutionId", AuditTargetTypes.AgentToolExecution);

        foreach (var approval in linkedApprovals ?? [])
        {
            entities.Add(new AuditEntityReferenceDto(
                AuditTargetTypes.ApprovalRequest,
                approval.Id.ToString(),
                null));
            entities.Add(new AuditEntityReferenceDto(
                approval.TargetEntityType,
                approval.TargetEntityId.ToString(),
                ResolveEntityLabel(approval.TargetEntityType, approval.TargetEntityId.ToString(), related)));
        }

        foreach (var execution in linkedToolExecutions ?? [])
        {
            entities.Add(new AuditEntityReferenceDto(AuditTargetTypes.AgentToolExecution, execution.Id.ToString(), null));
            entities.Add(new AuditEntityReferenceDto(AuditTargetTypes.Agent, execution.AgentId.ToString(), execution.AgentLabel));
            AddKnownEntity(entities, related, AuditTargetTypes.WorkTask, execution.TaskId);
            AddKnownEntity(entities, related, AuditTargetTypes.WorkflowInstance, execution.WorkflowInstanceId);
        }

        return entities
            .Where(x => !string.IsNullOrWhiteSpace(x.EntityId))
            .GroupBy(x => $"{x.EntityType}:{x.EntityId}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();
    }

    private static bool MatchesAgentFilter(AuditEvent auditEvent, Guid? agentId) =>
        !agentId.HasValue ||
        (auditEvent.ActorId == agentId.Value && IsAgentActor(auditEvent.ActorType)) ||
        auditEvent.RelatedAgentId == agentId.Value ||
        TargetEquals(auditEvent, AuditTargetTypes.Agent, agentId.Value) ||
        MetadataEquals(auditEvent, "agentId", agentId.Value);

    private static bool MatchesTaskFilter(AuditEvent auditEvent, Guid? taskId) =>
        !taskId.HasValue ||
        auditEvent.RelatedTaskId == taskId.Value ||
        TargetEquals(auditEvent, AuditTargetTypes.WorkTask, taskId.Value) ||
        MetadataEquals(auditEvent, "taskId", taskId.Value) ||
        MetadataEquals(auditEvent, "workTaskId", taskId.Value);

    private static bool MatchesWorkflowFilter(AuditEvent auditEvent, Guid? workflowInstanceId) =>
        !workflowInstanceId.HasValue ||
        auditEvent.RelatedWorkflowInstanceId == workflowInstanceId.Value ||
        TargetEquals(auditEvent, AuditTargetTypes.WorkflowInstance, workflowInstanceId.Value) ||
        MetadataEquals(auditEvent, "workflowInstanceId", workflowInstanceId.Value);

    private static bool IsAgentActor(string actorType) =>
        string.Equals(actorType, AuditActorTypes.Agent, StringComparison.OrdinalIgnoreCase);

    private static bool TargetEquals(AuditEvent auditEvent, string targetType, Guid targetId) =>
        string.Equals(auditEvent.TargetType, targetType, StringComparison.OrdinalIgnoreCase) &&
        Guid.TryParse(auditEvent.TargetId, out var parsed) &&
        parsed == targetId;

    private static bool MetadataEquals(AuditEvent auditEvent, string key, Guid expected) =>
        auditEvent.Metadata.TryGetValue(key, out var value) &&
        Guid.TryParse(value, out var parsed) &&
        parsed == expected;

    private static void AddIfTargetGuid(AuditEvent auditEvent, string targetType, ISet<Guid> ids)
    {
        if (TargetEquals(auditEvent, targetType, Guid.Empty))
        {
            return;
        }

        if (string.Equals(auditEvent.TargetType, targetType, StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParse(auditEvent.TargetId, out var id))
        {
            ids.Add(id);
        }
    }

    private static void AddMetadataGuid(AuditEvent auditEvent, string key, ISet<Guid> ids)
    {
        if (auditEvent.Metadata.TryGetValue(key, out var value) &&
            Guid.TryParse(value, out var id))
        {
            ids.Add(id);
        }
    }

    private static void AddSourceGuid(
        string sourceType,
        string? sourceId,
        ISet<Guid> agentIds,
        ISet<Guid> taskIds,
        ISet<Guid> workflowIds,
        ISet<Guid> documentIds,
        ISet<Guid> knowledgeChunkIds,
        ISet<Guid> memoryIds,
        ISet<Guid> approvalIds,
        ISet<Guid> toolExecutionIds,
        ISet<Guid> conversationIds,
        ISet<Guid> messageIds)
    {
        if (!Guid.TryParse(sourceId, out var id))
        {
            return;
        }

        switch (sourceType)
        {
            case AuditTargetTypes.Agent:
            case GroundedContextSourceTypes.AgentRecord:
                agentIds.Add(id);
                break;
            case AuditTargetTypes.WorkTask:
            case "task":
            case GroundedContextSourceTypes.RecentTask:
                taskIds.Add(id);
                break;
            case AuditTargetTypes.WorkflowInstance:
            case "workflow":
                workflowIds.Add(id);
                break;
            case AuditTargetTypes.CompanyDocument:
            case "document":
            case "knowledge_document":
                documentIds.Add(id);
                break;
            case GroundedContextSourceTypes.KnowledgeChunk:
            case "document_chunk":
                knowledgeChunkIds.Add(id);
                break;
            case AuditTargetTypes.MemoryItem:
            case "memory":
                memoryIds.Add(id);
                break;
            case AuditTargetTypes.ApprovalRequest:
                approvalIds.Add(id);
                break;
            case AuditTargetTypes.AgentToolExecution:
            case "tool_execution":
            case "tool_execution_attempt":
                toolExecutionIds.Add(id);
                break;
            case "conversation":
                conversationIds.Add(id);
                break;
            case "message":
                messageIds.Add(id);
                break;
        }
    }

    private static void AddReferenceQueryStringGuid(string reference, string key, ISet<Guid> ids)
    {
        var token = $"{key}=";
        var index = reference.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return;
        }

        var valueStart = index + token.Length;
        var valueEnd = reference.IndexOfAny(['&', '#', ' '], valueStart);
        var value = valueEnd < 0
            ? reference[valueStart..]
            : reference[valueStart..valueEnd];

        if (Guid.TryParse(value, out var id))
        {
            ids.Add(id);
        }
    }

    private static void AddMetadataEntity(
        AuditEvent auditEvent,
        RelatedReferenceSet related,
        List<AuditEntityReferenceDto> entities,
        string key,
        string entityType)
    {
        if (auditEvent.Metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            entities.Add(new AuditEntityReferenceDto(entityType, value, ResolveEntityLabel(entityType, value, related)));
        }
    }

    private static void AddKnownEntity(
        List<AuditEntityReferenceDto> entities,
        RelatedReferenceSet related,
        string entityType,
        Guid? entityId)
    {
        if (entityId is not Guid id)
        {
            return;
        }

        entities.Add(new AuditEntityReferenceDto(entityType, id.ToString(), ResolveEntityLabel(entityType, id.ToString(), related)));
    }

    private static string? ResolveActorLabel(AuditEvent auditEvent, RelatedReferenceSet related) =>
        auditEvent.ActorId is Guid actorId && IsAgentActor(auditEvent.ActorType)
            ? related.Agents.GetValueOrDefault(actorId)
            : null;

    private static string? ResolveTargetLabel(AuditEvent auditEvent, RelatedReferenceSet related) =>
        ResolveEntityLabel(auditEvent.TargetType, auditEvent.TargetId, related);

    private static string? ResolveSourceLabel(string sourceType, string? sourceId, RelatedReferenceSet related)
    {
        if (!Guid.TryParse(sourceId, out var id))
        {
            return null;
        }

        return sourceType switch
        {
            AuditTargetTypes.Agent or GroundedContextSourceTypes.AgentRecord => related.Agents.GetValueOrDefault(id),
            AuditTargetTypes.WorkTask or "task" or GroundedContextSourceTypes.RecentTask => related.Tasks.GetValueOrDefault(id),
            AuditTargetTypes.WorkflowInstance or "workflow" => related.Workflows.GetValueOrDefault(id),
            AuditTargetTypes.CompanyDocument or "document" or "knowledge_document" => related.Documents.GetValueOrDefault(id),
            GroundedContextSourceTypes.KnowledgeChunk or "document_chunk" => related.KnowledgeChunks.GetValueOrDefault(id),
            AuditTargetTypes.MemoryItem or "memory" or GroundedContextSourceTypes.MemoryItem => related.Memories.GetValueOrDefault(id),
            AuditTargetTypes.ApprovalRequest or GroundedContextSourceTypes.ApprovalRequest => related.Approvals.GetValueOrDefault(id),
            AuditTargetTypes.AgentToolExecution or "tool_execution" or "tool_execution_attempt" => related.ToolExecutions.GetValueOrDefault(id),
            "conversation" => related.Conversations.GetValueOrDefault(id),
            "message" => related.Messages.GetValueOrDefault(id),
            _ => null
        };
    }

    private static string? ResolveSourceContext(AuditDataSourceUsed source, RelatedReferenceSet related)
    {
        if (string.Equals(source.SourceType, GroundedContextSourceTypes.KnowledgeChunk, StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParse(source.SourceId, out var chunkId) &&
            related.KnowledgeChunks.TryGetValue(chunkId, out var chunkLabel))
        {
            return chunkLabel;
        }

        return string.IsNullOrWhiteSpace(source.Reference)
            ? null
            : source.Reference;
    }

    private static string? ResolveEntityLabel(string entityType, string entityId, RelatedReferenceSet related)
    {
        if (!Guid.TryParse(entityId, out var id))
        {
            return null;
        }

        if (string.Equals(entityType, AuditTargetTypes.Agent, StringComparison.OrdinalIgnoreCase))
        {
            return related.Agents.GetValueOrDefault(id);
        }

        if (string.Equals(entityType, AuditTargetTypes.WorkTask, StringComparison.OrdinalIgnoreCase))
        {
            return related.Tasks.GetValueOrDefault(id);
        }

        if (string.Equals(entityType, AuditTargetTypes.WorkflowInstance, StringComparison.OrdinalIgnoreCase))
        {
            return related.Workflows.GetValueOrDefault(id);
        }

        if (string.Equals(entityType, AuditTargetTypes.ApprovalRequest, StringComparison.OrdinalIgnoreCase))
        {
            return related.Approvals.GetValueOrDefault(id);
        }

        if (string.Equals(entityType, AuditTargetTypes.AgentToolExecution, StringComparison.OrdinalIgnoreCase))
        {
            return related.ToolExecutions.GetValueOrDefault(id);
        }

        if (string.Equals(entityType, AuditTargetTypes.CompanyDocument, StringComparison.OrdinalIgnoreCase))
        {
            return related.Documents.GetValueOrDefault(id);
        }

        if (string.Equals(entityType, "conversation", StringComparison.OrdinalIgnoreCase))
        {
            return related.Conversations.GetValueOrDefault(id);
        }

        if (string.Equals(entityType, "message", StringComparison.OrdinalIgnoreCase))
        {
            return related.Messages.GetValueOrDefault(id);
        }

        return null;
    }

    private static string ToDisplayName(string key) =>
        string.Concat(key.Select((character, index) =>
            index > 0 && char.IsUpper(character)
                ? $" {char.ToLowerInvariant(character)}"
                : character.ToString()));

    private sealed record RelatedReferenceSet(
        IReadOnlyDictionary<Guid, string> Agents,
        IReadOnlyDictionary<Guid, string> Tasks,
        IReadOnlyDictionary<Guid, string> Workflows,
        IReadOnlyDictionary<Guid, string> Documents,
        IReadOnlyDictionary<Guid, string> KnowledgeChunks,
        IReadOnlyDictionary<Guid, string> Memories,
        IReadOnlyDictionary<Guid, string> Approvals,
        IReadOnlyDictionary<Guid, string> ToolExecutions,
        IReadOnlyDictionary<Guid, string> Conversations,
        IReadOnlyDictionary<Guid, string> Messages)
    {
        public static RelatedReferenceSet Empty { get; } = new(
            new Dictionary<Guid, string>(),
            new Dictionary<Guid, string>(),
            new Dictionary<Guid, string>(),
            new Dictionary<Guid, string>(),
            new Dictionary<Guid, string>(),
            new Dictionary<Guid, string>(),
            new Dictionary<Guid, string>(),
            new Dictionary<Guid, string>(),
            new Dictionary<Guid, string>(),
            new Dictionary<Guid, string>());
    }
}
