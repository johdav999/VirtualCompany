using System.Text.Json.Nodes;
using VirtualCompany.Domain.ValueObjects;

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
        DateTime? createdUtc = null,
        string? conversationLanguage = null)
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
        ConversationLanguage = CommunicationLanguageTag.NormalizeOptional(conversationLanguage, nameof(conversationLanguage));
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
    public string? ConversationLanguage { get; private set; }
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

    public void SetConversationLanguage(string? language)
    {
        ConversationLanguage = CommunicationLanguageTag.NormalizeOptional(language, nameof(language));
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
