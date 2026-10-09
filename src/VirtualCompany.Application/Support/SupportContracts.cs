namespace VirtualCompany.Application.Support;

using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;

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
    Task<IReadOnlyList<SupportAssigneeOptionDto>> ListAssigneesAsync(Guid companyId, CancellationToken cancellationToken);
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

public interface ISupportReplySafetyPolicy
{
    Task<SupportReplySafetyDecision> EvaluateAsync(Guid companyId, Guid supportCaseId, string draftBody, string? sourceReferencesJson, CancellationToken cancellationToken);
}

public interface ISupportOutboundEmailSender
{
    Task<SupportOutboundEmailSendResult> SendReplyAsync(SupportOutboundEmailSendRequest request, CancellationToken cancellationToken);
}

public interface ISupportToolActionService
{
    Task<SupportToolActionResult> ExecuteAsync(Guid companyId, Guid agentId, SupportToolActionRequest request, CancellationToken cancellationToken);
}

public interface ISupportAgentOrchestrationService
{
    Task<SupportAgentExecutionDto?> RunAsync(Guid companyId, Guid userId, Guid supportCaseId, RunSupportAgentRequest request, CancellationToken cancellationToken);
}

public interface ISupportRefundWorkflowService
{
    Task<SupportRefundRequestDto?> RequestRefundAsync(Guid companyId, Guid userId, Guid supportCaseId, CreateSupportRefundRequest request, CancellationToken cancellationToken);
}

public interface ISupportRefundApprovalOutcomeHandler
{
    Task<bool> ProcessAsync(Guid companyId, Guid approvalRequestId, string approvalStatus, Guid? decidedByUserId, string? decisionSummary, CancellationToken cancellationToken);
}

public interface ISupportRefundFinanceService
{
    Task<SupportRefundFinanceActionResult> CreateApprovedActionAsync(Guid companyId, Guid refundRequestId, CancellationToken cancellationToken);
    Task<SupportRefundRequestDto> RequestExecutionAsync(Guid companyId, Guid refundRequestId, Guid? actorUserId, string actorDisplayName, CancellationToken cancellationToken);
    Task<SupportRefundRequestDto?> RefreshExecutionAsync(Guid companyId, Guid financeActionReferenceId, CancellationToken cancellationToken);
    Task<SupportRefundRequestDto?> RefreshByWriteRequestAsync(Guid companyId, Guid writeRequestId, CancellationToken cancellationToken);
    Task<SupportRefundRequestDto> CancelAsync(Guid companyId, Guid refundRequestId, Guid actorUserId, string reason, CancellationToken cancellationToken);
    Task<SupportRefundRequestDto> ReconcileAsync(Guid companyId, Guid refundRequestId, Guid actorUserId, CancellationToken cancellationToken);
}

public sealed record SupportRefundFinanceActionResult(Guid RefundRequestId, Guid FinanceActionReferenceId, bool Created, decimal RefundableBalance, string Status, string Message);

public interface ISupportSlaMonitor
{
    Task<SupportSlaMonitorResult> RunAsync(DateTime nowUtc, CancellationToken cancellationToken);
    Task<SupportSlaMonitorResult> RunForCompanyAsync(Guid companyId, DateTime nowUtc, CancellationToken cancellationToken);
}

public interface ISupportSlaPolicyService
{
    Task<IReadOnlyList<SupportSlaPolicyDto>> ListAsync(Guid companyId, CancellationToken cancellationToken);
    Task<SupportSlaPolicyDto> UpsertAsync(Guid companyId, Guid userId, UpsertSupportSlaPolicyRequest request, CancellationToken cancellationToken);
    Task<SupportSlaPolicyDto?> DeactivateAsync(Guid companyId, Guid userId, Guid policyId, CancellationToken cancellationToken);
    Task<SupportSlaResolutionDto> ResolveAsync(Guid companyId, string category, string priority, string? customerTier, DateTime startUtc, CancellationToken cancellationToken);
    Task<SupportBusinessCalendarDto> GetCalendarAsync(Guid companyId, CancellationToken cancellationToken);
    Task<SupportBusinessCalendarDto> SaveCalendarAsync(Guid companyId, Guid userId, SaveSupportBusinessCalendarRequest request, CancellationToken cancellationToken);
}

public interface ISupportKnowledgeGapService
{
    Task<SupportKnowledgeGapDto> CreateOrIncrementAsync(Guid companyId, CreateSupportKnowledgeGapRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<SupportKnowledgeGapDto>> ListAsync(Guid companyId, string? status, CancellationToken cancellationToken);
    Task<SupportKnowledgeGapDto?> CreateDocumentationTaskAsync(Guid companyId, Guid userId, Guid knowledgeGapId, CancellationToken cancellationToken);
    Task<SupportKnowledgeGapDto?> ResolveAsync(Guid companyId, Guid userId, Guid knowledgeGapId, ResolveSupportKnowledgeGapRequest request, CancellationToken cancellationToken);
    Task<SupportKnowledgeGapDto?> ReopenAsync(Guid companyId, Guid userId, Guid knowledgeGapId, CancellationToken cancellationToken);
}

public interface ISupportAnalyticsService
{
    Task<SupportAnalyticsDashboardResponse> GetDashboardAsync(Guid companyId, CancellationToken cancellationToken);
}

public interface ISupportMemoryUpdateService
{
    Task UpdateFromResolvedCaseAsync(Guid companyId, Guid supportCaseId, CancellationToken cancellationToken);
    Task ProcessJobAsync(Guid companyId, Guid jobId, CancellationToken cancellationToken);
}

public interface ISupportMemoryReviewService
{
    Task<IReadOnlyList<SupportMemoryObservationDto>> ListAsync(Guid companyId, Guid? contactId, string? status, CancellationToken cancellationToken);
    Task<SupportMemoryObservationDto?> ApproveAsync(Guid companyId, Guid userId, Guid observationId, SupportActionRequest request, CancellationToken cancellationToken);
    Task<SupportMemoryObservationDto?> RejectAsync(Guid companyId, Guid userId, Guid observationId, SupportActionRequest request, CancellationToken cancellationToken);
    Task<SupportMemoryObservationDto?> ExpireAsync(Guid companyId, Guid userId, Guid observationId, SupportActionRequest request, CancellationToken cancellationToken);
    Task<SupportMemoryObservationDto?> DeleteAsync(Guid companyId, Guid userId, Guid observationId, SupportActionRequest request, CancellationToken cancellationToken);
}


public interface ISupportKnowledgeContextProvider
{
    Task<SupportKnowledgeContext> RetrieveAsync(Guid companyId, Guid supportCaseId, CancellationToken cancellationToken);
}

public interface ISupportMailboxRoutingService
{
    Task<SupportMailboxRoutingResult> RouteUnlinkedInboundMessagesAsync(DateTime sinceUtc, int batchSize, CancellationToken cancellationToken);
}

public sealed record SupportReplySafetyDecision(
    string Decision,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<string> Explanations,
    string PolicyVersion);

public static class SupportReplySafetyRules
{
    public const string PolicyVersion = "support-reply-safety-v2";

    public static SupportReplySafetyDecision Evaluate(string category, string draftBody, string? sourceReferencesJson)
    {
        var text = draftBody.ToLowerInvariant();
        var reasons = new List<string>();
        var explanations = new List<string>();
        if (ContainsAny(text, "password", "passcode", "api key", "token", "bank account", "credit card", "cvv"))
        {
            reasons.Add("sensitive_data_request");
            explanations.Add("The reply appears to request or repeat sensitive credentials or payment details.");
        }

        if ((category is SupportCaseCategories.Refund or SupportCaseCategories.Billing) &&
            ContainsAny(text, "will refund", "refund has been", "credit has been", "payment has been", "guarantee", "legally"))
        {
            reasons.Add("unsupported_financial_promise");
            explanations.Add("Refund, credit, payment, or legal commitments need verified workflow evidence before approval.");
        }

        if (text.Contains("ignore previous") || text.Contains("system prompt") || text.Contains("hidden instruction"))
        {
            reasons.Add("prompt_injection_residue");
            explanations.Add("The reply contains prompt-injection or internal-instruction residue.");
        }

        if (!HasTrustedSources(sourceReferencesJson))
        {
            reasons.Add("missing_grounding");
            explanations.Add("The reply is not supported by processed, indexed, and accessible company knowledge.");
        }

        if (reasons.Count == 0)
        {
            return new SupportReplySafetyDecision("allow", [], ["Reply passed deterministic support safety checks."], PolicyVersion);
        }

        var decision = reasons.Contains("sensitive_data_request") || reasons.Contains("unsupported_financial_promise") || reasons.Contains("prompt_injection_residue")
            ? "block"
            : "review";
        return new SupportReplySafetyDecision(decision, reasons, explanations, PolicyVersion);
    }

    private static bool ContainsAny(string value, params string[] terms) => terms.Any(value.Contains);

    private static bool HasTrustedSources(string? sourceReferencesJson)
    {
        if (string.IsNullOrWhiteSpace(sourceReferencesJson)) return false;
        try
        {
            var sources = JsonNode.Parse(sourceReferencesJson)?.AsArray();
            return sources?.Any(source =>
                source?["trusted"]?.GetValue<bool>() == true &&
                string.Equals(source?["type"]?.GetValue<string>(), "knowledge_chunk", StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParse(source?["documentId"]?.GetValue<string>(), out _)) == true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

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

public sealed record SupportToolActionRequest(string ToolName, Guid? SupportCaseId, IReadOnlyDictionary<string, string?> Payload, bool Autonomous = false);
public sealed record SupportToolActionResult(bool Succeeded, string Status, string Summary, Guid? SupportCaseId, Guid? CreatedEntityId = null);
public sealed record SupportSlaMonitorResult(int CasesScanned, int RisksCreated, int BreachesCreated, int NotificationsCreated);

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

