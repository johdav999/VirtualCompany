namespace VirtualCompany.Application.Support;

public interface ISupportCaseService
{
    Task<SupportCaseListResponse> ListCasesAsync(Guid companyId, SupportCaseListQuery query, CancellationToken cancellationToken);
    Task<SupportCaseDetailResponse?> GetCaseAsync(Guid companyId, Guid supportCaseId, CancellationToken cancellationToken);
    Task<SupportCaseDetailResponse> CreateCaseAsync(Guid companyId, Guid userId, CreateSupportCaseRequest request, CancellationToken cancellationToken);
    Task<SupportCaseDetailResponse?> AddInternalNoteAsync(Guid companyId, Guid userId, Guid supportCaseId, AddSupportInternalNoteRequest request, CancellationToken cancellationToken);
    Task<SupportCaseDetailResponse?> ChangeStatusAsync(Guid companyId, Guid userId, Guid supportCaseId, ChangeSupportStatusRequest request, CancellationToken cancellationToken);
    Task<SupportCaseDetailResponse?> ChangePriorityAsync(Guid companyId, Guid userId, Guid supportCaseId, ChangeSupportPriorityRequest request, CancellationToken cancellationToken);
    Task<SupportCaseDetailResponse?> ChangeCategoryAsync(Guid companyId, Guid userId, Guid supportCaseId, ChangeSupportCategoryRequest request, CancellationToken cancellationToken);
    Task<SupportCaseDetailResponse?> AssignAsync(Guid companyId, Guid userId, Guid supportCaseId, AssignSupportCaseRequest request, CancellationToken cancellationToken);
    Task<SupportCaseDetailResponse?> ResolveAsync(Guid companyId, Guid userId, Guid supportCaseId, ResolveSupportCaseRequest request, CancellationToken cancellationToken);
    Task<SupportCaseDetailResponse?> ReopenAsync(Guid companyId, Guid userId, Guid supportCaseId, SupportActionRequest request, CancellationToken cancellationToken);
    Task<SupportCaseDetailResponse?> CloseAsync(Guid companyId, Guid userId, Guid supportCaseId, SupportActionRequest request, CancellationToken cancellationToken);
}

public interface ISupportMailboxIngestionService
{
    Task<SupportMailboxIngestionResult> IngestMessageAsync(Guid companyId, SupportMailboxMessageInput input, CancellationToken cancellationToken);
}

public interface ISupportContextResolutionService
{
    Task<SupportCaseContextSummary> ResolveAsync(Guid companyId, Guid supportCaseId, CancellationToken cancellationToken);
}

public interface ISupportTriageService
{
    Task<SupportTriageResult?> TriageAsync(Guid companyId, Guid userId, Guid supportCaseId, CancellationToken cancellationToken);
}

public interface ISupportReplyDraftService
{
    Task<SupportReplyDraftDto?> GenerateDraftAsync(Guid companyId, Guid userId, Guid supportCaseId, GenerateSupportReplyDraftRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<SupportReplyDraftDto>> ListDraftsAsync(Guid companyId, Guid supportCaseId, CancellationToken cancellationToken);
    Task<SupportReplyDraftDto?> EditDraftAsync(Guid companyId, Guid userId, Guid draftId, EditSupportReplyDraftRequest request, CancellationToken cancellationToken);
    Task<SupportReplyDraftDto?> ApproveDraftAsync(Guid companyId, Guid userId, Guid draftId, SupportActionRequest request, CancellationToken cancellationToken);
    Task<SupportReplyDraftDto?> RejectDraftAsync(Guid companyId, Guid userId, Guid draftId, SupportActionRequest request, CancellationToken cancellationToken);
    Task<SupportCaseDetailResponse?> SendDraftAsync(Guid companyId, Guid userId, Guid draftId, SendSupportReplyDraftRequest request, CancellationToken cancellationToken);
}

public interface ISupportOutboundEmailSender
{
    Task<SupportOutboundEmailSendResult> SendReplyAsync(SupportOutboundEmailSendRequest request, CancellationToken cancellationToken);
}

public interface ISupportToolActionService
{
    Task<SupportToolActionResult> ExecuteAsync(Guid companyId, Guid agentId, SupportToolActionRequest request, CancellationToken cancellationToken);
}

public interface ISupportRefundWorkflowService
{
    Task<SupportRefundRequestDto?> RequestRefundAsync(Guid companyId, Guid userId, Guid supportCaseId, CreateSupportRefundRequest request, CancellationToken cancellationToken);
}

public interface ISupportSlaMonitor
{
    Task<SupportSlaMonitorResult> RunAsync(DateTime nowUtc, CancellationToken cancellationToken);
}

public interface ISupportKnowledgeGapService
{
    Task<SupportKnowledgeGapDto> CreateOrIncrementAsync(Guid companyId, CreateSupportKnowledgeGapRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<SupportKnowledgeGapDto>> ListAsync(Guid companyId, string? status, CancellationToken cancellationToken);
    Task<SupportKnowledgeGapDto?> CreateDocumentationTaskAsync(Guid companyId, Guid userId, Guid knowledgeGapId, CancellationToken cancellationToken);
}

public interface ISupportAnalyticsService
{
    Task<SupportAnalyticsDashboardResponse> GetDashboardAsync(Guid companyId, CancellationToken cancellationToken);
}

public interface ISupportMemoryUpdateService
{
    Task UpdateFromResolvedCaseAsync(Guid companyId, Guid supportCaseId, CancellationToken cancellationToken);
}


public interface ISupportKnowledgeContextProvider
{
    Task<SupportKnowledgeContext> RetrieveAsync(Guid companyId, Guid supportCaseId, CancellationToken cancellationToken);
}

public interface ISupportMailboxRoutingService
{
    Task<SupportMailboxRoutingResult> RouteUnlinkedInboundMessagesAsync(DateTime sinceUtc, int batchSize, CancellationToken cancellationToken);
}

public sealed record SupportKnowledgeContext(
    Guid SupportCaseId,
    IReadOnlyList<SupportKnowledgeSourceReference> Sources,
    IReadOnlyList<string> CustomerMemorySummaries,
    IReadOnlyList<string> SimilarCaseSummaries,
    decimal RetrievalConfidence,
    string RationaleSummary)
{
    public bool HasGrounding => Sources.Count > 0 || CustomerMemorySummaries.Count > 0 || SimilarCaseSummaries.Count > 0;
}

public sealed record SupportKnowledgeSourceReference(
    string Type,
    string Label,
    Guid? EntityId,
    string? Excerpt,
    decimal Relevance);

public sealed record SupportMailboxRoutingResult(int MessagesScanned, int MessagesRouted, int CasesCreated, int DuplicatesSkipped);

public sealed class SupportOperationsWorkerOptions
{
    public const string SectionName = "Support:OperationsWorker";
    public bool Enabled { get; set; } = true;
    public int PollSeconds { get; set; } = 60;
    public int MailboxLookbackMinutes { get; set; } = 120;
    public int MailboxBatchSize { get; set; } = 50;
}
public sealed class SupportValidationException : Exception
{
    public SupportValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("The support request is invalid.")
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public static void ThrowIfEmpty(Guid value, string field)
    {
        if (value == Guid.Empty)
        {
            throw new SupportValidationException(new Dictionary<string, string[]> { [field] = ["This field is required."] });
        }
    }

    public static void ThrowIfBlank(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new SupportValidationException(new Dictionary<string, string[]> { [field] = ["This field is required."] });
        }
    }
}

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
    int Skip = 0,
    int Take = 50);

public sealed record SupportCaseListResponse(
    IReadOnlyList<SupportCaseListItem> Items,
    int TotalCount,
    SupportCaseSummaryCounts Summary);

public sealed record SupportCaseSummaryCounts(
    int Open,
    int AwaitingApproval,
    int Escalated,
    int SlaRisk,
    int SlaBreached,
    int ResolvedToday);

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
    bool IsVipRisk);

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
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    IReadOnlyList<SupportMessageDto> Messages,
    IReadOnlyList<SupportCaseEventDto> Events,
    IReadOnlyList<SupportReplyDraftDto> ReplyDrafts,
    IReadOnlyList<SupportRefundRequestDto> RefundRequests,
    IReadOnlyList<SupportKnowledgeGapDto> KnowledgeGaps,
    SupportCaseContextSummary Context);

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

public sealed record SupportCaseEventDto(
    Guid Id,
    string EventType,
    string EventLabel,
    string Summary,
    string ActorType,
    Guid? ActorId,
    DateTime OccurredUtc);

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
    DateTime UpdatedUtc);

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
    string Status,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

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
    Guid? LinkedTaskId);

public sealed record SupportCaseContextSummary(
    Guid SupportCaseId,
    string? CustomerName,
    string? ContactName,
    string? ContactEmail,
    IReadOnlyList<SupportContextReference> References,
    decimal MatchConfidence,
    string MatchRationale);

public sealed record SupportContextReference(
    string Type,
    string Label,
    Guid? EntityId,
    string? SecondaryText = null);

public sealed record CreateSupportCaseRequest(
    string Subject,
    string? Description,
    string? Source,
    string? SenderEmail = null,
    Guid? ContactId = null,
    Guid? CustomerCompanyId = null);

public sealed record AddSupportInternalNoteRequest(string Body);
public sealed record ChangeSupportStatusRequest(string Status, string? Note = null);
public sealed record ChangeSupportPriorityRequest(string Priority, string? Note = null);
public sealed record ChangeSupportCategoryRequest(string Category, string? Note = null);
public sealed record AssignSupportCaseRequest(Guid? AssignedAgentId, Guid? AssignedUserId, string? Reason = null);
public sealed record ResolveSupportCaseRequest(string Summary, string Outcome);
public sealed record SupportActionRequest(string? Note = null);
public sealed record GenerateSupportReplyDraftRequest(string? Tone = null, bool ForceReview = false);
public sealed record EditSupportReplyDraftRequest(string DraftBody, string Tone);
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
public sealed record CreateSupportRefundRequest(decimal Amount, string Currency, string ReasonCode, string Explanation, Guid? InvoiceId = null, Guid? PaymentId = null);
public sealed record CreateSupportKnowledgeGapRequest(Guid? SupportCaseId, Guid? SupportReplyDraftId, string Category, string QuestionSummary, string MissingInformationSummary, string? RetrievalSourceSummary = null);

public sealed record SupportOutboundEmailSendRequest(
    Guid CompanyId,
    Guid SupportCaseId,
    Guid ReplyDraftId,
    Guid? MailboxConnectionId,
    string ToEmail,
    string? ToDisplayName,
    string Subject,
    string BodyText,
    string OriginalMessageId,
    string? ProviderThreadId,
    string? InternetMessageId,
    string IdempotencyKey);

public sealed record SupportOutboundEmailSendResult(
    string Provider,
    Guid MailboxConnectionId,
    string ProviderMessageId,
    string? ProviderThreadId,
    string Status);

public sealed record SupportMailboxMessageInput(
    Guid? MailboxConnectionId,
    Guid? EmailMessageSnapshotId,
    string SenderEmail,
    string? SenderName,
    string? RecipientEmail,
    string Subject,
    string Body,
    string? ProviderMessageId,
    string? ProviderThreadId,
    DateTime OccurredUtc);

public sealed record SupportMailboxIngestionResult(Guid SupportCaseId, Guid SupportMessageId, bool CreatedCase, bool Deduplicated);

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

public sealed record SupportToolActionRequest(string ToolName, Guid? SupportCaseId, IReadOnlyDictionary<string, string?> Payload, bool Autonomous = false);
public sealed record SupportToolActionResult(bool Succeeded, string Status, string Summary, Guid? SupportCaseId, Guid? CreatedEntityId = null);
public sealed record SupportSlaMonitorResult(int CasesScanned, int RisksCreated, int BreachesCreated, int NotificationsCreated);

public sealed record SupportAnalyticsDashboardResponse(
    SupportCaseSummaryCounts Summary,
    IReadOnlyList<SupportMetricBucket> ByStatus,
    IReadOnlyList<SupportMetricBucket> ByCategory,
    IReadOnlyList<SupportMetricBucket> ByPriority,
    IReadOnlyList<SupportRootCauseInsight> Insights);

public sealed record SupportMetricBucket(string Key, string Label, int Count);
public sealed record SupportRootCauseInsight(string Title, string Summary, string Category, int CaseCount, string SuggestedAction);

public static class SupportLabels
{
    public static string Status(string value) => value switch
    {
        "new" => "New",
        "triaged" => "Triaged",
        "waiting_for_customer" => "Waiting for customer",
        "waiting_internal" => "Waiting internally",
        "escalated" => "Escalated",
        "awaiting_approval" => "Awaiting approval",
        "resolved" => "Resolved",
        "reopened" => "Reopened",
        "closed" => "Closed",
        _ => ToTitle(value)
    };

    public static string Priority(string value) => value switch
    {
        "low" => "Low",
        "normal" => "Normal",
        "high" => "High",
        "urgent" => "Urgent",
        _ => ToTitle(value)
    };

    public static string Category(string value) => value switch
    {
        "general_question" => "General question",
        "billing" => "Billing",
        "refund" => "Refund",
        "technical_issue" => "Technical issue",
        "account_access" => "Account access",
        "delivery" => "Delivery",
        "complaint" => "Complaint",
        "feature_request" => "Feature request",
        "bug_report" => "Bug report",
        "churn_risk" => "Churn risk",
        _ => ToTitle(value)
    };

    public static string Event(string value) => ToTitle(value);
    public static string DraftStatus(string value) => ToTitle(value);
    public static string KnowledgeGapStatus(string value) => ToTitle(value);

    private static string ToTitle(string value) =>
        string.Join(' ', value.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}

