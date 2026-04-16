namespace VirtualCompany.Domain.Entities;

public sealed record ActivityEntityLinkReference(string EntityType, Guid EntityId)
{
    public ActivityEntityLinkReference Normalize() =>
        new(ActivityEntityTypes.Normalize(EntityType), EntityId);
}

public static class ActivityEntityTypes
{
    public const string Task = "task";
    public const string WorkflowInstance = "workflow_instance";
    public const string Approval = "approval";
    public const string ToolExecution = "tool_execution";

    public static string Normalize(string entityType)
    {
        if (string.IsNullOrWhiteSpace(entityType))
        {
            throw new ArgumentException("EntityType is required.", nameof(entityType));
        }

        return entityType.Trim().ToLowerInvariant() switch
        {
            "task" or "work_task" or "worktask" => Task,
            "workflow" or "workflow_instance" or "workflowinstance" => WorkflowInstance,
            "approval" or "approval_request" or "approvalrequest" => Approval,
            "tool" or "tool_execution" or "tool_execution_attempt" or "toolexecution" or "toolexecutionattempt" => ToolExecution,
            var normalized => normalized
        };
    }

    public static bool IsSupported(string entityType)
    {
        var normalized = Normalize(entityType);
        return normalized is Task or WorkflowInstance or Approval or ToolExecution;
    }

    public static string ToKey(string entityType, Guid entityId) =>
        $"{Normalize(entityType)}:{entityId:N}";

    public static string ToKey(ActivityEntityLinkReference reference) =>
        ToKey(reference.EntityType, reference.EntityId);
}
