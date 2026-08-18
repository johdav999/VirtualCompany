using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public sealed partial class SalesApiClient
{
    private const string CompanyContextHeaderName = "X-Company-Id";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly bool _useOfflineMode;
    private readonly IApiProblemMessageResolver? _problemResolver;

    public SalesApiClient(HttpClient httpClient, bool useOfflineMode = false, IApiProblemMessageResolver? problemResolver = null)
    {
        _httpClient = httpClient;
        _useOfflineMode = useOfflineMode;
        _problemResolver = problemResolver;
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

    public async Task<IReadOnlyList<SalesLeadSourceEmailResponse>> ListLeadSourceEmailsAsync(Guid companyId, Guid leadId, CancellationToken cancellationToken = default) =>
        await GetAsync<List<SalesLeadSourceEmailResponse>>(companyId, $"api/sales/leads/{leadId:D}/source-emails", allowNotFound: false, cancellationToken) ?? [];
    public async Task<IReadOnlyList<SalesCalendarConnectionResponse>> ListCalendarConnectionsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        await GetAsync<List<SalesCalendarConnectionResponse>>(companyId, "api/sales/calendar-connections", allowNotFound: false, cancellationToken) ?? [];

    public Task<SalesMeetingAvailabilityResponse> GetCalendarAvailabilityAsync(Guid companyId, SalesMeetingAvailabilityRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<SalesMeetingAvailabilityRequest, SalesMeetingAvailabilityResponse>(companyId, HttpMethod.Post, "api/sales/calendar-availability", request, cancellationToken);

    public async Task<IReadOnlyList<SalesMeetingInvitationResponse>> ListMeetingInvitationsAsync(Guid companyId, Guid leadId, CancellationToken cancellationToken = default) =>
        await GetAsync<List<SalesMeetingInvitationResponse>>(companyId, $"api/sales/leads/{leadId:D}/meeting-invitations", allowNotFound: false, cancellationToken) ?? [];

    public Task<SalesMeetingInvitationResponse> CreateMeetingInvitationAsync(Guid companyId, Guid leadId, CreateSalesMeetingInvitationRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<CreateSalesMeetingInvitationRequest, SalesMeetingInvitationResponse>(companyId, HttpMethod.Post, $"api/sales/leads/{leadId:D}/meeting-invitations", request, cancellationToken);
    public async Task<IReadOnlyList<SalesMeetingChangeRequestResponse>> ListMeetingChangesAsync(Guid companyId, Guid invitationId, CancellationToken cancellationToken = default) =>
        await GetAsync<List<SalesMeetingChangeRequestResponse>>(companyId, $"api/sales/meeting-invitations/{invitationId:D}/changes", allowNotFound: false, cancellationToken) ?? [];
    public Task<SalesMeetingChangeRequestResponse> RequestMeetingRescheduleAsync(Guid companyId, Guid invitationId, CreateSalesMeetingRescheduleRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<CreateSalesMeetingRescheduleRequest, SalesMeetingChangeRequestResponse>(companyId, HttpMethod.Post, $"api/sales/meeting-invitations/{invitationId:D}/reschedule", request, cancellationToken);
    public Task<SalesMeetingChangeRequestResponse> RequestMeetingCancellationAsync(Guid companyId, Guid invitationId, CancellationToken cancellationToken = default) =>
        SendAsync<object, SalesMeetingChangeRequestResponse>(companyId, HttpMethod.Post, $"api/sales/meeting-invitations/{invitationId:D}/cancel", new { }, cancellationToken);
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

    public Task<CampaignInitiativeResponse?> GetCampaignInitiativeAsync(Guid companyId, Guid campaignId, CancellationToken cancellationToken = default) =>
        GetAsync<CampaignInitiativeResponse>(companyId, $"api/sales/campaigns/{campaignId:D}/initiative", allowNotFound: true, cancellationToken);

    public Task<CampaignReadinessResponse?> GetCampaignReadinessAsync(Guid companyId, Guid campaignId, CancellationToken cancellationToken = default) =>
        GetAsync<CampaignReadinessResponse>(companyId, $"api/sales/campaigns/{campaignId:D}/readiness", allowNotFound: true, cancellationToken);

    public async Task<IReadOnlyList<CampaignActivityResponse>> ListCampaignActivitiesAsync(Guid companyId, Guid campaignId, CancellationToken cancellationToken = default) =>
        await GetAsync<List<CampaignActivityResponse>>(companyId, $"api/sales/campaigns/{campaignId:D}/activities", allowNotFound: false, cancellationToken) ?? [];

    public Task<CampaignPerformanceResponse?> GetCampaignPerformanceAsync(Guid companyId, Guid campaignId, CancellationToken cancellationToken = default) =>
        GetAsync<CampaignPerformanceResponse>(companyId, $"api/sales/campaigns/{campaignId:D}/performance", allowNotFound: true, cancellationToken);

    public Task<CampaignPerformanceResponse> CaptureCampaignPerformanceSnapshotAsync(
        Guid companyId, Guid campaignId, CancellationToken cancellationToken = default) =>
        SendAsync<object, CampaignPerformanceResponse>(
            companyId, HttpMethod.Post, $"api/sales/campaigns/{campaignId:D}/performance-snapshots", new { }, cancellationToken);

    public async Task<IReadOnlyList<CampaignSegmentResponse>> ListCampaignSegmentsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        await GetAsync<List<CampaignSegmentResponse>>(companyId, "api/sales/campaigns/segments", allowNotFound: false, cancellationToken) ?? [];

    public Task<CampaignAudiencePreviewResponse?> PreviewCampaignSegmentAsync(Guid companyId, Guid segmentId, CancellationToken cancellationToken = default) =>
        GetAsync<CampaignAudiencePreviewResponse>(companyId, $"api/sales/campaigns/segments/{segmentId:D}/preview", allowNotFound: true, cancellationToken);

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

    public async Task<IReadOnlyList<IcpProfileResponse>> ListIcpProfilesAsync(Guid companyId, CancellationToken cancellationToken = default) => await GetAsync<List<IcpProfileResponse>>(companyId, "api/sales/prospecting/icp", false, cancellationToken) ?? [];
    public Task<IcpSuggestionResponse> SuggestIcpAsync(Guid companyId, SuggestIcpRequest request, CancellationToken cancellationToken = default) => SendAsync<SuggestIcpRequest, IcpSuggestionResponse>(companyId, HttpMethod.Post, "api/sales/prospecting/icp/suggest", request, cancellationToken);
    public Task<IcpProfileResponse> CreateIcpProfileAsync(Guid companyId, SaveIcpProfileRequest request, CancellationToken cancellationToken = default) => SendAsync<SaveIcpProfileRequest, IcpProfileResponse>(companyId, HttpMethod.Post, "api/sales/prospecting/icp", request, cancellationToken);
    public Task<IcpProfileResponse> UpdateIcpProfileAsync(Guid companyId, Guid id, SaveIcpProfileRequest request, CancellationToken cancellationToken = default) => SendAsync<SaveIcpProfileRequest, IcpProfileResponse>(companyId, HttpMethod.Put, $"api/sales/prospecting/icp/{id:D}", request, cancellationToken);
    public Task<IcpProfileResponse> ActivateIcpProfileAsync(Guid companyId, Guid id, CancellationToken cancellationToken = default) => SendAsync<object, IcpProfileResponse>(companyId, HttpMethod.Post, $"api/sales/prospecting/icp/{id:D}/activate", new { }, cancellationToken);
    public Task<IcpProfileResponse> CloneIcpProfileAsync(Guid companyId, Guid id, CancellationToken cancellationToken = default) => SendAsync<object, IcpProfileResponse>(companyId, HttpMethod.Post, $"api/sales/prospecting/icp/{id:D}/clone", new { }, cancellationToken);
    public Task<SourcePolicyResponse> GetProspectSourcePolicyAsync(Guid companyId, CancellationToken cancellationToken = default) => GetAsync<SourcePolicyResponse>(companyId, "api/sales/prospecting/sources", false, cancellationToken)!;
    public Task<SourcePolicyResponse> SaveProspectSourcePolicyAsync(Guid companyId, SaveSourcePolicyRequest request, CancellationToken cancellationToken = default) => SendAsync<SaveSourcePolicyRequest, SourcePolicyResponse>(companyId, HttpMethod.Put, "api/sales/prospecting/sources", request, cancellationToken);
    public async Task<IReadOnlyList<ProspectingRunResponse>> ListProspectingRunsAsync(Guid companyId, CancellationToken cancellationToken = default) => await GetAsync<List<ProspectingRunResponse>>(companyId, "api/sales/prospecting/runs", false, cancellationToken) ?? [];
    public Task<ProspectingRunResponse> CreateProspectingRunAsync(Guid companyId, CreateProspectingRunRequest request, CancellationToken cancellationToken = default) => SendAsync<CreateProspectingRunRequest, ProspectingRunResponse>(companyId, HttpMethod.Post, "api/sales/prospecting/runs", request, cancellationToken);
    public Task<ProspectingRunResponse> StartProspectingRunAsync(Guid companyId, Guid id, CancellationToken cancellationToken = default) => SendAsync<object, ProspectingRunResponse>(companyId, HttpMethod.Post, $"api/sales/prospecting/runs/{id:D}/start", new { }, cancellationToken);
    public Task<ProspectingRunResponse> ChangeProspectingRunAsync(Guid companyId, Guid id, string action, CancellationToken cancellationToken = default) => SendAsync<object, ProspectingRunResponse>(companyId, HttpMethod.Post, $"api/sales/prospecting/runs/{id:D}/{action}", new { }, cancellationToken);
    public async Task<ProspectImportResponse> ImportProspectsAsync(Guid companyId, Guid runId, Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent(); using var file = new StreamContent(stream); content.Add(file, "file", fileName);
        using var request = CreateCompanyRequest(companyId, HttpMethod.Post, $"api/sales/prospecting/runs/{runId:D}/import", content); using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw await CreateExceptionAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ProspectImportResponse>(SerializerOptions, cancellationToken) ?? throw new SalesApiException("The import returned no result.");
    }
    public Task<ProspectPageResponse> ListProspectsAsync(Guid companyId, string? search = null, string? status = null, int page = 1, CancellationToken cancellationToken = default) => GetAsync<ProspectPageResponse>(companyId, $"api/sales/prospecting/accounts?search={Uri.EscapeDataString(search ?? "")}&status={Uri.EscapeDataString(status ?? "")}&page={page}&pageSize=50", false, cancellationToken)!;
    public Task<ProspectAccountResponse?> GetProspectAsync(Guid companyId, Guid id, CancellationToken cancellationToken = default) => GetAsync<ProspectAccountResponse>(companyId, $"api/sales/prospecting/accounts/{id:D}", true, cancellationToken);
    public Task<ProspectAccountResponse> ReviewProspectAsync(Guid companyId, Guid id, string action, string? reason, CancellationToken cancellationToken = default) => SendAsync<ReviewProspectRequest, ProspectAccountResponse>(companyId, HttpMethod.Post, $"api/sales/prospecting/accounts/{id:D}/review", new(action, reason), cancellationToken);
    public Task<ProspectAccountResponse> RefreshProspectAsync(Guid companyId, Guid id, CancellationToken cancellationToken = default) => SendAsync<object, ProspectAccountResponse>(companyId, HttpMethod.Post, $"api/sales/prospecting/accounts/{id:D}/refresh", new { }, cancellationToken);
    public Task<LeadConversionResponse> ConvertProspectAsync(Guid companyId, Guid id, Guid? contactId, CancellationToken cancellationToken = default) => SendAsync<object, LeadConversionResponse>(companyId, HttpMethod.Post, $"api/sales/prospecting/accounts/{id:D}/convert{(contactId.HasValue ? $"?contactId={contactId:D}" : "")}", new { }, cancellationToken);
    public Task<ProspectContactResponse> AddProspectContactAsync(Guid companyId, Guid accountId, SaveProspectContactRequest request, CancellationToken cancellationToken = default) => SendAsync<SaveProspectContactRequest, ProspectContactResponse>(companyId, HttpMethod.Post, $"api/sales/prospecting/accounts/{accountId:D}/contacts", request, cancellationToken);
    public Task<ProspectSignalResponse> AddProspectSignalAsync(Guid companyId, Guid accountId, SaveProspectSignalRequest request, CancellationToken cancellationToken = default) => SendAsync<SaveProspectSignalRequest, ProspectSignalResponse>(companyId, HttpMethod.Post, $"api/sales/prospecting/accounts/{accountId:D}/signals", request, cancellationToken);
    public async Task<IReadOnlyList<SuppressionResponse>> ListSuppressionsAsync(Guid companyId, CancellationToken cancellationToken = default) => await GetAsync<List<SuppressionResponse>>(companyId, "api/sales/prospecting/suppressions", false, cancellationToken) ?? [];
    public Task<SuppressionResponse> AddSuppressionAsync(Guid companyId, SaveSuppressionRequest request, CancellationToken cancellationToken = default) => SendAsync<SaveSuppressionRequest, SuppressionResponse>(companyId, HttpMethod.Post, "api/sales/prospecting/suppressions", request, cancellationToken);
    public Task<LeadGenerationMetricsResponse> GetLeadGenerationMetricsAsync(Guid companyId, CancellationToken cancellationToken = default) => GetAsync<LeadGenerationMetricsResponse>(companyId, "api/sales/prospecting/metrics", false, cancellationToken)!;

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

    private async Task<SalesApiException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentType?.MediaType is not ("application/json" or "application/problem+json"))
        {
            return new SalesApiException($"The sales request failed with status code {(int)response.StatusCode}.");
        }

        var problem = await response.Content.ReadFromJsonAsync<ApiProblemResponse>(SerializerOptions, cancellationToken);
        return problem?.Errors is { Count: > 0 }
            ? new SalesApiException(_problemResolver?.Resolve(problem, FormatProblem(problem)) ?? FormatProblem(problem), problem.Errors)
            : new SalesApiException(_problemResolver?.Resolve(problem, "The sales request failed.") ?? problem?.Detail ?? problem?.Title ?? "The sales request failed.");
    }

    private static string FormatProblem(ApiProblemResponse problem)
    {
        var firstError = problem.Errors?.SelectMany(x => x.Value).FirstOrDefault();
        return firstError ?? problem.Detail ?? problem.Title ?? "The sales request failed.";
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
    IReadOnlyList<SalesDealSummaryResponse> DealsRequiringAction,
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

public sealed record SalesCalendarConnectionResponse(Guid Id, string Provider, string EmailAddress, string? DisplayName, string Status, bool HasCalendarPermission, bool RequiresReconnect);
public sealed record CreateSalesMeetingInvitationRequest(Guid CalendarConnectionId, DateTime StartsUtc, DateTime EndsUtc, string TimeZoneId, string Title, string Description, string? Location, bool CreateOnlineMeeting = true);
public sealed record SalesMeetingAvailabilityRequest(Guid CalendarConnectionId, DateTime FromUtc, DateTime ToUtc, string TimeZoneId, int DurationMinutes = 30);
public sealed record CalendarBusyWindow(DateTime StartsUtc, DateTime EndsUtc);
public sealed record CalendarAvailableSlot(DateTime StartsUtc, DateTime EndsUtc);
public sealed record SalesMeetingAvailabilityResponse(Guid CalendarConnectionId, string Provider, IReadOnlyList<CalendarBusyWindow> BusyWindows, IReadOnlyList<CalendarAvailableSlot> SuggestedSlots);
public sealed record CreateSalesMeetingRescheduleRequest(DateTime StartsUtc, DateTime EndsUtc, string TimeZoneId, string Title, string Description, string? Location, bool CreateOnlineMeeting = true);
public sealed record SalesMeetingChangeRequestResponse(
    Guid Id, Guid InvitationId, string Operation, string Status,
    DateTime? StartsUtc, DateTime? EndsUtc, string? TimeZoneId,
    string? Title, string? Description, string? Location, bool? CreateOnlineMeeting,
    Guid? ApprovalRequestId, int ExecutionAttemptCount,
    string? LastErrorCode, string? LastErrorSummary,
    DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? CompletedUtc);
public sealed record SalesMeetingInvitationResponse(
    Guid Id, Guid LeadId, Guid? DealId, Guid? ContactId, Guid CalendarConnectionId,
    string Provider, string OrganizerEmail, string AttendeeEmail, string? AttendeeName,
    string Title, string Description, DateTime StartsUtc, DateTime EndsUtc,
    string TimeZoneId, string? Location, bool CreateOnlineMeeting, string Status,
    Guid? ApprovalRequestId, string? ExternalEventId, string? ProviderWebUrl,
    string? OnlineMeetingUrl, int ExecutionAttemptCount, string? LastErrorCode,
    string? LastErrorSummary, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? ScheduledUtc,
    string ConfirmationStatus, Guid? ConfirmationMailboxConnectionId,
    string? ConfirmationProviderMessageId, string? ConfirmationProviderThreadId,
    string ConfirmationThreadingMode, int ConfirmationAttemptCount, string? ConfirmationErrorCode,
    string? ConfirmationErrorSummary, DateTime? ConfirmationSentUtc);
public sealed record SalesLeadSourceEmailResponse(
    Guid LinkId,
    string ProviderMessageId,
    string? InternetMessageId,
    string? Subject,
    string? SenderName,
    string? SenderEmail,
    IReadOnlyList<string> Recipients,
    DateTime? ReceivedUtc,
    string? PlainTextBody,
    string? DetectedIntent,
    string? ProductOrServiceInterest,
    decimal? Confidence,
    string? ClassificationEvidence,
    string? SafeFailureMessage);
public sealed record SalesDealSummaryResponse(Guid Id, string Title, Guid StageId, string StageName, string Status, decimal Amount, string Currency, string? CustomerCompanyName, string? ContactName, DateTime? ExpectedCloseUtc, DateTime UpdatedUtc);
public sealed record SalesDealDetailResponse(Guid Id, string Title, Guid StageId, string StageName, string Status, decimal Amount, string Currency, string Summary, string? ContactName, string? ContactEmail, string? CustomerCompanyName, string AgentAnalysis, string SuggestedReply, IReadOnlyList<SalesActivityResponse> Activities, IReadOnlyList<SalesRecommendationResponse> Recommendations, IReadOnlyList<string> AvailableActions, SalesFinanceHandoffResponse? FinanceHandoff, CustomerMemoryContext? CustomerMemory = null, Guid? SourceLeadId = null);
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
public sealed record CampaignInitiativeResponse(Guid Id, string Name, string CampaignType, string LifecycleStatus, string? Description,
    Guid? OwnerUserId, Guid? OwnerAgentId, CampaignObjectiveResponse? PrimaryObjective, DateTime? PlanningStartsUtc,
    DateTime? ScheduledLaunchUtc, DateTime? EndsUtc, DateTime? ReviewDueUtc, string? TimeZoneId, decimal? PlannedBudget,
    string? BudgetCurrency, bool LegacySetupRequired, long Version, IReadOnlyList<string> MissingRequirements,
    CampaignMarketingContextResponse? MarketingContext = null);
public sealed record CampaignMarketingContextResponse(Guid PlanId, string PlanName, int PlanVersion, Guid? ObjectiveId,
    string ObjectiveContribution, IReadOnlyList<Guid> SegmentVersionIds, IReadOnlyList<string> EvidenceReferences,
    Guid? PlanApprovalRequestId);
public sealed record CampaignObjectiveResponse(string Type, decimal Target, string Unit, DateTime TargetUtc);
public sealed record CampaignReadinessResponse(Guid CampaignId, string LifecycleStatus, bool IsReady, long Version, IReadOnlyList<string> MissingRequirements);
public sealed record CampaignActivityResponse(Guid Id, string Name, string ActivityType, string Channel, string ExecutionMode, string Status,
    DateTime PlannedStartUtc, DateTime DueUtc, Guid? OwnerUserId, Guid? OwnerAgentId, Guid? DependsOnActivityId,
    string? RequiredToolCapability, int AttemptCount, string? ResultSummary, string? FailureReason);
public sealed record CampaignPerformanceResponse(Guid CampaignId, string LifecycleStatus, CampaignObjectiveResponse? Objective,
    decimal? ObjectiveProgress, int Audience, int Sent, int Delivered, int Replied, int Bounced, int Opportunities, int WonDeals,
    IReadOnlyList<CampaignCurrencyAmountResponse> DirectRevenue, IReadOnlyList<CampaignCurrencyAmountResponse> PlannedBudget,
    IReadOnlyList<CampaignCurrencyAmountResponse> Costs, IReadOnlyList<CampaignMetricResponse> Metrics,
    IReadOnlyList<CampaignAttributionEvidenceResponse> Attribution, IReadOnlyList<CampaignEventResponse> Timeline, DateTime ObservedUtc);
public sealed record CampaignCurrencyAmountResponse(decimal Amount, string Currency, string Classification);
public sealed record CampaignMetricResponse(string Key, string Label, decimal? Value, string Unit, decimal? Target,
    int DefinitionVersion, string EvidenceSummary);
public sealed record CampaignAttributionEvidenceResponse(Guid SubjectId, string SubjectType, string Model,
    string Classification, decimal Confidence, int WindowDays, IReadOnlyList<Guid> SourceEventIds);
public sealed record CampaignEventResponse(Guid Id, string EventType, DateTime OccurredUtc, string Summary,
    string SourceType, Guid? ContactId, Guid? DealId, Guid? ActivityId);
public sealed record CampaignSegmentResponse(Guid Id, string Name, string SegmentKind, int Version, bool IsActive, string? Industry,
    string? Country, int? MinEmployees, int? MaxEmployees, string? BuyingRole, string? CustomerLifecycle, string? ProductInterest,
    string? PreferredLanguage, bool RequireCommunicationPermission, bool ExcludeOpenCriticalSupportCases);
public sealed record CampaignAudiencePreviewMemberResponse(Guid ContactId, string ContactName, string Email, Guid? CustomerCompanyId,
    string? CustomerCompanyName, string EligibilityStatus, string Reason, string ConsentStatus, string? CommunicationLanguage);
public sealed record CampaignAudiencePreviewResponse(Guid SegmentId, int SegmentVersion, int Eligible, int Excluded, int Suppressed,
    int Ambiguous, int MissingData, IReadOnlyList<CampaignAudiencePreviewMemberResponse> Members);
public sealed record OutboundPolicyResponse(bool OutboundEnabled, int MaxEmailsPerDay, bool ApprovalRequired);
public sealed record OutboundCampaignContactResponse(Guid ContactId, string ContactName, string Email, string Status, int? CurrentStepOrder, DateTime EnrolledUtc);
public sealed record SequenceStepResponse(Guid Id, int StepOrder, int DelayDays, string Subject, bool AiPersonalizationEnabled);
public sealed record SequenceExecutionResponse(Guid Id, Guid ContactId, string ContactName, string Status, string? StopReason, IReadOnlyList<SequenceExecutionStepResponse> Steps);
public sealed record SequenceExecutionStepResponse(Guid Id, int StepOrder, string Status, DateTime ScheduledSendUtc, DateTime? SentUtc, string? ProviderMessageId, string DeliveryStatus, string? BounceStatus, string? CancellationReason = null, string? CancellationSourceReference = null, string? OriginalGeneratedSubject = null, string? OriginalGeneratedBody = null, string? CurrentDraftSubject = null, string? CurrentDraftBody = null, string? FinalSentSubject = null, string? FinalSentBody = null, DateTime? GeneratedDraftUtc = null, DateTime? DraftUpdatedUtc = null);
public sealed record OutboundAudienceOptionsResponse(IReadOnlyList<OutboundAudienceContactResponse> Contacts, IReadOnlyList<OutboundAudienceSourceResponse> Sources);
public sealed record OutboundAudienceContactResponse(Guid ContactId, string ContactName, string Email, string? CustomerCompanyName, IReadOnlyList<string> SourceTypes);
public sealed record OutboundAudienceSourceResponse(string SourceType, string Label, int ContactCount);
public sealed record SuggestIcpRequest(Guid AgentId, string? Focus = null);
public sealed record IcpSuggestionEvidenceResponse(string SourceId, string Type, string Title);
public sealed record IcpSuggestionResponse(Guid RunId, Guid AgentId, string AgentName, SaveIcpProfileRequest Profile, string Rationale, decimal Confidence, IReadOnlyList<IcpSuggestionEvidenceResponse> Evidence, IReadOnlyList<string> MissingEvidence, bool RequiresReview);
public sealed class SaveIcpProfileRequest(string name, string countries, string industries, int? employeeMin, int? employeeMax, decimal? revenueMin, decimal? revenueMax, string buyerRoles, string technologies, string painHypotheses, string positiveCriteria, string disqualifiers) { public string Name { get; set; } = name; public string Countries { get; set; } = countries; public string Industries { get; set; } = industries; public int? EmployeeMin { get; set; } = employeeMin; public int? EmployeeMax { get; set; } = employeeMax; public decimal? RevenueMin { get; set; } = revenueMin; public decimal? RevenueMax { get; set; } = revenueMax; public string BuyerRoles { get; set; } = buyerRoles; public string Technologies { get; set; } = technologies; public string PainHypotheses { get; set; } = painHypotheses; public string PositiveCriteria { get; set; } = positiveCriteria; public string Disqualifiers { get; set; } = disqualifiers; }
public sealed record IcpProfileResponse(Guid Id, string Name, int Version, string Status, string Countries, string Industries, int? EmployeeMin, int? EmployeeMax, decimal? RevenueMin, decimal? RevenueMax, string BuyerRoles, string Technologies, string PainHypotheses, string PositiveCriteria, string Disqualifiers, DateTime UpdatedUtc, DateTime? ActivatedUtc);
public sealed record ProspectProviderCapabilitiesResponse(bool AccountSearch, bool ContactSearch, bool Enrichment, bool Signals, bool IsPaid);
public sealed record ProspectProviderDescriptorResponse(string Key, string Label, ProspectProviderCapabilitiesResponse Capabilities, string Health);
public sealed class SaveSourcePolicyRequest(string enabledSources, string allowedCountries, string allowedFields, decimal perRunBudget, decimal monthlyBudget, decimal approvalThreshold, int retentionDays, int refreshDays) { public string EnabledSources { get; set; } = enabledSources; public string AllowedCountries { get; set; } = allowedCountries; public string AllowedFields { get; set; } = allowedFields; public decimal PerRunBudget { get; set; } = perRunBudget; public decimal MonthlyBudget { get; set; } = monthlyBudget; public decimal ApprovalThreshold { get; set; } = approvalThreshold; public int RetentionDays { get; set; } = retentionDays; public int RefreshDays { get; set; } = refreshDays; }
public sealed record SourcePolicyResponse(Guid Id, int Version, string EnabledSources, string AllowedCountries, string AllowedFields, decimal PerRunBudget, decimal MonthlyBudget, decimal ApprovalThreshold, int RetentionDays, int RefreshDays, decimal ReservedThisMonth, decimal ActualThisMonth, IReadOnlyList<ProspectProviderDescriptorResponse> Providers);
public sealed class CreateProspectingRunRequest(Guid icpProfileId, string name, int accountLimit, int contactLimit, string sources, string geography, int freshnessDays, decimal estimatedCost, string? schedule) { public Guid IcpProfileId { get; set; } = icpProfileId; public string Name { get; set; } = name; public int AccountLimit { get; set; } = accountLimit; public int ContactLimit { get; set; } = contactLimit; public string Sources { get; set; } = sources; public string Geography { get; set; } = geography; public int FreshnessDays { get; set; } = freshnessDays; public decimal EstimatedCost { get; set; } = estimatedCost; public string? Schedule { get; set; } = schedule; }
public sealed record ProspectingRunResponse(Guid Id, Guid IcpProfileId, string Name, string Status, string CurrentStep, int AccountLimit, int ContactLimit, int AccountsFound, int ContactsFound, string Sources, string Geography, decimal EstimatedCost, decimal ActualCost, string? FailureSummary, DateTime CreatedUtc, DateTime? StartedUtc, DateTime? CompletedUtc);
public sealed record ProspectImportResponse(int Imported, int Duplicates, int Rejected, IReadOnlyList<string> Errors);
public sealed record ProspectPageResponse(IReadOnlyList<ProspectAccountResponse> Items, int Total, int Page, int PageSize);
public sealed record ProspectAccountResponse(Guid Id, Guid RunId, Guid ProfileId, string Name, string? Domain, string? Country, string? Industry, int? Employees, decimal? Revenue, string Technologies, string Source, string Status, string FitOutcome, decimal FitScore, decimal TimingScore, decimal RoleScore, decimal DataConfidenceScore, decimal OverallScore, string ScoreBand, string EvaluationJson, string ResearchBriefJson, string? RejectionReason, Guid? LeadId, DateTime LastObservedUtc, IReadOnlyList<ProspectContactResponse> Contacts, IReadOnlyList<ProspectSignalResponse> Signals, IReadOnlyList<string> AllowedActions);
public sealed record ReviewProspectRequest(string Action, string? Reason);
public sealed class SaveProspectContactRequest(string fullName, string? title, string buyingRoles, string? department, string? seniority, string? email, string emailStatus, string? phone, string? profileUrl, decimal confidence, string sourceKey, string sourceReference) { public string FullName { get; set; } = fullName; public string? Title { get; set; } = title; public string BuyingRoles { get; set; } = buyingRoles; public string? Department { get; set; } = department; public string? Seniority { get; set; } = seniority; public string? Email { get; set; } = email; public string EmailStatus { get; set; } = emailStatus; public string? Phone { get; set; } = phone; public string? ProfileUrl { get; set; } = profileUrl; public decimal Confidence { get; set; } = confidence; public string SourceKey { get; set; } = sourceKey; public string SourceReference { get; set; } = sourceReference; }
public sealed record ProspectContactResponse(Guid Id, Guid AccountId, string FullName, string? Title, string? Department, string? Seniority, string BuyingRoles, string? Email, string EmailStatus, string? Phone, string? ProfileUrl, string EmploymentStatus, decimal Confidence, string Status, string? RejectionReason, Guid? ContactId);
public sealed class SaveProspectSignalRequest(string type, string sourceKey, string sourceReference, string summary, DateTime eventUtc, decimal confidence, int freshnessDays) { public string Type { get; set; } = type; public string SourceKey { get; set; } = sourceKey; public string SourceReference { get; set; } = sourceReference; public string Summary { get; set; } = summary; public DateTime EventUtc { get; set; } = eventUtc; public decimal Confidence { get; set; } = confidence; public int FreshnessDays { get; set; } = freshnessDays; }
public sealed record ProspectSignalResponse(Guid Id, string Type, string Source, string Summary, DateTime EventUtc, DateTime FreshUntilUtc, decimal Confidence, decimal Relevance, string Status);
public sealed class SaveSuppressionRequest(string scopeType, string scopeValue, string reason, string source, DateTime? expiresUtc) { public string ScopeType { get; set; } = scopeType; public string ScopeValue { get; set; } = scopeValue; public string Reason { get; set; } = reason; public string Source { get; set; } = source; public DateTime? ExpiresUtc { get; set; } = expiresUtc; }
public sealed record SuppressionResponse(Guid Id, string ScopeType, string ScopeValue, string Reason, string Source, DateTime CreatedUtc, DateTime? ExpiresUtc);
public sealed record LeadConversionResponse(Guid AccountId, Guid CustomerCompanyId, Guid? ContactId, Guid LeadId, bool ExistingLead);
public sealed record LeadGenerationMetricsResponse(int Candidates, int Qualified, int Accepted, int Converted, int Rejected, decimal AcceptanceRate, decimal AverageCompleteness, IReadOnlyDictionary<string, int> SourceYield);
