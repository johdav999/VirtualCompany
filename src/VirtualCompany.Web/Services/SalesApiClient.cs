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
    public Task<SalesMeetingInvitationResponse> RetryMeetingInvitationDeliveryAsync(Guid companyId, Guid invitationId, CancellationToken cancellationToken = default) =>
        SendAsync<object, SalesMeetingInvitationResponse>(companyId, HttpMethod.Post, $"api/sales/meeting-invitations/{invitationId:D}/retry", new { }, cancellationToken);
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

    public Task<SalesDealDetailResponse> LinkDealCustomerCompanyAsync(Guid companyId, Guid dealId, string companyName, CancellationToken cancellationToken = default) =>
        SendAsync<LinkDealCustomerCompanyRequest, SalesDealDetailResponse>(companyId, HttpMethod.Put, $"api/sales/deals/{dealId:D}/customer-company", new(companyName), cancellationToken);

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
    public Task<CampaignActivityResponse> AddCampaignActivityAsync(Guid companyId, Guid campaignId, CreateCampaignActivityRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<CreateCampaignActivityRequest, CampaignActivityResponse>(companyId, HttpMethod.Post, $"api/sales/campaigns/{campaignId:D}/activities", request, cancellationToken);

    public Task<CampaignPresentationActivityResponse?> GetCampaignPresentationActivityAsync(Guid companyId, Guid campaignId, Guid activityId, CancellationToken cancellationToken = default) =>
        GetAsync<CampaignPresentationActivityResponse>(companyId, $"api/sales/campaigns/{campaignId:D}/activities/{activityId:D}/presentation", allowNotFound: true, cancellationToken);

    public Task<CampaignPresentationActivityResponse> SaveCampaignPresentationActivityAsync(Guid companyId, Guid campaignId, Guid activityId, SaveCampaignPresentationActivityRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<SaveCampaignPresentationActivityRequest, CampaignPresentationActivityResponse>(companyId, HttpMethod.Put, $"api/sales/campaigns/{campaignId:D}/activities/{activityId:D}/presentation", request, cancellationToken);

    public Task<CampaignPresentationActivityResponse> RetryCampaignPresentationActivityAsync(Guid companyId, Guid campaignId, Guid activityId, CancellationToken cancellationToken = default) =>
        SendAsync<object, CampaignPresentationActivityResponse>(companyId, HttpMethod.Post, $"api/sales/campaigns/{campaignId:D}/activities/{activityId:D}/presentation/retry", new { }, cancellationToken);

    public Task RemoveCampaignPresentationActivityAsync(Guid companyId, Guid campaignId, Guid activityId, int expectedVersion, CancellationToken cancellationToken = default) =>
        DeleteAsync(companyId, $"api/sales/campaigns/{campaignId:D}/activities/{activityId:D}/presentation?expectedVersion={expectedVersion}", cancellationToken);

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

    private async Task DeleteAsync(Guid companyId, string uri, CancellationToken cancellationToken)
    {
        if (_useOfflineMode) throw new SalesApiException("Sales actions need the backend API. Start the API project before changing live tenant data.");
        try
        {
            using var request = CreateCompanyRequest(companyId, HttpMethod.Delete, uri, null);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) throw await CreateExceptionAsync(response, cancellationToken);
        }
        catch (HttpRequestException) { throw new SalesApiException("The sales workspace could not reach the backend API."); }
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
            ? new SalesApiException(string.Join(" ", problem.Errors.Values.SelectMany(x => x).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct()), problem.Errors, problem.Code)
            : new SalesApiException(_problemResolver?.Resolve(problem, "The sales request failed.") ?? problem?.Detail ?? problem?.Title ?? "The sales request failed.", code: problem?.Code);
    }

    private static string FormatProblem(ApiProblemResponse problem)
    {
        var firstError = problem.Errors?.SelectMany(x => x.Value).FirstOrDefault();
        return firstError ?? problem.Detail ?? problem.Title ?? "The sales request failed.";
    }

}

public sealed class SalesApiException : Exception
{
    public SalesApiException(string message, IReadOnlyDictionary<string, string[]>? errors = null, string? code = null) : base(message)
    {
        Errors = errors;
        Code = code;
    }

    public IReadOnlyDictionary<string, string[]>? Errors { get; }
    public string? Code { get; }
    public bool RequiresCalendarReconnect => Code == "calendar.reconnect_required";
}
