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
    DateTime? OccurredUtc = null);

// Business audit history is an explicit application concern.
// Technical diagnostics belong on ILogger and must not be inferred from audit records.
public interface IAuditEventWriter
{
    Task WriteAsync(AuditEventWriteRequest auditEvent, CancellationToken cancellationToken);
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
    public const string WorkflowException = "workflow_exception";
}

public static class AuditEventOutcomes
{
    public const string Succeeded = "succeeded";
    public const string Denied = "denied";
    public const string Pending = "pending";
    public const string Failed = "failed";
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
    public const string AgentToolExecutionApprovalRequested = "agent.tool_execution.approval_requested";
    public const string WorkflowInstanceStarted = "workflow.instance.started";
    public const string WorkflowExceptionCreated = "workflow.exception.created";
    public const string WorkflowExceptionReviewed = "workflow.exception.reviewed";
}
