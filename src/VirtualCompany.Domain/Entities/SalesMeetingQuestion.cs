using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class SalesMeetingQuestion : ICompanyOwnedEntity
{
    private SalesMeetingQuestion() { }
    public SalesMeetingQuestion(Guid id, Guid companyId, Guid sessionId, Guid clientQuestionId, long sequence,
        Guid agentId, string questionText, SalesMeetingSpeakerType askerType, string? askerLabel,
        SalesMeetingInputSource inputSource, Guid? visibleSlideId, long presentationVersion,
        Guid askedByUserId, DateTime askedUtc)
    {
        SalesMeetingTranscriptSegment.EnsureIds(companyId, sessionId, clientQuestionId, agentId, askedByUserId);
        if (visibleSlideId == Guid.Empty) throw new ArgumentException("VisibleSlideId cannot be empty.", nameof(visibleSlideId));
        if (presentationVersion < 1) throw new ArgumentOutOfRangeException(nameof(presentationVersion));
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; SessionId = sessionId;
        ClientQuestionId = clientQuestionId; Sequence = SalesMeetingTranscriptSegment.Positive(sequence, nameof(sequence)); AgentId = agentId;
        QuestionText = SalesMeetingTranscriptSegment.Required(questionText, nameof(questionText), 2000);
        AskerType = askerType; _ = askerType.ToStorageValue(); AskerLabel = SalesMeetingTranscriptSegment.Optional(askerLabel, 160);
        InputSource = inputSource; _ = inputSource.ToStorageValue(); VisibleSlideId = visibleSlideId; PresentationVersion = presentationVersion;
        AskedByUserId = askedByUserId; AskedUtc = SalesMeetingTranscriptSegment.Utc(askedUtc);
        Status = SalesMeetingQuestionStatus.Pending; Visibility = SalesMeetingAnswerVisibility.Private;
        ReviewState = SalesMeetingReviewState.Unreviewed; CreatedUtc = AskedUtc; UpdatedUtc = AskedUtc; ConcurrencyVersion = 1;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid ClientQuestionId { get; private set; }
    public long Sequence { get; private set; }
    public Guid AgentId { get; private set; }
    public string QuestionText { get; private set; } = null!;
    public string? AnswerText { get; private set; }
    public SalesMeetingSpeakerType AskerType { get; private set; }
    public string? AskerLabel { get; private set; }
    public SalesMeetingInputSource InputSource { get; private set; }
    public Guid? VisibleSlideId { get; private set; }
    public long PresentationVersion { get; private set; }
    public SalesMeetingQuestionStatus Status { get; private set; }
    public decimal? Confidence { get; private set; }
    public bool FollowUpRequired { get; private set; }
    public SalesMeetingReviewState ReviewState { get; private set; }
    public SalesMeetingAnswerVisibility Visibility { get; private set; }
    public Guid? AiRunId { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public Guid AskedByUserId { get; private set; }
    public Guid? StageApprovedByUserId { get; private set; }
    public DateTime AskedUtc { get; private set; }
    public DateTime? AnsweredUtc { get; private set; }
    public DateTime? StageApprovedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long ConcurrencyVersion { get; private set; }
    public SalesMeetingSession Session { get; private set; } = null!;
    public Agent Agent { get; private set; } = null!;
    public SalesPresentationSlide? VisibleSlide { get; private set; }
    public ICollection<SalesMeetingQuestionEvidence> Evidence { get; } = new List<SalesMeetingQuestionEvidence>();

    public void MarkAnswering(DateTime nowUtc) { Status = SalesMeetingQuestionStatus.Answering; FailureCode = null; FailureSummary = null; Touch(nowUtc); }
    public void Complete(string answer, decimal confidence, bool followUpRequired, Guid runId, bool verified, DateTime nowUtc)
    {
        AnswerText = SalesMeetingTranscriptSegment.Required(answer, nameof(answer), 8000);
        Confidence = SalesMeetingTranscriptSegment.ConfidenceValue(confidence); FollowUpRequired = followUpRequired;
        AiRunId = runId == Guid.Empty ? null : runId; Status = verified ? SalesMeetingQuestionStatus.Completed : SalesMeetingQuestionStatus.Unverified;
        AnsweredUtc = SalesMeetingTranscriptSegment.Utc(nowUtc); FailureCode = null; FailureSummary = null; Touch(nowUtc);
    }
    public void Fail(string code, string summary, bool cancelled, DateTime nowUtc)
    {
        FailureCode = SalesMeetingTranscriptSegment.Required(code, nameof(code), 100);
        FailureSummary = SalesMeetingTranscriptSegment.Required(summary, nameof(summary), 1000);
        Status = cancelled ? SalesMeetingQuestionStatus.Cancelled : SalesMeetingQuestionStatus.Failed;
        FollowUpRequired = true; AnswerText = null; AnsweredUtc = null; Touch(nowUtc);
    }
    public void ApproveForStage(Guid actorUserId, long expectedVersion, DateTime nowUtc)
    {
        if (expectedVersion != ConcurrencyVersion) throw new InvalidOperationException("The question changed after it was opened.");
        if (Status != SalesMeetingQuestionStatus.Completed) throw new InvalidOperationException("Only a verified completed answer can be shared to the stage.");
        SalesMeetingTranscriptSegment.EnsureIds(actorUserId); Visibility = SalesMeetingAnswerVisibility.ApprovedForStage;
        ReviewState = SalesMeetingReviewState.Reviewed; StageApprovedByUserId = actorUserId;
        StageApprovedUtc = SalesMeetingTranscriptSegment.Utc(nowUtc); Touch(nowUtc);
    }
    private void Touch(DateTime nowUtc) { UpdatedUtc = SalesMeetingTranscriptSegment.Utc(nowUtc); ConcurrencyVersion++; }
}

public sealed class SalesMeetingQuestionEvidence : ICompanyOwnedEntity
{
    private SalesMeetingQuestionEvidence() { }
    public SalesMeetingQuestionEvidence(Guid id, Guid companyId, Guid questionId, int claimOrder, string claimText,
        string claimType, decimal confidence, string sourceId, string sourceType, string sourceTitle, DateTime createdUtc)
    {
        SalesMeetingTranscriptSegment.EnsureIds(companyId, questionId); if (claimOrder < 0) throw new ArgumentOutOfRangeException(nameof(claimOrder));
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; QuestionId = questionId; ClaimOrder = claimOrder;
        ClaimText = SalesMeetingTranscriptSegment.Required(claimText, nameof(claimText), 4000);
        ClaimType = SalesMeetingTranscriptSegment.Required(claimType, nameof(claimType), 100);
        Confidence = SalesMeetingTranscriptSegment.ConfidenceValue(confidence) ?? 0;
        SourceId = SalesMeetingTranscriptSegment.Required(sourceId, nameof(sourceId), 500);
        SourceType = SalesMeetingTranscriptSegment.Required(sourceType, nameof(sourceType), 100);
        SourceTitle = SalesMeetingTranscriptSegment.Required(sourceTitle, nameof(sourceTitle), 500);
        CreatedUtc = SalesMeetingTranscriptSegment.Utc(createdUtc);
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid QuestionId { get; private set; }
    public int ClaimOrder { get; private set; }
    public string ClaimText { get; private set; } = null!;
    public string ClaimType { get; private set; } = null!;
    public decimal Confidence { get; private set; }
    public string SourceId { get; private set; } = null!;
    public string SourceType { get; private set; } = null!;
    public string SourceTitle { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public SalesMeetingQuestion Question { get; private set; } = null!;
}
