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
    public const string WorkflowStateChanged = "workflow_state_changed";

    private static readonly string[] EventTypes =
    [
        TaskCreated,
        TaskUpdated,
        DocumentUploaded,
        WorkflowStateChanged
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