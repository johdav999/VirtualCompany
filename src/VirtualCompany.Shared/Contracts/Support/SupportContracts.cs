

namespace VirtualCompany.Application.Support;

public sealed record SupportActionRequest(string? Note = null);

public sealed record ResolveSupportKnowledgeGapRequest(Guid KnowledgeDocumentId);


public sealed record SupportTriageResult(
    Guid SupportCaseId,
    string Category,
    string Priority,
    string Sentiment,
    decimal Confidence,
    string SuggestedNextAction,
    string RationaleSummary,
    bool IsVipRisk,
    bool IsChurnRisk,
    bool IsSlaRisk);


public sealed record SupportCaseContextSummary(
    Guid SupportCaseId,
    string? CustomerName,
    string? ContactName,
    string? ContactEmail,
    IReadOnlyList<SupportContextReference> References,
    decimal MatchConfidence,
    string MatchRationale);


public sealed record SupportCaseListQuery(
    string? Status = null,
    string? Priority = null,
    string? Category = null,
    Guid? AssignedAgentId = null,
    Guid? AssignedUserId = null,
    Guid? ContactId = null,
    Guid? CustomerCompanyId = null,
    string? Search = null,
    bool? SlaRisk = null,
    DateTime? CreatedFromUtc = null,
    DateTime? CreatedToUtc = null,
    bool OpenOnly = false,
    bool ResolvedToday = false,
    bool? Unassigned = null,
    bool AssignedToMe = false,
    bool? SlaBreached = null,
    bool WaitingTooLong = false,
    bool FailedReply = false,
    string? SortBy = null,
    string? SortDirection = null,
    int Skip = 0,
    int Take = 50);


public sealed record SupportCaseListResponse(
    IReadOnlyList<SupportCaseListItem> Items,
    int TotalCount,
    SupportCaseSummaryCounts Summary);


public sealed record SupportKnowledgeGapDto(
    Guid Id,
    Guid? SupportCaseId,
    Guid? SupportReplyDraftId,
    string Category,
    string CategoryLabel,
    string QuestionSummary,
    string MissingInformationSummary,
    string? RetrievalSourceSummary,
    int FrequencyCount,
    string Status,
    string StatusLabel,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    Guid? LinkedTaskId,
    Guid? LinkedKnowledgeDocumentId);


public sealed record SupportMemoryObservationDto(
    Guid Id,
    Guid SupportCaseId,
    Guid SupportCaseResolutionId,
    Guid ContactId,
    Guid? CustomerMemoryProfilePreferenceId,
    string Status,
    string StatusLabel,
    string? Value,
    string EvidenceSummary,
    decimal Confidence,
    DateTime ObservedUtc,
    DateTime? ValidUntilUtc,
    string PolicyVersion,
    string SourceEventKey,
    DateTime UpdatedUtc,
    IReadOnlyList<string> AllowedActions);


public sealed record CreateSupportCaseRequest(
    string Subject,
    string? Description,
    string? Source,
    string? SenderEmail = null,
    Guid? ContactId = null,
    Guid? CustomerCompanyId = null,
    string? ConversationLanguage = null);


public sealed record SupportKnowledgeContext(
    Guid SupportCaseId,
    IReadOnlyList<SupportKnowledgeSourceReference> Sources,
    IReadOnlyList<string> CustomerMemorySummaries,
    IReadOnlyList<string> SimilarCaseSummaries,
    decimal RetrievalConfidence,
    string RationaleSummary)
{
    public bool HasTrustedGrounding => Sources.Any(x =>
        x.Type is "knowledge_chunk" or "business_record" &&
        x.IsTrusted &&
        x.Relevance >= 0.55m);
}


public sealed record SupportCaseEventDto(
    Guid Id,
    string EventType,
    string EventLabel,
    string Summary,
    string ActorType,
    Guid? ActorId,
    DateTime OccurredUtc);


public sealed record SupportCaseListItem(
    Guid Id,
    string CaseNumber,
    string Subject,
    string Status,
    string StatusLabel,
    string Priority,
    string PriorityLabel,
    string Category,
    string CategoryLabel,
    string Source,
    string? CustomerName,
    string? ContactName,
    string? ContactEmail,
    Guid? AssignedAgentId,
    Guid? AssignedUserId,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    DateTime? FirstResponseDueUtc,
    DateTime? ResolutionDueUtc,
    bool IsSlaRisk,
    bool IsSlaBreached,
    bool IsChurnRisk,
    bool IsVipRisk,
    string? ConversationLanguage = null);


public sealed record SupportContextReference(
    string Type,
    string Label,
    Guid? EntityId,
    string? SecondaryText = null);

public sealed record ResolveSupportCaseRequest(string Summary, string Outcome, string RootCauseCategory = "other", string? ActionTaken = null, string? ReusableAnswer = null, string? CustomerPreferenceObservations = null, IReadOnlyList<Guid>? RelevantEntityIds = null, bool ReuseEligible = false);

public sealed record GenerateSupportReplyDraftRequest(string? Tone = null, bool ForceReview = false);

public sealed record UpsertSupportSlaPolicyRequest(Guid? Id, string Name, string Category, string Priority, int FirstResponseMinutes, int ResolutionMinutes, string? CustomerTier = null, bool IsActive = true, string TimeBasis = "elapsed", int RiskThresholdMinutes = 240, string EscalationRecipientRole = "support_supervisor");

public sealed record SupportSlaPerformanceSummary(
    int OpenAtRisk,
    int OpenBreached,
    int FirstResponsesMet,
    int FirstResponsesMissed,
    int ResolutionsMet,
    int ResolutionsMissed,
    int MissingTargets,
    string Rationale);

public sealed record ChangeSupportStatusRequest(string Status, string? Note = null);


public sealed record SupportMessageDto(
    Guid Id,
    string Direction,
    string Channel,
    string Sender,
    string? Recipient,
    string Body,
    DateTime OccurredUtc,
    Guid? EmailMessageSnapshotId,
    string? ProviderMessageId,
    string? ProviderThreadId);

public sealed record SupportAgentExecutionDto(Guid Id, Guid SupportCaseId, Guid? AgentId, string Status, string CurrentStep, Guid? CreatedDraftId, string Summary, string? FailureSummary, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? CompletedUtc);

public sealed record ChangeSupportCategoryRequest(string Category, string? Note = null);

public sealed record RunSupportAgentRequest(string? IdempotencyKey = null, bool ForceReview = false);


public sealed record SupportCaseSummaryCounts(
    int Open,
    int AwaitingApproval,
    int Escalated,
    int SlaRisk,
    int SlaBreached,
    int ResolvedToday);

public sealed record AssignSupportCaseRequest(Guid? AssignedAgentId, Guid? AssignedUserId, string? Reason = null);

public sealed record SupportRootCauseInsight(string Title, string Summary, string Category, int CaseCount, string SuggestedAction);


public sealed record SupportRefundRequestDto(
    Guid Id,
    Guid SupportCaseId,
    decimal Amount,
    string Currency,
    string ReasonCode,
    string Explanation,
    Guid? InvoiceId,
    Guid? PaymentId,
    Guid? ApprovalRequestId,
    Guid? FinanceActionReferenceId,
    Guid? ProviderWriteRequestId,
    Guid? ProviderApprovalRequestId,
    string Status,
    string StatusLabel,
    string? LastFailureSummary,
    DateTime? ExecutionRequestedUtc,
    DateTime? CompletedUtc,
    IReadOnlyList<string> AllowedActions,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);


public sealed record SupportSlaPolicyDto(Guid Id, string Name, string Category, string CategoryLabel, string Priority, string PriorityLabel, string? CustomerTier, int FirstResponseMinutes, int ResolutionMinutes, bool IsActive, DateTime UpdatedUtc, string TimeBasis = "elapsed", int RiskThresholdMinutes = 240, string EscalationRecipientRole = "support_supervisor");


public sealed record SupportAssigneeOptionDto(Guid Id, string Type, string DisplayName, string SecondaryText, bool Available, int OpenCaseCount);

public sealed record EditSupportReplyDraftRequest(string DraftBody, string Tone);


public sealed record SupportCaseDetailResponse(
    Guid Id,
    string CaseNumber,
    string Subject,
    string Summary,
    string? Description,
    string Status,
    string StatusLabel,
    string Priority,
    string PriorityLabel,
    string Category,
    string CategoryLabel,
    string Source,
    string? Sentiment,
    decimal? ConfidenceScore,
    string? SuggestedNextAction,
    string? RationaleSummary,
    Guid? ContactId,
    Guid? CustomerCompanyId,
    Guid? RelatedInvoiceId,
    Guid? RelatedPaymentId,
    string? CustomerName,
    string? ContactName,
    string? ContactEmail,
    Guid? AssignedAgentId,
    Guid? AssignedUserId,
    DateTime? FirstResponseDueUtc,
    DateTime? ResolutionDueUtc,
    bool IsSlaRisk,
    bool IsSlaBreached,
    bool IsChurnRisk,
    bool IsVipRisk,
    IReadOnlyList<string> AllowedActions,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    IReadOnlyList<SupportMessageDto> Messages,
    IReadOnlyList<SupportCaseEventDto> Events,
    IReadOnlyList<SupportReplyDraftDto> ReplyDrafts,
    IReadOnlyList<SupportRefundRequestDto> RefundRequests,
    IReadOnlyList<SupportKnowledgeGapDto> KnowledgeGaps,
    SupportCaseContextSummary Context,
    string? CommunicationLanguage = null,
    string? CommunicationLanguageSource = null,
    decimal? CommunicationLanguageConfidence = null,
    bool CommunicationLanguageRequiresReview = false);


public sealed record AddSupportInternalNoteRequest(string Body);

public sealed record CreateSupportRefundRequest(decimal Amount, string Currency, string ReasonCode, string Explanation, Guid? InvoiceId = null, Guid? PaymentId = null);


public sealed record SupportAnalyticsDashboardResponse(
    SupportCaseSummaryCounts Summary,
    IReadOnlyList<SupportMetricBucket> ByStatus,
    IReadOnlyList<SupportMetricBucket> ByCategory,
    IReadOnlyList<SupportMetricBucket> ByPriority,
    SupportSlaPerformanceSummary SlaPerformance,
    SupportLearningEffectivenessSummary Learning,
    IReadOnlyList<SupportRootCauseInsight> Insights);


public sealed record SupportMetricBucket(string Key, string Label, int Count);

public sealed record SupportBusinessCalendarDto(string TimeZoneId, TimeOnly WorkdayStart, TimeOnly WorkdayEnd, IReadOnlyList<DayOfWeek> WorkingDays, IReadOnlyList<DateOnly> Holidays);

public sealed record SendSupportReplyDraftRequest(
    bool ResolveAfterSend = false,
    bool Autonomous = false,
    Guid? MailboxConnectionId = null,
    string? ToEmail = null,
    string? ToDisplayName = null,
    string? Subject = null,
    string? OriginalMessageId = null,
    string? ProviderThreadId = null,
    string? InternetMessageId = null);


public sealed record SupportKnowledgeSourceReference(
    string Type,
    string Label,
    Guid? EntityId,
    string? Excerpt,
    decimal Relevance,
    bool IsTrusted = false,
    Guid? DocumentId = null,
    string? SourceReference = null);

public sealed record SupportSlaResolutionDto(Guid? PolicyId, string PolicyName, int FirstResponseMinutes, int ResolutionMinutes, DateTime FirstResponseDueUtc, DateTime ResolutionDueUtc, string Rationale, int RiskThresholdMinutes = 240, string EscalationRecipientRole = "support_supervisor");

public sealed record SupportLearningEffectivenessSummary(
    int ApprovedMemoryObservations,
    int ReviewMemoryObservations,
    int RejectedMemoryObservations,
    int DraftsUsingMemory,
    decimal? AverageAnswerabilityWithMemory,
    decimal? AverageAnswerabilityWithoutMemory,
    int ApprovedDrafts,
    int RejectedDrafts,
    int SentReplies,
    int ReopenedCases,
    string Rationale);


public sealed record SupportReplyDraftDto(
    Guid Id,
    Guid SupportCaseId,
    string DraftBody,
    string Tone,
    string Status,
    string StatusLabel,
    decimal Confidence,
    decimal Answerability,
    string? RationaleSummary,
    string? SourceReferencesJson,
    Guid? CreatedByAgentId,
    Guid? CreatedByUserId,
    Guid? ApprovedByUserId,
    DateTime? ApprovedUtc,
    DateTime? SentUtc,
    string? SendFailureSummary,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    string? SafetyDecision = null,
    string? SafetyReasonCodesJson = null,
    string? SafetyPolicyVersion = null,
    DateTime? SafetyEvaluatedUtc = null, string DeliveryStatus = "pending", DateTime? LastDeliveryAttemptUtc = null, bool DeliveryRequested = false);

public sealed record SaveSupportBusinessCalendarRequest(string TimeZoneId, TimeOnly WorkdayStart, TimeOnly WorkdayEnd, IReadOnlyList<DayOfWeek> WorkingDays, IReadOnlyList<DateOnly> Holidays);

public sealed record ChangeSupportPriorityRequest(string Priority, string? Note = null);
