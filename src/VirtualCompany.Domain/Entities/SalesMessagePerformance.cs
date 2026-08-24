using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class SalesMessagePerformance : ICompanyOwnedEntity
{
    private SalesMessagePerformance()
    {
    }

    public SalesMessagePerformance(
        Guid id,
        Guid companyId,
        string messageKey,
        Guid? campaignId,
        Guid? sequenceId,
        Guid? sequenceStepId,
        Guid? sequenceExecutionStepId,
        Guid contactId,
        string? provider = null,
        string? providerMessageId = null,
        string? providerThreadId = null,
        string? internetMessageId = null,
        string? variantKey = null,
        int? stepOrder = null,
        DateTime? createdUtc = null)
    {
        SalesEntityText.EnsureCompany(companyId);
        if (contactId == Guid.Empty)
        {
            throw new ArgumentException("ContactId is required.", nameof(contactId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        MessageKey = NormalizeRequired(messageKey, nameof(messageKey), 512);
        CampaignId = SalesEntityText.NormalizeOptionalId(campaignId, nameof(campaignId));
        SequenceId = SalesEntityText.NormalizeOptionalId(sequenceId, nameof(sequenceId));
        SequenceStepId = SalesEntityText.NormalizeOptionalId(sequenceStepId, nameof(sequenceStepId));
        SequenceExecutionStepId = SalesEntityText.NormalizeOptionalId(sequenceExecutionStepId, nameof(sequenceExecutionStepId));
        ContactId = contactId;
        Provider = SalesEntityText.NormalizeOptional(provider, nameof(provider), 64);
        ProviderMessageId = SalesEntityText.NormalizeOptional(providerMessageId, nameof(providerMessageId), 256);
        ProviderThreadId = SalesEntityText.NormalizeOptional(providerThreadId, nameof(providerThreadId), 256);
        InternetMessageId = SalesEntityText.NormalizeOptional(internetMessageId, nameof(internetMessageId), 512);
        VariantKey = NormalizeVariantKey(variantKey, sequenceStepId, stepOrder);
        StepOrder = stepOrder;
        CreatedUtc = NormalizeUtc(createdUtc ?? DateTime.UtcNow);
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string MessageKey { get; private set; } = null!;
    public Guid? CampaignId { get; private set; }
    public Guid? SequenceId { get; private set; }
    public Guid? SequenceStepId { get; private set; }
    public Guid? SequenceExecutionStepId { get; private set; }
    public Guid ContactId { get; private set; }
    public Guid? DealId { get; private set; }
    public string? Provider { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public string? ProviderThreadId { get; private set; }
    public string? InternetMessageId { get; private set; }
    public string? VariantKey { get; private set; }
    public int? StepOrder { get; private set; }
    public DateTime? SentAt { get; private set; }
    public DateTime? DeliveredAt { get; private set; }
    public DateTime? BouncedAt { get; private set; }
    public DateTime? OpenedAt { get; private set; }
    public DateTime? RepliedAt { get; private set; }
    public DateTime? DealCreatedAt { get; private set; }
    public DateTime? ConvertedAt { get; private set; }
    public decimal? ExpectedRevenueAmount { get; private set; }
    public string? ExpectedRevenueCurrency { get; private set; }
    public DateTime? ExpectedCloseAt { get; private set; }
    public decimal? PipelineRiskScore { get; private set; }
    public DateTime? LastRiskCalculatedAt { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }

    public bool IsSent => SentAt.HasValue;
    public bool IsDelivered => DeliveredAt.HasValue;
    public bool IsBounced => BouncedAt.HasValue;
    public bool IsOpened => OpenedAt.HasValue;
    public bool IsReplied => RepliedAt.HasValue;
    public bool IsDealCreated => DealCreatedAt.HasValue;
    public bool IsConverted => ConvertedAt.HasValue;

    public Company Company { get; private set; } = null!;
    public SalesCampaign? Campaign { get; private set; }
    public SalesSequence? Sequence { get; private set; }
    public SalesSequenceStep? SequenceStep { get; private set; }
    public SalesSequenceExecutionStep? SequenceExecutionStep { get; private set; }
    public Contact Contact { get; private set; } = null!;
    public Deal? Deal { get; private set; }

    public void MergeAttribution(
        Guid? campaignId,
        Guid? sequenceId,
        Guid? sequenceStepId,
        Guid? sequenceExecutionStepId,
        Guid? dealId,
        string? provider,
        string? providerMessageId,
        string? providerThreadId,
        string? internetMessageId,
        string? variantKey,
        int? stepOrder)
    {
        CampaignId ??= SalesEntityText.NormalizeOptionalId(campaignId, nameof(campaignId));
        SequenceId ??= SalesEntityText.NormalizeOptionalId(sequenceId, nameof(sequenceId));
        SequenceStepId ??= SalesEntityText.NormalizeOptionalId(sequenceStepId, nameof(sequenceStepId));
        SequenceExecutionStepId ??= SalesEntityText.NormalizeOptionalId(sequenceExecutionStepId, nameof(sequenceExecutionStepId));
        DealId ??= SalesEntityText.NormalizeOptionalId(dealId, nameof(dealId));
        Provider ??= SalesEntityText.NormalizeOptional(provider, nameof(provider), 64);
        ProviderMessageId ??= SalesEntityText.NormalizeOptional(providerMessageId, nameof(providerMessageId), 256);
        ProviderThreadId ??= SalesEntityText.NormalizeOptional(providerThreadId, nameof(providerThreadId), 256);
        InternetMessageId ??= SalesEntityText.NormalizeOptional(internetMessageId, nameof(internetMessageId), 512);
        VariantKey ??= NormalizeVariantKey(variantKey, sequenceStepId, stepOrder);
        StepOrder ??= stepOrder;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void ApplyEvent(ConversionAnalyticsEventType eventType, DateTime occurredUtc)
    {
        var normalized = NormalizeUtc(occurredUtc);
        // Webhooks can be retried or arrive out of order. Keep the earliest provider timestamp
        // so repeated events cannot inflate downstream funnel counts.
        switch (eventType)
        {
            case ConversionAnalyticsEventType.Sent:
                SentAt = Earliest(SentAt, normalized);
                break;
            case ConversionAnalyticsEventType.Delivered:
                DeliveredAt = Earliest(DeliveredAt, normalized);
                break;
            case ConversionAnalyticsEventType.Bounced:
                BouncedAt = Earliest(BouncedAt, normalized);
                break;
            case ConversionAnalyticsEventType.Opened:
                OpenedAt = Earliest(OpenedAt, normalized);
                break;
            case ConversionAnalyticsEventType.Replied:
                RepliedAt = Earliest(RepliedAt, normalized);
                break;
            case ConversionAnalyticsEventType.DealCreated:
                DealCreatedAt = Earliest(DealCreatedAt, normalized);
                break;
            case ConversionAnalyticsEventType.Converted:
                ConvertedAt = Earliest(ConvertedAt, normalized);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Unsupported conversion analytics event type.");
        }

        UpdatedUtc = DateTime.UtcNow;
    }

    public void UpdateRevenueContext(decimal? amount, string? currency, DateTime? expectedCloseAt, decimal? riskScore, DateTime? riskCalculatedAt)
    {
        ExpectedRevenueAmount = amount;
        ExpectedRevenueCurrency = SalesEntityText.NormalizeOptional(currency, nameof(currency), 3)?.ToUpperInvariant();
        ExpectedCloseAt = expectedCloseAt.HasValue ? NormalizeUtc(expectedCloseAt.Value) : null;
        PipelineRiskScore = riskScore;
        LastRiskCalculatedAt = riskCalculatedAt.HasValue ? NormalizeUtc(riskCalculatedAt.Value) : null;
        UpdatedUtc = DateTime.UtcNow;
    }

    private static DateTime? Earliest(DateTime? current, DateTime incoming) =>
        !current.HasValue || incoming < current.Value ? incoming : current;

    private static string NormalizeRequired(string value, string name, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{name} is required.", name)
            : value.Trim().Length > maxLength
                ? throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.")
                : value.Trim();

    private static string? NormalizeVariantKey(string? variantKey, Guid? sequenceStepId, int? stepOrder) =>
        SalesEntityText.NormalizeOptional(variantKey, nameof(variantKey), 120)
        ?? (sequenceStepId.HasValue ? $"step:{sequenceStepId.Value:N}" : null)
        ?? (stepOrder.HasValue ? $"step-order:{stepOrder.Value}" : null);

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
