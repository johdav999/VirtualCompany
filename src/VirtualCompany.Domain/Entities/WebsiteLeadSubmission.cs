namespace VirtualCompany.Domain.Entities;
public sealed class WebsiteLeadSubmission : ICompanyOwnedEntity
{
    private WebsiteLeadSubmission() { }

    public WebsiteLeadSubmission(
        Guid id,
        Guid companyId,
        string normalizedEmail,
        string? name,
        string? companyName,
        string? message,
        string? sourceUrl,
        string? formId,
        string? phone = null,
        string? externalSubmissionId = null,
        string? sourceMetadataJson = null)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        NormalizedEmail = SalesEntityText.NormalizeRequired(normalizedEmail, nameof(normalizedEmail), 256).ToLowerInvariant();
        Name = SalesEntityText.NormalizeOptional(name, nameof(name), 160);
        CompanyName = SalesEntityText.NormalizeOptional(companyName, nameof(companyName), 200);
        Message = SalesEntityText.NormalizeOptional(message, nameof(message), 2000);
        SourceUrl = SalesEntityText.NormalizeOptional(sourceUrl, nameof(sourceUrl), 512);
        FormId = SalesEntityText.NormalizeOptional(formId, nameof(formId), 120);
        Phone = SalesEntityText.NormalizeOptional(phone, nameof(phone), 64);
        ExternalSubmissionId = SalesEntityText.NormalizeOptional(externalSubmissionId, nameof(externalSubmissionId), 256);
        SourceMetadataJson = SalesEntityText.NormalizeOptional(sourceMetadataJson, nameof(sourceMetadataJson), 8000);
        DeduplicationDecision = "new";
        SequenceEnrollmentStatus = SalesStatuses.Pending;
        Status = SalesStatuses.Open;
        ReceivedUtc = DateTime.UtcNow;
        CreatedUtc = ReceivedUtc;
        UpdatedUtc = CreatedUtc;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? LeadId { get; private set; }
    public Guid? ContactId { get; private set; }
    public Guid? MergedIntoSubmissionId { get; private set; }
    public Guid? EnrollmentOutboxMessageId { get; private set; }
    public Guid? FollowUpSequenceId { get; private set; }
    public Guid? SequenceExecutionId { get; private set; }
    public string NormalizedEmail { get; private set; } = null!;
    public string? Name { get; private set; }
    public string? CompanyName { get; private set; }
    public string? Message { get; private set; }
    public string? SourceUrl { get; private set; }
    public string? FormId { get; private set; }
    public string? Phone { get; private set; }
    public string? ExternalSubmissionId { get; private set; }
    public string? SourceMetadataJson { get; private set; }
    public string DeduplicationDecision { get; private set; } = null!;
    public string SequenceEnrollmentStatus { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public DateTime ReceivedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Lead? Lead { get; private set; }

    public void LinkLead(Guid leadId, Guid contactId)
    {
        LeadId = SalesEntityText.NormalizeOptionalId(leadId, nameof(leadId))!.Value;
        ContactId = SalesEntityText.NormalizeOptionalId(contactId, nameof(contactId))!.Value;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkMerged(Guid targetSubmissionId)
    {
        MergedIntoSubmissionId = SalesEntityText.NormalizeOptionalId(targetSubmissionId, nameof(targetSubmissionId))!.Value;
        DeduplicationDecision = "merged";
        Status = "merged";
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkExistingLeadUpdated() => DeduplicationDecision = "updated_existing_lead";

    public void MarkEnrollmentQueued(Guid outboxMessageId)
    {
        EnrollmentOutboxMessageId = SalesEntityText.NormalizeOptionalId(outboxMessageId, nameof(outboxMessageId))!.Value;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void RecordSequenceEnrollment(Guid sequenceId, Guid sequenceExecutionId)
    {
        FollowUpSequenceId = SalesEntityText.NormalizeOptionalId(sequenceId, nameof(sequenceId))!.Value;
        SequenceExecutionId = SalesEntityText.NormalizeOptionalId(sequenceExecutionId, nameof(sequenceExecutionId))!.Value;
        SequenceEnrollmentStatus = "enrolled";
        UpdatedUtc = DateTime.UtcNow;
    }
}

