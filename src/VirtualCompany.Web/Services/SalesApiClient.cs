using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public sealed class SalesApiClient
{
    private const string CompanyContextHeaderName = "X-Company-Id";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly bool _useOfflineMode;

    public SalesApiClient(HttpClient httpClient, bool useOfflineMode = false)
    {
        _httpClient = httpClient;
        _useOfflineMode = useOfflineMode;
    }

    public Task<SalesDashboardResponse> GetDashboardAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        GetAsync<SalesDashboardResponse>(companyId, "api/sales/dashboard", allowNotFound: false, cancellationToken)!;

    public Task<SalesAnalyticsDashboardResponse> GetAnalyticsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        GetAsync<SalesAnalyticsDashboardResponse>(companyId, "api/sales/analytics", allowNotFound: false, cancellationToken)!;

    public Task<RevenueForecastSnapshotResponse> GetRevenueForecastAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        GetAsync<RevenueForecastSnapshotResponse>(companyId, "api/sales/forecast", allowNotFound: false, cancellationToken)!;

    public async Task<IReadOnlyList<SalesLeadSummaryResponse>> ListLeadsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        await GetAsync<List<SalesLeadSummaryResponse>>(companyId, "api/sales/leads", allowNotFound: false, cancellationToken) ?? [];

    public Task<SalesLeadDetailResponse?> GetLeadAsync(Guid companyId, Guid leadId, CancellationToken cancellationToken = default) =>
        GetAsync<SalesLeadDetailResponse>(companyId, $"api/sales/leads/{leadId:D}", allowNotFound: true, cancellationToken);

    public Task<SalesLeadDetailResponse> UpdateLeadQualificationAsync(Guid companyId, Guid leadId, UpdateLeadQualificationRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<UpdateLeadQualificationRequest, SalesLeadDetailResponse>(companyId, HttpMethod.Put, $"api/sales/leads/{leadId:D}/qualification", request, cancellationToken);

    public Task<SalesLeadDetailResponse> QualifyLeadAsync(Guid companyId, Guid leadId, string? note, CancellationToken cancellationToken = default) =>
        SendAsync<SalesActionRequest, SalesLeadDetailResponse>(companyId, HttpMethod.Post, $"api/sales/leads/{leadId:D}/qualify", new(note), cancellationToken);

    public Task<SalesLeadDetailResponse> RejectLeadAsync(Guid companyId, Guid leadId, string? note, CancellationToken cancellationToken = default) =>
        SendAsync<SalesActionRequest, SalesLeadDetailResponse>(companyId, HttpMethod.Post, $"api/sales/leads/{leadId:D}/reject", new(note), cancellationToken);

    public Task<SalesDealDetailResponse> ConvertLeadAsync(Guid companyId, Guid leadId, ConvertLeadRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ConvertLeadRequest, SalesDealDetailResponse>(companyId, HttpMethod.Post, $"api/sales/leads/{leadId:D}/convert", request, cancellationToken);

    public Task<SalesPipelineResponse> GetPipelineAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        GetAsync<SalesPipelineResponse>(companyId, "api/sales/pipeline", allowNotFound: false, cancellationToken)!;

    public Task<SalesDealDetailResponse?> GetDealAsync(Guid companyId, Guid dealId, CancellationToken cancellationToken = default) =>
        GetAsync<SalesDealDetailResponse>(companyId, $"api/sales/deals/{dealId:D}", allowNotFound: true, cancellationToken);

    public async Task<IReadOnlyList<SalesActivityResponse>> ListDealActivitiesAsync(Guid companyId, Guid dealId, CancellationToken cancellationToken = default) =>
        await GetAsync<List<SalesActivityResponse>>(companyId, $"api/sales/deals/{dealId:D}/activities", allowNotFound: false, cancellationToken) ?? [];

    public async Task<IReadOnlyList<SalesEmailTimelineResponse>> ListDealEmailsAsync(Guid companyId, Guid dealId, CancellationToken cancellationToken = default) =>
        await GetAsync<List<SalesEmailTimelineResponse>>(companyId, $"api/sales/deals/{dealId:D}/emails", allowNotFound: false, cancellationToken) ?? [];

    public async Task<IReadOnlyList<SalesRecommendationResponse>> ListRecommendationsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        await GetAsync<List<SalesRecommendationResponse>>(companyId, "api/sales/recommendations", allowNotFound: false, cancellationToken) ?? [];

    public Task<CustomerMemoryContext?> GetContactProfileAsync(Guid companyId, Guid contactId, CancellationToken cancellationToken = default) =>
        GetAsync<CustomerMemoryContext>(companyId, $"api/sales/contacts/{contactId:D}/profile", allowNotFound: true, cancellationToken);

    public Task<SalesDealDetailResponse> ChangeDealStageAsync(Guid companyId, Guid dealId, Guid stageId, string? note, CancellationToken cancellationToken = default) =>
        SendAsync<ChangeDealStageRequest, SalesDealDetailResponse>(companyId, HttpMethod.Post, $"api/sales/deals/{dealId:D}/stage", new(stageId, note), cancellationToken);

    public Task<SalesDealDetailResponse> MarkWonAsync(Guid companyId, Guid dealId, string? note, CancellationToken cancellationToken = default) =>
        SendAsync<SalesActionRequest, SalesDealDetailResponse>(companyId, HttpMethod.Post, $"api/sales/deals/{dealId:D}/won", new(note), cancellationToken);

    public Task<SalesDealDetailResponse> MarkLostAsync(Guid companyId, Guid dealId, string? note, CancellationToken cancellationToken = default) =>
        SendAsync<SalesActionRequest, SalesDealDetailResponse>(companyId, HttpMethod.Post, $"api/sales/deals/{dealId:D}/lost", new(note), cancellationToken);

    public Task<SalesRecommendationResponse> ApproveRecommendationAsync(Guid companyId, Guid recommendationId, string? note, CancellationToken cancellationToken = default) =>
        SendAsync<SalesActionRequest, SalesRecommendationResponse>(companyId, HttpMethod.Post, $"api/sales/recommendations/{recommendationId:D}/approve", new(note), cancellationToken);

    public Task<SalesRecommendationResponse> RetryRecommendationAsync(Guid companyId, Guid recommendationId, CancellationToken cancellationToken = default) =>
        SendAsync<object, SalesRecommendationResponse>(companyId, HttpMethod.Post, $"api/sales/recommendations/{recommendationId:D}/retry", new { }, cancellationToken);

    public Task<SalesFinanceHandoffResponse?> GetFinanceHandoffAsync(Guid companyId, Guid dealId, CancellationToken cancellationToken = default) =>
        GetAsync<SalesFinanceHandoffResponse>(companyId, $"api/sales/deals/{dealId:D}/finance-handoff", allowNotFound: true, cancellationToken);

    public Task<SalesFinanceHandoffResponse> ApproveFinanceHandoffAsync(Guid companyId, Guid dealId, string? note, CancellationToken cancellationToken = default) =>
        SendAsync<SalesActionRequest, SalesFinanceHandoffResponse>(companyId, HttpMethod.Post, $"api/sales/deals/{dealId:D}/finance-handoff/approve", new(note), cancellationToken);

    public Task<SalesFinanceHandoffResponse> RetryFinanceHandoffAsync(Guid companyId, Guid dealId, CancellationToken cancellationToken = default) =>
        SendAsync<object, SalesFinanceHandoffResponse>(companyId, HttpMethod.Post, $"api/sales/deals/{dealId:D}/finance-handoff/retry", new { }, cancellationToken);

    public async Task<IReadOnlyList<OutboundCampaignSummaryResponse>> ListCampaignsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        await GetAsync<List<OutboundCampaignSummaryResponse>>(companyId, "api/sales/campaigns", allowNotFound: false, cancellationToken) ?? [];

    public Task<OutboundCampaignDetailResponse?> GetCampaignAsync(Guid companyId, Guid campaignId, CancellationToken cancellationToken = default) =>
        GetAsync<OutboundCampaignDetailResponse>(companyId, $"api/sales/campaigns/{campaignId:D}", allowNotFound: true, cancellationToken);

    public Task<OutboundAudienceOptionsResponse> GetCampaignAudienceOptionsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        GetAsync<OutboundAudienceOptionsResponse>(companyId, "api/sales/campaigns/audience-options", allowNotFound: false, cancellationToken)!;

    public Task<OutboundCampaignDetailResponse> CreateCampaignAsync(Guid companyId, CreateOutboundCampaignRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<CreateOutboundCampaignRequest, OutboundCampaignDetailResponse>(companyId, HttpMethod.Post, "api/sales/campaigns", request, cancellationToken);

    public Task<OutboundCampaignDetailResponse> LaunchCampaignAsync(Guid companyId, Guid campaignId, CancellationToken cancellationToken = default) =>
        SendAsync<object, OutboundCampaignDetailResponse>(companyId, HttpMethod.Post, $"api/sales/campaigns/{campaignId:D}/launch", new { }, cancellationToken);

    public Task<OutboundCampaignDetailResponse> PauseCampaignAsync(Guid companyId, Guid campaignId, CancellationToken cancellationToken = default) =>
        SendAsync<object, OutboundCampaignDetailResponse>(companyId, HttpMethod.Post, $"api/sales/campaigns/{campaignId:D}/pause", new { }, cancellationToken);

    public Task<OutboundCampaignDetailResponse> StopCampaignAsync(Guid companyId, Guid campaignId, string? reason, CancellationToken cancellationToken = default) =>
        SendAsync<StopCampaignRequest, OutboundCampaignDetailResponse>(companyId, HttpMethod.Post, $"api/sales/campaigns/{campaignId:D}/stop", new(reason), cancellationToken);

    public Task<SequenceExecutionStepResponse> SaveCampaignDraftAsync(Guid companyId, Guid campaignId, Guid stepId, SaveSequenceDraftRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<SaveSequenceDraftRequest, SequenceExecutionStepResponse>(companyId, HttpMethod.Put, $"api/sales/campaigns/{campaignId:D}/steps/{stepId:D}/draft", request, cancellationToken);

    private async Task<T?> GetAsync<T>(Guid companyId, string uri, bool allowNotFound, CancellationToken cancellationToken)
    {
        if (_useOfflineMode)
        {
            throw new SalesApiException("Sales needs the backend API. Start the API project to review live tenant data.");
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
            throw new SalesApiException("The sales workspace could not reach the backend API.");
        }
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(Guid companyId, HttpMethod method, string uri, TRequest payload, CancellationToken cancellationToken)
    {
        if (_useOfflineMode)
        {
            throw new SalesApiException("Sales actions need the backend API. Start the API project before changing live tenant data.");
        }

        try
        {
            using var request = CreateCompanyRequest(companyId, method, uri, JsonContent.Create(payload, options: SerializerOptions));
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<TResponse>(SerializerOptions, cancellationToken)
                    ?? throw new SalesApiException("The sales API returned an empty response.");
            }

            throw await CreateExceptionAsync(response, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new SalesApiException("The sales workspace could not reach the backend API.");
        }
    }

    private static HttpRequestMessage CreateCompanyRequest(Guid companyId, HttpMethod method, string uri, HttpContent? content)
    {
        var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.TryAddWithoutValidation(CompanyContextHeaderName, companyId.ToString("D"));
        return request;
    }

    private static async Task<SalesApiException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentType?.MediaType is not ("application/json" or "application/problem+json"))
        {
            return new SalesApiException($"The sales request failed with status code {(int)response.StatusCode}.");
        }

        var problem = await response.Content.ReadFromJsonAsync<ApiProblemResponse>(SerializerOptions, cancellationToken);
        return problem?.Errors is { Count: > 0 }
            ? new SalesApiException(FormatProblem(problem), problem.Errors)
            : new SalesApiException(problem?.Detail ?? problem?.Title ?? "The sales request failed.");
    }

    private static string FormatProblem(ApiProblemResponse problem)
    {
        var firstError = problem.Errors?.SelectMany(x => x.Value).FirstOrDefault();
        return firstError ?? problem.Detail ?? problem.Title ?? "The sales request failed.";
    }

    private sealed class ApiProblemResponse
    {
        public string? Title { get; set; }
        public string? Detail { get; set; }
        public Dictionary<string, string[]>? Errors { get; set; }
    }
}

public sealed class SalesApiException : Exception
{
    public SalesApiException(string message, IReadOnlyDictionary<string, string[]>? errors = null) : base(message)
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]>? Errors { get; }
}

public sealed record SalesDashboardResponse(
    decimal PipelineValue,
    string Currency,
    int NewLeads,
    int HotLeads,
    int DealsNeedingAttention,
    decimal ForecastRevenue,
    IReadOnlyList<SalesRecommendationResponse> AgentRecommendations,
    IReadOnlyList<SalesActivityResponse> RecentActivity);

public sealed record PerformanceFunnelCounts(int Sent, int Delivered, int Bounced, int Opened, int Replied, int DealCreated, int Converted);
public sealed record PerformanceFunnelRates(decimal DeliveryRate, decimal OpenRate, decimal ReplyRate, decimal ConversionRate);
public sealed record RiskDistributionSummary(int Unknown, int Low, int Medium, int High);
public sealed record RevenueForecastWindowResponse(int Days, decimal GrossPipelineValue, decimal ExpectedRevenue, int DealCount);
public sealed record RevenueForecastSnapshotResponse(Guid Id, Guid CompanyId, DateTime AsOfUtc, DateTime CalculatedUtc, string Currency, IReadOnlyList<RevenueForecastWindowResponse> Windows, RiskDistributionSummary RiskDistribution);
public sealed record CampaignPerformanceListItemResponse(Guid CampaignId, string CampaignName, Guid? SequenceId, PerformanceFunnelCounts Counts, PerformanceFunnelRates Rates);
public sealed record VariantPerformanceSummaryResponse(Guid? CampaignId, Guid? SequenceId, Guid? SequenceStepId, string VariantKey, PerformanceFunnelCounts Counts, PerformanceFunnelRates Rates);
public sealed record SalesAnalyticsDashboardResponse(
    Guid CompanyId,
    PerformanceFunnelCounts Funnel,
    PerformanceFunnelRates Rates,
    IReadOnlyList<CampaignPerformanceListItemResponse> Campaigns,
    IReadOnlyList<VariantPerformanceSummaryResponse> Variants);

public sealed record SalesLeadSummaryResponse(
    Guid Id,
    string Title,
    string Status,
    string Temperature,
    string? SourceEmail,
    string QualificationStatus,
    decimal? ConfidenceScore,
    string SuggestedNextAction,
    decimal? EstimatedValue,
    string? Currency,
    string? Fit,
    string? Priority,
    DateTime? QualifiedUtc,
    Guid? QualifiedByUserId,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record SalesLeadDetailResponse(
    Guid Id,
    string Title,
    string Status,
    string QualificationStatus,
    string Temperature,
    string? SourceEmail,
    string? ContactName,
    string? CustomerCompanyName,
    decimal? EstimatedValue,
    string? Currency,
    string SuggestedNextAction,
    string? Fit,
    string? Priority,
    DateTime? QualifiedUtc,
    Guid? QualifiedByUserId,
    IReadOnlyList<SalesActivityResponse> Activities,
    IReadOnlyList<SalesRecommendationResponse> Recommendations);

public sealed record SalesDealSummaryResponse(Guid Id, string Title, Guid StageId, string StageName, string Status, decimal Amount, string Currency, string? CustomerCompanyName, string? ContactName, DateTime? ExpectedCloseUtc, DateTime UpdatedUtc);
public sealed record SalesDealDetailResponse(Guid Id, string Title, Guid StageId, string StageName, string Status, decimal Amount, string Currency, string Summary, string? ContactName, string? ContactEmail, string? CustomerCompanyName, string AgentAnalysis, string SuggestedReply, IReadOnlyList<SalesActivityResponse> Activities, IReadOnlyList<SalesRecommendationResponse> Recommendations, IReadOnlyList<string> AvailableActions, SalesFinanceHandoffResponse? FinanceHandoff, CustomerMemoryContext? CustomerMemory = null);
public sealed record SalesFinanceHandoffResponse(
    Guid Id,
    Guid DealId,
    string Status,
    string ApprovalStatus,
    string ExecutionStatus,
    string Summary,
    string DocumentType,
    string ExternalSystem,
    string? ExternalDocumentId,
    string? ExternalDocumentNumber,
    Guid? ApprovalId,
    Guid? WriteRequestId,
    string IdempotencyKey,
    string? FailureSummary,
    bool CanRetry,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    DateTime? ApprovedUtc,
    DateTime? ExecutedUtc,
    DateTime? FailedUtc,
    DateTime? RetriedUtc);
public sealed record CustomerMemoryContext(
    Guid CompanyId,
    Guid ContactId,
    string ContactName,
    string ContactEmail,
    string? CustomerCompanyName,
    string? Industry,
    string AiSummary,
    string RelationshipMemory,
    string? LastOutreachSummary,
    decimal EngagementScore,
    IReadOnlyList<CustomerConversationMemory> PastConversations,
    IReadOnlyList<CustomerDealMemory> PreviousDeals,
    IReadOnlyList<CustomerMemorySignal> Preferences,
    IReadOnlyList<CustomerMemorySignal> PriceSensitivityIndicators,
    IReadOnlyList<CustomerMemorySignal> IndustrySignals,
    IReadOnlyList<OfferExposureMemory> OfferExposureHistory,
    DateTime RefreshedUtc);
public sealed record CustomerConversationMemory(Guid? ConversationId, string Summary, DateTime OccurredUtc, string SourceType);
public sealed record CustomerDealMemory(Guid DealId, string Title, string Status, decimal Amount, string Currency, DateTime? ClosedUtc, string Summary);
public sealed record CustomerMemorySignal(string Key, string Value, decimal Confidence, DateTime ObservedUtc, string SourceSummary);
public sealed record OfferExposureMemory(string OfferKey, Guid? CampaignId, Guid? DealId, DateTime OccurredUtc, string SourceType, string Summary);
public sealed record SalesPipelineResponse(IReadOnlyList<SalesPipelineStageResponse> Stages);
public sealed record SalesPipelineStageResponse(Guid StageId, string Name, int DisplayOrder, decimal TotalValue, int DealCount, IReadOnlyList<SalesDealSummaryResponse> Deals);
public sealed record SalesActivityResponse(Guid Id, string ActivityType, string Summary, string Status, DateTime OccurredUtc, Guid? LeadId, Guid? DealId);
public sealed record SalesEmailTimelineResponse(Guid Id, string ProviderMessageId, string Status, string? DetectedIntent, string? ProductOrServiceInterest, decimal? Confidence, string? Rationale, DateTime OccurredUtc, Guid? LeadId, Guid? DealId);
public sealed record SalesRecommendationResponse(Guid Id, string Recommendation, string Rationale, string Status, Guid? LeadId, Guid? DealId, string Category, string TriggerCondition, string ActionType, string RiskLevel, bool RequiresApproval, string ApprovalStatus, string ExecutionStatus, string? FailureSummary, DateTime CreatedUtc);
public sealed record SalesActionRequest(string? Note);
public sealed record UpdateLeadQualificationRequest(string Fit, string Temperature, string Priority, string SuggestedNextAction, string? Note);
public sealed record ConvertLeadRequest(decimal Amount, string Currency, DateTime? ExpectedCloseUtc, string? Note);
public sealed record ChangeDealStageRequest(Guid StageId, string? Note);
public sealed record CreateOutboundCampaignRequest(string Name, string? Description, string AudienceType, IReadOnlyList<Guid> ContactIds, OutboundPolicyRequest Policy, IReadOnlyList<CreateSequenceStepRequest> Steps);
public sealed record OutboundPolicyRequest(bool OutboundEnabled, int MaxEmailsPerDay, bool ApprovalRequired);
public sealed record CreateSequenceStepRequest(int StepOrder, int DelayDays, string Subject, string Body, bool AiPersonalizationEnabled);
public sealed record StopCampaignRequest(string? Reason);
public sealed record SaveSequenceDraftRequest(string Subject, string Body);
public sealed record OutboundCampaignSummaryResponse(Guid Id, string Name, string Status, int AudienceCount, int PendingSteps, int SentSteps, int BouncedSteps, DateTime UpdatedUtc);
public sealed record OutboundCampaignDetailResponse(Guid Id, string Name, string? Description, string Status, string AudienceType, OutboundPolicyResponse Policy, IReadOnlyList<OutboundCampaignContactResponse> Audience, IReadOnlyList<SequenceStepResponse> Steps, IReadOnlyList<SequenceExecutionResponse> Executions, DateTime CreatedUtc, DateTime UpdatedUtc);
public sealed record OutboundPolicyResponse(bool OutboundEnabled, int MaxEmailsPerDay, bool ApprovalRequired);
public sealed record OutboundCampaignContactResponse(Guid ContactId, string ContactName, string Email, string Status, int? CurrentStepOrder, DateTime EnrolledUtc);
public sealed record SequenceStepResponse(Guid Id, int StepOrder, int DelayDays, string Subject, bool AiPersonalizationEnabled);
public sealed record SequenceExecutionResponse(Guid Id, Guid ContactId, string ContactName, string Status, string? StopReason, IReadOnlyList<SequenceExecutionStepResponse> Steps);
public sealed record SequenceExecutionStepResponse(Guid Id, int StepOrder, string Status, DateTime ScheduledSendUtc, DateTime? SentUtc, string? ProviderMessageId, string DeliveryStatus, string? BounceStatus, string? CancellationReason = null, string? CancellationSourceReference = null, string? OriginalGeneratedSubject = null, string? OriginalGeneratedBody = null, string? CurrentDraftSubject = null, string? CurrentDraftBody = null, string? FinalSentSubject = null, string? FinalSentBody = null, DateTime? GeneratedDraftUtc = null, DateTime? DraftUpdatedUtc = null);
public sealed record OutboundAudienceOptionsResponse(IReadOnlyList<OutboundAudienceContactResponse> Contacts, IReadOnlyList<OutboundAudienceSourceResponse> Sources);
public sealed record OutboundAudienceContactResponse(Guid ContactId, string ContactName, string Email, string? CustomerCompanyName, IReadOnlyList<string> SourceTypes);
public sealed record OutboundAudienceSourceResponse(string SourceType, string Label, int ContactCount);
