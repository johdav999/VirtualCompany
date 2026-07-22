namespace VirtualCompany.Domain.Entities;
public sealed partial class SalesSequenceExecutionStep : ICompanyOwnedEntity
{
    private SalesSequenceExecutionStep()
    {
    }

    public SalesSequenceExecutionStep(Guid id, Guid companyId, Guid sequenceExecutionId, Guid salesCampaignId, Guid contactId, Guid salesSequenceStepId, int stepOrder, DateTime scheduledSendUtc, string idempotencyKey)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SequenceExecutionId = sequenceExecutionId == Guid.Empty ? throw new ArgumentException("SequenceExecutionId is required.", nameof(sequenceExecutionId)) : sequenceExecutionId;
        SalesCampaignId = salesCampaignId == Guid.Empty ? throw new ArgumentException("SalesCampaignId is required.", nameof(salesCampaignId)) : salesCampaignId;
        ContactId = contactId == Guid.Empty ? throw new ArgumentException("ContactId is required.", nameof(contactId)) : contactId;
        SalesSequenceStepId = salesSequenceStepId == Guid.Empty ? throw new ArgumentException("SalesSequenceStepId is required.", nameof(salesSequenceStepId)) : salesSequenceStepId;
        StepOrder = stepOrder <= 0 ? throw new ArgumentOutOfRangeException(nameof(stepOrder), "Step order must be positive.") : stepOrder;
        ScheduledSendUtc = SalesEntityText.NormalizeUtc(scheduledSendUtc, nameof(scheduledSendUtc));
        Status = SalesStatuses.Pending;
        DeliveryStatus = SalesStatuses.Pending;
        IdempotencyKey = SalesEntityText.NormalizeRequired(idempotencyKey, nameof(idempotencyKey), 256);
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SequenceExecutionId { get; private set; }
    public Guid SalesCampaignId { get; private set; }
    public Guid ContactId { get; private set; }
    public Guid SalesSequenceStepId { get; private set; }
    public int StepOrder { get; private set; }
    public string Status { get; private set; } = null!;
    public DateTime ScheduledSendUtc { get; private set; }
    public DateTime? SentUtc { get; private set; }
    public DateTime? CancelledUtc { get; private set; }
    public string DeliveryStatus { get; private set; } = null!;
    public string? BounceStatus { get; private set; }
    public string? BounceReason { get; private set; }
    public string? CancellationReason { get; private set; }
    public string? CancellationSourceReference { get; private set; }
    public string? Provider { get; private set; }
    public Guid? MailboxConnectionId { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public string? ProviderThreadId { get; private set; }
    public string? InternetMessageId { get; private set; }
    public string? OriginalGeneratedSubject { get; private set; }
    public string? OriginalGeneratedBody { get; private set; }
    public string? CurrentDraftSubject { get; private set; }
    public string? CurrentDraftBody { get; private set; }
    public string? FinalSentSubject { get; private set; }
    public string? FinalSentBody { get; private set; }
    public DateTime? GeneratedDraftUtc { get; private set; }
    public DateTime? DraftUpdatedUtc { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public SalesSequenceExecution SequenceExecution { get; private set; } = null!;
    public SalesSequenceStep SalesSequenceStep { get; private set; } = null!;

    public void RecordGeneratedDraft(string subject, string body, DateTime generatedUtc)
    {
        var effectiveGeneratedUtc = SalesEntityText.NormalizeUtc(generatedUtc, nameof(generatedUtc));
        var normalizedSubject = SalesEntityText.NormalizeRequired(subject, nameof(subject), 300);
        var normalizedBody = SalesEntityText.NormalizeRequired(body, nameof(body), 16000);

        OriginalGeneratedSubject = string.IsNullOrWhiteSpace(OriginalGeneratedSubject) ? normalizedSubject : OriginalGeneratedSubject;
        OriginalGeneratedBody = string.IsNullOrWhiteSpace(OriginalGeneratedBody) ? normalizedBody : OriginalGeneratedBody;
        CurrentDraftSubject = normalizedSubject;
        CurrentDraftBody = normalizedBody;
        GeneratedDraftUtc ??= effectiveGeneratedUtc;
        DraftUpdatedUtc = effectiveGeneratedUtc;
        UpdatedUtc = effectiveGeneratedUtc;
    }

    public void UpdateDraftContent(string subject, string body, DateTime updatedUtc)
    {
        CurrentDraftSubject = SalesEntityText.NormalizeRequired(subject, nameof(subject), 300);
        CurrentDraftBody = SalesEntityText.NormalizeRequired(body, nameof(body), 16000);
        DraftUpdatedUtc = SalesEntityText.NormalizeUtc(updatedUtc, nameof(updatedUtc));
        UpdatedUtc = DraftUpdatedUtc.Value;
    }

    public void MarkSending()
    {
        if (Status != SalesStatuses.Pending)
        {
            throw new InvalidOperationException("Only pending sequence steps can be sent.");
        }

        Status = SalesStatuses.InProgress;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkSent(string provider, Guid? mailboxConnectionId, string providerMessageId, string? providerThreadId, string? internetMessageId, string deliveryStatus, DateTime sentUtc, string? finalSubject = null, string? finalBody = null)
    {
        Provider = SalesEntityText.NormalizeRequired(provider, nameof(provider), 64);
        MailboxConnectionId = mailboxConnectionId;
        ProviderMessageId = SalesEntityText.NormalizeRequired(providerMessageId, nameof(providerMessageId), 256);
        ProviderThreadId = SalesEntityText.NormalizeOptional(providerThreadId, nameof(providerThreadId), 256);
        InternetMessageId = SalesEntityText.NormalizeOptional(internetMessageId, nameof(internetMessageId), 512);
        DeliveryStatus = SalesEntityText.NormalizeRequired(deliveryStatus, nameof(deliveryStatus), 32).ToLowerInvariant();
        Status = SalesStatuses.Completed;
        FinalSentSubject = SalesEntityText.NormalizeOptional(finalSubject, nameof(finalSubject), 300) ?? CurrentDraftSubject ?? OriginalGeneratedSubject;
        FinalSentBody = SalesEntityText.NormalizeOptional(finalBody, nameof(finalBody), 16000) ?? CurrentDraftBody ?? OriginalGeneratedBody;
        SentUtc = SalesEntityText.NormalizeUtc(sentUtc, nameof(sentUtc));
        UpdatedUtc = SentUtc.Value;
    }

    public void Cancel(string? reason = null, string? sourceReference = null, DateTime? cancelledUtc = null)
    {
        if (Status is SalesStatuses.Completed or SalesStatuses.Cancelled)
        {
            return;
        }

        var effectiveCancelledUtc = SalesEntityText.NormalizeUtc(cancelledUtc ?? DateTime.UtcNow, nameof(cancelledUtc));
        Status = SalesStatuses.Cancelled;
        DeliveryStatus = SalesStatuses.Cancelled;
        CancelledUtc = effectiveCancelledUtc;
        CancellationReason = string.IsNullOrWhiteSpace(reason)
            ? CancellationReason
            : SalesEntityText.NormalizeOptional(reason, nameof(reason), 80)?.ToLowerInvariant();
        CancellationSourceReference = string.IsNullOrWhiteSpace(sourceReference)
            ? CancellationSourceReference
            : SalesEntityText.NormalizeOptional(sourceReference, nameof(sourceReference), 256);
        UpdatedUtc = effectiveCancelledUtc;
    }

    public void MarkDeliveryStatus(string deliveryStatus, DateTime occurredUtc)
    {
        DeliveryStatus = SalesEntityText.NormalizeRequired(deliveryStatus, nameof(deliveryStatus), 32).ToLowerInvariant();
        UpdatedUtc = SalesEntityText.NormalizeUtc(occurredUtc, nameof(occurredUtc));
    }

    public void MarkBounce(string bounceStatus, string? reason, DateTime occurredUtc)
    {
        BounceStatus = SalesEntityText.NormalizeRequired(bounceStatus, nameof(bounceStatus), 32).ToLowerInvariant();
        BounceReason = SalesEntityText.NormalizeOptional(reason, nameof(reason), 1000);
        DeliveryStatus = SalesStatuses.Bounced;
        UpdatedUtc = SalesEntityText.NormalizeUtc(occurredUtc, nameof(occurredUtc));
    }
}

