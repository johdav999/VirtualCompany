using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class ProactiveMessage : ICompanyOwnedEntity
{
    private const int RecipientMaxLength = 200;
    private const int SubjectMaxLength = 200;
    private const int BodyMaxLength = 16000;
    private const int PolicyDecisionReasonMaxLength = 200;

    private ProactiveMessage()
    {
    }

    public ProactiveMessage(
        Guid id,
        Guid companyId,
        ProactiveMessageChannel channel,
        Guid recipientUserId,
        string recipient,
        string subject,
        string body,
        ProactiveMessageSourceEntityType sourceEntityType,
        Guid sourceEntityId,
        Guid originatingAgentId,
        Guid? notificationId,
        DateTime sentUtc,
        IDictionary<string, JsonNode?>? policyDecision,
        string? policyDecisionReason)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (recipientUserId == Guid.Empty)
        {
            throw new ArgumentException("RecipientUserId is required.", nameof(recipientUserId));
        }

        if (sourceEntityId == Guid.Empty)
        {
            throw new ArgumentException("SourceEntityId is required.", nameof(sourceEntityId));
        }

        if (originatingAgentId == Guid.Empty)
        {
            throw new ArgumentException("OriginatingAgentId is required.", nameof(originatingAgentId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Channel = channel;
        RecipientUserId = recipientUserId;
        Recipient = NormalizeRequired(recipient, nameof(recipient), RecipientMaxLength);
        Subject = NormalizeRequired(subject, nameof(subject), SubjectMaxLength);
        Body = NormalizeRequired(body, nameof(body), BodyMaxLength);
        SourceEntityType = sourceEntityType;
        SourceEntityId = sourceEntityId;
        OriginatingAgentId = originatingAgentId;
        NotificationId = notificationId;
        Status = ProactiveMessageDeliveryStatus.Delivered;
        SentUtc = NormalizeUtc(sentUtc);
        PolicyDecision = CloneNodes(policyDecision);
        PolicyDecisionReason = NormalizeOptional(policyDecisionReason, nameof(policyDecisionReason), PolicyDecisionReasonMaxLength);
        CreatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public ProactiveMessageChannel Channel { get; private set; }
    public Guid RecipientUserId { get; private set; }
    public string Recipient { get; private set; } = null!;
    public string Subject { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public ProactiveMessageSourceEntityType SourceEntityType { get; private set; }
    public Guid SourceEntityId { get; private set; }
    public Guid OriginatingAgentId { get; private set; }
    public Guid? NotificationId { get; private set; }
    public ProactiveMessageDeliveryStatus Status { get; private set; }
    public DateTime SentUtc { get; private set; }
    public Dictionary<string, JsonNode?> PolicyDecision { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? PolicyDecisionReason { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public User RecipientUser { get; private set; } = null!;
    public Agent OriginatingAgent { get; private set; } = null!;
    public CompanyNotification? Notification { get; private set; }

    private static DateTime NormalizeUtc(DateTime value)
    {
        if (value == default)
        {
            throw new ArgumentException("SentUtc is required.", nameof(value));
        }

        return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
    }

    private static Dictionary<string, JsonNode?> CloneNodes(IDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
}
