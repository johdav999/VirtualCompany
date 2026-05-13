using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Entities;

public sealed class CustomerMemoryProfile : ICompanyOwnedEntity
{
    private const int SummaryMaxLength = 4000;

    private CustomerMemoryProfile()
    {
    }

    public CustomerMemoryProfile(
        Guid id,
        Guid companyId,
        Guid contactId,
        string? aiSummary = null,
        string? relationshipMemory = null,
        string? lastOutreachSummary = null,
        decimal? engagementScore = null,
        IDictionary<string, JsonNode?>? metadata = null,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null)
    {
        CustomerMemoryProfileText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        ContactId = CustomerMemoryProfileText.RequireId(contactId, nameof(contactId));
        AiSummary = CustomerMemoryProfileText.NormalizeOptional(aiSummary, nameof(aiSummary), SummaryMaxLength);
        RelationshipMemory = CustomerMemoryProfileText.NormalizeOptional(relationshipMemory, nameof(relationshipMemory), SummaryMaxLength);
        LastOutreachSummary = CustomerMemoryProfileText.NormalizeOptional(lastOutreachSummary, nameof(lastOutreachSummary), SummaryMaxLength);
        EngagementScore = NormalizeScore(engagementScore, nameof(engagementScore));
        Metadata = CustomerMemoryProfileText.CloneNodes(metadata);
        CreatedUtc = CustomerMemoryProfileText.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        UpdatedUtc = CustomerMemoryProfileText.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ContactId { get; private set; }
    public string? AiSummary { get; private set; }
    public string? RelationshipMemory { get; private set; }
    public string? LastOutreachSummary { get; private set; }
    public decimal? EngagementScore { get; private set; }
    public Dictionary<string, JsonNode?> Metadata { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public Contact Contact { get; private set; } = null!;
    public ICollection<CustomerMemoryProfileConversation> Conversations { get; } = new List<CustomerMemoryProfileConversation>();
    public ICollection<CustomerMemoryProfileDeal> Deals { get; } = new List<CustomerMemoryProfileDeal>();
    public ICollection<CustomerMemoryProfileEngagementAttribute> EngagementAttributes { get; } = new List<CustomerMemoryProfileEngagementAttribute>();
    public ICollection<CustomerMemoryProfilePreference> Preferences { get; } = new List<CustomerMemoryProfilePreference>();
    public ICollection<CustomerMemoryProfilePriceSignal> PriceSignals { get; } = new List<CustomerMemoryProfilePriceSignal>();
    public ICollection<CustomerMemoryProfileIndustrySignal> IndustrySignals { get; } = new List<CustomerMemoryProfileIndustrySignal>();

    public void Refresh(
        string? aiSummary,
        string? relationshipMemory,
        string? lastOutreachSummary,
        decimal? engagementScore,
        IDictionary<string, JsonNode?>? metadata,
        DateTime updatedUtc)
    {
        AiSummary = CustomerMemoryProfileText.NormalizeOptional(aiSummary, nameof(aiSummary), SummaryMaxLength);
        RelationshipMemory = CustomerMemoryProfileText.NormalizeOptional(relationshipMemory, nameof(relationshipMemory), SummaryMaxLength);
        LastOutreachSummary = CustomerMemoryProfileText.NormalizeOptional(lastOutreachSummary, nameof(lastOutreachSummary), SummaryMaxLength);
        EngagementScore = NormalizeScore(engagementScore, nameof(engagementScore));
        Metadata = CustomerMemoryProfileText.CloneNodes(metadata);
        UpdatedUtc = CustomerMemoryProfileText.NormalizeUtc(updatedUtc, nameof(updatedUtc));
    }

    private static decimal? NormalizeScore(decimal? value, string name)
    {
        if (!value.HasValue)
        {
            return null;
        }

        if (value.Value < 0m || value.Value > 100m)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be between 0 and 100.");
        }

        return decimal.Round(value.Value, 2, MidpointRounding.AwayFromZero);
    }
}

public sealed class CustomerMemoryProfileConversation : ICompanyOwnedEntity
{
    private CustomerMemoryProfileConversation()
    {
    }

    public CustomerMemoryProfileConversation(
        Guid id,
        Guid companyId,
        Guid customerMemoryProfileId,
        Guid conversationId,
        string? summary = null,
        DateTime? lastMessageUtc = null,
        decimal? relevance = null,
        IDictionary<string, JsonNode?>? metadata = null,
        DateTime? createdUtc = null)
    {
        CustomerMemoryProfileText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        CustomerMemoryProfileId = CustomerMemoryProfileText.RequireId(customerMemoryProfileId, nameof(customerMemoryProfileId));
        ConversationId = CustomerMemoryProfileText.RequireId(conversationId, nameof(conversationId));
        Summary = CustomerMemoryProfileText.NormalizeOptional(summary, nameof(summary), 2000);
        LastMessageUtc = lastMessageUtc.HasValue ? CustomerMemoryProfileText.NormalizeUtc(lastMessageUtc.Value, nameof(lastMessageUtc)) : null;
        Relevance = CustomerMemoryProfileText.NormalizeRatio(relevance, nameof(relevance));
        Metadata = CustomerMemoryProfileText.CloneNodes(metadata);
        CreatedUtc = CustomerMemoryProfileText.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CustomerMemoryProfileId { get; private set; }
    public Guid ConversationId { get; private set; }
    public string? Summary { get; private set; }
    public DateTime? LastMessageUtc { get; private set; }
    public decimal? Relevance { get; private set; }
    public Dictionary<string, JsonNode?> Metadata { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedUtc { get; private set; }
    public CustomerMemoryProfile CustomerMemoryProfile { get; private set; } = null!;
    public Conversation Conversation { get; private set; } = null!;
}

public sealed class CustomerMemoryProfileDeal : ICompanyOwnedEntity
{
    private CustomerMemoryProfileDeal()
    {
    }

    public CustomerMemoryProfileDeal(
        Guid id,
        Guid companyId,
        Guid customerMemoryProfileId,
        Guid dealId,
        string? dealRole = null,
        string? outcome = null,
        DateTime? closedUtc = null,
        string? summary = null,
        IDictionary<string, JsonNode?>? metadata = null,
        DateTime? createdUtc = null)
    {
        CustomerMemoryProfileText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        CustomerMemoryProfileId = CustomerMemoryProfileText.RequireId(customerMemoryProfileId, nameof(customerMemoryProfileId));
        DealId = CustomerMemoryProfileText.RequireId(dealId, nameof(dealId));
        DealRole = CustomerMemoryProfileText.NormalizeOptional(dealRole, nameof(dealRole), 80);
        Outcome = CustomerMemoryProfileText.NormalizeOptional(outcome, nameof(outcome), 80);
        ClosedUtc = closedUtc.HasValue ? CustomerMemoryProfileText.NormalizeUtc(closedUtc.Value, nameof(closedUtc)) : null;
        Summary = CustomerMemoryProfileText.NormalizeOptional(summary, nameof(summary), 2000);
        Metadata = CustomerMemoryProfileText.CloneNodes(metadata);
        CreatedUtc = CustomerMemoryProfileText.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CustomerMemoryProfileId { get; private set; }
    public Guid DealId { get; private set; }
    public string? DealRole { get; private set; }
    public string? Outcome { get; private set; }
    public DateTime? ClosedUtc { get; private set; }
    public string? Summary { get; private set; }
    public Dictionary<string, JsonNode?> Metadata { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedUtc { get; private set; }
    public CustomerMemoryProfile CustomerMemoryProfile { get; private set; } = null!;
    public Deal Deal { get; private set; } = null!;
}

public sealed class CustomerMemoryProfileEngagementAttribute : ICompanyOwnedEntity
{
    private CustomerMemoryProfileEngagementAttribute()
    {
    }

    public CustomerMemoryProfileEngagementAttribute(
        Guid id,
        Guid companyId,
        Guid customerMemoryProfileId,
        string attributeType,
        string attributeKey,
        string? attributeValue,
        decimal? scoreImpact,
        DateTime observedUtc,
        IDictionary<string, JsonNode?>? metadata = null,
        DateTime? createdUtc = null)
    {
        CustomerMemoryProfileText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        CustomerMemoryProfileId = CustomerMemoryProfileText.RequireId(customerMemoryProfileId, nameof(customerMemoryProfileId));
        AttributeType = CustomerMemoryProfileText.NormalizeRequired(attributeType, nameof(attributeType), 80).ToLowerInvariant();
        AttributeKey = CustomerMemoryProfileText.NormalizeRequired(attributeKey, nameof(attributeKey), 120).ToLowerInvariant();
        AttributeValue = CustomerMemoryProfileText.NormalizeOptional(attributeValue, nameof(attributeValue), 1000);
        ScoreImpact = NormalizeScoreImpact(scoreImpact, nameof(scoreImpact));
        ObservedUtc = CustomerMemoryProfileText.NormalizeUtc(observedUtc, nameof(observedUtc));
        Metadata = CustomerMemoryProfileText.CloneNodes(metadata);
        CreatedUtc = CustomerMemoryProfileText.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CustomerMemoryProfileId { get; private set; }
    public string AttributeType { get; private set; } = null!;
    public string AttributeKey { get; private set; } = null!;
    public string? AttributeValue { get; private set; }
    public decimal? ScoreImpact { get; private set; }
    public DateTime ObservedUtc { get; private set; }
    public Dictionary<string, JsonNode?> Metadata { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedUtc { get; private set; }
    public CustomerMemoryProfile CustomerMemoryProfile { get; private set; } = null!;

    private static decimal? NormalizeScoreImpact(decimal? value, string name)
    {
        if (!value.HasValue)
        {
            return null;
        }

        if (value.Value < -100m || value.Value > 100m)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be between -100 and 100.");
        }

        return decimal.Round(value.Value, 3, MidpointRounding.AwayFromZero);
    }
}

public sealed class CustomerMemoryProfilePreference : ICompanyOwnedEntity
{
    private CustomerMemoryProfilePreference()
    {
    }

    public CustomerMemoryProfilePreference(
        Guid id,
        Guid companyId,
        Guid customerMemoryProfileId,
        string preferenceKey,
        string preferenceValue,
        string? sourceSummary = null,
        decimal? confidence = null,
        DateTime? observedUtc = null,
        DateTime? createdUtc = null)
    {
        CustomerMemoryProfileText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        CustomerMemoryProfileId = CustomerMemoryProfileText.RequireId(customerMemoryProfileId, nameof(customerMemoryProfileId));
        PreferenceKey = CustomerMemoryProfileText.NormalizeRequired(preferenceKey, nameof(preferenceKey), 120).ToLowerInvariant();
        PreferenceValue = CustomerMemoryProfileText.NormalizeRequired(preferenceValue, nameof(preferenceValue), 1000);
        SourceSummary = CustomerMemoryProfileText.NormalizeOptional(sourceSummary, nameof(sourceSummary), 1000);
        Confidence = CustomerMemoryProfileText.NormalizeRatio(confidence, nameof(confidence));
        ObservedUtc = CustomerMemoryProfileText.NormalizeUtc(observedUtc ?? DateTime.UtcNow, nameof(observedUtc));
        CreatedUtc = CustomerMemoryProfileText.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CustomerMemoryProfileId { get; private set; }
    public string PreferenceKey { get; private set; } = null!;
    public string PreferenceValue { get; private set; } = null!;
    public string? SourceSummary { get; private set; }
    public decimal? Confidence { get; private set; }
    public DateTime ObservedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public CustomerMemoryProfile CustomerMemoryProfile { get; private set; } = null!;
}

public sealed class CustomerMemoryProfilePriceSignal : ICompanyOwnedEntity
{
    private CustomerMemoryProfilePriceSignal()
    {
    }

    public CustomerMemoryProfilePriceSignal(
        Guid id,
        Guid companyId,
        Guid customerMemoryProfileId,
        string signalKey,
        string signalValue,
        decimal? confidence = null,
        DateTime? observedUtc = null,
        string? sourceSummary = null,
        DateTime? createdUtc = null)
    {
        CustomerMemoryProfileText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        CustomerMemoryProfileId = CustomerMemoryProfileText.RequireId(customerMemoryProfileId, nameof(customerMemoryProfileId));
        SignalKey = CustomerMemoryProfileText.NormalizeRequired(signalKey, nameof(signalKey), 120).ToLowerInvariant();
        SignalValue = CustomerMemoryProfileText.NormalizeRequired(signalValue, nameof(signalValue), 1000);
        Confidence = CustomerMemoryProfileText.NormalizeRatio(confidence, nameof(confidence));
        ObservedUtc = CustomerMemoryProfileText.NormalizeUtc(observedUtc ?? DateTime.UtcNow, nameof(observedUtc));
        SourceSummary = CustomerMemoryProfileText.NormalizeOptional(sourceSummary, nameof(sourceSummary), 1000);
        CreatedUtc = CustomerMemoryProfileText.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CustomerMemoryProfileId { get; private set; }
    public string SignalKey { get; private set; } = null!;
    public string SignalValue { get; private set; } = null!;
    public decimal? Confidence { get; private set; }
    public DateTime ObservedUtc { get; private set; }
    public string? SourceSummary { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public CustomerMemoryProfile CustomerMemoryProfile { get; private set; } = null!;
}

public sealed class CustomerMemoryProfileIndustrySignal : ICompanyOwnedEntity
{
    private CustomerMemoryProfileIndustrySignal()
    {
    }

    public CustomerMemoryProfileIndustrySignal(
        Guid id,
        Guid companyId,
        Guid customerMemoryProfileId,
        string signalKey,
        string signalValue,
        decimal? confidence = null,
        DateTime? observedUtc = null,
        string? sourceSummary = null,
        DateTime? createdUtc = null)
    {
        CustomerMemoryProfileText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        CustomerMemoryProfileId = CustomerMemoryProfileText.RequireId(customerMemoryProfileId, nameof(customerMemoryProfileId));
        SignalKey = CustomerMemoryProfileText.NormalizeRequired(signalKey, nameof(signalKey), 120).ToLowerInvariant();
        SignalValue = CustomerMemoryProfileText.NormalizeRequired(signalValue, nameof(signalValue), 1000);
        Confidence = CustomerMemoryProfileText.NormalizeRatio(confidence, nameof(confidence));
        ObservedUtc = CustomerMemoryProfileText.NormalizeUtc(observedUtc ?? DateTime.UtcNow, nameof(observedUtc));
        SourceSummary = CustomerMemoryProfileText.NormalizeOptional(sourceSummary, nameof(sourceSummary), 1000);
        CreatedUtc = CustomerMemoryProfileText.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CustomerMemoryProfileId { get; private set; }
    public string SignalKey { get; private set; } = null!;
    public string SignalValue { get; private set; } = null!;
    public decimal? Confidence { get; private set; }
    public DateTime ObservedUtc { get; private set; }
    public string? SourceSummary { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public CustomerMemoryProfile CustomerMemoryProfile { get; private set; } = null!;
}

internal static class CustomerMemoryProfileText
{
    public static void EnsureCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }
    }

    public static Guid RequireId(Guid value, string name) =>
        value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;

    public static string NormalizeRequired(string value, string name, int maxLength)
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

    public static string? NormalizeOptional(string? value, string name, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : NormalizeRequired(value, name, maxLength);

    public static decimal? NormalizeRatio(decimal? value, string name)
    {
        if (!value.HasValue)
        {
            return null;
        }

        if (value.Value < 0m || value.Value > 1m)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be between 0 and 1.");
        }

        return decimal.Round(value.Value, 3, MidpointRounding.AwayFromZero);
    }

    public static DateTime NormalizeUtc(DateTime value, string name) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                : value.ToUniversalTime();

    public static Dictionary<string, JsonNode?> CloneNodes(IDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .ToDictionary(pair => pair.Key.Trim(), pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
}
