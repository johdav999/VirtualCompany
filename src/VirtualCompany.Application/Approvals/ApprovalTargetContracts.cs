using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Application.Approvals;

/// <summary>
/// Target-specific approval work within the coordinator's unit of work.
/// Implementations must scope every lookup to the approval's company and must
/// not commit the coordinator's transaction. Provider execution stays behind
/// the owning module's existing approval and durable-delivery boundaries.
/// </summary>
public interface IApprovalTargetHandler
{
    Task<bool> ExistsAsync(Guid companyId, Guid targetEntityId, CancellationToken cancellationToken);

    Task ValidateCreationAsync(Guid companyId, CreateApprovalRequestCommand command, Guid actorUserId, CancellationToken cancellationToken) => Task.CompletedTask;

    Task BindCreatedAsync(ApprovalRequest approval, CancellationToken cancellationToken) => Task.CompletedTask;

    Task<object?> GetReviewMaterialAsync(ApprovalRequest approval, CancellationToken cancellationToken) => Task.FromResult<object?>(null);

    Task<ApprovalTargetReviewDetails> GetReviewDetailsAsync(ApprovalRequest approval, CancellationToken cancellationToken) =>
        Task.FromResult(new ApprovalTargetReviewDetails([], [], null));

    Task<bool> CanReadAsync(ApprovalRequest approval, CancellationToken cancellationToken) => Task.FromResult(true);

    Task<ApprovalTargetStateTransition?> ApplyDecisionAsync(ApprovalRequest approval, CancellationToken cancellationToken) =>
        Task.FromResult<ApprovalTargetStateTransition?>(null);

    Task AfterDecisionPersistedAsync(ApprovalRequest approval, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed record ApprovalTargetReviewDetails(
    IReadOnlyList<ApprovalComparisonDto> Comparison,
    IReadOnlyList<ApprovalEvidenceDto> Evidence,
    string? ExecutionStatus);

public sealed record ApprovalTargetStateTransition(
    string AuditTargetType, string TargetId, string PreviousState, string CurrentState, string DataSource)
{
    public static ApprovalTargetStateTransition ForTask(Guid id, string previousState, string currentState) =>
        new(AuditTargetTypes.WorkTask, id.ToString("N"), previousState, currentState, "tasks");
    public static ApprovalTargetStateTransition ForWorkflow(Guid id, string previousState, string currentState) =>
        new(AuditTargetTypes.WorkflowInstance, id.ToString("N"), previousState, currentState, "workflow_instances");
    public static ApprovalTargetStateTransition ForAction(Guid id, string previousState, string currentState) =>
        new(AuditTargetTypes.AgentToolExecution, id.ToString("N"), previousState, currentState, "agent_tool_executions");
    public static ApprovalTargetStateTransition ForFinanceIntegrationWrite(Guid id, string previousState, string currentState) =>
        new(AuditTargetTypes.IntegrationConnection, id.ToString("N"), previousState, currentState, "fortnox_write_commands");
    public static ApprovalTargetStateTransition ForSalesMeetingInvitation(Guid id, string previousState, string currentState) =>
        new("sales_meeting_invitation", id.ToString("N"), previousState, currentState, "sales_meeting_invitations");
    public static ApprovalTargetStateTransition ForSalesMeetingChangeRequest(Guid id, string previousState, string currentState) =>
        new("sales_meeting_change_request", id.ToString("N"), previousState, currentState, "sales_meeting_change_requests");
    public static ApprovalTargetStateTransition ForOperatingPlan(Guid id, string previousState, string currentState) =>
        new("operating_plan", id.ToString("N"), previousState, currentState, "operating_plans");
}
