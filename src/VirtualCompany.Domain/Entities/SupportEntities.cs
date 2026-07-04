using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Entities;

public sealed class SupportCase : ICompanyOwnedEntity
{
    private SupportCase()
    {
    }

    public SupportCase(
        Guid id,
        Guid companyId,
        string caseNumber,
        string subject,
        string? description,
        string source,
        Guid? contactId = null,
        Guid? customerCompanyId = null,
        Guid? assignedAgentId = null,
        Guid? assignedUserId = null,
        DateTime? createdUtc = null)
    {
        SupportEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        CaseNumber = SupportEntityText.NormalizeRequired(caseNumber, nameof(caseNumber), 40);
        Subject = SupportEntityText.NormalizeRequired(subject, nameof(subject), 240);
        Summary = SupportEntityText.NormalizeOptional(description, nameof(description), 1000) ?? Subject;
        Description = SupportEntityText.NormalizeOptional(description, nameof(description), 4000);
        Status = SupportCaseStatuses.New;
        Priority = SupportPriorities.Normal;
        Category = SupportCaseCategories.GeneralQuestion;
        Source = SupportEntityText.NormalizeRequired(source, nameof(source), 80);
        ContactId = SupportEntityText.NormalizeOptionalId(contactId, nameof(contactId));
        CustomerCompanyId = SupportEntityText.NormalizeOptionalId(customerCompanyId, nameof(customerCompanyId));
        AssignedAgentId = SupportEntityText.NormalizeOptionalId(assignedAgentId, nameof(assignedAgentId));
        AssignedUserId = SupportEntityText.NormalizeOptionalId(assignedUserId, nameof(assignedUserId));
        CreatedUtc = SupportEntityText.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string CaseNumber { get; private set; } = null!;
    public string Subject { get; private set; } = null!;
    public string Summary { get; private set; } = null!;
    public string? Description { get; private set; }
    public string Status { get; private set; } = null!;
    public string Priority { get; private set; } = null!;
    public string Category { get; private set; } = null!;
    public string Source { get; private set; } = null!;
    public string? Sentiment { get; private set; }
    public decimal? ConfidenceScore { get; private set; }
    public string? SuggestedNextAction { get; private set; }
    public string? RationaleSummary { get; private set; }
    public Guid? ContactId { get; private set; }
    public Guid? CustomerCompanyId { get; private set; }
    public Guid? RelatedInvoiceId { get; private set; }
    public Guid? RelatedPaymentId { get; private set; }
    public Guid? AssignedAgentId { get; private set; }
    public Guid? AssignedUserId { get; private set; }
    public DateTime? FirstResponseDueUtc { get; private set; }
    public DateTime? ResolutionDueUtc { get; private set; }
    public DateTime? LastCustomerMessageUtc { get; private set; }
    public DateTime? LastInternalActivityUtc { get; private set; }
    public DateTime? FirstResponseSentUtc { get; private set; }
    public DateTime? ResolvedUtc { get; private set; }
    public DateTime? ClosedUtc { get; private set; }
    public bool IsSlaRisk { get; private set; }
    public bool IsSlaBreached { get; private set; }
    public bool IsVipRisk { get; private set; }
    public bool IsChurnRisk { get; private set; }
    public string? ProviderThreadId { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public JsonObject Metadata { get; private set; } = [];
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }

    public ICollection<SupportMessage> Messages { get; } = new List<SupportMessage>();
    public ICollection<SupportCaseEvent> Events { get; } = new List<SupportCaseEvent>();
    public ICollection<SupportCaseAssignment> Assignments { get; } = new List<SupportCaseAssignment>();
    public SupportCaseResolution? Resolution { get; private set; }
    public ICollection<SupportReplyDraft> ReplyDrafts { get; } = new List<SupportReplyDraft>();
    public ICollection<SupportRefundRequest> RefundRequests { get; } = new List<SupportRefundRequest>();
    public ICollection<SupportKnowledgeGap> KnowledgeGaps { get; } = new List<SupportKnowledgeGap>();

    public void UpdateSummary(string? summary)
    {
        Summary = SupportEntityText.NormalizeOptional(summary, nameof(summary), 1000) ?? Subject;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void LinkProviderMessage(string? providerThreadId, string? providerMessageId)
    {
        ProviderThreadId = SupportEntityText.NormalizeOptional(providerThreadId, nameof(providerThreadId), 256);
        ProviderMessageId = SupportEntityText.NormalizeOptional(providerMessageId, nameof(providerMessageId), 256);
        UpdatedUtc = DateTime.UtcNow;
    }

    public void LinkContext(Guid? contactId, Guid? customerCompanyId, Guid? invoiceId, Guid? paymentId)
    {
        ContactId = SupportEntityText.NormalizeOptionalId(contactId, nameof(contactId));
        CustomerCompanyId = SupportEntityText.NormalizeOptionalId(customerCompanyId, nameof(customerCompanyId));
        RelatedInvoiceId = SupportEntityText.NormalizeOptionalId(invoiceId, nameof(invoiceId));
        RelatedPaymentId = SupportEntityText.NormalizeOptionalId(paymentId, nameof(paymentId));
        UpdatedUtc = DateTime.UtcNow;
    }

    public void Assign(Guid? assignedAgentId, Guid? assignedUserId)
    {
        AssignedAgentId = SupportEntityText.NormalizeOptionalId(assignedAgentId, nameof(assignedAgentId));
        AssignedUserId = SupportEntityText.NormalizeOptionalId(assignedUserId, nameof(assignedUserId));
        UpdatedUtc = DateTime.UtcNow;
    }

    public void SetPriority(string priority)
    {
        Priority = SupportPriorities.Normalize(priority);
        UpdatedUtc = DateTime.UtcNow;
    }

    public void SetCategory(string category)
    {
        Category = SupportCaseCategories.Normalize(category);
        UpdatedUtc = DateTime.UtcNow;
    }

    public void SetStatus(string status)
    {
        var normalized = SupportCaseStatuses.Normalize(status);
        Status = normalized;
        if (normalized == SupportCaseStatuses.Resolved)
        {
            ResolvedUtc ??= DateTime.UtcNow;
        }
        else if (normalized == SupportCaseStatuses.Closed)
        {
            ClosedUtc ??= DateTime.UtcNow;
        }
        else if (normalized == SupportCaseStatuses.Reopened)
        {
            ClosedUtc = null;
        }

        UpdatedUtc = DateTime.UtcNow;
    }

    public void SetTriage(
        string category,
        string priority,
        string? sentiment,
        decimal confidence,
        string? suggestedNextAction,
        string? rationaleSummary,
        bool isVipRisk,
        bool isChurnRisk,
        bool isSlaRisk)
    {
        if (confidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), "Confidence must be between 0 and 1.");
        }

        Category = SupportCaseCategories.Normalize(category);
        Priority = SupportPriorities.Normalize(priority);
        Sentiment = SupportEntityText.NormalizeOptional(sentiment, nameof(sentiment), 80);
        ConfidenceScore = confidence;
        SuggestedNextAction = SupportEntityText.NormalizeOptional(suggestedNextAction, nameof(suggestedNextAction), 1000);
        RationaleSummary = SupportEntityText.NormalizeOptional(rationaleSummary, nameof(rationaleSummary), 2000);
        IsVipRisk = isVipRisk;
        IsChurnRisk = isChurnRisk;
        IsSlaRisk = isSlaRisk;
        Status = confidence >= 0.55m ? SupportCaseStatuses.Triaged : SupportCaseStatuses.New;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void SetSla(DateTime? firstResponseDueUtc, DateTime? resolutionDueUtc)
    {
        FirstResponseDueUtc = firstResponseDueUtc;
        ResolutionDueUtc = resolutionDueUtc;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkCustomerMessage(DateTime occurredUtc)
    {
        LastCustomerMessageUtc = SupportEntityText.NormalizeUtc(occurredUtc, nameof(occurredUtc));
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkInternalActivity(DateTime occurredUtc)
    {
        LastInternalActivityUtc = SupportEntityText.NormalizeUtc(occurredUtc, nameof(occurredUtc));
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkFirstResponseSent(DateTime occurredUtc)
    {
        FirstResponseSentUtc ??= SupportEntityText.NormalizeUtc(occurredUtc, nameof(occurredUtc));
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkSlaState(bool isRisk, bool isBreached)
    {
        IsSlaRisk = isRisk;
        IsSlaBreached = isBreached;
        UpdatedUtc = DateTime.UtcNow;
    }
}

public sealed class SupportMessage : ICompanyOwnedEntity
{
    private SupportMessage()
    {
    }

    public SupportMessage(
        Guid id,
        Guid companyId,
        Guid supportCaseId,
        string direction,
        string channel,
        string sender,
        string? recipient,
        string body,
        DateTime occurredUtc,
        Guid? emailMessageSnapshotId = null,
        string? providerMessageId = null,
        string? providerThreadId = null,
        Guid? replyDraftId = null)
    {
        SupportEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SupportCaseId = supportCaseId == Guid.Empty ? throw new ArgumentException("SupportCaseId is required.", nameof(supportCaseId)) : supportCaseId;
        Direction = SupportMessageDirections.Normalize(direction);
        Channel = SupportEntityText.NormalizeRequired(channel, nameof(channel), 80);
        Sender = SupportEntityText.NormalizeRequired(sender, nameof(sender), 256);
        Recipient = SupportEntityText.NormalizeOptional(recipient, nameof(recipient), 256);
        Body = SupportEntityText.NormalizeRequired(body, nameof(body), 8000);
        OccurredUtc = SupportEntityText.NormalizeUtc(occurredUtc, nameof(occurredUtc));
        EmailMessageSnapshotId = SupportEntityText.NormalizeOptionalId(emailMessageSnapshotId, nameof(emailMessageSnapshotId));
        ProviderMessageId = SupportEntityText.NormalizeOptional(providerMessageId, nameof(providerMessageId), 256);
        ProviderThreadId = SupportEntityText.NormalizeOptional(providerThreadId, nameof(providerThreadId), 256);
        ReplyDraftId = SupportEntityText.NormalizeOptionalId(replyDraftId, nameof(replyDraftId));
        CreatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SupportCaseId { get; private set; }
    public string Direction { get; private set; } = null!;
    public string Channel { get; private set; } = null!;
    public string Sender { get; private set; } = null!;
    public string? Recipient { get; private set; }
    public string Body { get; private set; } = null!;
    public Guid? EmailMessageSnapshotId { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public string? ProviderThreadId { get; private set; }
    public Guid? ReplyDraftId { get; private set; }
    public DateTime OccurredUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public SupportCase SupportCase { get; private set; } = null!;
}

public sealed class SupportCaseEvent : ICompanyOwnedEntity
{
    private SupportCaseEvent()
    {
    }

    public SupportCaseEvent(Guid id, Guid companyId, Guid supportCaseId, string eventType, string summary, string actorType, Guid? actorId, DateTime occurredUtc, JsonObject? metadata = null)
    {
        SupportEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SupportCaseId = supportCaseId == Guid.Empty ? throw new ArgumentException("SupportCaseId is required.", nameof(supportCaseId)) : supportCaseId;
        EventType = SupportCaseEventTypes.Normalize(eventType);
        Summary = SupportEntityText.NormalizeRequired(summary, nameof(summary), 1000);
        ActorType = SupportEntityText.NormalizeRequired(actorType, nameof(actorType), 64);
        ActorId = SupportEntityText.NormalizeOptionalId(actorId, nameof(actorId));
        OccurredUtc = SupportEntityText.NormalizeUtc(occurredUtc, nameof(occurredUtc));
        Metadata = metadata is null ? [] : JsonNode.Parse(metadata.ToJsonString())!.AsObject();
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SupportCaseId { get; private set; }
    public string EventType { get; private set; } = null!;
    public string Summary { get; private set; } = null!;
    public string ActorType { get; private set; } = null!;
    public Guid? ActorId { get; private set; }
    public JsonObject Metadata { get; private set; } = [];
    public DateTime OccurredUtc { get; private set; }
    public SupportCase SupportCase { get; private set; } = null!;
}

public sealed class SupportCaseAssignment : ICompanyOwnedEntity
{
    private SupportCaseAssignment()
    {
    }

    public SupportCaseAssignment(Guid id, Guid companyId, Guid supportCaseId, Guid? assignedAgentId, Guid? assignedUserId, Guid assignedByUserId, DateTime assignedUtc, string? reason = null)
    {
        SupportEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SupportCaseId = supportCaseId == Guid.Empty ? throw new ArgumentException("SupportCaseId is required.", nameof(supportCaseId)) : supportCaseId;
        AssignedAgentId = SupportEntityText.NormalizeOptionalId(assignedAgentId, nameof(assignedAgentId));
        AssignedUserId = SupportEntityText.NormalizeOptionalId(assignedUserId, nameof(assignedUserId));
        AssignedByUserId = assignedByUserId == Guid.Empty ? throw new ArgumentException("AssignedByUserId is required.", nameof(assignedByUserId)) : assignedByUserId;
        AssignedUtc = SupportEntityText.NormalizeUtc(assignedUtc, nameof(assignedUtc));
        Reason = SupportEntityText.NormalizeOptional(reason, nameof(reason), 1000);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SupportCaseId { get; private set; }
    public Guid? AssignedAgentId { get; private set; }
    public Guid? AssignedUserId { get; private set; }
    public Guid AssignedByUserId { get; private set; }
    public string? Reason { get; private set; }
    public DateTime AssignedUtc { get; private set; }
    public SupportCase SupportCase { get; private set; } = null!;
}

public sealed class SupportSlaPolicy : ICompanyOwnedEntity
{
    private SupportSlaPolicy()
    {
    }

    public SupportSlaPolicy(Guid id, Guid companyId, string name, string category, string priority, int firstResponseMinutes, int resolutionMinutes, string? customerTier = null, bool isActive = true)
    {
        SupportEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Name = SupportEntityText.NormalizeRequired(name, nameof(name), 160);
        Category = SupportCaseCategories.Normalize(category);
        Priority = SupportPriorities.Normalize(priority);
        CustomerTier = SupportEntityText.NormalizeOptional(customerTier, nameof(customerTier), 80);
        FirstResponseMinutes = firstResponseMinutes <= 0 ? throw new ArgumentOutOfRangeException(nameof(firstResponseMinutes)) : firstResponseMinutes;
        ResolutionMinutes = resolutionMinutes <= 0 ? throw new ArgumentOutOfRangeException(nameof(resolutionMinutes)) : resolutionMinutes;
        IsActive = isActive;
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Category { get; private set; } = null!;
    public string Priority { get; private set; } = null!;
    public string? CustomerTier { get; private set; }
    public int FirstResponseMinutes { get; private set; }
    public int ResolutionMinutes { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
}

public sealed class SupportCaseResolution : ICompanyOwnedEntity
{
    private SupportCaseResolution()
    {
    }

    public SupportCaseResolution(Guid id, Guid companyId, Guid supportCaseId, string summary, string outcome, Guid resolvedByUserId, DateTime resolvedUtc)
    {
        SupportEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SupportCaseId = supportCaseId == Guid.Empty ? throw new ArgumentException("SupportCaseId is required.", nameof(supportCaseId)) : supportCaseId;
        Summary = SupportEntityText.NormalizeRequired(summary, nameof(summary), 2000);
        Outcome = SupportEntityText.NormalizeRequired(outcome, nameof(outcome), 120);
        ResolvedByUserId = resolvedByUserId == Guid.Empty ? throw new ArgumentException("ResolvedByUserId is required.", nameof(resolvedByUserId)) : resolvedByUserId;
        ResolvedUtc = SupportEntityText.NormalizeUtc(resolvedUtc, nameof(resolvedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SupportCaseId { get; private set; }
    public string Summary { get; private set; } = null!;
    public string Outcome { get; private set; } = null!;
    public Guid ResolvedByUserId { get; private set; }
    public DateTime ResolvedUtc { get; private set; }
    public SupportCase SupportCase { get; private set; } = null!;
}

public sealed class SupportReplyDraft : ICompanyOwnedEntity
{
    private SupportReplyDraft()
    {
    }

    public SupportReplyDraft(Guid id, Guid companyId, Guid supportCaseId, string draftBody, string tone, decimal confidence, decimal answerability, string? rationaleSummary, string? sourceReferencesJson, Guid? createdByAgentId, Guid? createdByUserId)
    {
        SupportEntityText.EnsureCompany(companyId);
        if (confidence is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(confidence));
        if (answerability is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(answerability));
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SupportCaseId = supportCaseId == Guid.Empty ? throw new ArgumentException("SupportCaseId is required.", nameof(supportCaseId)) : supportCaseId;
        DraftBody = SupportEntityText.NormalizeRequired(draftBody, nameof(draftBody), 8000);
        Tone = SupportEntityText.NormalizeRequired(tone, nameof(tone), 80);
        Status = confidence >= 0.75m && answerability >= 0.75m ? SupportReplyDraftStatuses.Draft : SupportReplyDraftStatuses.NeedsReview;
        Confidence = confidence;
        Answerability = answerability;
        RationaleSummary = SupportEntityText.NormalizeOptional(rationaleSummary, nameof(rationaleSummary), 2000);
        SourceReferencesJson = SupportEntityText.NormalizeOptional(sourceReferencesJson, nameof(sourceReferencesJson), 8000);
        CreatedByAgentId = SupportEntityText.NormalizeOptionalId(createdByAgentId, nameof(createdByAgentId));
        CreatedByUserId = SupportEntityText.NormalizeOptionalId(createdByUserId, nameof(createdByUserId));
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SupportCaseId { get; private set; }
    public string DraftBody { get; private set; } = null!;
    public string Tone { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public decimal Confidence { get; private set; }
    public decimal Answerability { get; private set; }
    public string? RationaleSummary { get; private set; }
    public string? SourceReferencesJson { get; private set; }
    public Guid? CreatedByAgentId { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTime? ApprovedUtc { get; private set; }
    public DateTime? SentUtc { get; private set; }
    public string? SendFailureSummary { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public SupportCase SupportCase { get; private set; } = null!;

    public void Edit(string body, string tone)
    {
        DraftBody = SupportEntityText.NormalizeRequired(body, nameof(body), 8000);
        Tone = SupportEntityText.NormalizeRequired(tone, nameof(tone), 80);
        Status = SupportReplyDraftStatuses.Draft;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void Approve(Guid userId)
    {
        ApprovedByUserId = userId == Guid.Empty ? throw new ArgumentException("UserId is required.", nameof(userId)) : userId;
        ApprovedUtc = DateTime.UtcNow;
        Status = SupportReplyDraftStatuses.Approved;
        UpdatedUtc = ApprovedUtc.Value;
    }

    public void Reject()
    {
        Status = SupportReplyDraftStatuses.Rejected;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkSent(DateTime sentUtc)
    {
        SentUtc = SupportEntityText.NormalizeUtc(sentUtc, nameof(sentUtc));
        SendFailureSummary = null;
        UpdatedUtc = SentUtc.Value;
    }

    public void MarkSendFailed(string failureSummary)
    {
        SendFailureSummary = SupportEntityText.NormalizeRequired(failureSummary, nameof(failureSummary), 1000);
        UpdatedUtc = DateTime.UtcNow;
    }
}

public sealed class SupportRefundRequest : ICompanyOwnedEntity
{
    private SupportRefundRequest()
    {
    }

    public SupportRefundRequest(Guid id, Guid companyId, Guid supportCaseId, decimal amount, string currency, string reasonCode, string explanation, Guid? invoiceId, Guid? paymentId, Guid? requestedByAgentId, Guid? requestedByUserId)
    {
        SupportEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SupportCaseId = supportCaseId == Guid.Empty ? throw new ArgumentException("SupportCaseId is required.", nameof(supportCaseId)) : supportCaseId;
        Amount = amount <= 0 ? throw new ArgumentOutOfRangeException(nameof(amount)) : amount;
        Currency = SupportEntityText.NormalizeRequired(currency, nameof(currency), 3).ToUpperInvariant();
        ReasonCode = SupportEntityText.NormalizeRequired(reasonCode, nameof(reasonCode), 80);
        Explanation = SupportEntityText.NormalizeRequired(explanation, nameof(explanation), 2000);
        InvoiceId = SupportEntityText.NormalizeOptionalId(invoiceId, nameof(invoiceId));
        PaymentId = SupportEntityText.NormalizeOptionalId(paymentId, nameof(paymentId));
        RequestedByAgentId = SupportEntityText.NormalizeOptionalId(requestedByAgentId, nameof(requestedByAgentId));
        RequestedByUserId = SupportEntityText.NormalizeOptionalId(requestedByUserId, nameof(requestedByUserId));
        Status = SupportRefundRequestStatuses.PendingApproval;
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SupportCaseId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public string ReasonCode { get; private set; } = null!;
    public string Explanation { get; private set; } = null!;
    public Guid? InvoiceId { get; private set; }
    public Guid? PaymentId { get; private set; }
    public Guid? RequestedByAgentId { get; private set; }
    public Guid? RequestedByUserId { get; private set; }
    public Guid? ApprovalRequestId { get; private set; }
    public Guid? FinanceActionReferenceId { get; private set; }
    public string Status { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public SupportCase SupportCase { get; private set; } = null!;

    public void LinkApproval(Guid approvalRequestId)
    {
        ApprovalRequestId = approvalRequestId == Guid.Empty ? throw new ArgumentException("ApprovalRequestId is required.", nameof(approvalRequestId)) : approvalRequestId;
        UpdatedUtc = DateTime.UtcNow;
    }
}

public sealed class SupportKnowledgeGap : ICompanyOwnedEntity
{
    private SupportKnowledgeGap()
    {
    }

    public SupportKnowledgeGap(Guid id, Guid companyId, Guid? supportCaseId, Guid? supportReplyDraftId, string category, string questionSummary, string missingInformationSummary, string? retrievalSourceSummary)
    {
        SupportEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SupportCaseId = SupportEntityText.NormalizeOptionalId(supportCaseId, nameof(supportCaseId));
        SupportReplyDraftId = SupportEntityText.NormalizeOptionalId(supportReplyDraftId, nameof(supportReplyDraftId));
        Category = SupportCaseCategories.Normalize(category);
        QuestionSummary = SupportEntityText.NormalizeRequired(questionSummary, nameof(questionSummary), 1000);
        MissingInformationSummary = SupportEntityText.NormalizeRequired(missingInformationSummary, nameof(missingInformationSummary), 2000);
        RetrievalSourceSummary = SupportEntityText.NormalizeOptional(retrievalSourceSummary, nameof(retrievalSourceSummary), 2000);
        Status = SupportKnowledgeGapStatuses.Open;
        FrequencyCount = 1;
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? SupportCaseId { get; private set; }
    public Guid? SupportReplyDraftId { get; private set; }
    public string Category { get; private set; } = null!;
    public string QuestionSummary { get; private set; } = null!;
    public string MissingInformationSummary { get; private set; } = null!;
    public string? RetrievalSourceSummary { get; private set; }
    public int FrequencyCount { get; private set; }
    public string Status { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? ResolvedUtc { get; private set; }
    public Guid? LinkedTaskId { get; private set; }

    public void Increment()
    {
        FrequencyCount++;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void LinkTask(Guid taskId)
    {
        LinkedTaskId = taskId == Guid.Empty ? throw new ArgumentException("TaskId is required.", nameof(taskId)) : taskId;
        Status = SupportKnowledgeGapStatuses.LinkedToTask;
        UpdatedUtc = DateTime.UtcNow;
    }
}

public static class SupportCaseStatuses
{
    public const string New = "new";
    public const string Triaged = "triaged";
    public const string WaitingForCustomer = "waiting_for_customer";
    public const string WaitingInternal = "waiting_internal";
    public const string Escalated = "escalated";
    public const string AwaitingApproval = "awaiting_approval";
    public const string Resolved = "resolved";
    public const string Reopened = "reopened";
    public const string Closed = "closed";
    public static string Normalize(string value) => NormalizeKnownForSupport(value, [New, Triaged, WaitingForCustomer, WaitingInternal, Escalated, AwaitingApproval, Resolved, Reopened, Closed], nameof(value));
    public static string NormalizeKnownForSupport(string value, IReadOnlyCollection<string> known, string name)
    {
        var normalized = SupportEntityText.NormalizeRequired(value, name, 80).Trim().ToLowerInvariant().Replace(' ', '_');
        return known.Contains(normalized, StringComparer.OrdinalIgnoreCase) ? normalized : throw new ArgumentException($"Unsupported value '{value}'.", name);
    }
}

public static class SupportPriorities
{
    public const string Low = "low";
    public const string Normal = "normal";
    public const string High = "high";
    public const string Urgent = "urgent";
    public static string Normalize(string value) => SupportCaseStatuses.NormalizeKnownForSupport(value, [Low, Normal, High, Urgent], nameof(value));
}

public static class SupportCaseCategories
{
    public const string GeneralQuestion = "general_question";
    public const string Billing = "billing";
    public const string Refund = "refund";
    public const string TechnicalIssue = "technical_issue";
    public const string AccountAccess = "account_access";
    public const string Delivery = "delivery";
    public const string Complaint = "complaint";
    public const string FeatureRequest = "feature_request";
    public const string BugReport = "bug_report";
    public const string ChurnRisk = "churn_risk";
    public static string Normalize(string value) => SupportCaseStatuses.NormalizeKnownForSupport(value, [GeneralQuestion, Billing, Refund, TechnicalIssue, AccountAccess, Delivery, Complaint, FeatureRequest, BugReport, ChurnRisk], nameof(value));
}

public static class SupportCaseEventTypes
{
    public const string Created = "created";
    public const string MessageReceived = "message_received";
    public const string Triaged = "triaged";
    public const string Assigned = "assigned";
    public const string StatusChanged = "status_changed";
    public const string PriorityChanged = "priority_changed";
    public const string ReplyDrafted = "reply_drafted";
    public const string ReplySent = "reply_sent";
    public const string Escalated = "escalated";
    public const string ApprovalRequested = "approval_requested";
    public const string ApprovalResolved = "approval_resolved";
    public const string InternalTaskCreated = "internal_task_created";
    public const string Resolved = "resolved";
    public const string Reopened = "reopened";
    public const string Closed = "closed";
    public const string SlaRisk = "sla_risk";
    public const string SlaBreached = "sla_breached";
    public static string Normalize(string value) => SupportCaseStatuses.NormalizeKnownForSupport(value, [Created, MessageReceived, Triaged, Assigned, StatusChanged, PriorityChanged, ReplyDrafted, ReplySent, Escalated, ApprovalRequested, ApprovalResolved, InternalTaskCreated, Resolved, Reopened, Closed, SlaRisk, SlaBreached], nameof(value));
}

public static class SupportMessageDirections
{
    public const string Inbound = "inbound";
    public const string Outbound = "outbound";
    public const string Internal = "internal";
    public static string Normalize(string value) => SupportCaseStatuses.NormalizeKnownForSupport(value, [Inbound, Outbound, Internal], nameof(value));
}

public static class SupportReplyDraftStatuses
{
    public const string Draft = "draft";
    public const string NeedsReview = "needs_review";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Superseded = "superseded";
}

public static class SupportRefundRequestStatuses
{
    public const string PendingApproval = "pending_approval";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Executed = "executed";
    public const string Failed = "failed";
}

public static class SupportKnowledgeGapStatuses
{
    public const string Open = "open";
    public const string LinkedToTask = "linked_to_task";
    public const string Resolved = "resolved";
    public const string Ignored = "ignored";
}

internal static class SupportEntityText
{
    public static void EnsureCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }
    }

    public static Guid? NormalizeOptionalId(Guid? value, string name) =>
        value == Guid.Empty ? throw new ArgumentException($"{name} cannot be empty.", name) : value;

    public static DateTime NormalizeUtc(DateTime value, string name) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    public static string NormalizeRequired(string value, string name, int maxLength)
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

    public static string? NormalizeOptional(string? value, string name, int maxLength)
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
}
