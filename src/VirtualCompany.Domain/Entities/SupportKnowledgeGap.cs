using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Entities;
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
    public Guid? LinkedKnowledgeDocumentId { get; private set; }

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

    public void Resolve(Guid knowledgeDocumentId)
    {
        LinkedKnowledgeDocumentId = knowledgeDocumentId == Guid.Empty ? throw new ArgumentException("KnowledgeDocumentId is required.", nameof(knowledgeDocumentId)) : knowledgeDocumentId;
        Status = SupportKnowledgeGapStatuses.Resolved;
        ResolvedUtc = DateTime.UtcNow;
        UpdatedUtc = ResolvedUtc.Value;
    }

    public void Reopen()
    {
        if (Status != SupportKnowledgeGapStatuses.Resolved) throw new InvalidOperationException("Only resolved knowledge gaps can be reopened.");
        Status = LinkedTaskId.HasValue ? SupportKnowledgeGapStatuses.LinkedToTask : SupportKnowledgeGapStatuses.Open;
        LinkedKnowledgeDocumentId = null;
        ResolvedUtc = null;
        UpdatedUtc = DateTime.UtcNow;
    }
}

