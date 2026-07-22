namespace VirtualCompany.Domain.Entities;
public sealed class DealIntelligenceSignal : ICompanyOwnedEntity
{
    private DealIntelligenceSignal()
    {
    }

    public DealIntelligenceSignal(
        Guid id,
        Guid companyId,
        string signalType,
        decimal confidenceScore,
        string explanation,
        DateTime detectedUtc,
        Guid? dealId = null,
        Guid? conversationId = null,
        Guid? messageId = null,
        Guid? sequenceId = null,
        Guid? sequenceStepId = null,
        string signalState = DealIntelligenceSignalStates.Detected,
        string sourceType = DealIntelligenceSignalSourceTypes.InboundReply,
        string? sourceMessageId = null,
        string? sourceThreadId = null,
        string? sourceMetadataJson = null,
        DateTime? sourceWindowStartedUtc = null,
        DateTime? sourceWindowEndedUtc = null,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        DealId = SalesEntityText.NormalizeOptionalId(dealId, nameof(dealId));
        ConversationId = SalesEntityText.NormalizeOptionalId(conversationId, nameof(conversationId));
        MessageId = SalesEntityText.NormalizeOptionalId(messageId, nameof(messageId));
        SequenceId = SalesEntityText.NormalizeOptionalId(sequenceId, nameof(sequenceId));
        SequenceStepId = SalesEntityText.NormalizeOptionalId(sequenceStepId, nameof(sequenceStepId));
        SignalType = DealIntelligenceSignalTypes.Normalize(signalType);
        SignalState = SalesEntityText.NormalizeRequired(signalState, nameof(signalState), 32).ToLowerInvariant();
        ConfidenceScore = ValidateConfidence(confidenceScore);
        Explanation = SalesEntityText.NormalizeRequired(explanation, nameof(explanation), 1000);
        SourceType = SalesEntityText.NormalizeRequired(sourceType, nameof(sourceType), 64).ToLowerInvariant();
        SourceMessageId = SalesEntityText.NormalizeOptional(sourceMessageId, nameof(sourceMessageId), 256);
        SourceThreadId = SalesEntityText.NormalizeOptional(sourceThreadId, nameof(sourceThreadId), 256);
        SourceMetadataJson = SalesEntityText.NormalizeOptional(sourceMetadataJson, nameof(sourceMetadataJson), 8000);
        DetectedUtc = SalesEntityText.NormalizeUtc(detectedUtc, nameof(detectedUtc));
        SourceWindowStartedUtc = sourceWindowStartedUtc.HasValue ? SalesEntityText.NormalizeUtc(sourceWindowStartedUtc.Value, nameof(sourceWindowStartedUtc)) : null;
        SourceWindowEndedUtc = sourceWindowEndedUtc.HasValue ? SalesEntityText.NormalizeUtc(sourceWindowEndedUtc.Value, nameof(sourceWindowEndedUtc)) : null;
        CreatedUtc = SalesEntityText.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        UpdatedUtc = SalesEntityText.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? DealId { get; private set; }
    public Guid? ConversationId { get; private set; }
    public Guid? MessageId { get; private set; }
    public Guid? SequenceId { get; private set; }
    public Guid? SequenceStepId { get; private set; }
    public string SignalType { get; private set; } = null!;
    public string SignalState { get; private set; } = null!;
    public decimal ConfidenceScore { get; private set; }
    public string Explanation { get; private set; } = null!;
    public string SourceType { get; private set; } = null!;
    public string? SourceMessageId { get; private set; }
    public string? SourceThreadId { get; private set; }
    public string? SourceMetadataJson { get; private set; }
    public DateTime DetectedUtc { get; private set; }
    public DateTime? SourceWindowStartedUtc { get; private set; }
    public DateTime? SourceWindowEndedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public Deal? Deal { get; private set; }

    public void UpdateDetection(decimal confidenceScore, string explanation, string? sourceMetadataJson, DateTime detectedUtc, DateTime? sourceWindowStartedUtc, DateTime? sourceWindowEndedUtc)
    {
        ConfidenceScore = ValidateConfidence(confidenceScore);
        Explanation = SalesEntityText.NormalizeRequired(explanation, nameof(explanation), 1000);
        SourceMetadataJson = SalesEntityText.NormalizeOptional(sourceMetadataJson, nameof(sourceMetadataJson), 8000);
        DetectedUtc = SalesEntityText.NormalizeUtc(detectedUtc, nameof(detectedUtc));
        SourceWindowStartedUtc = sourceWindowStartedUtc.HasValue ? SalesEntityText.NormalizeUtc(sourceWindowStartedUtc.Value, nameof(sourceWindowStartedUtc)) : null;
        SourceWindowEndedUtc = sourceWindowEndedUtc.HasValue ? SalesEntityText.NormalizeUtc(sourceWindowEndedUtc.Value, nameof(sourceWindowEndedUtc)) : null;
        UpdatedUtc = DateTime.UtcNow;
    }

    private static decimal ValidateConfidence(decimal value) =>
        value is < 0m or > 1m
            ? throw new ArgumentOutOfRangeException(nameof(value), "Confidence score must be between 0 and 1.")
            : Math.Round(value, 4, MidpointRounding.AwayFromZero);
}

