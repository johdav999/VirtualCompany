using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Application.Auditing;

public sealed record AuditEventWriteRequest(
    Guid CompanyId,
    string ActorType,
    Guid? ActorId,
    string Action,
    string TargetType,
    string TargetId,
    string Outcome,
    string? RationaleSummary = null,
    IReadOnlyCollection<string>? DataSources = null,
    IReadOnlyDictionary<string, string?>? Metadata = null,
    string? CorrelationId = null,
    DateTime? OccurredUtc = null,
    IReadOnlyCollection<AuditDataSourceUsed>? DataSourcesUsed = null);

// Business audit history is an explicit application concern.
// Technical diagnostics belong on ILogger and must not be inferred from audit records.
public interface IAuditEventWriter
{
    Task WriteAsync(AuditEventWriteRequest auditEvent, CancellationToken cancellationToken);
}

public sealed record AuditHistoryFilter(
    Guid? AgentId = null,
    Guid? TaskId = null,
    Guid? WorkflowInstanceId = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    int? Skip = null,
    int? Take = null);

public sealed record AuditHistoryListItem(
    Guid Id,
    Guid CompanyId,
    string ActorType,
    Guid? ActorId,
    string? ActorLabel,
    string Action,
    string TargetType,
    string TargetId,
    string? TargetLabel,
    string Outcome,
    string? RationaleSummary,
    DateTime OccurredAt,
    AuditSafeExplanationDto Explanation,
    string? CorrelationId,
    IReadOnlyList<AuditEntityReferenceDto> AffectedEntities);

public sealed record AuditHistoryResult(
    IReadOnlyList<AuditHistoryListItem> Items,
    int TotalCount,
    int Skip,
    int Take);

public sealed record AuditDetailDto(
    Guid Id,
    Guid CompanyId,
    string ActorType,
    Guid? ActorId,
    string? ActorLabel,
    string Action,
    string TargetType,
    string TargetId,
    string? TargetLabel,
    string Outcome,
    string? RationaleSummary,
    IReadOnlyList<string> DataSources,
    AuditSafeExplanationDto Explanation,
    IReadOnlyList<AuditSourceReferenceDto> SourceReferences,
    DateTime OccurredAt,
    string? CorrelationId,
    IReadOnlyDictionary<string, string?> Metadata,
    IReadOnlyList<AuditApprovalReferenceDto> LinkedApprovals,
    IReadOnlyList<AuditToolExecutionReferenceDto> LinkedToolExecutions,
    IReadOnlyList<AuditEntityReferenceDto> AffectedEntities);

public sealed record AuditSafeExplanationDto(
    string Summary,
    string WhyThisAction,
    string Outcome,
    IReadOnlyList<string> DataSources);

public sealed record AuditSourceReferenceDto(
    // Label is the concise user-facing text used by audit and explainability views.
    string Label,
    string? Reference,
    string? Type = null,
    string? SourceType = null,
    string? DisplayName = null,
    string? SecondaryText = null,
    string? EntityType = null,
    string? EntityId = null,
    string? Snippet = null);

public sealed record AuditApprovalReferenceDto(
    Guid Id,
    string ApprovalType,
    string Status,
    string TargetEntityType,
    Guid TargetEntityId,
    string? DecisionSummary,
    DateTime CreatedAt,
    DateTime? DecidedAt);

public sealed record AuditToolExecutionReferenceDto(
    Guid Id,
    Guid AgentId,
    string? AgentLabel,
    string ToolName,
    string ActionType,
    string Status,
    Guid? TaskId,
    Guid? WorkflowInstanceId,
    Guid? ApprovalRequestId,
    DateTime StartedAt,
    DateTime? CompletedAt);

public sealed record AuditEntityReferenceDto(
    string EntityType,
    string EntityId,
    string? Label);

public interface IAuditQueryService
{
    Task<AuditHistoryResult> ListAsync(Guid companyId, AuditHistoryFilter filter, CancellationToken cancellationToken);
    Task<AuditDetailDto> GetAsync(Guid companyId, Guid auditEventId, CancellationToken cancellationToken);
}

public static class AuditActorTypes
{
    public const string User = "user";
    public const string System = "system";
    public const string Agent = "agent";
}

public static class AuditTargetTypes
{
    public const string CompanyInvitation = "company_invitation";
    public const string CompanyMembership = "company_membership";
    public const string Agent = "agent";
    public const string AgentToolExecution = "agent_tool_execution";
    public const string CompanyDocument = "company_document";
    public const string ApprovalRequest = "approval_request";
    public const string MemoryItem = "memory_item";
    public const string WorkflowInstance = "workflow_instance";
    public const string WorkTask = "work_task";
    public const string LinkedEntity = "linked_entity";
    public const string ConversationTaskLink = "conversation_task_link";
    public const string WorkflowException = "workflow_exception";
    public const string ExecutionException = "execution_exception";
    public const string CompanyNotification = "company_notification";
}

public static class AuditEventOutcomes
{
    public const string Succeeded = "succeeded";
    public const string Denied = "denied";
    public const string Pending = "pending";
    public const string Failed = "failed";
    public const string Requested = "requested";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}

public static class AuditEventActions
{
    public const string CompanyInvitationCreated = "company.invitation.created";
    public const string CompanyInvitationResent = "company.invitation.resent";
    public const string CompanyInvitationRevoked = "company.invitation.revoked";
    public const string CompanyInvitationAccepted = "company.invitation.accepted";
    public const string CompanyMembershipRoleChanged = "company.membership.role_changed";
    public const string AgentHired = "agent.hired";
    public const string AgentOperatingProfileUpdated = "agent.operating_profile.updated";
    public const string AgentStatusUpdated = "agent.status.updated";
    public const string AgentToolExecutionDenied = "agent.tool_execution.denied";
    public const string AgentToolExecutionExecuted = "agent.tool_execution.executed";
    public const string CompanyDocumentUploaded = "company.document.uploaded";
    public const string CompanyDocumentUploadFailed = "company.document.upload_failed";
    public const string CompanyDocumentProcessed = "company.document.processed";
    public const string CompanyDocumentFailed = "company.document.failed";
    public const string MemoryItemExpired = "memory.item.expired";
    public const string MemoryItemDeleted = "memory.item.deleted";
    public const string ApprovalCreated = "approval.created";
    public const string ApprovalStepApproved = "approval.step.approved";
    public const string ApprovalStepRejected = "approval.step.rejected";
    public const string ApprovalChainAdvanced = "approval.chain.advanced";
    public const string ApprovalCompleted = "approval.completed";
    public const string ApprovalRejected = "approval.rejected";
    public const string ApprovalLinkedEntityStateUpdated = "approval.linked_entity.state_updated";
    public const string AgentToolExecutionApprovalRequested = "agent.tool_execution.approval_requested";
    public const string WorkflowInstanceStarted = "workflow.instance.started";
    public const string WorkflowExceptionCreated = "workflow.exception.created";
    public const string WorkflowExceptionReviewed = "workflow.exception.reviewed";
    public const string ExecutionExceptionCreated = "execution.exception.created";
    public const string DirectChatTaskCreated = "direct_chat.task.created";
    public const string DirectChatTaskLinked = "direct_chat.task.linked";
    public const string SingleAgentTaskOrchestrationExecuted = "single_agent_task.orchestration.executed";
    public const string MultiAgentCollaborationStarted = "multi_agent.collaboration.started";
    public const string MultiAgentCollaborationPlanCreated = "multi_agent.collaboration.plan_created";
    public const string MultiAgentWorkerSubtaskCreated = "multi_agent.worker_subtask.created";
    public const string MultiAgentCollaborationGuardrailDenied = "multi_agent.collaboration.guardrail_denied";
    public const string MultiAgentWorkerCompleted = "multi_agent.worker.completed";
    public const string MultiAgentWorkerFailed = "multi_agent.worker.failed";
    public const string MultiAgentCollaborationConsolidated = "multi_agent.collaboration.consolidated";
    public const string CompanyNotificationActioned = "company.notification.actioned";
}
