using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public sealed class SupportApiClient
{
    private const string CompanyContextHeaderName = "X-Company-Id";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly bool _useOfflineMode;

    public SupportApiClient(HttpClient httpClient, bool useOfflineMode = false)
    {
        _httpClient = httpClient;
        _useOfflineMode = useOfflineMode;
    }

    public Task<SupportCaseListResponse> ListCasesAsync(Guid companyId, SupportCaseListQuery query, CancellationToken cancellationToken = default)
    {
        var parameters = new List<string>();
        Add(parameters, "status", query.Status);
        Add(parameters, "priority", query.Priority);
        Add(parameters, "category", query.Category);
        Add(parameters, "search", query.Search);
        Add(parameters, "slaRisk", query.SlaRisk?.ToString());
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

    public Task<SupportTriageResult> TriageAsync(Guid companyId, Guid supportCaseId, CancellationToken cancellationToken = default) =>
        SendAsync<object, SupportTriageResult>(companyId, HttpMethod.Post, $"api/support/cases/{supportCaseId:D}/triage", new { }, cancellationToken);

    public Task<SupportReplyDraftDto> GenerateDraftAsync(Guid companyId, Guid supportCaseId, string? tone = null, CancellationToken cancellationToken = default) =>
        SendAsync<GenerateSupportReplyDraftRequest, SupportReplyDraftDto>(companyId, HttpMethod.Post, $"api/support/cases/{supportCaseId:D}/reply-drafts/generate", new(tone), cancellationToken);

    public Task<SupportReplyDraftDto> ApproveDraftAsync(Guid companyId, Guid draftId, string? note = null, CancellationToken cancellationToken = default) =>
        SendAsync<SupportActionRequest, SupportReplyDraftDto>(companyId, HttpMethod.Post, $"api/support/reply-drafts/{draftId:D}/approve", new(note), cancellationToken);

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

    private static async Task<SupportApiException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentType?.MediaType is not ("application/json" or "application/problem+json"))
        {
            return new SupportApiException($"The support request failed with status code {(int)response.StatusCode}.");
        }

        var problem = await response.Content.ReadFromJsonAsync<ApiProblemResponse>(SerializerOptions, cancellationToken);
        return problem?.Errors is { Count: > 0 }
            ? new SupportApiException(FormatProblem(problem), problem.Errors)
            : new SupportApiException(problem?.Detail ?? problem?.Title ?? "The support request failed.");
    }

    private static string FormatProblem(ApiProblemResponse problem)
    {
        var firstError = problem.Errors?.SelectMany(x => x.Value).FirstOrDefault();
        return firstError ?? problem.Detail ?? problem.Title ?? "The support request failed.";
    }

    private sealed class ApiProblemResponse
    {
        public string? Title { get; set; }
        public string? Detail { get; set; }
        public Dictionary<string, string[]>? Errors { get; set; }
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

public sealed record SupportCaseListQuery(string? Status = null, string? Priority = null, string? Category = null, string? Search = null, bool? SlaRisk = null, int Skip = 0, int Take = 50);
public sealed record SupportCaseListResponse(IReadOnlyList<SupportCaseListItem> Items, int TotalCount, SupportCaseSummaryCounts Summary);
public sealed record SupportCaseSummaryCounts(int Open, int AwaitingApproval, int Escalated, int SlaRisk, int SlaBreached, int ResolvedToday);
public sealed record SupportCaseListItem(Guid Id, string CaseNumber, string Subject, string Status, string StatusLabel, string Priority, string PriorityLabel, string Category, string CategoryLabel, string Source, string? CustomerName, string? ContactName, string? ContactEmail, Guid? AssignedAgentId, Guid? AssignedUserId, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? FirstResponseDueUtc, DateTime? ResolutionDueUtc, bool IsSlaRisk, bool IsSlaBreached, bool IsChurnRisk, bool IsVipRisk);
public sealed record SupportCaseDetailResponse(Guid Id, string CaseNumber, string Subject, string Summary, string? Description, string Status, string StatusLabel, string Priority, string PriorityLabel, string Category, string CategoryLabel, string Source, string? Sentiment, decimal? ConfidenceScore, string? SuggestedNextAction, string? RationaleSummary, Guid? ContactId, Guid? CustomerCompanyId, Guid? RelatedInvoiceId, Guid? RelatedPaymentId, string? CustomerName, string? ContactName, string? ContactEmail, Guid? AssignedAgentId, Guid? AssignedUserId, DateTime? FirstResponseDueUtc, DateTime? ResolutionDueUtc, bool IsSlaRisk, bool IsSlaBreached, bool IsChurnRisk, bool IsVipRisk, DateTime CreatedUtc, DateTime UpdatedUtc, IReadOnlyList<SupportMessageDto> Messages, IReadOnlyList<SupportCaseEventDto> Events, IReadOnlyList<SupportReplyDraftDto> ReplyDrafts, IReadOnlyList<SupportRefundRequestDto> RefundRequests, IReadOnlyList<SupportKnowledgeGapDto> KnowledgeGaps, SupportCaseContextSummary Context);
public sealed record SupportMessageDto(Guid Id, string Direction, string Channel, string Sender, string? Recipient, string Body, DateTime OccurredUtc, Guid? EmailMessageSnapshotId, string? ProviderMessageId, string? ProviderThreadId);
public sealed record SupportCaseEventDto(Guid Id, string EventType, string EventLabel, string Summary, string ActorType, Guid? ActorId, DateTime OccurredUtc);
public sealed record SupportReplyDraftDto(Guid Id, Guid SupportCaseId, string DraftBody, string Tone, string Status, string StatusLabel, decimal Confidence, decimal Answerability, string? RationaleSummary, string? SourceReferencesJson, Guid? CreatedByAgentId, Guid? CreatedByUserId, Guid? ApprovedByUserId, DateTime? ApprovedUtc, DateTime? SentUtc, string? SendFailureSummary, DateTime CreatedUtc, DateTime UpdatedUtc);
public sealed record SupportRefundRequestDto(Guid Id, Guid SupportCaseId, decimal Amount, string Currency, string ReasonCode, string Explanation, Guid? InvoiceId, Guid? PaymentId, Guid? ApprovalRequestId, Guid? FinanceActionReferenceId, string Status, DateTime CreatedUtc, DateTime UpdatedUtc);
public sealed record SupportKnowledgeGapDto(Guid Id, Guid? SupportCaseId, Guid? SupportReplyDraftId, string Category, string CategoryLabel, string QuestionSummary, string MissingInformationSummary, string? RetrievalSourceSummary, int FrequencyCount, string Status, string StatusLabel, DateTime CreatedUtc, DateTime UpdatedUtc, Guid? LinkedTaskId);
public sealed record SupportCaseContextSummary(Guid SupportCaseId, string? CustomerName, string? ContactName, string? ContactEmail, IReadOnlyList<SupportContextReference> References, decimal MatchConfidence, string MatchRationale);
public sealed record SupportContextReference(string Type, string Label, Guid? EntityId, string? SecondaryText = null);
public sealed record SupportTriageResult(Guid SupportCaseId, string Category, string Priority, string Sentiment, decimal Confidence, string SuggestedNextAction, string RationaleSummary, bool IsVipRisk, bool IsChurnRisk, bool IsSlaRisk);
public sealed record SupportAnalyticsDashboardResponse(SupportCaseSummaryCounts Summary, IReadOnlyList<SupportMetricBucket> ByStatus, IReadOnlyList<SupportMetricBucket> ByCategory, IReadOnlyList<SupportMetricBucket> ByPriority, IReadOnlyList<SupportRootCauseInsight> Insights);
public sealed record SupportMetricBucket(string Key, string Label, int Count);
public sealed record SupportRootCauseInsight(string Title, string Summary, string Category, int CaseCount, string SuggestedAction);
public sealed record CreateSupportCaseRequest(string Subject, string? Description, string? Source, string? SenderEmail = null, Guid? ContactId = null, Guid? CustomerCompanyId = null);
public sealed record AddSupportInternalNoteRequest(string Body);
public sealed record ChangeSupportStatusRequest(string Status, string? Note = null);
public sealed record ChangeSupportPriorityRequest(string Priority, string? Note = null);
public sealed record ChangeSupportCategoryRequest(string Category, string? Note = null);
public sealed record ResolveSupportCaseRequest(string Summary, string Outcome);
public sealed record SupportActionRequest(string? Note = null);
public sealed record GenerateSupportReplyDraftRequest(string? Tone = null, bool ForceReview = false);
public sealed record SendSupportReplyDraftRequest(bool ResolveAfterSend = false, bool Autonomous = false, Guid? MailboxConnectionId = null, string? ToEmail = null, string? ToDisplayName = null, string? Subject = null, string? OriginalMessageId = null, string? ProviderThreadId = null, string? InternetMessageId = null);
public sealed record CreateSupportRefundRequest(decimal Amount, string Currency, string ReasonCode, string Explanation, Guid? InvoiceId = null, Guid? PaymentId = null);
