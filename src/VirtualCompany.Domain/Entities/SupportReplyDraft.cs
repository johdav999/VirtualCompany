using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Entities;
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
        DeliveryStatus = SupportReplyDeliveryStatuses.Pending;
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
    public string? SafetyDecision { get; private set; }
    public string? SafetyReasonCodesJson { get; private set; }
    public string? SafetyPolicyVersion { get; private set; }
    public DateTime? SafetyEvaluatedUtc { get; private set; }
    public Guid? CreatedByAgentId { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTime? ApprovedUtc { get; private set; }
    public DateTime? SentUtc { get; private set; }
    public string DeliveryStatus { get; private set; } = null!;
    public DateTime? LastDeliveryAttemptUtc { get; private set; }
    public string? SendFailureSummary { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public SupportCase SupportCase { get; private set; } = null!;

    public void Edit(string body, string tone)
    {
        if (SentUtc.HasValue || Status is SupportReplyDraftStatuses.Approved or SupportReplyDraftStatuses.Rejected or SupportReplyDraftStatuses.Superseded)
        {
            throw new InvalidOperationException("Only an unsent draft awaiting review can be edited.");
        }
        DraftBody = SupportEntityText.NormalizeRequired(body, nameof(body), 8000);
        Tone = SupportEntityText.NormalizeRequired(tone, nameof(tone), 80);
        Status = SupportReplyDraftStatuses.Draft;
        SafetyDecision = null;
        SafetyReasonCodesJson = null;
        SafetyPolicyVersion = null;
        SafetyEvaluatedUtc = null;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void Approve(Guid userId)
    {
        if (SentUtc.HasValue || Status is SupportReplyDraftStatuses.Rejected or SupportReplyDraftStatuses.Superseded)
        {
            throw new InvalidOperationException("This draft can no longer be approved.");
        }
        ApprovedByUserId = userId == Guid.Empty ? throw new ArgumentException("UserId is required.", nameof(userId)) : userId;
        ApprovedUtc = DateTime.UtcNow;
        Status = SupportReplyDraftStatuses.Approved;
        UpdatedUtc = ApprovedUtc.Value;
    }

    public void Reject()
    {
        if (SentUtc.HasValue || Status == SupportReplyDraftStatuses.Superseded)
        {
            throw new InvalidOperationException("A sent or superseded draft cannot be rejected.");
        }
        Status = SupportReplyDraftStatuses.Rejected;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkSent(DateTime sentUtc)
    {
        SentUtc = SupportEntityText.NormalizeUtc(sentUtc, nameof(sentUtc));
        DeliveryStatus = SupportReplyDeliveryStatuses.Sent;
        LastDeliveryAttemptUtc = SentUtc;
        SendFailureSummary = null;
        UpdatedUtc = SentUtc.Value;
    }

    public void MarkSendFailed(string failureSummary)
    {
        DeliveryStatus = SupportReplyDeliveryStatuses.Failed;
        LastDeliveryAttemptUtc = DateTime.UtcNow;
        SendFailureSummary = SupportEntityText.NormalizeRequired(failureSummary, nameof(failureSummary), 1000);
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkDeliveryReconciliationRequired(string failureSummary, DateTime attemptedUtc)
    {
        DeliveryStatus = SupportReplyDeliveryStatuses.ReconciliationRequired;
        LastDeliveryAttemptUtc = SupportEntityText.NormalizeUtc(attemptedUtc, nameof(attemptedUtc));
        SendFailureSummary = SupportEntityText.NormalizeRequired(failureSummary, nameof(failureSummary), 1000);
        UpdatedUtc = LastDeliveryAttemptUtc.Value;
    }

    public void RecordSafetyDecision(string decision, string reasonCodesJson, string policyVersion, DateTime evaluatedUtc)
    {
        SafetyDecision = SupportEntityText.NormalizeRequired(decision, nameof(decision), 40);
        SafetyReasonCodesJson = SupportEntityText.NormalizeRequired(reasonCodesJson, nameof(reasonCodesJson), 1000);
        SafetyPolicyVersion = SupportEntityText.NormalizeRequired(policyVersion, nameof(policyVersion), 40);
        SafetyEvaluatedUtc = SupportEntityText.NormalizeUtc(evaluatedUtc, nameof(evaluatedUtc));
        UpdatedUtc = DateTime.UtcNow;
    }
}
