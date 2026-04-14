namespace VirtualCompany.Domain.Enums;

public enum ProactiveMessageChannel
{
    Inbox = 1,
    Notification = 2
}

public enum ProactiveMessageSourceEntityType
{
    ProactiveTask = 1,
    Alert = 2,
    Escalation = 3
}

public enum ProactiveMessageDeliveryStatus
{
    Delivered = 1,
    Blocked = 2
}

public enum ProactiveMessagePolicyDecisionOutcome
{
    Allowed = 1,
    Blocked = 2,
    RequiresApproval = 3
}

public static class ProactiveMessageChannelValues
{
    public static string ToStorageValue(this ProactiveMessageChannel channel) =>
        channel switch
        {
            ProactiveMessageChannel.Inbox => "inbox",
            ProactiveMessageChannel.Notification => "notification",
            _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unsupported proactive message channel.")
        };

    public static bool TryParse(string? value, out ProactiveMessageChannel channel)
    {
        channel = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return trimmed.Equals("inbox", StringComparison.OrdinalIgnoreCase) && (channel = ProactiveMessageChannel.Inbox) == ProactiveMessageChannel.Inbox ||
               trimmed.Equals("notification", StringComparison.OrdinalIgnoreCase) && (channel = ProactiveMessageChannel.Notification) == ProactiveMessageChannel.Notification ||
               Enum.TryParse(trimmed, ignoreCase: true, out channel) && Enum.IsDefined(channel);
    }

    public static ProactiveMessageChannel Parse(string value) =>
        TryParse(value, out var channel)
            ? channel
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported proactive message channel value.");
}

public static class ProactiveMessageSourceEntityTypeValues
{
    public static string ToStorageValue(this ProactiveMessageSourceEntityType sourceEntityType) =>
        sourceEntityType switch
        {
            ProactiveMessageSourceEntityType.ProactiveTask => "proactive_task",
            ProactiveMessageSourceEntityType.Alert => "alert",
            ProactiveMessageSourceEntityType.Escalation => "escalation",
            _ => throw new ArgumentOutOfRangeException(nameof(sourceEntityType), sourceEntityType, "Unsupported proactive message source entity type.")
        };

    public static bool TryParse(string? value, out ProactiveMessageSourceEntityType sourceEntityType)
    {
        sourceEntityType = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return trimmed.Equals("proactive_task", StringComparison.OrdinalIgnoreCase) && (sourceEntityType = ProactiveMessageSourceEntityType.ProactiveTask) == ProactiveMessageSourceEntityType.ProactiveTask ||
               trimmed.Equals("work_task", StringComparison.OrdinalIgnoreCase) && (sourceEntityType = ProactiveMessageSourceEntityType.ProactiveTask) == ProactiveMessageSourceEntityType.ProactiveTask ||
               trimmed.Equals("task", StringComparison.OrdinalIgnoreCase) && (sourceEntityType = ProactiveMessageSourceEntityType.ProactiveTask) == ProactiveMessageSourceEntityType.ProactiveTask ||
               trimmed.Equals("alert", StringComparison.OrdinalIgnoreCase) && (sourceEntityType = ProactiveMessageSourceEntityType.Alert) == ProactiveMessageSourceEntityType.Alert ||
               trimmed.Equals("escalation", StringComparison.OrdinalIgnoreCase) && (sourceEntityType = ProactiveMessageSourceEntityType.Escalation) == ProactiveMessageSourceEntityType.Escalation ||
               Enum.TryParse(trimmed, ignoreCase: true, out sourceEntityType) && Enum.IsDefined(sourceEntityType);
    }

    public static ProactiveMessageSourceEntityType Parse(string value) =>
        TryParse(value, out var sourceEntityType)
            ? sourceEntityType
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported proactive message source entity type value.");
}

public static class ProactiveMessageDeliveryStatusValues
{
    public static string ToStorageValue(this ProactiveMessageDeliveryStatus status) =>
        status switch
        {
            ProactiveMessageDeliveryStatus.Delivered => "delivered",
            ProactiveMessageDeliveryStatus.Blocked => "blocked",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported proactive message delivery status.")
        };
}

public static class ProactiveMessagePolicyDecisionOutcomeValues
{
    public static string ToStorageValue(this ProactiveMessagePolicyDecisionOutcome outcome) =>
        outcome switch
        {
            ProactiveMessagePolicyDecisionOutcome.Allowed => "allow",
            ProactiveMessagePolicyDecisionOutcome.Blocked => "deny",
            ProactiveMessagePolicyDecisionOutcome.RequiresApproval => "require_approval",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unsupported proactive message policy decision outcome.")
        };

    public static bool TryParse(string? value, out ProactiveMessagePolicyDecisionOutcome outcome)
    {
        outcome = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return trimmed.Equals("allow", StringComparison.OrdinalIgnoreCase) && (outcome = ProactiveMessagePolicyDecisionOutcome.Allowed) == ProactiveMessagePolicyDecisionOutcome.Allowed ||
               trimmed.Equals("allowed", StringComparison.OrdinalIgnoreCase) && (outcome = ProactiveMessagePolicyDecisionOutcome.Allowed) == ProactiveMessagePolicyDecisionOutcome.Allowed ||
               trimmed.Equals("deny", StringComparison.OrdinalIgnoreCase) && (outcome = ProactiveMessagePolicyDecisionOutcome.Blocked) == ProactiveMessagePolicyDecisionOutcome.Blocked ||
               trimmed.Equals("denied", StringComparison.OrdinalIgnoreCase) && (outcome = ProactiveMessagePolicyDecisionOutcome.Blocked) == ProactiveMessagePolicyDecisionOutcome.Blocked ||
               trimmed.Equals("blocked", StringComparison.OrdinalIgnoreCase) && (outcome = ProactiveMessagePolicyDecisionOutcome.Blocked) == ProactiveMessagePolicyDecisionOutcome.Blocked ||
               trimmed.Equals("require_approval", StringComparison.OrdinalIgnoreCase) && (outcome = ProactiveMessagePolicyDecisionOutcome.RequiresApproval) == ProactiveMessagePolicyDecisionOutcome.RequiresApproval ||
               Enum.TryParse(trimmed, ignoreCase: true, out outcome) && Enum.IsDefined(outcome);
    }

    public static ProactiveMessagePolicyDecisionOutcome Parse(string value) =>
        TryParse(value, out var outcome)
            ? outcome
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported proactive message policy decision outcome value.");
}
