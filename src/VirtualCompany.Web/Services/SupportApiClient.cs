using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public sealed partial class SupportApiClient
{
    private const string CompanyContextHeaderName = "X-Company-Id";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly bool _useOfflineMode;
    private readonly IApiProblemMessageResolver? _problemResolver;

    public SupportApiClient(HttpClient httpClient, bool useOfflineMode = false, IApiProblemMessageResolver? problemResolver = null)
    {
        _httpClient = httpClient;
        _useOfflineMode = useOfflineMode;
        _problemResolver = problemResolver;
    }

    public Task<SupportCaseListResponse> ListCasesAsync(Guid companyId, SupportCaseListQuery query, CancellationToken cancellationToken = default)
    {
        var parameters = new List<string>();
        Add(parameters, "status", query.Status);
        Add(parameters, "priority", query.Priority);
        Add(parameters, "category", query.Category);
        Add(parameters, "search", query.Search);
        Add(parameters, "slaRisk", query.SlaRisk?.ToString());
        Add(parameters, "openOnly", query.OpenOnly.ToString());
        Add(parameters, "resolvedToday", query.ResolvedToday.ToString());
        Add(parameters, "unassigned", query.Unassigned?.ToString());
        Add(parameters, "assignedToMe", query.AssignedToMe.ToString());
        Add(parameters, "slaBreached", query.SlaBreached?.ToString());
        Add(parameters, "waitingTooLong", query.WaitingTooLong.ToString());
        Add(parameters, "failedReply", query.FailedReply.ToString());
        Add(parameters, "sortBy", query.SortBy);
        Add(parameters, "sortDirection", query.SortDirection);
        Add(parameters, "skip", query.Skip.ToString());
        Add(parameters, "take", query.Take.ToString());
        var uri = parameters.Count == 0 ? "api/support/cases" : $"api/support/cases?{string.Join('&', parameters)}";
        return GetAsync<SupportCaseListResponse>(companyId, uri, allowNotFound: false, cancellationToken)!;
    }

    public Task<SupportAnalyticsDashboardResponse> GetAnalyticsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        GetAsync<SupportAnalyticsDashboardResponse>(companyId, "api/support/analytics", allowNotFound: false, cancellationToken)!;

    public Task<SupportCaseDetailResponse?> GetCaseAsync(Guid companyId, Guid supportCaseId, CancellationToken cancellationToken = default) =>
        GetAsync<SupportCaseDetailResponse>(companyId, $"api/support/cases/{supportCaseId:D}", allowNotFound: true, cancellationToken);

    public Task<SupportCaseDetailResponse> CreateCaseAsync(Guid companyId, CreateSupportCaseRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<CreateSupportCaseRequest, SupportCaseDetailResponse>(companyId, HttpMethod.Post, "api/support/cases", request, cancellationToken);

    public Task<SupportCaseDetailResponse> AddNoteAsync(Guid companyId, Guid supportCaseId, string body, CancellationToken cancellationToken = default) =>
        SendAsync<AddSupportInternalNoteRequest, SupportCaseDetailResponse>(companyId, HttpMethod.Post, $"api/support/cases/{supportCaseId:D}/notes", new(body), cancellationToken);

    public Task<SupportCaseDetailResponse> ChangeStatusAsync(Guid companyId, Guid supportCaseId, string status, string? note = null, CancellationToken cancellationToken = default) =>
        SendAsync<ChangeSupportStatusRequest, SupportCaseDetailResponse>(companyId, HttpMethod.Post, $"api/support/cases/{supportCaseId:D}/status", new(status, note), cancellationToken);

    public Task<SupportCaseDetailResponse> ChangePriorityAsync(Guid companyId, Guid supportCaseId, string priority, string? note = null, CancellationToken cancellationToken = default) =>
        SendAsync<ChangeSupportPriorityRequest, SupportCaseDetailResponse>(companyId, HttpMethod.Post, $"api/support/cases/{supportCaseId:D}/priority", new(priority, note), cancellationToken);

    public Task<SupportCaseDetailResponse> ChangeCategoryAsync(Guid companyId, Guid supportCaseId, string category, string? note = null, CancellationToken cancellationToken = default) =>
        SendAsync<ChangeSupportCategoryRequest, SupportCaseDetailResponse>(companyId, HttpMethod.Post, $"api/support/cases/{supportCaseId:D}/category", new(category, note), cancellationToken);

    public Task<IReadOnlyList<SupportAssigneeOptionDto>> ListAssigneesAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<SupportAssigneeOptionDto>>(companyId, "api/support/assignees", allowNotFound: false, cancellationToken)!;

    public Task<SupportCaseDetailResponse> AssignAsync(Guid companyId, Guid supportCaseId, AssignSupportCaseRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<AssignSupportCaseRequest, SupportCaseDetailResponse>(companyId, HttpMethod.Post, $"api/support/cases/{supportCaseId:D}/assign", request, cancellationToken);

    public Task<SupportCaseDetailResponse> ResolveAsync(Guid companyId, Guid supportCaseId, ResolveSupportCaseRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ResolveSupportCaseRequest, SupportCaseDetailResponse>(companyId, HttpMethod.Post, $"api/support/cases/{supportCaseId:D}/resolve", request, cancellationToken);

    public Task<SupportCaseDetailResponse> ReopenAsync(Guid companyId, Guid supportCaseId, string note, CancellationToken cancellationToken = default) =>
        SendAsync<SupportActionRequest, SupportCaseDetailResponse>(companyId, HttpMethod.Post, $"api/support/cases/{supportCaseId:D}/reopen", new(note), cancellationToken);

    public Task<SupportCaseDetailResponse> CloseAsync(Guid companyId, Guid supportCaseId, string note, CancellationToken cancellationToken = default) =>
        SendAsync<SupportActionRequest, SupportCaseDetailResponse>(companyId, HttpMethod.Post, $"api/support/cases/{supportCaseId:D}/close", new(note), cancellationToken);

    public Task<SupportTriageResult> TriageAsync(Guid companyId, Guid supportCaseId, CancellationToken cancellationToken = default) =>
        SendAsync<object, SupportTriageResult>(companyId, HttpMethod.Post, $"api/support/cases/{supportCaseId:D}/triage", new { }, cancellationToken);

    public Task<SupportAgentExecutionDto> RunSupportAgentAsync(Guid companyId, Guid supportCaseId, bool forceReview = false, CancellationToken cancellationToken = default) =>
        SendAsync<RunSupportAgentRequest, SupportAgentExecutionDto>(companyId, HttpMethod.Post, $"api/support/cases/{supportCaseId:D}/agent/run", new(null, forceReview), cancellationToken);

    public Task<IReadOnlyList<SupportKnowledgeDocumentDto>> ListSupportKnowledgeAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<SupportKnowledgeDocumentDto>>(companyId, $"api/companies/{companyId:D}/documents", allowNotFound: false, cancellationToken)!;

    public Task<ImportDefaultSupportKnowledgeResponse> ImportDefaultSupportKnowledgeAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        SendAsync<object, ImportDefaultSupportKnowledgeResponse>(companyId, HttpMethod.Post, $"api/companies/{companyId:D}/documents/import-default-support-knowledge", new { }, cancellationToken);

    public Task<SupportReplyDraftDto> GenerateDraftAsync(Guid companyId, Guid supportCaseId, string? tone = null, CancellationToken cancellationToken = default) =>
        SendAsync<GenerateSupportReplyDraftRequest, SupportReplyDraftDto>(companyId, HttpMethod.Post, $"api/support/cases/{supportCaseId:D}/reply-drafts/generate", new(tone), cancellationToken);

    public Task<SupportReplyDraftDto> ApproveDraftAsync(Guid companyId, Guid draftId, string? note = null, CancellationToken cancellationToken = default) =>
        SendAsync<SupportActionRequest, SupportReplyDraftDto>(companyId, HttpMethod.Post, $"api/support/reply-drafts/{draftId:D}/approve", new(note), cancellationToken);

    public Task<SupportReplyDraftDto> EditDraftAsync(Guid companyId, Guid draftId, string body, string tone, CancellationToken cancellationToken = default) =>
        SendAsync<EditSupportReplyDraftRequest, SupportReplyDraftDto>(companyId, HttpMethod.Put, $"api/support/reply-drafts/{draftId:D}", new(body, tone), cancellationToken);

    public Task<SupportReplyDraftDto> RejectDraftAsync(Guid companyId, Guid draftId, string note, CancellationToken cancellationToken = default) =>
        SendAsync<SupportActionRequest, SupportReplyDraftDto>(companyId, HttpMethod.Post, $"api/support/reply-drafts/{draftId:D}/reject", new(note), cancellationToken);

    public Task<SupportCaseDetailResponse> SendDraftAsync(
        Guid companyId,
        Guid draftId,
        bool resolveAfterSend = false,
        Guid? mailboxConnectionId = null,
        string? toEmail = null,
        string? toDisplayName = null,
        string? subject = null,
        string? originalMessageId = null,
        string? providerThreadId = null,
        string? internetMessageId = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<SendSupportReplyDraftRequest, SupportCaseDetailResponse>(
            companyId,
            HttpMethod.Post,
            $"api/support/reply-drafts/{draftId:D}/send",
            new(resolveAfterSend, false, mailboxConnectionId, toEmail, toDisplayName, subject, originalMessageId, providerThreadId, internetMessageId),
            cancellationToken);

    public Task<SupportRefundRequestDto> RequestRefundAsync(Guid companyId, Guid supportCaseId, CreateSupportRefundRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<CreateSupportRefundRequest, SupportRefundRequestDto>(companyId, HttpMethod.Post, $"api/support/cases/{supportCaseId:D}/refund-requests", request, cancellationToken);

    public Task<SupportRefundRequestDto> RequestRefundExecutionAsync(Guid companyId, Guid refundRequestId, CancellationToken cancellationToken = default) =>
        SendAsync<object, SupportRefundRequestDto>(companyId, HttpMethod.Post, $"api/support/refund-requests/{refundRequestId:D}/execute", new { }, cancellationToken);

    public Task<SupportRefundRequestDto> CancelRefundAsync(Guid companyId, Guid refundRequestId, string reason, CancellationToken cancellationToken = default) =>
        SendAsync<SupportActionRequest, SupportRefundRequestDto>(companyId, HttpMethod.Post, $"api/support/refund-requests/{refundRequestId:D}/cancel", new(reason), cancellationToken);

    public Task<SupportRefundRequestDto> ReconcileRefundAsync(Guid companyId, Guid refundRequestId, CancellationToken cancellationToken = default) =>
        SendAsync<object, SupportRefundRequestDto>(companyId, HttpMethod.Post, $"api/support/refund-requests/{refundRequestId:D}/reconcile", new { }, cancellationToken);

    public Task<IReadOnlyList<SupportKnowledgeGapDto>> ListKnowledgeGapsAsync(Guid companyId, string? status = null, CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<SupportKnowledgeGapDto>>(companyId, string.IsNullOrWhiteSpace(status) ? "api/support/knowledge-gaps" : $"api/support/knowledge-gaps?status={Uri.EscapeDataString(status)}", allowNotFound: false, cancellationToken)!;

    public Task<SupportKnowledgeGapDto> CreateKnowledgeDocumentationTaskAsync(Guid companyId, Guid gapId, CancellationToken cancellationToken = default) =>
        SendAsync<object, SupportKnowledgeGapDto>(companyId, HttpMethod.Post, $"api/support/knowledge-gaps/{gapId:D}/documentation-task", new { }, cancellationToken);

    public Task<SupportKnowledgeGapDto> ResolveKnowledgeGapAsync(Guid companyId, Guid gapId, Guid knowledgeDocumentId, CancellationToken cancellationToken = default) =>
        SendAsync<ResolveSupportKnowledgeGapRequest, SupportKnowledgeGapDto>(companyId, HttpMethod.Post, $"api/support/knowledge-gaps/{gapId:D}/resolve", new(knowledgeDocumentId), cancellationToken);

    public Task<SupportKnowledgeGapDto> ReopenKnowledgeGapAsync(Guid companyId, Guid gapId, CancellationToken cancellationToken = default) =>
        SendAsync<object, SupportKnowledgeGapDto>(companyId, HttpMethod.Post, $"api/support/knowledge-gaps/{gapId:D}/reopen", new { }, cancellationToken);

    public Task<IReadOnlyList<SupportSlaPolicyDto>> ListSlaPoliciesAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<SupportSlaPolicyDto>>(companyId, "api/support/sla/policies", allowNotFound: false, cancellationToken)!;

    public Task<SupportSlaPolicyDto> SaveSlaPolicyAsync(Guid companyId, UpsertSupportSlaPolicyRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<UpsertSupportSlaPolicyRequest, SupportSlaPolicyDto>(companyId, HttpMethod.Post, "api/support/sla/policies", request, cancellationToken);

    public Task<SupportSlaPolicyDto> DeactivateSlaPolicyAsync(Guid companyId, Guid policyId, CancellationToken cancellationToken = default) =>
        SendAsync<object, SupportSlaPolicyDto>(companyId, HttpMethod.Post, $"api/support/sla/policies/{policyId:D}/deactivate", new { }, cancellationToken);

    public Task<SupportBusinessCalendarDto> GetSlaCalendarAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        GetAsync<SupportBusinessCalendarDto>(companyId, "api/support/sla/calendar", allowNotFound: false, cancellationToken)!;

    public Task<SupportBusinessCalendarDto> SaveSlaCalendarAsync(Guid companyId, SaveSupportBusinessCalendarRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<SaveSupportBusinessCalendarRequest, SupportBusinessCalendarDto>(companyId, HttpMethod.Post, "api/support/sla/calendar", request, cancellationToken);

    public Task<SupportSlaResolutionDto> PreviewSlaPolicyAsync(Guid companyId, string category, string priority, string? customerTier, CancellationToken cancellationToken = default)
    {
        var uri = $"api/support/sla/preview?category={Uri.EscapeDataString(category)}&priority={Uri.EscapeDataString(priority)}";
        if (!string.IsNullOrWhiteSpace(customerTier)) uri += $"&customerTier={Uri.EscapeDataString(customerTier)}";
        return GetAsync<SupportSlaResolutionDto>(companyId, uri, allowNotFound: false, cancellationToken)!;
    }

    public Task<IReadOnlyList<SupportMemoryObservationDto>> ListMemoryObservationsAsync(Guid companyId, string? status = null, Guid? contactId = null, CancellationToken cancellationToken = default)
    {
        var parameters = new List<string>();
        Add(parameters, "status", status);
        Add(parameters, "contactId", contactId?.ToString("D"));
        var uri = parameters.Count == 0 ? "api/support/memory/observations" : $"api/support/memory/observations?{string.Join('&', parameters)}";
        return GetAsync<IReadOnlyList<SupportMemoryObservationDto>>(companyId, uri, allowNotFound: false, cancellationToken)!;
    }

    public Task<SupportMemoryObservationDto> ApproveMemoryObservationAsync(Guid companyId, Guid observationId, string? note = null, CancellationToken cancellationToken = default) =>
        SendAsync<SupportActionRequest, SupportMemoryObservationDto>(companyId, HttpMethod.Post, $"api/support/memory/observations/{observationId:D}/approve", new(note), cancellationToken);

    public Task<SupportMemoryObservationDto> RejectMemoryObservationAsync(Guid companyId, Guid observationId, string? note = null, CancellationToken cancellationToken = default) =>
        SendAsync<SupportActionRequest, SupportMemoryObservationDto>(companyId, HttpMethod.Post, $"api/support/memory/observations/{observationId:D}/reject", new(note), cancellationToken);

    public Task<SupportMemoryObservationDto> ExpireMemoryObservationAsync(Guid companyId, Guid observationId, string? note = null, CancellationToken cancellationToken = default) =>
        SendAsync<SupportActionRequest, SupportMemoryObservationDto>(companyId, HttpMethod.Post, $"api/support/memory/observations/{observationId:D}/expire", new(note), cancellationToken);

    public Task<SupportMemoryObservationDto> DeleteMemoryObservationAsync(Guid companyId, Guid observationId, string? note = null, CancellationToken cancellationToken = default) =>
        SendAsync<SupportActionRequest, SupportMemoryObservationDto>(companyId, HttpMethod.Post, $"api/support/memory/observations/{observationId:D}/delete", new(note), cancellationToken);

    private async Task<T?> GetAsync<T>(Guid companyId, string uri, bool allowNotFound, CancellationToken cancellationToken)
    {
        if (_useOfflineMode)
        {
            throw new SupportApiException("Support needs the backend API. Start the API project to review live support work.");
        }

        try
        {
            using var request = CreateCompanyRequest(companyId, HttpMethod.Get, uri, null);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
            }

            throw await CreateExceptionAsync(response, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new SupportApiException("The support workspace could not reach the backend API.");
        }
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(Guid companyId, HttpMethod method, string uri, TRequest payload, CancellationToken cancellationToken)
    {
        if (_useOfflineMode)
        {
            throw new SupportApiException("Support actions need the backend API. Start the API project before changing live tenant data.");
        }

        try
        {
            using var request = CreateCompanyRequest(companyId, method, uri, JsonContent.Create(payload, options: SerializerOptions));
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<TResponse>(SerializerOptions, cancellationToken)
                    ?? throw new SupportApiException("The support API returned an empty response.");
            }

            throw await CreateExceptionAsync(response, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new SupportApiException("The support workspace could not reach the backend API.");
        }
    }

    private static HttpRequestMessage CreateCompanyRequest(Guid companyId, HttpMethod method, string uri, HttpContent? content)
    {
        var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.TryAddWithoutValidation(CompanyContextHeaderName, companyId.ToString("D"));
        return request;
    }

    private static void Add(ICollection<string> parameters, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parameters.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
        }
    }

    private async Task<SupportApiException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentType?.MediaType is not ("application/json" or "application/problem+json"))
        {
            return new SupportApiException($"The support request failed with status code {(int)response.StatusCode}.");
        }

        var problem = await response.Content.ReadFromJsonAsync<ApiProblemResponse>(SerializerOptions, cancellationToken);
        return problem?.Errors is { Count: > 0 }
            ? new SupportApiException(_problemResolver?.Resolve(problem, FormatProblem(problem)) ?? FormatProblem(problem), problem.Errors)
            : new SupportApiException(_problemResolver?.Resolve(problem, "The support request failed.") ?? problem?.Detail ?? problem?.Title ?? "The support request failed.");
    }

    private static string FormatProblem(ApiProblemResponse problem)
    {
        var firstError = problem.Errors?.SelectMany(x => x.Value).FirstOrDefault();
        return firstError ?? problem.Detail ?? problem.Title ?? "The support request failed.";
    }

}

public sealed class SupportApiException : Exception
{
    public SupportApiException(string message, IReadOnlyDictionary<string, string[]>? errors = null) : base(message)
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]>? Errors { get; }
}

public sealed record SupportCaseListQuery(string? Status = null, string? Priority = null, string? Category = null, string? Search = null, bool? SlaRisk = null, bool OpenOnly = false, bool ResolvedToday = false, bool? Unassigned = null, bool AssignedToMe = false, bool? SlaBreached = null, bool WaitingTooLong = false, bool FailedReply = false, string? SortBy = null, string? SortDirection = null, int Skip = 0, int Take = 50);
public sealed record SupportCaseListResponse(IReadOnlyList<SupportCaseListItem> Items, int TotalCount, SupportCaseSummaryCounts Summary);
public sealed record SupportCaseSummaryCounts(int Open, int AwaitingApproval, int Escalated, int SlaRisk, int SlaBreached, int ResolvedToday);
public sealed record SupportCaseListItem(Guid Id, string CaseNumber, string Subject, string Status, string StatusLabel, string Priority, string PriorityLabel, string Category, string CategoryLabel, string Source, string? CustomerName, string? ContactName, string? ContactEmail, Guid? AssignedAgentId, Guid? AssignedUserId, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? FirstResponseDueUtc, DateTime? ResolutionDueUtc, bool IsSlaRisk, bool IsSlaBreached, bool IsChurnRisk, bool IsVipRisk);
public sealed record SupportCaseDetailResponse(Guid Id, string CaseNumber, string Subject, string Summary, string? Description, string Status, string StatusLabel, string Priority, string PriorityLabel, string Category, string CategoryLabel, string Source, string? Sentiment, decimal? ConfidenceScore, string? SuggestedNextAction, string? RationaleSummary, Guid? ContactId, Guid? CustomerCompanyId, Guid? RelatedInvoiceId, Guid? RelatedPaymentId, string? CustomerName, string? ContactName, string? ContactEmail, Guid? AssignedAgentId, Guid? AssignedUserId, DateTime? FirstResponseDueUtc, DateTime? ResolutionDueUtc, bool IsSlaRisk, bool IsSlaBreached, bool IsChurnRisk, bool IsVipRisk, IReadOnlyList<string> AllowedActions, DateTime CreatedUtc, DateTime UpdatedUtc, IReadOnlyList<SupportMessageDto> Messages, IReadOnlyList<SupportCaseEventDto> Events, IReadOnlyList<SupportReplyDraftDto> ReplyDrafts, IReadOnlyList<SupportRefundRequestDto> RefundRequests, IReadOnlyList<SupportKnowledgeGapDto> KnowledgeGaps, SupportCaseContextSummary Context);
public sealed record SupportMessageDto(Guid Id, string Direction, string Channel, string Sender, string? Recipient, string Body, DateTime OccurredUtc, Guid? EmailMessageSnapshotId, string? ProviderMessageId, string? ProviderThreadId);
public sealed record SupportCaseEventDto(Guid Id, string EventType, string EventLabel, string Summary, string ActorType, Guid? ActorId, DateTime OccurredUtc);
public sealed record SupportReplyDraftDto(Guid Id, Guid SupportCaseId, string DraftBody, string Tone, string Status, string StatusLabel, decimal Confidence, decimal Answerability, string? RationaleSummary, string? SourceReferencesJson, Guid? CreatedByAgentId, Guid? CreatedByUserId, Guid? ApprovedByUserId, DateTime? ApprovedUtc, DateTime? SentUtc, string? SendFailureSummary, DateTime CreatedUtc, DateTime UpdatedUtc, string? SafetyDecision = null, string? SafetyReasonCodesJson = null, string? SafetyPolicyVersion = null, DateTime? SafetyEvaluatedUtc = null);
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
public sealed record SupportKnowledgeGapDto(Guid Id, Guid? SupportCaseId, Guid? SupportReplyDraftId, string Category, string CategoryLabel, string QuestionSummary, string MissingInformationSummary, string? RetrievalSourceSummary, int FrequencyCount, string Status, string StatusLabel, DateTime CreatedUtc, DateTime UpdatedUtc, Guid? LinkedTaskId, Guid? LinkedKnowledgeDocumentId);
public sealed record SupportCaseContextSummary(Guid SupportCaseId, string? CustomerName, string? ContactName, string? ContactEmail, IReadOnlyList<SupportContextReference> References, decimal MatchConfidence, string MatchRationale);
public sealed record SupportContextReference(string Type, string Label, Guid? EntityId, string? SecondaryText = null);
public sealed record SupportTriageResult(Guid SupportCaseId, string Category, string Priority, string Sentiment, decimal Confidence, string SuggestedNextAction, string RationaleSummary, bool IsVipRisk, bool IsChurnRisk, bool IsSlaRisk);
public sealed record RunSupportAgentRequest(string? IdempotencyKey = null, bool ForceReview = false);
public sealed record SupportKnowledgeDocumentDto(
    Guid Id,
    string Title,
    string IngestionStatus,
    string IndexingStatus,
    int ActiveChunkCount,
    DateTime UpdatedUtc);
public sealed record ImportDefaultSupportKnowledgeResponse(
    IReadOnlyList<SupportKnowledgeDocumentDto> Imported,
    IReadOnlyList<string> Skipped);
public sealed record SupportAgentExecutionDto(Guid Id, Guid SupportCaseId, Guid? AgentId, string Status, string CurrentStep, Guid? CreatedDraftId, string Summary, string? FailureSummary, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? CompletedUtc);
public sealed record SupportAnalyticsDashboardResponse(SupportCaseSummaryCounts Summary, IReadOnlyList<SupportMetricBucket> ByStatus, IReadOnlyList<SupportMetricBucket> ByCategory, IReadOnlyList<SupportMetricBucket> ByPriority, SupportSlaPerformanceSummary SlaPerformance, SupportLearningEffectivenessSummary Learning, IReadOnlyList<SupportRootCauseInsight> Insights);
public sealed record SupportMetricBucket(string Key, string Label, int Count);
public sealed record SupportSlaPerformanceSummary(int OpenAtRisk, int OpenBreached, int FirstResponsesMet, int FirstResponsesMissed, int ResolutionsMet, int ResolutionsMissed, int MissingTargets, string Rationale);
public sealed record SupportLearningEffectivenessSummary(int ApprovedMemoryObservations, int ReviewMemoryObservations, int RejectedMemoryObservations, int DraftsUsingMemory, decimal? AverageAnswerabilityWithMemory, decimal? AverageAnswerabilityWithoutMemory, int ApprovedDrafts, int RejectedDrafts, int SentReplies, int ReopenedCases, string Rationale);
public sealed record SupportRootCauseInsight(string Title, string Summary, string Category, int CaseCount, string SuggestedAction);
public sealed record CreateSupportCaseRequest(string Subject, string? Description, string? Source, string? SenderEmail = null, Guid? ContactId = null, Guid? CustomerCompanyId = null);
public sealed record AddSupportInternalNoteRequest(string Body);
public sealed record ChangeSupportStatusRequest(string Status, string? Note = null);
public sealed record ChangeSupportPriorityRequest(string Priority, string? Note = null);
public sealed record ChangeSupportCategoryRequest(string Category, string? Note = null);
public sealed record AssignSupportCaseRequest(Guid? AssignedAgentId, Guid? AssignedUserId, string? Reason = null);
public sealed record SupportAssigneeOptionDto(Guid Id, string Type, string DisplayName, string SecondaryText, bool Available, int OpenCaseCount);
public sealed record ResolveSupportCaseRequest(string Summary, string Outcome, string RootCauseCategory = "other", string? ActionTaken = null, string? ReusableAnswer = null, string? CustomerPreferenceObservations = null, IReadOnlyList<Guid>? RelevantEntityIds = null, bool ReuseEligible = false);
public sealed record SupportActionRequest(string? Note = null);
public sealed record GenerateSupportReplyDraftRequest(string? Tone = null, bool ForceReview = false);
public sealed record EditSupportReplyDraftRequest(string DraftBody, string Tone);
public sealed record SendSupportReplyDraftRequest(bool ResolveAfterSend = false, bool Autonomous = false, Guid? MailboxConnectionId = null, string? ToEmail = null, string? ToDisplayName = null, string? Subject = null, string? OriginalMessageId = null, string? ProviderThreadId = null, string? InternetMessageId = null);
public sealed record CreateSupportRefundRequest(decimal Amount, string Currency, string ReasonCode, string Explanation, Guid? InvoiceId = null, Guid? PaymentId = null);
public sealed record ResolveSupportKnowledgeGapRequest(Guid KnowledgeDocumentId);
public sealed record SupportSlaPolicyDto(Guid Id, string Name, string Category, string CategoryLabel, string Priority, string PriorityLabel, string? CustomerTier, int FirstResponseMinutes, int ResolutionMinutes, bool IsActive, DateTime UpdatedUtc, string TimeBasis = "elapsed", int RiskThresholdMinutes = 240, string EscalationRecipientRole = "support_supervisor");
public sealed record UpsertSupportSlaPolicyRequest(Guid? Id, string Name, string Category, string Priority, int FirstResponseMinutes, int ResolutionMinutes, string? CustomerTier = null, bool IsActive = true, string TimeBasis = "elapsed", int RiskThresholdMinutes = 240, string EscalationRecipientRole = "support_supervisor");
public sealed record SupportSlaResolutionDto(Guid? PolicyId, string PolicyName, int FirstResponseMinutes, int ResolutionMinutes, DateTime FirstResponseDueUtc, DateTime ResolutionDueUtc, string Rationale, int RiskThresholdMinutes = 240, string EscalationRecipientRole = "support_supervisor");
public sealed record SupportBusinessCalendarDto(string TimeZoneId, TimeOnly WorkdayStart, TimeOnly WorkdayEnd, IReadOnlyList<DayOfWeek> WorkingDays, IReadOnlyList<DateOnly> Holidays);
public sealed record SaveSupportBusinessCalendarRequest(string TimeZoneId, TimeOnly WorkdayStart, TimeOnly WorkdayEnd, IReadOnlyList<DayOfWeek> WorkingDays, IReadOnlyList<DateOnly> Holidays);
public sealed record SupportMemoryObservationDto(Guid Id, Guid SupportCaseId, Guid SupportCaseResolutionId, Guid ContactId, Guid? CustomerMemoryProfilePreferenceId, string Status, string StatusLabel, string? Value, string EvidenceSummary, decimal Confidence, DateTime ObservedUtc, DateTime? ValidUntilUtc, string PolicyVersion, string SourceEventKey, DateTime UpdatedUtc, IReadOnlyList<string> AllowedActions);
