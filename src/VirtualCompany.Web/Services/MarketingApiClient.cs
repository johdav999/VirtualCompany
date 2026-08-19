using System.Net.Http.Json;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public sealed class MarketingApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ICompanyApiTransport transport;
    private readonly bool offline;

    public MarketingApiClient(ICompanyApiTransport transport, bool offline)
    {
        this.transport = transport;
        this.offline = offline;
    }

    public Task<MarketingDashboardViewModel> GetDashboardAsync(Guid companyId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) =>
        GetAsync<MarketingDashboardViewModel>(companyId,
            $"api/marketing/dashboard?fromUtc={Uri.EscapeDataString(fromUtc.ToString("O"))}&toUtc={Uri.EscapeDataString(toUtc.ToString("O"))}", ct);

    public Task<MarketingObjectiveViewModel> CreateObjectiveAsync(Guid companyId, CreateMarketingObjectiveViewModel request, CancellationToken ct = default) =>
        SendAsync<CreateMarketingObjectiveViewModel, MarketingObjectiveViewModel>(companyId, HttpMethod.Post, "api/marketing/objectives", request, ct);
    public Task<MarketingObjectiveViewModel> ActivateObjectiveAsync(Guid companyId, Guid objectiveId, CancellationToken ct = default) =>
        SendAsync<object, MarketingObjectiveViewModel>(companyId, HttpMethod.Post, $"api/marketing/objectives/{objectiveId:D}/activate", new { }, ct);

    public Task<MarketingPlanViewModel> CreatePlanAsync(Guid companyId, CreateMarketingPlanViewModel request, CancellationToken ct = default) =>
        SendAsync<CreateMarketingPlanViewModel, MarketingPlanViewModel>(companyId, HttpMethod.Post, "api/marketing/plans", request, ct);
    public Task<MarketingPlanDetailViewModel> CreateGroundedPlanAsync(Guid companyId, CreateGroundedMarketingPlanViewModel request, CancellationToken ct = default) =>
        SendAsync<CreateGroundedMarketingPlanViewModel, MarketingPlanDetailViewModel>(companyId, HttpMethod.Post, "api/marketing/plans/grounded", request, ct);
    public Task<MarketingPlanViewModel> ActivatePlanAsync(Guid companyId, Guid planId, CancellationToken ct = default) =>
        SendAsync<object, MarketingPlanViewModel>(companyId, HttpMethod.Post, $"api/marketing/plans/{planId:D}/activate", new { }, ct);
    public Task<IReadOnlyList<MarketingPlanListItemViewModel>> GetPlanPortfolioAsync(Guid companyId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<MarketingPlanListItemViewModel>>(companyId, "api/marketing/plan-portfolio", ct);
    public Task<MarketingPlanDetailViewModel> GetPlanPortfolioAsync(Guid companyId, Guid planId, CancellationToken ct = default) =>
        GetAsync<MarketingPlanDetailViewModel>(companyId, $"api/marketing/plans/{planId:D}/portfolio", ct);
    public Task<MarketingDailyReviewViewModel?> GetDailyReviewAsync(Guid companyId, DateTime dateUtc, CancellationToken ct = default) =>
        GetAsync<MarketingDailyReviewViewModel?>(companyId, $"api/marketing/daily-review?dateUtc={Uri.EscapeDataString(dateUtc.ToString("O"))}", ct);
    public Task<MarketingCampaignPortfolioProposalViewModel> PrepareCampaignPortfolioAsync(Guid companyId, Guid planId,
        PrepareMarketingCampaignPortfolioViewModel request, CancellationToken ct = default) =>
        SendAsync<PrepareMarketingCampaignPortfolioViewModel, MarketingCampaignPortfolioProposalViewModel>(companyId, HttpMethod.Post,
            $"api/marketing/plans/{planId:D}/campaign-portfolio/proposal", request, ct);
    public Task<MarketingCampaignPortfolioResultViewModel> CommitCampaignPortfolioAsync(Guid companyId, Guid planId,
        PrepareMarketingCampaignPortfolioViewModel request, CancellationToken ct = default) =>
        SendAsync<object, MarketingCampaignPortfolioResultViewModel>(companyId, HttpMethod.Post,
            $"api/marketing/plans/{planId:D}/campaign-portfolio/commit", new { portfolio = request }, ct);
    public Task<MarketingPlanDetailViewModel> SubmitPlanForReviewAsync(Guid companyId, Guid planId, int expectedVersion, CancellationToken ct = default) =>
        SendAsync<object, MarketingPlanDetailViewModel>(companyId, HttpMethod.Post,
            $"api/marketing/plans/{planId:D}/submit-grounded?expectedVersion={expectedVersion}", new { }, ct);
    public Task<MarketingPlanDetailViewModel> ActivateGroundedPlanAsync(Guid companyId, Guid planId, int expectedVersion, CancellationToken ct = default) =>
        SendAsync<object, MarketingPlanDetailViewModel>(companyId, HttpMethod.Post,
            $"api/marketing/plans/{planId:D}/activate-grounded?expectedVersion={expectedVersion}", new { }, ct);

    public Task<MarketingContentBriefViewModel> CreateContentAsync(Guid companyId, CreateMarketingContentBriefViewModel request, CancellationToken ct = default) =>
        SendAsync<CreateMarketingContentBriefViewModel, MarketingContentBriefViewModel>(companyId, HttpMethod.Post, "api/marketing/content", request, ct);

    public Task ReviewContentAsync(Guid companyId, Guid briefId, bool approved, CancellationToken ct = default) =>
        SendNoContentAsync(companyId, HttpMethod.Post, $"api/marketing/content/{briefId:D}/review", new { approved }, ct);
    public Task SubmitContentAsync(Guid companyId, Guid briefId, CancellationToken ct = default) =>
        SendNoContentAsync(companyId, HttpMethod.Post, $"api/marketing/content/{briefId:D}/submit", new { }, ct);
    public Task<MarketingContentPreflightViewModel> PreflightContentAsync(Guid companyId, Guid briefId, CancellationToken ct = default) =>
        GetAsync<MarketingContentPreflightViewModel>(companyId, $"api/marketing/content/{briefId:D}/preflight", ct);
    public Task<GenerateMarketingContentVariantsResultViewModel> GenerateContentVariantsAsync(Guid companyId,
        Guid briefId, GenerateMarketingContentVariantsViewModel request, CancellationToken ct = default) =>
        SendAsync<GenerateMarketingContentVariantsViewModel, GenerateMarketingContentVariantsResultViewModel>(
            companyId, HttpMethod.Post, $"api/marketing/content/{briefId:D}/generate", request, ct);

    public Task<MarketingQualificationDefinitionViewModel> CreateQualificationDefinitionAsync(
        Guid companyId, CreateMarketingQualificationDefinitionViewModel request, CancellationToken ct = default) =>
        SendAsync<CreateMarketingQualificationDefinitionViewModel, MarketingQualificationDefinitionViewModel>(
            companyId, HttpMethod.Post, "api/marketing/qualification-definitions", request, ct);
    public Task<MarketingQualificationDefinitionViewModel> ActivateQualificationDefinitionAsync(
        Guid companyId, Guid definitionId, CancellationToken ct = default) =>
        SendAsync<object, MarketingQualificationDefinitionViewModel>(
            companyId, HttpMethod.Post, $"api/marketing/qualification-definitions/{definitionId:D}/activate", new { }, ct);

    public Task<MarketingSalesHandoffViewModel> DecideHandoffAsync(Guid companyId, Guid handoffId, bool accepted, string reason, CancellationToken ct = default) =>
        SendAsync<object, MarketingSalesHandoffViewModel>(companyId, HttpMethod.Post, $"api/marketing/handoffs/{handoffId:D}/decision",
            new { accepted, reason, leadId = (Guid?)null, dealId = (Guid?)null }, ct);

    public Task<MarketingExperimentViewModel> CreateExperimentAsync(Guid companyId, CreateMarketingExperimentViewModel request, CancellationToken ct = default) =>
        SendAsync<CreateMarketingExperimentViewModel, MarketingExperimentViewModel>(companyId, HttpMethod.Post, "api/marketing/experiments", request, ct);
    public Task<MarketingExperimentViewModel> ActivateExperimentAsync(Guid companyId, Guid experimentId, CancellationToken ct = default) =>
        SendAsync<object, MarketingExperimentViewModel>(companyId, HttpMethod.Post, $"api/marketing/experiments/{experimentId:D}/activate", new { }, ct);
    public Task<MarketingExperimentViewModel> CompleteExperimentAsync(Guid companyId, Guid experimentId, string decision, CancellationToken ct = default) =>
        SendAsync<object, MarketingExperimentViewModel>(companyId, HttpMethod.Post, $"api/marketing/experiments/{experimentId:D}/complete", new { decision }, ct);

    public Task<RoleAgentAnalysisViewModel> AnalyzeAsync(Guid companyId, Guid agentId, string objective, CancellationToken ct = default) =>
        SendAsync<object, RoleAgentAnalysisViewModel>(companyId, HttpMethod.Post, $"api/marketing/agents/{agentId:D}/analysis",
            new { analysisType = "operating_cadence", horizonDays = 30, objective }, ct);

    public Task<IReadOnlyList<MarketingStrategyViewModel>> GetStrategiesAsync(Guid companyId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<MarketingStrategyViewModel>>(companyId, "api/marketing/strategies", ct);
    public Task<MarketingStrategyProposalViewModel> PrepareStrategyProposalAsync(Guid companyId,
        PrepareMarketingStrategyProposalViewModel request, CancellationToken ct = default) =>
        SendAsync<PrepareMarketingStrategyProposalViewModel, MarketingStrategyProposalViewModel>(companyId,
            HttpMethod.Post, "api/marketing/strategies/proposal", request, ct);
    public Task<MarketingStrategyViewModel> CommitStrategyProposalAsync(Guid companyId,
        CommitMarketingStrategyProposalViewModel request, CancellationToken ct = default) =>
        SendAsync<CommitMarketingStrategyProposalViewModel, MarketingStrategyViewModel>(companyId,
            HttpMethod.Post, "api/marketing/strategies/proposal/commit", request, ct);
    public Task<MarketingDecompositionProposalViewModel> PrepareDecompositionAsync(Guid companyId,
        PrepareMarketingDecompositionViewModel request, CancellationToken ct = default) =>
        SendAsync<PrepareMarketingDecompositionViewModel, MarketingDecompositionProposalViewModel>(companyId,
            HttpMethod.Post, "api/marketing/strategies/decomposition/preview", request, ct);
    public Task<MarketingDecompositionResultViewModel> CommitDecompositionAsync(Guid companyId,
        CommitMarketingDecompositionViewModel request, CancellationToken ct = default) =>
        SendAsync<CommitMarketingDecompositionViewModel, MarketingDecompositionResultViewModel>(companyId,
            HttpMethod.Post, "api/marketing/strategies/decomposition/commit", request, ct);
    public Task<IReadOnlyList<MarketingIntelligenceViewModel>> GetIntelligenceAsync(Guid companyId, bool freshnessQueue = false, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<MarketingIntelligenceViewModel>>(companyId, $"api/marketing/intelligence?freshnessQueue={freshnessQueue.ToString().ToLowerInvariant()}", ct);
    public Task<MarketingIntelligenceViewModel> GetIntelligenceAsync(Guid companyId, Guid intelligenceId, CancellationToken ct = default) =>
        GetAsync<MarketingIntelligenceViewModel>(companyId, $"api/marketing/intelligence/{intelligenceId:D}", ct);
    public Task<MarketingIntelligenceViewModel> UpdateIntelligenceAsync(Guid companyId, Guid intelligenceId,
        UpdateMarketingIntelligenceViewModel request, CancellationToken ct = default) =>
        SendAsync<UpdateMarketingIntelligenceViewModel, MarketingIntelligenceViewModel>(companyId, HttpMethod.Put,
            $"api/marketing/intelligence/{intelligenceId:D}", request, ct);
    public Task<MarketingIntelligenceViewModel> ReviewIntelligenceAsync(Guid companyId, Guid intelligenceId,
        ReviewMarketingIntelligenceViewModel request, CancellationToken ct = default) =>
        SendAsync<ReviewMarketingIntelligenceViewModel, MarketingIntelligenceViewModel>(companyId, HttpMethod.Post,
            $"api/marketing/intelligence/{intelligenceId:D}/review", request, ct);
    public Task<MarketingIntelligenceViewModel> ArchiveIntelligenceAsync(Guid companyId, Guid intelligenceId,
        int expectedVersion, CancellationToken ct = default) =>
        SendAsync<object, MarketingIntelligenceViewModel>(companyId, HttpMethod.Post,
            $"api/marketing/intelligence/{intelligenceId:D}/archive", new { expectedVersion }, ct);
    public Task<IReadOnlyList<MarketingIntelligenceReviewViewModel>> GetIntelligenceReviewsAsync(Guid companyId,
        Guid intelligenceId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<MarketingIntelligenceReviewViewModel>>(companyId,
            $"api/marketing/intelligence/{intelligenceId:D}/reviews", ct);
    public Task<IReadOnlyList<MarketingSegmentViewModel>> GetSegmentsAsync(Guid companyId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<MarketingSegmentViewModel>>(companyId, "api/marketing/segments", ct);
    public Task<MarketingSegmentImpactViewModel> GetSegmentImpactAsync(Guid companyId, Guid versionId, CancellationToken ct = default) =>
        GetAsync<MarketingSegmentImpactViewModel>(companyId, $"api/marketing/segment-versions/{versionId:D}/impact", ct);
    public Task<MarketingSegmentDecisionDataViewModel> GetSegmentDecisionDataAsync(Guid companyId, Guid versionId, CancellationToken ct = default) =>
        GetAsync<MarketingSegmentDecisionDataViewModel>(companyId, $"api/marketing/segment-versions/{versionId:D}/decision-data", ct);
    public Task<MarketingSegmentProposalViewModel> PrepareSegmentProposalAsync(Guid companyId, PrepareMarketingSegmentProposalViewModel request, CancellationToken ct = default) =>
        SendAsync<PrepareMarketingSegmentProposalViewModel, MarketingSegmentProposalViewModel>(companyId, HttpMethod.Post, "api/marketing/segments/proposal", request, ct);
    public Task<MarketingSegmentVersionViewModel> CommitSegmentProposalAsync(Guid companyId, CommitMarketingSegmentProposalViewModel request, CancellationToken ct = default) =>
        SendAsync<CommitMarketingSegmentProposalViewModel, MarketingSegmentVersionViewModel>(companyId, HttpMethod.Post, "api/marketing/segments/proposal/commit", request, ct);
    public Task<IReadOnlyList<MarketingOperatingRunViewModel>> GetOperatingRunsAsync(Guid companyId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<MarketingOperatingRunViewModel>>(companyId, "api/marketing/operating-runs?take=25", ct);
    public Task<IReadOnlyList<MarketingOperatingActionViewModel>> GetOperatingRunActionsAsync(Guid companyId, Guid runId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<MarketingOperatingActionViewModel>>(companyId, $"api/marketing/operating-runs/{runId:D}/actions", ct);
    public Task<MarketingOperatingActionViewModel> RetryOperatingActionAsync(Guid companyId, Guid runId, Guid actionId, string rationale, CancellationToken ct = default) =>
        SendAsync<object, MarketingOperatingActionViewModel>(companyId, HttpMethod.Post,
            $"api/marketing/operating-runs/{runId:D}/actions/{actionId:D}/retry", new { recoveryRationale = rationale }, ct);
    public Task<MarketingOperatingActionViewModel> CancelOperatingActionAsync(Guid companyId, Guid runId, Guid actionId, string rationale, CancellationToken ct = default) =>
        SendAsync<object, MarketingOperatingActionViewModel>(companyId, HttpMethod.Post,
            $"api/marketing/operating-runs/{runId:D}/actions/{actionId:D}/cancel", new { rationale }, ct);
    public Task<IReadOnlyList<MarketingWorkEvidenceViewModel>> GetWorkEvidenceAsync(Guid companyId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<MarketingWorkEvidenceViewModel>>(companyId, "api/marketing/work-evidence", ct);
    public Task<IReadOnlyList<MarketingCompanySignalViewModel>> GetCompanySignalsAsync(Guid companyId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<MarketingCompanySignalViewModel>>(companyId, "api/marketing/company-signals", ct);
    public Task<MarketingOperatingRunViewModel> RequestOperatingRunAsync(Guid companyId, Guid agentId, string reason, CancellationToken ct = default)
    {
        var key = $"operator:{companyId:N}:{agentId:N}:{Guid.NewGuid():N}";
        return SendAsync<object, MarketingOperatingRunViewModel>(companyId, HttpMethod.Post,
            $"api/marketing/agents/{agentId:D}/operating-runs",
            new { triggerType = "operator", triggerReference = reason, idempotencyKey = key, correlationId = key, cadence = "on_demand" }, ct);
    }
    public Task<IReadOnlyList<MarketingChannelConnectionViewModel>> GetChannelConnectionsAsync(Guid companyId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<MarketingChannelConnectionViewModel>>(companyId, "api/marketing/channel-connections", ct);
    public Task<IReadOnlyList<MarketingChannelDestinationViewModel>> GetChannelDestinationsAsync(Guid companyId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<MarketingChannelDestinationViewModel>>(companyId, "api/marketing/channel-destinations", ct);
    public Task<MarketingChannelOAuthStartViewModel> StartChannelOAuthAsync(Guid companyId, string provider, string redirectUri, CancellationToken ct = default) =>
        SendAsync<object, MarketingChannelOAuthStartViewModel>(companyId, HttpMethod.Post, "api/marketing/channel-connections/oauth/start", new { provider, redirectUri }, ct);
    public Task<IReadOnlyList<MarketingChannelDestinationViewModel>> RefreshChannelDestinationsAsync(Guid companyId, Guid connectionId, CancellationToken ct = default) =>
        SendAsync<object, IReadOnlyList<MarketingChannelDestinationViewModel>>(companyId, HttpMethod.Post, $"api/marketing/channel-connections/{connectionId:D}/refresh-destinations", new { }, ct);
    public async Task DisconnectChannelAsync(Guid companyId, Guid connectionId, CancellationToken ct = default) =>
        await SendNoContentAsync(companyId, HttpMethod.Post, $"api/marketing/channel-connections/{connectionId:D}/disconnect", new { }, ct);
    public Task<IReadOnlyList<MarketingChannelActionViewModel>> GetChannelActionsAsync(Guid companyId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<MarketingChannelActionViewModel>>(companyId, "api/marketing/channel-actions", ct);
    public Task<MarketingChannelActionViewModel> SynchronizeChannelActionAsync(Guid companyId, Guid actionId, CancellationToken ct = default) =>
        SendAsync<object, MarketingChannelActionViewModel>(companyId, HttpMethod.Post, $"api/marketing/channel-actions/{actionId:D}/synchronize-approval", new { }, ct);
    public Task<MarketingChannelActionViewModel> CancelChannelActionAsync(Guid companyId, Guid actionId, CancellationToken ct = default) =>
        SendAsync<object, MarketingChannelActionViewModel>(companyId, HttpMethod.Post, $"api/marketing/channel-actions/{actionId:D}/cancel", new { }, ct);
    public Task<MarketingChannelActionViewModel> ReconcileChannelActionAsync(Guid companyId, Guid actionId, CancellationToken ct = default) =>
        SendAsync<object, MarketingChannelActionViewModel>(companyId, HttpMethod.Post, $"api/marketing/channel-actions/{actionId:D}/reconcile", new { }, ct);
    public Task<IReadOnlyList<MarketingJourneyViewModel>> GetJourneysAsync(Guid companyId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<MarketingJourneyViewModel>>(companyId, "api/marketing/journeys", ct);
    public Task<IReadOnlyList<MarketingJourneyEnrollmentViewModel>> GetJourneyEnrollmentsAsync(Guid companyId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<MarketingJourneyEnrollmentViewModel>>(companyId, "api/marketing/journey-enrollments", ct);
    public Task<MarketingJourneyValidationViewModel> ValidateJourneyAsync(Guid companyId, Guid journeyId, CancellationToken ct = default) =>
        SendAsync<object, MarketingJourneyValidationViewModel>(companyId, HttpMethod.Post, $"api/marketing/journeys/{journeyId:D}/validate", new { }, ct);
    public Task<MarketingJourneyAudiencePreviewViewModel> PreviewJourneyAudienceAsync(Guid companyId, Guid journeyId, CancellationToken ct = default) =>
        GetAsync<MarketingJourneyAudiencePreviewViewModel>(companyId, $"api/marketing/journeys/{journeyId:D}/audience-preview?sampleSize=20", ct);
    public Task<MarketingJourneyViewModel> PauseJourneyAsync(Guid companyId, Guid journeyId, CancellationToken ct = default) => JourneyTransitionAsync(companyId, journeyId, "pause", ct);
    public Task<MarketingJourneyViewModel> ResumeJourneyAsync(Guid companyId, Guid journeyId, CancellationToken ct = default) => JourneyTransitionAsync(companyId, journeyId, "resume", ct);
    public Task<MarketingJourneyViewModel> CompleteJourneyAsync(Guid companyId, Guid journeyId, CancellationToken ct = default) => JourneyTransitionAsync(companyId, journeyId, "complete", ct);
    public Task<MarketingJourneyViewModel> CancelJourneyAsync(Guid companyId, Guid journeyId, CancellationToken ct = default) => JourneyTransitionAsync(companyId, journeyId, "cancel", ct);
    private Task<MarketingJourneyViewModel> JourneyTransitionAsync(Guid companyId, Guid journeyId, string transition, CancellationToken ct) =>
        SendAsync<object, MarketingJourneyViewModel>(companyId, HttpMethod.Post, $"api/marketing/journeys/{journeyId:D}/{transition}", new { }, ct);
    public Task<IReadOnlyList<MarketingCreativeAssetViewModel>> GetCreativeAssetsAsync(Guid companyId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<MarketingCreativeAssetViewModel>>(companyId, "api/marketing/creative-assets", ct);
    public Task<IReadOnlyList<MarketingCreativeAssetScanViewModel>> GetCreativeAssetScansAsync(Guid companyId, Guid assetId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<MarketingCreativeAssetScanViewModel>>(companyId, $"api/marketing/creative-assets/{assetId:D}/scans", ct);
    public Task<MarketingCreativeAssetScanViewModel> RescanCreativeAssetAsync(Guid companyId, Guid assetId, CancellationToken ct = default) =>
        SendAsync<object, MarketingCreativeAssetScanViewModel>(companyId, HttpMethod.Post, $"api/marketing/creative-assets/{assetId:D}/rescan", new { }, ct);
    public Task<MarketingCreativeAssetViewModel> GenerateCreativeAssetAsync(Guid companyId, GenerateMarketingCreativeAssetViewModel request, CancellationToken ct = default) =>
        SendAsync<GenerateMarketingCreativeAssetViewModel, MarketingCreativeAssetViewModel>(companyId, HttpMethod.Post, "api/marketing/creative-assets/generate", request, ct);
    public Task<MarketingCreativeAssetViewModel> SubmitCreativeAssetAsync(Guid companyId, Guid assetId, CancellationToken ct = default) =>
        SendAsync<object, MarketingCreativeAssetViewModel>(companyId, HttpMethod.Post, $"api/marketing/creative-assets/{assetId:D}/submit", new { }, ct);
    public Task<MarketingCreativeAssetViewModel> ReviewCreativeAssetAsync(Guid companyId, Guid assetId, bool approved, CancellationToken ct = default) =>
        SendAsync<object, MarketingCreativeAssetViewModel>(companyId, HttpMethod.Post, $"api/marketing/creative-assets/{assetId:D}/review?approved={approved.ToString().ToLowerInvariant()}", new { }, ct);
    public Task<MarketingCreativeAssetViewModel> RequestCreativeAssetChangesAsync(Guid companyId, Guid assetId, CancellationToken ct = default) =>
        SendAsync<object, MarketingCreativeAssetViewModel>(companyId, HttpMethod.Post, $"api/marketing/creative-assets/{assetId:D}/request-changes", new { }, ct);
    public Task<MarketingCreativeAssetViewModel> RetireCreativeAssetAsync(Guid companyId, Guid assetId, CancellationToken ct = default) =>
        SendAsync<object, MarketingCreativeAssetViewModel>(companyId, HttpMethod.Post, $"api/marketing/creative-assets/{assetId:D}/retire", new { }, ct);
    public Task<IReadOnlyList<MarketingAttributionViewModel>> GetAttributionAsync(Guid companyId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<MarketingAttributionViewModel>>(companyId, "api/marketing/attribution", ct);
    public Task<IReadOnlyList<MarketingEventTriggerViewModel>> GetEventsAsync(Guid companyId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<MarketingEventTriggerViewModel>>(companyId, "api/marketing/events", ct);
    public Task<MarketingEventTriggerViewModel> ResolveEventAsync(Guid companyId, Guid eventId, CancellationToken ct = default) =>
        SendAsync<object, MarketingEventTriggerViewModel>(companyId, HttpMethod.Post, $"api/marketing/events/{eventId:D}/resolve", new { }, ct);

    private async Task<T> GetAsync<T>(Guid companyId, string uri, CancellationToken ct)
    {
        EnsureOnline();
        using var response = await transport.SendAsync(companyId, HttpMethod.Get, uri, null, ct);
        return await ReadAsync<T>(response, ct);
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(Guid companyId, HttpMethod method, string uri, TRequest payload, CancellationToken ct)
    {
        EnsureOnline();
        using var response = await transport.SendAsync(companyId, method, uri, JsonContent.Create(payload, options: JsonOptions), ct);
        return await ReadAsync<TResponse>(response, ct);
    }

    private async Task SendNoContentAsync<TRequest>(Guid companyId, HttpMethod method, string uri, TRequest payload, CancellationToken ct)
    {
        EnsureOnline();
        using var response = await transport.SendAsync(companyId, method, uri, JsonContent.Create(payload, options: JsonOptions), ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new MarketingApiException(await ReadProblemAsync(response, ct));
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new MarketingApiException(await ReadProblemAsync(response, ct));
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct)
            ?? throw new MarketingApiException("The marketing API returned an empty response.");
    }

    private static async Task<string> ReadProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<MarketingProblem>(JsonOptions, ct);
            return problem?.Detail ?? problem?.Title ?? $"Marketing request failed ({(int)response.StatusCode}).";
        }
        catch (JsonException)
        {
            return $"Marketing request failed ({(int)response.StatusCode}).";
        }
    }

    private void EnsureOnline()
    {
        if (offline)
        {
            throw new MarketingApiException("Marketing needs the backend API. Start the API to use live company data.");
        }
    }

    private sealed record MarketingProblem(string? Title, string? Detail);
}

public sealed class MarketingApiException(string message) : Exception(message);
public sealed record MarketingDashboardViewModel(Guid CompanyId, DateTime GeneratedUtc,
    IReadOnlyList<MarketingMetricViewModel> Metrics, IReadOnlyList<MarketingObjectiveViewModel> Objectives,
    IReadOnlyList<MarketingPlanViewModel> Plans, IReadOnlyList<MarketingCalendarItemViewModel> Calendar,
    IReadOnlyList<MarketingContentBriefViewModel> Content, IReadOnlyList<MarketingSalesHandoffViewModel> Handoffs,
    IReadOnlyList<MarketingExperimentViewModel> Experiments,
    IReadOnlyList<MarketingQualificationDefinitionViewModel> QualificationDefinitions,
    IReadOnlyList<MarketingQualificationEvaluationViewModel> QualificationEvaluations);
public sealed record MarketingMetricViewModel(string Name, decimal? Value, string Unit, string State, string Explanation);
public sealed record MarketingStrategyViewModel(Guid Id, string Title, string Summary, string BusinessContext,
    DateTime ValidFromUtc, DateTime ValidToUtc, string SectionsJson, string EvidenceReferencesJson,
    string MissingEvidenceJson, string Status, Guid? ApprovalRequestId, int Version, DateTime UpdatedUtc,
    IReadOnlyList<Guid> SegmentVersionIds);
public sealed record PrepareMarketingStrategyProposalViewModel(Guid AgentId, string Objective, string Title,
    DateTime ValidFromUtc, DateTime ValidToUtc, IReadOnlyList<Guid> TargetSegmentVersionIds);
public sealed record CommitMarketingStrategyProposalViewModel(Guid AgentId, Guid RunId, string Title,
    string BusinessContext, DateTime ValidFromUtc, DateTime ValidToUtc,
    IReadOnlyList<Guid> TargetSegmentVersionIds, string IdempotencyKey);
public sealed record MarketingStrategyRecommendationViewModel(string Area, string Recommendation,
    string Classification, decimal Confidence, IReadOnlyList<Guid> TargetSegmentVersionIds,
    IReadOnlyList<string> SourceIds);
public sealed record MarketingStrategyProposalViewModel(Guid RunId, Guid AgentId, string Status, string Title,
    string Summary, string BusinessContext, DateTime ValidFromUtc, DateTime ValidToUtc,
    IReadOnlyList<MarketingStrategyRecommendationViewModel> MarketCustomerSynthesis,
    IReadOnlyList<MarketingStrategyRecommendationViewModel> StpAndPositioning,
    IReadOnlyList<MarketingStrategyRecommendationViewModel> FourPs,
    IReadOnlyList<MarketingStrategyRecommendationViewModel> CompetitiveAnalysis,
    IReadOnlyList<MarketingStrategyRecommendationViewModel> SwotAndFiveForces,
    IReadOnlyList<AgentAiSourceViewModel> Sources, IReadOnlyList<string> MissingEvidence,
    bool RequiresReview, string CapabilityVersion, string PromptVersion);
public sealed record MarketingDecompositionActivityViewModel(string Name, string ActivityType, string Channel,
    DateTime StartsUtc, DateTime DueUtc, Guid? OwnerAgentId = null, string? DependsOnName = null,
    bool ContentRequired = false);
public sealed record PrepareMarketingDecompositionViewModel(Guid StrategyId, Guid CampaignId,
    Guid TargetSegmentVersionId, Guid ObjectiveId, string PlanName, string PlanSummary, DateTime StartsUtc,
    DateTime EndsUtc, decimal? PlannedBudget, string BudgetCurrency,
    IReadOnlyList<MarketingDecompositionActivityViewModel> Activities);
public sealed record MarketingDecompositionProposalViewModel(string ProposalKey, Guid StrategyId, Guid CampaignId,
    Guid TargetSegmentVersionId, Guid ObjectiveId, string PlanName, string PlanSummary, DateTime StartsUtc,
    DateTime EndsUtc, decimal? PlannedBudget, string BudgetCurrency,
    IReadOnlyList<MarketingDecompositionActivityViewModel> Activities, IReadOnlyList<string> ReadinessGaps,
    bool ReadyToCommit);
public sealed record CommitMarketingDecompositionViewModel(string IdempotencyKey,
    PrepareMarketingDecompositionViewModel Decomposition);
public sealed record MarketingDecompositionResultViewModel(Guid Id, Guid StrategyId, Guid PlanId, Guid CampaignId,
    Guid TargetSegmentVersionId, IReadOnlyList<Guid> ActivityIds, IReadOnlyList<Guid> TaskIds,
    IReadOnlyList<string> ReadinessGaps, string Status);
public sealed record MarketingIntelligenceViewModel(Guid Id, string Kind, string Subject, string Summary,
    string Classification, decimal Confidence, string SourceType, string SourceReference, DateTime ObservedUtc,
    DateTime ReviewDueUtc, string DimensionsJson, string ReviewStatus, bool IsArchived, int Version);
public sealed record UpdateMarketingIntelligenceViewModel(string Subject, string Summary, string Classification,
    decimal Confidence, string SourceType, string SourceReference, DateTime ObservedUtc, DateTime ReviewDueUtc,
    string DimensionsJson, int ExpectedVersion);
public sealed record ReviewMarketingIntelligenceViewModel(bool Verified, string Rationale, int ExpectedVersion);
public sealed record MarketingIntelligenceReviewViewModel(Guid Id, Guid IntelligenceId, int ReviewNumber,
    Guid ReviewerUserId, string Outcome, string Rationale, string BeforeJson, string AfterJson, DateTime CreatedUtc);
public sealed record MarketingSegmentViewModel(Guid Id, string Name, string Description, bool IsArchived,
    IReadOnlyList<MarketingSegmentVersionViewModel> Versions);
public sealed record MarketingSegmentImpactItemViewModel(string ArtifactType, Guid ArtifactId, string Label,
    string Status, string ReviewReason);
public sealed record MarketingSegmentImpactViewModel(Guid SegmentVersionId, bool IsCurrentVersion,
    bool RequiresReview, IReadOnlyList<MarketingSegmentImpactItemViewModel> Artifacts, DateTime AssessedUtc);
public sealed record PrepareMarketingSegmentProposalViewModel(Guid AgentId, string SegmentName, string Objective);
public sealed record MarketingSegmentProposalViewModel(Guid RunId, Guid AgentId, string SegmentName, string Summary,
    IReadOnlyList<MarketingStrategyRecommendationViewModel> Claims, IReadOnlyList<AgentAiSourceViewModel> Sources,
    IReadOnlyList<string> MissingEvidence, decimal Confidence, bool RequiresReview, bool CanCreateDraft,
    string CapabilityVersion, string PromptVersion);
public sealed record CreateMarketingSegmentVersionViewModel(string CriteriaJson, string NeedsJson, string BehaviorsJson,
    string ChannelsJson, string PricingJson, long? SizeLow, long? SizeHigh, string SizeMethod, decimal Confidence,
    string EconomicsJson, string ScorecardJson, IReadOnlyDictionary<string, decimal> ScoreDimensions,
    string EvidenceJson, DateTime EvidenceObservedUtc, string IdempotencyKey);
public sealed record CommitMarketingSegmentProposalViewModel(Guid AgentId, Guid RunId, string SegmentName,
    string Description, CreateMarketingSegmentVersionViewModel Version, string IdempotencyKey);
public sealed record MarketingSegmentVersionViewModel(Guid Id, Guid SegmentId, int VersionNumber, string CriteriaJson,
    string NeedsJson, string BehaviorsJson, string ChannelsJson, string PricingJson, long? SizeLow, long? SizeHigh,
    string SizeMethod, decimal Confidence, string EconomicsJson, string ScorecardJson, decimal AttractivenessScore,
    string EvidenceJson, DateTime EvidenceObservedUtc, string Status, string TargetState, string? TargetRationale,
    Guid? ApprovalRequestId, int ConcurrencyVersion);
public sealed record MarketingSegmentSizeEstimateViewModel(Guid Id, Guid SegmentVersionId, decimal? Low, decimal? High,
    string Unit, string Period, string Geography, string? Currency, string Method, string AssumptionsJson,
    string SourceIdsJson, decimal Confidence, DateTime ObservedUtc, DateTime AsOfUtc, string Classification);
public sealed record MarketingSegmentEconomicEstimateViewModel(Guid Id, Guid SegmentVersionId, string MetricCode,
    decimal? Low, decimal? High, string Unit, string? Currency, string Method, decimal Confidence,
    string SourceIdsJson, DateTime ObservedUtc, string Classification);
public sealed record MarketingSegmentScoreDimensionViewModel(Guid Id, string Code, decimal Weight, decimal? Score, string EvidenceJson);
public sealed record MarketingSegmentScorePolicyViewModel(Guid Id, Guid SegmentVersionId, decimal TargetThreshold,
    string MissingEvidenceBehavior, string ExclusionsJson, string RiskJson, decimal? CalculatedScore,
    string Decision, IReadOnlyList<MarketingSegmentScoreDimensionViewModel> Dimensions);
public sealed record MarketingSegmentTargetDecisionViewModel(Guid Id, Guid SegmentVersionId, string TargetType,
    string Rationale, string ExpectedImpactJson, decimal Confidence, string RisksJson, DateTime ReviewUtc,
    string ApprovalStatus, Guid ActorId, Guid? ApprovalRequestId, string IdempotencyKey, DateTime DecidedUtc);
public sealed record MarketingSegmentArtifactMappingViewModel(Guid Id, Guid SegmentVersionId, string MappingType,
    Guid ArtifactId, string Label, DateTime CreatedUtc);
public sealed record MarketingSegmentDecisionDataViewModel(Guid SegmentVersionId,
    IReadOnlyList<MarketingSegmentSizeEstimateViewModel> SizeEstimates,
    IReadOnlyList<MarketingSegmentEconomicEstimateViewModel> EconomicEstimates,
    MarketingSegmentScorePolicyViewModel? ScorePolicy,
    IReadOnlyList<MarketingSegmentTargetDecisionViewModel> TargetDecisions,
    IReadOnlyList<MarketingSegmentArtifactMappingViewModel> Mappings);
public sealed record MarketingOperatingRunViewModel(Guid Id, Guid CompanyId, Guid AgentId, Guid? CompanyGoalId,
    Guid? OperatingInitiativeId, Guid? WorkTaskId, string TriggerType, string TriggerReference,
    string EffectiveAuthority, string Status, string SelectedWorkJson, string EvidenceJson,
    string MissingEvidenceJson, string? OutcomeSummary, string? RecoveryCode, decimal? BudgetLimit,
    decimal BudgetUsed, int AttemptCount, DateTime CreatedUtc, DateTime? CompletedUtc,
    string AssignmentContextJson, int ProgressCount, int OutcomeCount);
public sealed record MarketingOperatingActionViewModel(Guid Id, Guid MarketingOperatingRunId, int Sequence, int Version,
    string ActionType, string Title, string? Capability, string? Tool, string TargetJson, string SourceVersion,
    string GoalRelevance, string DependenciesJson, string ExpectedCompletionEvidence, string AuthorityDecision,
    bool RequiresApproval, string IdempotencyKey, decimal EstimatedCost, decimal ActualCost, string Status,
    int AttemptCount, int MaximumAttempts, DateTime? LeaseExpiresUtc, string? ArtifactType, Guid? ArtifactId,
    string ActualEvidenceJson, string? RecoveryCode, string? RecoveryGuidance, DateTime? NextAttemptUtc,
    DateTime CreatedUtc, DateTime? CompletedUtc);
public sealed record MarketingWorkEvidenceViewModel(Guid Id, Guid CompanyId, Guid MarketingOperatingRunId,
    Guid OperatingInitiativeId, Guid? WorkTaskId, string RecordType, int Version, string IdempotencyKey,
    string EvidenceVersion, string CompletedArtifactsJson, string ExpectedResultsJson,
    string ActualResultsJson, decimal? Confidence, string DataGapsJson, string BlockersJson,
    string DependenciesJson, string ChangedForecastJson, string Lessons, string RequestedNextAction,
    string CorrelationId, DateTime CreatedUtc);
public sealed record MarketingCompanySignalViewModel(Guid Id, Guid CompanyId, Guid? MarketingOperatingRunId,
    string SignalType, string Severity, string Summary, string EvidenceJson, string Status,
    bool CycleEvaluationRequested, string IdempotencyKey, string CorrelationId,
    DateTime CreatedUtc, DateTime UpdatedUtc);
public sealed record MarketingChannelConnectionViewModel(Guid Id, string Provider, string DisplayName,
    string CapabilitiesJson, string Status, string HealthStatus, string? FailureSummary, DateTime? LastCheckedUtc);
public sealed record MarketingChannelDestinationViewModel(Guid Id, Guid ConnectionId, string ProviderReference,
    string DisplayName, string DestinationType, string CapabilitiesJson, string Status, DateTime LastDiscoveredUtc);
public sealed record MarketingChannelOAuthStartViewModel(string Provider, Uri AuthorizationUri, DateTime ExpiresUtc);
public sealed record MarketingChannelActionViewModel(Guid Id, Guid ConnectionId, string DestinationReference,
    string ActionType, string PayloadJson, DateTime? ScheduledUtc, string Status, Guid? ApprovalRequestId,
    int Version, int AttemptCount, string? ProviderReference, string? FailureCode, int? ContentBriefVersion);
public sealed record MarketingJourneyViewModel(Guid Id, string Name, string AudienceEligibilityJson,
    string EntryExitCriteriaJson, string StepsJson, string GuardrailsJson, int FrequencyCap,
    DateTime ValidFromUtc, DateTime ValidToUtc, string Status, Guid? ApprovalRequestId, int Version,
    Guid? SupersedesJourneyId, int ConcurrencyVersion, Guid? SegmentVersionId);
public sealed record MarketingJourneyValidationViewModel(bool Valid, IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings, int StepCount);
public sealed record MarketingJourneyAudiencePreviewViewModel(int EligibleCount, int SuppressedCount,
    int MissingConsentCount, IReadOnlyList<Guid> SampleContactIds, DateTime EvaluatedUtc);
public sealed record MarketingJourneyEnrollmentViewModel(Guid Id, Guid JourneyId, Guid ContactId, int JourneyVersion,
    string ConsentEvidenceReference, string Status, int NextStepIndex, DateTime? NextStepUtc,
    int ActionsInWindow, Guid? LastChannelActionId, string? FailureCode, DateTime UpdatedUtc);
public sealed record MarketingCreativeAssetViewModel(Guid Id, Guid AssetFamilyId, int VersionNumber, Guid BriefId, Guid? CampaignId, string Name,
    string MediaType, string Dimensions, string Language, string GenerationSummary, string PromptVersion,
    string ProviderReference, string BrandProfileVersion, string SafetyResult, string AltText,
    string StorageReference, string Checksum, string Status, int Version, DateTime CreatedUtc, DateTime UpdatedUtc,
    Guid? ContentVariantId, string SourceAssetIdsJson, string ProvenanceJson, string AuditReference);
public sealed record MarketingCreativeAssetScanViewModel(Guid Id, Guid AssetId, string Provider, string ProviderReference,
    string ScannerVersion, string Result, string ReasonCode, string EvidenceJson, DateTime ScannedUtc);
public sealed record MarketingAttributionViewModel(Guid Id, string SubjectType, Guid SubjectId, string Model,
    string Classification, decimal AttributedValue, string Unit, string EvidenceJson, decimal Confidence,
    DateTime PeriodStartUtc, DateTime PeriodEndUtc, DateTime CreatedUtc);
public sealed record MarketingEventTriggerViewModel(Guid Id, string EventType, string SourceType, string SourceId,
    int SourceVersion, string Severity, string EvidenceJson, string CorrelationId, string Status,
    Guid? OperatingRunId, Guid? RelatedTaskId, string? FailureSummary, DateTime CreatedUtc, DateTime UpdatedUtc);
public sealed record GenerateMarketingCreativeAssetViewModel(Guid BriefId, Guid? CampaignId, string Name,
    string Prompt, string Dimensions, string Language, string BrandProfileVersion, string AltText,
    string IdempotencyKey, string Quality = "medium", string OutputFormat = "png", Guid? RegenerateFromAssetId = null);
public sealed record MarketingObjectiveViewModel(Guid Id, string Name, string ObjectiveType, decimal TargetValue,
    string Unit, decimal? BaselineValue, DateTime PeriodStartUtc, DateTime PeriodEndUtc, string Status, int Version);
public sealed record MarketingPlanViewModel(Guid Id, string Name, string Summary, DateTime StartsUtc, DateTime EndsUtc,
    decimal? PlannedBudget, string BudgetCurrency, string Status, int Version);
public sealed record MarketingCalendarItemViewModel(Guid Id, string Kind, string Name, DateTime StartsUtc, DateTime EndsUtc,
    string Status, Guid? CampaignId, Guid? OwnerAgentId, bool IsSpan = false, string SourceRecordType = "marketing",
    Guid? SourceRecordId = null, Guid? PlanId = null, string AttentionState = "none", string? NavigationTarget = null);
public sealed record MarketingPlanListItemViewModel(Guid Id, string Name, string? StrategyTitle, int? StrategyVersion,
    DateTime StartsUtc, DateTime EndsUtc, decimal? PlannedBudget, decimal AllocatedBudget, decimal? RemainingBudget,
    string BudgetCurrency, int ObjectiveCount, int SegmentCount, int CampaignCount, string ReadinessLabel,
    string StatusLabel, Guid? OwnerAgentId, int Version, string? AttentionReason);
public sealed record MarketingPlanObjectiveSummaryViewModel(Guid Id, string Name, string Status, DateTime StartsUtc, DateTime EndsUtc);
public sealed record MarketingPlanSegmentPortfolioViewModel(Guid Id, Guid SegmentVersionId, int SegmentVersionNumber,
    string SegmentName, string Role, int Priority, string Rationale, string ExpectedContribution, string Status);
public sealed record MarketingPlanCampaignPortfolioViewModel(Guid Id, Guid CampaignId, string CampaignName, string Purpose,
    Guid? ObjectiveId, string ObjectiveContribution, IReadOnlyList<Guid> SegmentVersionIds, decimal? AllocatedBudget,
    string BudgetCurrency, int Priority, string Status, string CampaignLifecycleStatus, DateTime? PlanningStartsUtc,
    DateTime? LaunchUtc, DateTime? ReviewUtc, DateTime? EndsUtc, Guid? OwnerAgentId, IReadOnlyList<string> ReadinessGaps);
public sealed record MarketingCoverageFindingViewModel(string Code, string Label, string Explanation, string Severity,
    Guid? ObjectiveId = null, Guid? SegmentVersionId = null, Guid? CampaignId = null);
public sealed record MarketingPlanDetailViewModel(MarketingPlanListItemViewModel Summary, string Description, string Rationale,
    IReadOnlyList<string> EvidenceReferences, IReadOnlyList<string> MissingEvidence,
    IReadOnlyList<MarketingPlanObjectiveSummaryViewModel> Objectives, IReadOnlyList<MarketingPlanSegmentPortfolioViewModel> Segments,
    IReadOnlyList<MarketingPlanCampaignPortfolioViewModel> Campaigns, IReadOnlyList<MarketingCoverageFindingViewModel> Coverage,
    Guid? ApprovalRequestId, IReadOnlyList<string> AllowedActions, bool StrategyGroundingAvailable);
public sealed record MarketingWorkNeedViewModel(string ReasonCode, string Label, string Urgency, bool Actionable,
    IReadOnlyList<Guid> AffectedIds, IReadOnlyList<string> EvidenceReferences, string Explanation,
    string RecommendedTool, bool RequiresApproval, string Fingerprint);
public sealed record MarketingDailyReviewViewModel(Guid RunId, DateTime RunDateUtc, string OutcomeLabel, string Summary,
    IReadOnlyList<string> CheckedEvidence, IReadOnlyList<MarketingWorkNeedViewModel> Needs, IReadOnlyList<string> Actions,
    IReadOnlyList<string> Blockers, string? NextHumanAction);
public sealed record MarketingCampaignPortfolioItemViewModel(string Name, string Purpose, Guid ObjectiveId,
    string ObjectiveContribution, IReadOnlyList<Guid> SegmentVersionIds, decimal? AllocatedBudget, string BudgetCurrency,
    int Priority, string CampaignType, string AudienceType, decimal ObjectiveTarget, string ObjectiveUnit,
    DateTime ObjectiveTargetUtc, DateTime PlanningStartsUtc, DateTime LaunchUtc, DateTime ReviewUtc, DateTime EndsUtc,
    string TimeZoneId, string? CommunicationLanguage, IReadOnlyList<string> Channels, string? OfferBasis,
    IReadOnlyList<string> Activities, IReadOnlyList<string> ContentNeeds, string AudienceApproach,
    string MeasurementApproach, IReadOnlyList<string> EvidenceReferences, IReadOnlyList<string> MissingEvidence,
    IReadOnlyList<string>? TaskNeeds = null, IReadOnlyList<string>? Assumptions = null, IReadOnlyList<string>? Risks = null);
public sealed record PrepareMarketingCampaignPortfolioViewModel(Guid PlanId, int ExpectedPlanVersion,
    IReadOnlyList<MarketingCampaignPortfolioItemViewModel> Campaigns, string IdempotencyKey, Guid? AgentId = null);
public sealed record MarketingPolicyDecisionViewModel(bool Allowed, string ReasonCode, string Explanation,
    bool RequiresApproval, IReadOnlyList<string> EvidenceReferences);
public sealed record MarketingCampaignPortfolioProposalViewModel(string ProposalKey, Guid PlanId, int PlanVersion,
    MarketingPolicyDecisionViewModel Decision, IReadOnlyList<MarketingCoverageFindingViewModel> Findings,
    IReadOnlyList<MarketingCampaignPortfolioItemViewModel> Campaigns);
public sealed record MarketingCampaignPortfolioResultViewModel(Guid PlanId, int PlanVersion,
    IReadOnlyList<MarketingPlanCampaignPortfolioViewModel> Campaigns, bool Idempotent, string Outcome);
public sealed record MarketingContentBriefViewModel(Guid Id, Guid? CampaignId, Guid? PlanId, string Title, string Purpose,
    string Audience, string Channel, string Language, string Tone, string CallToAction, DateTime? DueUtc,
    string Status, int Version, IReadOnlyList<MarketingContentVariantViewModel> Variants,
    Guid? SegmentVersionId = null, string MeasurableObjective = "", string FunnelStage = "",
    string CustomerInsight = "", string KeyMessage = "", string SupportingPointsJson = "[]",
    string Offer = "", string RequiredClaimsJson = "[]", string ProhibitedClaimsJson = "[]",
    string SeoRequirementsJson = "{}", string VisualDirection = "", string DesiredFormatsJson = "[]",
    string VariantRequirementsJson = "{}", string EvidenceRequirementsJson = "{}",
    string ApprovalPolicyJson = "{}");
public sealed record MarketingContentVariantViewModel(Guid Id, Guid VariantFamilyId, int VersionNumber, string Name,
    string Body, string ContentFormat, string SourceReferences, bool GeneratedByAi, Guid? GenerationRunId,
    string CapabilityVersion, string PromptVersion, string Status, DateTime CreatedUtc);
public sealed record GenerateMarketingContentVariantsViewModel(Guid AgentId, string ContentFormat,
    int VariantCount, string Instructions, string IdempotencyKey);
public sealed record GenerateMarketingContentVariantsResultViewModel(Guid RunId, string Status,
    IReadOnlyList<MarketingContentVariantViewModel> Variants, IReadOnlyList<string> MissingEvidence,
    bool RequiresReview);
public sealed record MarketingSalesHandoffViewModel(Guid Id, Guid? CampaignId, Guid? ContactId, Guid? CustomerCompanyId,
    Guid? LinkedLeadId, Guid? LinkedDealId, string Reason, string SuggestedAction, string Urgency,
    DateTime ExpiresUtc, string EvidenceReferences, string Status, string? DecisionReason, DateTime UpdatedUtc);
public sealed record MarketingExperimentViewModel(Guid Id, Guid? CampaignId, string Name, string Hypothesis,
    string PrimaryMetric, string GuardrailMetric, int MinimumSampleSize, DateTime StartsUtc, DateTime EndsUtc,
    string Status, string? Decision);
public sealed record MarketingContentPreflightViewModel(Guid BriefId, bool ReadyForReview,
    IReadOnlyList<MarketingContentPreflightIssueViewModel> Issues);
public sealed record MarketingContentPreflightIssueViewModel(string Code, string Severity, string Explanation,
    string? Field = null);
public sealed record MarketingQualificationDefinitionViewModel(Guid Id, string Name, string AudienceType,
    string Channel, decimal MinimumScore, int FreshnessDays, bool RequiresCustomerCompany,
    DateTime EffectiveFromUtc, DateTime? EffectiveToUtc, string Status, int Version);
public sealed record MarketingQualificationEvaluationViewModel(Guid Id, Guid DefinitionId, int DefinitionVersion,
    Guid ContactId, decimal Score, string Status, string ReasonCodesJson, DateTime ObservedUtc);
public sealed record CreateMarketingObjectiveViewModel(string Name, string ObjectiveType, decimal TargetValue, string Unit,
    DateTime PeriodStartUtc, DateTime PeriodEndUtc, decimal? BaselineValue = null);
public sealed record CreateMarketingPlanViewModel(string Name, string Summary, DateTime StartsUtc, DateTime EndsUtc,
    decimal? PlannedBudget, string BudgetCurrency, IReadOnlyList<Guid>? ObjectiveIds = null);
public sealed record MarketingPlanSegmentSelectionViewModel(Guid SegmentVersionId, string Role, int Priority,
    string Rationale, string ExpectedContribution);
public sealed record CreateGroundedMarketingPlanViewModel(string Name, string Summary, Guid StrategyId,
    int ExpectedStrategyVersion, DateTime StartsUtc, DateTime EndsUtc, decimal? PlannedBudget,
    string BudgetCurrency, IReadOnlyList<Guid> ObjectiveIds, IReadOnlyList<MarketingPlanSegmentSelectionViewModel> Segments,
    string Rationale, IReadOnlyList<string> EvidenceReferences, IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> Risks, IReadOnlyList<string> MissingEvidence, string IdempotencyKey,
    Guid? OwnerAgentId = null);
public sealed record CreateMarketingContentBriefViewModel(Guid? CampaignId, Guid? PlanId, string Title, string Purpose,
    string Audience, string Channel, string Language, string Tone, string CallToAction, DateTime? DueUtc,
    Guid? SegmentVersionId = null, string MeasurableObjective = "", string FunnelStage = "awareness",
    string? CustomerInsight = null, string KeyMessage = "", string SupportingPointsJson = "[]",
    string Offer = "", string RequiredClaimsJson = "[]", string ProhibitedClaimsJson = "[]",
    string SeoRequirementsJson = "{}", string VisualDirection = "", string DesiredFormatsJson = "[]",
    string VariantRequirementsJson = "{}", string EvidenceRequirementsJson = "{}",
    string ApprovalPolicyJson = "{}");
public sealed record CreateMarketingExperimentViewModel(Guid? CampaignId, string Name, string Hypothesis,
    string PrimaryMetric, string GuardrailMetric, int MinimumSampleSize, DateTime StartsUtc, DateTime EndsUtc);
public sealed record CreateMarketingQualificationDefinitionViewModel(string Name, string AudienceType,
    string Channel, decimal MinimumScore, int FreshnessDays, bool RequiresCustomerCompany,
    DateTime EffectiveFromUtc, DateTime? EffectiveToUtc = null, string RulesJson = "{}", string ExclusionsJson = "{}");
