using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class ProactiveMessagePolicyDecision : ICompanyOwnedEntity
{
    private const int RecipientMaxLength = 200;
    private const int ReasonCodeMaxLength = 200;
    private const int SubjectMaxLength = 200;
    private const int BodyMaxLength = 16000;
    private const int ReasonSummaryMaxLength = 2000;
    private const int EvaluatedAutonomyLevelMaxLength = 64;

    private ProactiveMessagePolicyDecision()
    {
    }

    public ProactiveMessagePolicyDecision(
        Guid id,
        Guid companyId,
        Guid? proactiveMessageId,
        ProactiveMessageChannel channel,
        Guid recipientUserId,
        string recipient,
        string subject,
        string body,
        ProactiveMessageSourceEntityType sourceEntityType,
        Guid sourceEntityId,
        Guid originatingAgentId,
        ProactiveMessagePolicyDecisionOutcome outcome,
        string? reasonCode,
        string? reasonSummary,
        string evaluatedAutonomyLevel,
        IDictionary<string, JsonNode?>? policyDecision,
        DateTime createdUtc)
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
        ProactiveMessageId = proactiveMessageId;
        Channel = channel;
        RecipientUserId = recipientUserId;
        Recipient = NormalizeRequired(recipient, nameof(recipient), RecipientMaxLength);
        Subject = NormalizeRequired(subject, nameof(subject), SubjectMaxLength);
        Body = NormalizeRequired(body, nameof(body), BodyMaxLength);
        SourceEntityType = sourceEntityType;
        SourceEntityId = sourceEntityId;
        OriginatingAgentId = originatingAgentId;
        Outcome = outcome;
        ReasonCode = NormalizeOptional(reasonCode, nameof(reasonCode), ReasonCodeMaxLength);
        ReasonSummary = NormalizeOptional(reasonSummary, nameof(reasonSummary), ReasonSummaryMaxLength);
        EvaluatedAutonomyLevel = NormalizeRequired(evaluatedAutonomyLevel, nameof(evaluatedAutonomyLevel), EvaluatedAutonomyLevelMaxLength);
        PolicyDecision = CloneNodes(policyDecision);
        CreatedUtc = NormalizeUtc(createdUtc);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? ProactiveMessageId { get; private set; }
    public ProactiveMessageChannel Channel { get; private set; }
    public Guid RecipientUserId { get; private set; }
    public string Recipient { get; private set; } = null!;
    public string Subject { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public ProactiveMessageSourceEntityType SourceEntityType { get; private set; }
    public Guid SourceEntityId { get; private set; }
    public Guid OriginatingAgentId { get; private set; }
    public ProactiveMessagePolicyDecisionOutcome Outcome { get; private set; }
    public string? ReasonCode { get; private set; }
    public string? ReasonSummary { get; private set; }
    public string EvaluatedAutonomyLevel { get; private set; } = null!;
    public Dictionary<string, JsonNode?> PolicyDecision { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public ProactiveMessage? ProactiveMessage { get; private set; }
    public User RecipientUser { get; private set; } = null!;
    public Agent OriginatingAgent { get; private set; } = null!;

    private static DateTime NormalizeUtc(DateTime value)
    {
        if (value == default)
        {
            throw new ArgumentException("CreatedUtc is required.", nameof(value));
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
