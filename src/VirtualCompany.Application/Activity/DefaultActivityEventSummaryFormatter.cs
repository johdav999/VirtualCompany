using System.Text;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Events;

namespace VirtualCompany.Application.Activity;

public sealed class DefaultActivityEventSummaryFormatter : IActivityEventSummaryFormatter
{
    private static readonly IReadOnlyDictionary<string, EventTemplate> Templates =
        new Dictionary<string, EventTemplate>(StringComparer.OrdinalIgnoreCase)
        {
            [SupportedPlatformEventTypeRegistry.TaskCreated] = new("task_created", "created task", ["taskTitle", "taskName", "title", "name", "targetName"]),
            [SupportedPlatformEventTypeRegistry.TaskUpdated] = new("task_updated", "updated task", ["taskTitle", "taskName", "title", "name", "targetName"]),
            [SupportedPlatformEventTypeRegistry.TaskAssigned] = new("task_assigned", "assigned task", ["taskTitle", "taskName", "title", "name", "targetName"]),
            [SupportedPlatformEventTypeRegistry.TaskStatusChanged] = new("task_status_changed", "changed task status", ["taskTitle", "taskName", "title", "name", "targetName"]),
            [SupportedPlatformEventTypeRegistry.TaskCompleted] = new("task_completed", "completed task", ["taskTitle", "taskName", "title", "name", "targetName"]),
            [SupportedPlatformEventTypeRegistry.TaskFailed] = new("task_failed", "failed task", ["taskTitle", "taskName", "title", "name", "targetName"]),
            [SupportedPlatformEventTypeRegistry.WorkflowStarted] = new("workflow_started", "started workflow", ["workflowName", "workflowTitle", "name", "targetName"]),
            [SupportedPlatformEventTypeRegistry.WorkflowStepCompleted] = new("workflow_step_completed", "completed workflow step", ["stepName", "workflowStepName", "targetName", "workflowName"]),
            [SupportedPlatformEventTypeRegistry.WorkflowFailed] = new("workflow_failed", "failed workflow", ["workflowName", "workflowTitle", "name", "targetName"]),
            [SupportedPlatformEventTypeRegistry.WorkflowStateChanged] = new("workflow_state_changed", "changed workflow state", ["workflowName", "workflowTitle", "name", "targetName"]),
            [SupportedPlatformEventTypeRegistry.ApprovalRequested] = new("approval_requested", "requested approval", ["approvalTitle", "requestTitle", "targetName", "title"]),
            [SupportedPlatformEventTypeRegistry.ApprovalApproved] = new("approval_approved", "approved", ["approvalTitle", "requestTitle", "targetName", "title"]),
            [SupportedPlatformEventTypeRegistry.ApprovalRejected] = new("approval_rejected", "rejected", ["approvalTitle", "requestTitle", "targetName", "title"]),
            [SupportedPlatformEventTypeRegistry.ApprovalDecision] = new("approval_decision", "recorded approval decision", ["approvalTitle", "requestTitle", "targetName", "title"]),
            [SupportedPlatformEventTypeRegistry.ApprovalUpdated] = new("approval_updated", "updated approval", ["approvalTitle", "requestTitle", "targetName", "title"]),
            [SupportedPlatformEventTypeRegistry.AgentHired] = new("agent_hired", "hired agent", ["agentDisplayName", "agentName", "targetName", "name"]),
            [SupportedPlatformEventTypeRegistry.AgentUpdated] = new("agent_updated", "updated agent", ["agentDisplayName", "agentName", "targetName", "name"]),
            [SupportedPlatformEventTypeRegistry.AgentPaused] = new("agent_paused", "paused agent", ["agentDisplayName", "agentName", "targetName", "name"]),
            [SupportedPlatformEventTypeRegistry.AgentArchived] = new("agent_archived", "archived agent", ["agentDisplayName", "agentName", "targetName", "name"]),
            [SupportedPlatformEventTypeRegistry.AgentStatusUpdated] = new("agent_status_updated", "updated agent status", ["agentDisplayName", "agentName", "targetName", "name"]),
            [SupportedPlatformEventTypeRegistry.AgentGeneratedAlert] = new("agent_generated_alert", "raised alert", ["alertTitle", "targetName", "title"]),
            [SupportedPlatformEventTypeRegistry.ToolExecutionAllowed] = new("tool_execution_allowed", "allowed tool execution", ["toolName", "toolExecutionName", "targetName", "name"]),
            [SupportedPlatformEventTypeRegistry.ToolExecutionDenied] = new("tool_execution_denied", "denied tool execution", ["toolName", "toolExecutionName", "targetName", "name"]),
            [SupportedPlatformEventTypeRegistry.DocumentUploaded] = new("document_uploaded", "uploaded document", ["documentName", "fileName", "targetName", "name"]),
            [SupportedPlatformEventTypeRegistry.DocumentProcessed] = new("document_processed", "processed document", ["documentName", "fileName", "targetName", "name"]),
            [SupportedPlatformEventTypeRegistry.MemoryItemCreated] = new("memory_item_created", "created memory item", ["memoryTitle", "targetName", "title", "name"]),
            [SupportedPlatformEventTypeRegistry.ConversationMessageSent] = new("conversation_message_sent", "sent conversation message", ["conversationTitle", "channelName", "targetName", "title"])
        };

    public ActivitySummaryDto Format(
        string eventType,
        string status,
        string? persistedSummary,
        IReadOnlyDictionary<string, JsonNode?>? rawPayload)
    {
        var normalizedEventType = Normalize(eventType);
        var payload = rawPayload ?? EmptyPayload.Value;
        var template = Templates.TryGetValue(normalizedEventType, out var registered)
            ? registered
            : new EventTemplate("fallback", HumanizeAction(normalizedEventType), ["targetName", "name", "title", "entityName"]);

        var actor = ReadFirstText(payload, "actor", "actorName", "userName", "userDisplayName", "agentName", "agentDisplayName", "performedBy");
        var target = ReadFirstText(payload, template.TargetKeys)
            ?? ReadFirstText(payload, "target", "targetName", "entityName", "sourceName", "sourceId", "targetId", "entityId");
        var outcome = ReadFirstText(payload, "outcome", "result", "decision", "status") ?? Clean(status);
        var action = ReadFirstText(payload, "actionText", "action") ?? template.Action;

        var text = BuildSummaryText(actor, action, target);
        if (string.IsNullOrWhiteSpace(text))
        {
            text = Clean(persistedSummary) ?? BuildFallbackText(normalizedEventType);
        }

        return new ActivitySummaryDto(
            normalizedEventType,
            template.FormatterKey,
            actor,
            action,
            target,
            outcome,
            text);
    }

    public static IReadOnlyCollection<string> SupportedFormatterKeys => Templates.Keys.ToArray();

    private static string Normalize(string eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return "unknown_event";
        }

        return eventType.Trim();
    }

    private static string BuildSummaryText(string? actor, string action, string? target)
    {
        var parts = new[] { actor, Clean(action), target }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim());

        return string.Join(' ', parts);
    }

    private static string BuildFallbackText(string eventType) =>
        string.Join(' ', new[] { "Recorded", HumanizeEventType(eventType) }.Where(x => !string.IsNullOrWhiteSpace(x)));

    private static string HumanizeAction(string eventType) =>
        string.Join(' ', new[] { "recorded", HumanizeEventType(eventType) }.Where(x => !string.IsNullOrWhiteSpace(x)));

    private static string HumanizeEventType(string eventType)
    {
        var cleaned = Clean(eventType);
        if (cleaned is null)
        {
            return "activity";
        }

        return cleaned.Replace('_', ' ').Replace('-', ' ');
    }

    private static string? ReadFirstText(IReadOnlyDictionary<string, JsonNode?> payload, IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            var value = ReadText(payload, key);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static string? ReadFirstText(IReadOnlyDictionary<string, JsonNode?> payload, params string[] keys) =>
        ReadFirstText(payload, (IEnumerable<string>)keys);

    private static string? ReadFirstText(JsonObject payload, params string[] keys) =>
        ReadFirstText(payload.ToDictionary(static pair => pair.Key, static pair => pair.Value), keys);

    private static string? ReadText(IReadOnlyDictionary<string, JsonNode?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var node))
        {
            return null;
        }

        return ReadText(node);
    }

    private static string? ReadText(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value)
        {
            return Clean(ReadJsonValue(value));
        }

        if (node is JsonObject jsonObject)
        {
            return ReadFirstText(jsonObject, "displayName", "name", "title", "id");
        }

        return Clean(node.ToJsonString());
    }

    private static string? ReadJsonValue(JsonValue value)
    {
        try
        {
            return value.GetValue<string>();
        }
        catch (InvalidOperationException)
        {
            try
            {
                return value.GetValue<object?>()?.ToString();
            }
            catch (InvalidOperationException)
            {
                return value.ToJsonString().Trim('"');
            }
        }
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var builder = new StringBuilder(value.Trim().Length);
        var previousWasWhiteSpace = false;
        foreach (var ch in value.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!previousWasWhiteSpace)
                {
                    builder.Append(' ');
                    previousWasWhiteSpace = true;
                }

                continue;
            }

            builder.Append(ch);
            previousWasWhiteSpace = false;
        }

        var cleaned = builder.ToString().Trim();
        if (cleaned.Length == 0 ||
            string.Equals(cleaned, "null", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(cleaned, "undefined", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return cleaned;
    }

    private sealed record EventTemplate(string FormatterKey, string Action, IReadOnlyList<string> TargetKeys);

    private static class EmptyPayload
    {
        public static readonly IReadOnlyDictionary<string, JsonNode?> Value =
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
    }
}
