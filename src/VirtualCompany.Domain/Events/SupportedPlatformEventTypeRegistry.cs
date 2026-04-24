namespace VirtualCompany.Domain.Events;

public interface ISupportedPlatformEventTypeRegistry
{
    IReadOnlyCollection<string> SupportedEventTypes { get; }

    bool IsSupported(string? eventType);

    string Normalize(string eventType);
}

public sealed class SupportedPlatformEventTypeRegistry : ISupportedPlatformEventTypeRegistry
{
    public const string TaskCreated = "task_created";
    public const string TaskUpdated = "task_updated";
    public const string DocumentUploaded = "document_uploaded";
    public const string TaskStatusChanged = "task_status_changed";
    public const string TaskAssigned = "task_assigned";
    public const string TaskCompleted = "task_completed";
    public const string TaskFailed = "task_failed";
    public const string WorkflowStarted = "workflow_started";
    public const string WorkflowStepCompleted = "workflow_step_completed";
    public const string WorkflowFailed = "workflow_failed";
    public const string ApprovalApproved = "approval_approved";
    public const string ApprovalRejected = "approval_rejected";
    public const string AgentHired = "agent_hired";
    public const string AgentUpdated = "agent_updated";
    public const string AgentPaused = "agent_paused";
    public const string AgentArchived = "agent_archived";
    public const string ToolExecutionAllowed = "tool_execution_allowed";
    public const string ToolExecutionDenied = "tool_execution_denied";
    public const string DocumentProcessed = "document_processed";
    public const string MemoryItemCreated = "memory_item_created";
    public const string ConversationMessageSent = "conversation_message_sent";
    public const string ApprovalRequested = "approval_requested";
    public const string ApprovalDecision = "approval_decision";
    public const string AgentGeneratedAlert = "agent_generated_alert";
    public const string WorkflowStateChanged = "workflow_state_changed";
    public const string ApprovalUpdated = "approval_updated";
    public const string AgentStatusUpdated = "agent_status_updated";
    public const string FinanceTransactionCreated = "finance.transaction.created";
    public const string FinanceInvoiceCreated = "finance.invoice.created";
    public const string FinanceBillCreated = "finance.bill.created";
    public const string FinancePaymentCreated = "finance.payment.created";
    public const string FinanceSimulationDayAdvanced = "finance.simulation.day_advanced";
    public const string FinanceThresholdBreached = "finance.threshold.breached";

    private static readonly string[] EventTypes =
    [
        TaskCreated,
        TaskUpdated,
        TaskAssigned,
        TaskStatusChanged,
        TaskCompleted,
        TaskFailed,
        DocumentUploaded,
        DocumentProcessed,
        WorkflowStarted,
        WorkflowStepCompleted,
        WorkflowFailed,
        WorkflowStateChanged,
        ApprovalApproved,
        ApprovalRejected,
        ApprovalRequested,
        ApprovalDecision,
        ApprovalUpdated,
        AgentHired,
        AgentUpdated,
        AgentPaused,
        AgentArchived,
        AgentGeneratedAlert,
        AgentStatusUpdated,
        ToolExecutionAllowed,
        ToolExecutionDenied,
        MemoryItemCreated,
        ConversationMessageSent
        ,
        FinanceTransactionCreated,
        FinanceInvoiceCreated,
        FinanceBillCreated,
        FinancePaymentCreated,
        FinanceSimulationDayAdvanced,
        FinanceThresholdBreached
    ];

    private static readonly HashSet<string> EventTypeSet =
        new(EventTypes, StringComparer.OrdinalIgnoreCase);

    public static SupportedPlatformEventTypeRegistry Instance { get; } = new();

    private SupportedPlatformEventTypeRegistry()
    {
    }

    public IReadOnlyCollection<string> SupportedEventTypes => EventTypes;

    public bool IsSupported(string? eventType) =>
        !string.IsNullOrWhiteSpace(eventType) && EventTypeSet.Contains(eventType.Trim());

    public string Normalize(string eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new ArgumentException("Event type is required.", nameof(eventType));
        }

        var trimmed = eventType.Trim();
        return EventTypes.FirstOrDefault(x => string.Equals(x, trimmed, StringComparison.OrdinalIgnoreCase)) ?? trimmed;
    }

    public static string BuildValidationMessage(string? attemptedValue = null) =>
        string.IsNullOrWhiteSpace(attemptedValue)
            ? $"Event type is required. Supported event types: {string.Join(", ", EventTypes)}."
            : $"Unsupported event type '{attemptedValue}'. Supported event types: {string.Join(", ", EventTypes)}.";
}