using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Entities;

public sealed class Conversation : ICompanyOwnedEntity
{
    private const int ChannelTypeMaxLength = 64;
    private const int SubjectMaxLength = 200;

    private Conversation()
    {
    }

    public Conversation(
        Guid id,
        Guid companyId,
        string channelType,
        string? subject,
        Guid createdByUserId,
        Guid? agentId,
        IDictionary<string, JsonNode?>? metadata = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException("CreatedByUserId is required.", nameof(createdByUserId));
        }

        if (agentId == Guid.Empty)
        {
            throw new ArgumentException("AgentId cannot be empty.", nameof(agentId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        ChannelType = NormalizeRequired(channelType, nameof(channelType), ChannelTypeMaxLength);
        Subject = NormalizeOptional(subject, nameof(subject), SubjectMaxLength);
        CreatedByUserId = createdByUserId;
        AgentId = agentId;
        Metadata = CloneNodes(metadata);
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string ChannelType { get; private set; } = null!;
    public string? Subject { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid? AgentId { get; private set; }
    public Dictionary<string, JsonNode?> Metadata { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public User CreatedByUser { get; private set; } = null!;
    public Agent? Agent { get; private set; }
    public ICollection<Message> Messages { get; } = new List<Message>();
    public ICollection<ConversationTaskLink> TaskLinks { get; } = new List<ConversationTaskLink>();

    public void Touch() => UpdatedUtc = DateTime.UtcNow;

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static Dictionary<string, JsonNode?> CloneNodes(IDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
}

public sealed class Message : ICompanyOwnedEntity
{
    private const int SenderTypeMaxLength = 64;
    private const int MessageTypeMaxLength = 64;
    private const int BodyMaxLength = 16000;

    private Message()
    {
    }

    public Message(
        Guid id,
        Guid companyId,
        Guid conversationId,
        string senderType,
        Guid? senderId,
        string messageType,
        string body,
        IDictionary<string, JsonNode?>? structuredPayload = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (conversationId == Guid.Empty)
        {
            throw new ArgumentException("ConversationId is required.", nameof(conversationId));
        }

        if (senderId == Guid.Empty)
        {
            throw new ArgumentException("SenderId cannot be empty.", nameof(senderId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        ConversationId = conversationId;
        SenderType = NormalizeRequired(senderType, nameof(senderType), SenderTypeMaxLength);
        SenderId = senderId;
        MessageType = NormalizeRequired(messageType, nameof(messageType), MessageTypeMaxLength);
        Body = NormalizeRequired(body, nameof(body), BodyMaxLength);
        StructuredPayload = CloneNodes(structuredPayload);
        CreatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ConversationId { get; private set; }
    public string SenderType { get; private set; } = null!;
    public Guid? SenderId { get; private set; }
    public string MessageType { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public Dictionary<string, JsonNode?> StructuredPayload { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public Conversation Conversation { get; private set; } = null!;
    public ICollection<ConversationTaskLink> TaskLinks { get; } = new List<ConversationTaskLink>();

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static Dictionary<string, JsonNode?> CloneNodes(IDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
}

public sealed class ConversationTaskLink : ICompanyOwnedEntity
{
    private const int LinkTypeMaxLength = 64;

    private ConversationTaskLink()
    {
    }

    public ConversationTaskLink(
        Guid id,
        Guid companyId,
        Guid conversationId,
        Guid? messageId,
        Guid taskId,
        string linkType,
        Guid createdByUserId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (conversationId == Guid.Empty)
        {
            throw new ArgumentException("ConversationId is required.", nameof(conversationId));
        }

        if (messageId == Guid.Empty)
        {
            throw new ArgumentException("MessageId cannot be empty.", nameof(messageId));
        }

        if (taskId == Guid.Empty)
        {
            throw new ArgumentException("TaskId is required.", nameof(taskId));
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException("CreatedByUserId is required.", nameof(createdByUserId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        ConversationId = conversationId;
        MessageId = messageId;
        TaskId = taskId;
        LinkType = NormalizeRequired(linkType, nameof(linkType), LinkTypeMaxLength);
        CreatedByUserId = createdByUserId;
        CreatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ConversationId { get; private set; }
    public Guid? MessageId { get; private set; }
    public Guid TaskId { get; private set; }
    public string LinkType { get; private set; } = null!;
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public Conversation Conversation { get; private set; } = null!;
    public Message? Message { get; private set; }
    public WorkTask Task { get; private set; } = null!;
    public User CreatedByUser { get; private set; } = null!;

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
}