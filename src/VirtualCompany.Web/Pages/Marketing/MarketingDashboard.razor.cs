using System.Globalization;
using Microsoft.AspNetCore.Components;
using VirtualCompany.Web.Features.Marketing;
using VirtualCompany.Web.Services;
using static VirtualCompany.Web.Features.Marketing.MarketingEvidencePresentation;

namespace VirtualCompany.Web.Pages.Marketing;

public partial class MarketingDashboard
{
    private readonly MarketingAudiencesState audiencesState = new();
    private readonly MarketingChannelsState channelsState = new();
    private readonly MarketingContentState contentState = new();
    private readonly MarketingCreativeState creativeState = new();
    private readonly MarketingExperimentsState experimentsState = new();
    private readonly MarketingIntelligenceState intelligenceState = new();
    private readonly MarketingJourneysState journeysState = new();
    private readonly MarketingObjectivesState objectivesState = new();
    private readonly MarketingOperationsState operationsState = new();
    private readonly MarketingOverviewState overviewState = new();
    private readonly MarketingPerformanceState performanceState = new();
    private readonly MarketingPlansState plansState = new();
    private readonly MarketingSegmentsState segmentsState = new();
    private readonly MarketingStrategyState strategyState = new();

    [SupplyParameterFromQuery(Name = "companyId")] public Guid? CompanyId { get; set; }
    [SupplyParameterFromQuery(Name = "section")] public string? Section { get; set; }
    [SupplyParameterFromQuery(Name = "planId")] public Guid? RequestedPlanId { get; set; }
    [SupplyParameterFromQuery] public Guid? BriefId { get; set; }
    [SupplyParameterFromQuery] public Guid? AssetId { get; set; }
    [SupplyParameterFromQuery] public Guid? ActionId { get; set; }
    [SupplyParameterFromQuery] public string? ReturnUrl { get; set; }
    [SupplyParameterFromQuery] public string? RecordReturnUrl { get; set; }
    private string WorkspaceOrigin => DashboardRoutes.NormalizeOverviewPath(ReturnUrl, resolvedCompanyId ?? CompanyId ?? Guid.Empty) ?? DashboardRoutes.BuildTodayPath(resolvedCompanyId ?? CompanyId ?? Guid.Empty, "marketing");
    private string PreviousView => MarketingJourneyRoutes.NormalizeRecordReturn(RecordReturnUrl, resolvedCompanyId ?? CompanyId ?? Guid.Empty) ?? WorkspaceOrigin;
    private readonly string[] tabs = ["Overview", "Strategy", "Segments", "Intelligence", "Objectives", "Plans", "Calendar", "Content", "Audiences", "Handoffs", "Experiments", "Channels", "Journeys", "Performance", "Operations"];
    private string activeTab = "Overview";
    private bool isLoading = true;
    private bool isSaving;
    private string? errorMessage;
    private Guid? resolvedCompanyId;
    private Guid? marketingAgentId;
    private MarketingDashboardViewModel? dashboard;

    private string? loadedRoute;
    protected override Task OnParametersSetAsync()
    {
        if (loadedRoute is null) { loadedRoute = Navigation.Uri; return LoadAsync(); }
        if (loadedRoute != Navigation.Uri) Navigation.NavigateTo(Navigation.Uri, forceLoad: true);
        return Task.CompletedTask;
    }

    private async Task LoadAsync()
    {
        isLoading = true; errorMessage = null;
        try
        {
            var context = await OnboardingApi.GetCurrentUserContextAsync(CompanyId);
            resolvedCompanyId = CompanyId ?? context?.ActiveCompany?.CompanyId ?? context?.Memberships.FirstOrDefault()?.CompanyId;
            if (resolvedCompanyId is not Guid companyId) throw new MarketingApiException("Choose a company before opening Marketing.");
            if (CompanyId is null) Navigation.NavigateTo($"/marketing?companyId={companyId:D}", true);
            if (!string.IsNullOrWhiteSpace(Section) && tabs.FirstOrDefault(x => x.Equals(Section, StringComparison.OrdinalIgnoreCase)) is { } requestedTab) activeTab = requestedTab;
            var to = DateTime.UtcNow.AddDays(1);
            dashboard = await MarketingApi.GetDashboardAsync(companyId, to.AddDays(-30), to);
            plansState.PlanPortfolio = await MarketingApi.GetPlanPortfolioAsync(companyId);
            var requestedPlan = RequestedPlanId.HasValue && plansState.PlanPortfolio.Any(x => x.Id == RequestedPlanId.Value)
                ? RequestedPlanId.Value
                : plansState.PlanPortfolio.FirstOrDefault()?.Id;
            if (plansState.SelectedPlan is null || !plansState.PlanPortfolio.Any(x => x.Id == plansState.SelectedPlan.Summary.Id) || RequestedPlanId.HasValue)
                plansState.SelectedPlan = requestedPlan.HasValue ? await MarketingApi.GetPlanPortfolioAsync(companyId, requestedPlan.Value) : null;
            plansState.DailyReview = await MarketingApi.GetDailyReviewAsync(companyId, DateTime.UtcNow);
            if (contentState.ContentGenerationForm.BriefId == Guid.Empty) contentState.ContentGenerationForm.BriefId = dashboard.Content.FirstOrDefault(x => x.Status.Equals("draft", StringComparison.OrdinalIgnoreCase))?.Id ?? Guid.Empty;
            if (creativeState.CreativeForm.BriefId == Guid.Empty) creativeState.CreativeForm.BriefId = dashboard.Content.FirstOrDefault(x => x.Status is "submitted" or "approved")?.Id ?? Guid.Empty;
            strategyState.Strategies = await MarketingApi.GetStrategiesAsync(companyId);
            segmentsState.Segments = await MarketingApi.GetSegmentsAsync(companyId);
            plansState.Strategies = strategyState.Strategies;
            plansState.Segments = segmentsState.Segments;
            plansState.Objectives = dashboard.Objectives;
            plansState.InitializePlanFormSelections();
            var strategyWorkshops = await GuidedWorkApi.ListAsync(companyId, artifactType: "marketing_strategy");
            strategyState.StrategyWorkshopDraft = strategyWorkshops.Items.FirstOrDefault(x => x.Status is "active" or "review_ready");
            var segmentWorkshops = await GuidedWorkApi.ListAsync(companyId, artifactType: "marketing_segment");
            segmentsState.SegmentWorkshopDraft = segmentWorkshops.Items.FirstOrDefault(x => x.Status is "active" or "review_ready");
            var planWorkshops = await GuidedWorkApi.ListAsync(companyId, artifactType: "marketing_plan");
            plansState.PlanWorkshopDraft = planWorkshops.Items.FirstOrDefault(x => x.Status is "active" or "review_ready");
            if (strategyState.StrategyProposalForm.SegmentVersionId == Guid.Empty) strategyState.StrategyProposalForm.SegmentVersionId = segmentsState.Segments.SelectMany(x => x.Versions).FirstOrDefault(x => x.Status is "approved" or "active")?.Id ?? Guid.Empty;
            strategyState.SalesCampaigns = await SalesApi.ListCampaignsAsync(companyId);
            strategyState.DecompositionForm.StrategyId = strategyState.DecompositionForm.StrategyId == Guid.Empty ? strategyState.Strategies.FirstOrDefault(x => x.Status is "approved" or "active")?.Id ?? Guid.Empty : strategyState.DecompositionForm.StrategyId;
            strategyState.DecompositionForm.CampaignId = strategyState.DecompositionForm.CampaignId == Guid.Empty ? strategyState.SalesCampaigns.FirstOrDefault()?.Id ?? Guid.Empty : strategyState.DecompositionForm.CampaignId;
            strategyState.DecompositionForm.ObjectiveId = strategyState.DecompositionForm.ObjectiveId == Guid.Empty ? dashboard.Objectives.FirstOrDefault()?.Id ?? Guid.Empty : strategyState.DecompositionForm.ObjectiveId;
            intelligenceState.Intelligence = await MarketingApi.GetIntelligenceAsync(companyId);
            if (intelligenceState.SelectedIntelligence is null || !intelligenceState.Intelligence.Any(x => x.Id == intelligenceState.SelectedIntelligence.Id))
                intelligenceState.SelectedIntelligence = intelligenceState.Intelligence.FirstOrDefault();
            if (intelligenceState.SelectedIntelligence is not null)
                intelligenceState.IntelligenceReviews = await MarketingApi.GetIntelligenceReviewsAsync(companyId, intelligenceState.SelectedIntelligence.Id);
            operationsState.OperatingRuns = await MarketingApi.GetOperatingRunsAsync(companyId);
            if (operationsState.SelectedOperatingRun is null || !operationsState.OperatingRuns.Any(x => x.Id == operationsState.SelectedOperatingRun.Id))
                operationsState.SelectedOperatingRun = operationsState.OperatingRuns.FirstOrDefault();
            operationsState.OperatingActions = operationsState.SelectedOperatingRun is null ? [] : await MarketingApi.GetOperatingRunActionsAsync(companyId, operationsState.SelectedOperatingRun.Id);
            channelsState.ChannelConnections = await MarketingApi.GetChannelConnectionsAsync(companyId);
            channelsState.ChannelDestinations = await MarketingApi.GetChannelDestinationsAsync(companyId);
            channelsState.ChannelActions = await MarketingApi.GetChannelActionsAsync(companyId);
            journeysState.Journeys = await MarketingApi.GetJourneysAsync(companyId);
            journeysState.JourneyEnrollments = await MarketingApi.GetJourneyEnrollmentsAsync(companyId);
            if (journeysState.SelectedJourney is null || !journeysState.Journeys.Any(x => x.Id == journeysState.SelectedJourney.Id)) journeysState.SelectedJourney = journeysState.Journeys.FirstOrDefault();
            if (journeysState.SelectedJourney is not null)
            {
                journeysState.JourneyValidation = await MarketingApi.ValidateJourneyAsync(companyId, journeysState.SelectedJourney.Id);
                journeysState.JourneyAudiencePreview = await MarketingApi.PreviewJourneyAudienceAsync(companyId, journeysState.SelectedJourney.Id);
            }
            creativeState.CreativeAssets = await MarketingApi.GetCreativeAssetsAsync(companyId);
            var scanSets = await Task.WhenAll(creativeState.CreativeAssets.Select(x => MarketingApi.GetCreativeAssetScansAsync(companyId, x.Id)));
            creativeState.CreativeScans = scanSets.SelectMany(x => x).GroupBy(x => x.AssetId).ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.ScannedUtc).First());
            performanceState.Attribution = await MarketingApi.GetAttributionAsync(companyId);
            operationsState.Events = await MarketingApi.GetEventsAsync(companyId);
            var roster = await AgentApi.GetRosterAsync(companyId);
            marketingAgentId = roster.FirstOrDefault(x => x.Department.Equals("Marketing", StringComparison.OrdinalIgnoreCase) && !x.Status.Equals("archived", StringComparison.OrdinalIgnoreCase))?.Id;
        }
        catch (Exception ex) when (ex is MarketingApiException or OnboardingApiException) { errorMessage = ex.Message; }
        finally { isLoading = false; }
    }

    private async Task ExecuteAndReloadAsync(Func<Guid, Task> action)
    {
        if (resolvedCompanyId is not Guid companyId) return;
        isSaving = true; errorMessage = null;
        try { await action(companyId); await LoadAsync(); }
        catch (MarketingApiException ex) { errorMessage = ex.Message; }
        finally { isSaving = false; }
    }

    private Task CreateObjectiveAsync() => ExecuteAndReloadAsync(async id => await MarketingApi.CreateObjectiveAsync(id,
        new(objectivesState.ObjectiveForm.Name, objectivesState.ObjectiveForm.ObjectiveType, objectivesState.ObjectiveForm.TargetValue, objectivesState.ObjectiveForm.Unit, DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddMonths(3))));
    private async Task ReviewSegmentImpactAsync(Guid versionId)
    {
        if (resolvedCompanyId is not Guid companyId) return;
        isSaving = true; errorMessage = null;
        try
        {
            var requests = new[] { MarketingApi.GetSegmentImpactAsync(companyId, versionId) };
            await Task.WhenAll(requests);
            segmentsState.SegmentImpact = requests[0].Result;
            segmentsState.SegmentDecisionData = await MarketingApi.GetSegmentDecisionDataAsync(companyId, versionId);
        }
        catch (MarketingApiException ex) { errorMessage = ex.Message; }
        finally { isSaving = false; }
    }
    private async Task PrepareSegmentProposalAsync()
    {
        if (resolvedCompanyId is not Guid companyId || marketingAgentId is not Guid agentId) return;
        isSaving = true; errorMessage = null;
        try { segmentsState.SegmentProposal = await MarketingApi.PrepareSegmentProposalAsync(companyId,
            new(agentId, segmentsState.SegmentProposalForm.Name, segmentsState.SegmentProposalForm.Objective)); }
        catch (MarketingApiException ex) { errorMessage = ex.Message; }
        finally { isSaving = false; }
    }
    private Task CommitSegmentProposalAsync() => ExecuteAndReloadAsync(async companyId =>
    {
        if (segmentsState.SegmentProposal is null) return;
        var claims = segmentsState.SegmentProposal.Claims.Select(x => new { area = x.Area, text = x.Recommendation,
            classification = x.Classification, confidence = x.Confidence, sourceIds = x.SourceIds }).ToArray();
        var claimJson = System.Text.Json.JsonSerializer.Serialize(claims);
        var evidenceJson = System.Text.Json.JsonSerializer.Serialize(new { sourceIds = segmentsState.SegmentProposal.Sources.Select(x => x.Id),
            missingEvidence = segmentsState.SegmentProposal.MissingEvidence });
        var scores = new Dictionary<string, decimal> { ["sizeGrowth"] = 0, ["needIntensity"] = 0,
            ["productFit"] = 0, ["differentiation"] = 0, ["reachability"] = 0, ["priceValueFit"] = 0,
            ["economics"] = 0, ["evidenceQuality"] = 0, ["risk"] = 100 };
        var key = $"segment-proposal:{segmentsState.SegmentProposal.RunId:N}";
        var version = new CreateMarketingSegmentVersionViewModel(
            System.Text.Json.JsonSerializer.Serialize(new { basis = "Maya grounded proposal", requiresHumanReview = true }),
            claimJson, claimJson, claimJson, claimJson, null, null, "not_estimated", segmentsState.SegmentProposal.Confidence,
            System.Text.Json.JsonSerializer.Serialize(new { status = "not_estimated", missingEvidence = true }),
            System.Text.Json.JsonSerializer.Serialize(new { policy = "segment-attractiveness-v1", missingEvidenceScore = 0 }),
            scores, evidenceJson, DateTime.UtcNow, key);
        await MarketingApi.CommitSegmentProposalAsync(companyId, new(segmentsState.SegmentProposal.AgentId, segmentsState.SegmentProposal.RunId,
            segmentsState.SegmentProposal.SegmentName, segmentsState.SegmentProposal.Summary, version, key));
        segmentsState.SegmentProposal = null;
    });
    private async Task PrepareStrategyProposalAsync()
    {
        if (resolvedCompanyId is not Guid companyId || marketingAgentId is not Guid agentId || strategyState.StrategyProposalForm.SegmentVersionId == Guid.Empty) return;
        isSaving = true; errorMessage = null;
        try { strategyState.StrategyProposal = await MarketingApi.PrepareStrategyProposalAsync(companyId, new(agentId,
            strategyState.StrategyProposalForm.Objective, strategyState.StrategyProposalForm.Title, DateTime.UtcNow.Date,
            DateTime.UtcNow.Date.AddMonths(6), [strategyState.StrategyProposalForm.SegmentVersionId])); }
        catch (MarketingApiException ex) { errorMessage = ex.Message; }
        finally { isSaving = false; }
    }

    private async Task SelectJourneyAsync(MarketingJourneyViewModel journey)
    {
        journeysState.SelectedJourney = journey;
        if (resolvedCompanyId is not Guid companyId) return;
        journeysState.JourneyValidation = await MarketingApi.ValidateJourneyAsync(companyId, journey.Id);
        journeysState.JourneyAudiencePreview = await MarketingApi.PreviewJourneyAudienceAsync(companyId, journey.Id);
    }

    private Task TransitionSelectedJourneyAsync(string transition) => ExecuteAndReloadAsync(async companyId =>
    {
        if (journeysState.SelectedJourney is null) return;
        _ = transition switch
        {
            "pause" => await MarketingApi.PauseJourneyAsync(companyId, journeysState.SelectedJourney.Id),
            "resume" => await MarketingApi.ResumeJourneyAsync(companyId, journeysState.SelectedJourney.Id),
            "complete" => await MarketingApi.CompleteJourneyAsync(companyId, journeysState.SelectedJourney.Id),
            "cancel" => await MarketingApi.CancelJourneyAsync(companyId, journeysState.SelectedJourney.Id),
            _ => throw new InvalidOperationException("Unsupported journey transition.")
        };
    });
    private Task CommitStrategyProposalAsync() => ExecuteAndReloadAsync(async companyId =>
    {
        if (strategyState.StrategyProposal is null) return;
        await MarketingApi.CommitStrategyProposalAsync(companyId, new(strategyState.StrategyProposal.AgentId,
            strategyState.StrategyProposal.RunId, strategyState.StrategyProposal.Title, strategyState.StrategyProposal.BusinessContext,
            strategyState.StrategyProposal.ValidFromUtc, strategyState.StrategyProposal.ValidToUtc,
            [strategyState.StrategyProposalForm.SegmentVersionId], $"strategy-proposal:{strategyState.StrategyProposal.RunId:N}"));
        strategyState.StrategyProposal = null;
    });
    private async Task PreviewDecompositionAsync()
    {
        if (resolvedCompanyId is not Guid companyId) return;
        var strategy = strategyState.Strategies.FirstOrDefault(x => x.Id == strategyState.DecompositionForm.StrategyId);
        var segmentId = strategy?.SegmentVersionIds.FirstOrDefault() ?? Guid.Empty;
        if (segmentId == Guid.Empty || strategyState.DecompositionForm.CampaignId == Guid.Empty || strategyState.DecompositionForm.ObjectiveId == Guid.Empty) return;
        var start = DateTime.UtcNow.Date; var end = start.AddDays(42);
        var activities = new MarketingDecompositionActivityViewModel[]
        {
            new("Audience and evidence validation", "research", "internal", start, start.AddDays(7), marketingAgentId, null, false),
            new("Campaign message and offer", "planning", "internal", start.AddDays(7), start.AddDays(14), marketingAgentId, "Audience and evidence validation", true),
            new("Content development", "content", "multi_channel", start.AddDays(14), start.AddDays(28), marketingAgentId, "Campaign message and offer", true),
            new("Measurement readiness", "measurement", "internal", start.AddDays(28), end, marketingAgentId, "Content development", false)
        };
        isSaving = true; errorMessage = null;
        try { strategyState.DecompositionProposal = await MarketingApi.PrepareDecompositionAsync(companyId,
            new(strategyState.DecompositionForm.StrategyId, strategyState.DecompositionForm.CampaignId, segmentId,
                strategyState.DecompositionForm.ObjectiveId, strategyState.DecompositionForm.PlanName,
                "Execute the approved strategy through reviewable Sales-campaign work.", start, end, null, "SEK", activities)); }
        catch (MarketingApiException ex) { errorMessage = ex.Message; }
        finally { isSaving = false; }
    }
    private Task CommitDecompositionAsync() => ExecuteAndReloadAsync(async companyId =>
    {
        if (strategyState.DecompositionProposal is null) return;
        var request = new PrepareMarketingDecompositionViewModel(strategyState.DecompositionProposal.StrategyId,
            strategyState.DecompositionProposal.CampaignId, strategyState.DecompositionProposal.TargetSegmentVersionId,
            strategyState.DecompositionProposal.ObjectiveId, strategyState.DecompositionProposal.PlanName, strategyState.DecompositionProposal.PlanSummary,
            strategyState.DecompositionProposal.StartsUtc, strategyState.DecompositionProposal.EndsUtc, strategyState.DecompositionProposal.PlannedBudget,
            strategyState.DecompositionProposal.BudgetCurrency, strategyState.DecompositionProposal.Activities);
        await MarketingApi.CommitDecompositionAsync(companyId, new(strategyState.DecompositionProposal.ProposalKey, request));
        strategyState.DecompositionProposal = null;
    });
    private async Task CreateGroundedPlanAsync()
    {
        if (resolvedCompanyId is not Guid companyId || !plansState.CanCreateGroundedPlan) return;
        var strategy = strategyState.Strategies.Single(x => x.Id == plansState.PlanForm.StrategyId);
        isSaving = true; plansState.PlanActionMessage = null;
        try
        {
            var evidence = ParseStringList(strategy.EvidenceReferencesJson);
            var missingEvidence = ParseStringList(strategy.MissingEvidenceJson);
            var request = new CreateGroundedMarketingPlanViewModel(plansState.PlanForm.Name.Trim(), plansState.PlanForm.Summary.Trim(),
                strategy.Id, strategy.Version, DateTime.SpecifyKind(plansState.PlanForm.StartsUtc.Date, DateTimeKind.Utc),
                DateTime.SpecifyKind(plansState.PlanForm.EndsUtc.Date, DateTimeKind.Utc), plansState.PlanForm.Budget,
                plansState.PlanForm.Currency.Trim().ToUpperInvariant(), [plansState.PlanForm.ObjectiveId],
                [new(plansState.PlanForm.SegmentVersionId, "primary", 1, "Approved target audience for this strategy.", "Contribute to the selected plan objective.")],
                "Created from the approved strategy, active objective, and approved target audience.", evidence, [], [], missingEvidence,
                $"marketing-plan:{companyId:N}:{Guid.NewGuid():N}", marketingAgentId);
            var created = await MarketingApi.CreateGroundedPlanAsync(companyId, request);
            plansState.ShowPlanForm = false;
            await LoadAsync();
            await SelectPlanAsync(created.Summary.Id);
        }
        catch (Exception ex) { plansState.PlanActionMessage = SafeMessage(ex); }
        finally { isSaving = false; }
    }
    private Task CreateContentAsync() => ExecuteAndReloadAsync(async id => await MarketingApi.CreateContentAsync(id,
        new(null, null, contentState.ContentForm.Title, contentState.ContentForm.Purpose, contentState.ContentForm.Audience, contentState.ContentForm.Channel, contentState.ContentForm.Language, "Clear and helpful", contentState.ContentForm.CallToAction, DateTime.UtcNow.AddDays(7),
            null, contentState.ContentForm.MeasurableObjective, contentState.ContentForm.FunnelStage, null, contentState.ContentForm.KeyMessage,
            "[]", contentState.ContentForm.Offer, "[]", "[]", "{}", "Not specified", $"[\"{contentState.ContentForm.Channel}\"]",
            "{\"count\":3}", "{\"citationsRequired\":true}", "{\"managerReviewRequired\":true}")));
    private Task CreateExperimentAsync() => ExecuteAndReloadAsync(async id => await MarketingApi.CreateExperimentAsync(id,
        new(null, experimentsState.ExperimentForm.Name, experimentsState.ExperimentForm.Hypothesis, experimentsState.ExperimentForm.PrimaryMetric, experimentsState.ExperimentForm.GuardrailMetric, experimentsState.ExperimentForm.MinimumSampleSize, DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(30))));
    private Task CreateQualificationAsync() => ExecuteAndReloadAsync(async id => await MarketingApi.CreateQualificationDefinitionAsync(id,
        new(audiencesState.QualificationForm.Name, audiencesState.QualificationForm.AudienceType, audiencesState.QualificationForm.Channel, audiencesState.QualificationForm.MinimumScore,
            audiencesState.QualificationForm.FreshnessDays, audiencesState.QualificationForm.RequiresCustomerCompany, DateTime.UtcNow)));
    private Task ActivateObjectiveAsync(Guid id) => ExecuteAndReloadAsync(async companyId => await MarketingApi.ActivateObjectiveAsync(companyId, id));
    private Task ActivatePlanAsync(Guid id) => ExecuteAndReloadAsync(async companyId => await MarketingApi.ActivatePlanAsync(companyId, id));

    private async Task SelectPlanAsync(Guid planId)
    {
        if (resolvedCompanyId is not Guid companyId) return;
        plansState.PortfolioProposal = null; plansState.PendingPortfolio = null; plansState.PlanActionMessage = null;
        plansState.SelectedPlan = await MarketingApi.GetPlanPortfolioAsync(companyId, planId);
        Navigation.NavigateTo(Navigation.GetUriWithQueryParameters(new Dictionary<string, object?> { ["section"] = "Plans", ["planId"] = planId }), forceLoad: true);
    }

    private async Task SubmitSelectedPlanAsync()
    {
        if (resolvedCompanyId is not Guid companyId || plansState.SelectedPlan is null) return;
        isSaving = true; errorMessage = null;
        try
        {
            plansState.SelectedPlan = await MarketingApi.SubmitPlanForReviewAsync(companyId, plansState.SelectedPlan.Summary.Id, plansState.SelectedPlan.Summary.Version);
            await LoadAsync();
        }
        catch (Exception ex) { errorMessage = SafeMessage(ex); }
        finally { isSaving = false; }
    }

    private async Task ActivateSelectedPlanAsync()
    {
        if (resolvedCompanyId is not Guid companyId || plansState.SelectedPlan is null) return;
        isSaving = true; errorMessage = null;
        try
        {
            plansState.SelectedPlan = await MarketingApi.ActivateGroundedPlanAsync(companyId, plansState.SelectedPlan.Summary.Id, plansState.SelectedPlan.Summary.Version);
            await LoadAsync();
        }
        catch (Exception ex) { errorMessage = SafeMessage(ex); }
        finally { isSaving = false; }
    }

    private async Task PrepareSelectedPlanPortfolioAsync()
    {
        if (resolvedCompanyId is not Guid companyId || plansState.SelectedPlan is null) return;
        if (plansState.SelectedPlan.Objectives.FirstOrDefault() is not { } objective || plansState.SelectedPlan.Segments.Count == 0)
        {
            plansState.PlanActionMessage = "This older plan has no linked objective or approved audience, so Maya cannot create a governed campaign portfolio. Create a grounded plan and select both prerequisites.";
            return;
        }
        isSaving = true; errorMessage = null;
        plansState.PlanActionMessage = null;
        try
        {
            var objectiveSource = dashboard?.Objectives.FirstOrDefault(x => x.Id == objective.Id);
            var starts = plansState.SelectedPlan.Summary.StartsUtc > DateTime.UtcNow ? plansState.SelectedPlan.Summary.StartsUtc : DateTime.UtcNow.Date.AddDays(7);
            var launch = starts.AddDays(14); var ends = plansState.SelectedPlan.Summary.EndsUtc;
            if (launch >= ends) launch = starts.AddDays(Math.Max(1, (ends - starts).Days / 2));
            var allocation = plansState.SelectedPlan.Summary.RemainingBudget is > 0 ? plansState.SelectedPlan.Summary.RemainingBudget : null;
            var item = new MarketingCampaignPortfolioItemViewModel(
                $"{plansState.SelectedPlan.Summary.Name} · {objective.Name}", $"Advance {objective.Name} for the selected plan audiences.", objective.Id,
                $"Contribute to {objective.Name}.", plansState.SelectedPlan.Segments.Select(x => x.SegmentVersionId).ToArray(), allocation,
                plansState.SelectedPlan.Summary.BudgetCurrency, plansState.SelectedPlan.Campaigns.Count + 1, "lead_generation", "marketing_segment",
                objectiveSource?.TargetValue ?? 1m, objectiveSource?.Unit ?? "result", ends, starts, launch, ends.AddDays(-1), ends,
                "UTC", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, ["internal"], "Grounded in the approved plan evidence",
                ["Prepare campaign assets", "Review campaign readiness"], ["Campaign message and channel brief"],
                string.Join("; ", plansState.SelectedPlan.Segments.Select(x => x.SegmentName)), "Measure the linked plan objective before and after launch.",
                plansState.SelectedPlan.EvidenceReferences, plansState.SelectedPlan.MissingEvidence);
            plansState.PendingPortfolio = new(plansState.SelectedPlan.Summary.Id, plansState.SelectedPlan.Summary.Version, [item],
                $"maya-plan-portfolio:{plansState.SelectedPlan.Summary.Id:N}:v{plansState.SelectedPlan.Summary.Version}", marketingAgentId);
            plansState.PortfolioProposal = await MarketingApi.PrepareCampaignPortfolioAsync(companyId, plansState.SelectedPlan.Summary.Id, plansState.PendingPortfolio);
        }
        catch (Exception ex) { plansState.PlanActionMessage = SafeMessage(ex); }
        finally { isSaving = false; }
    }

    private async Task CommitSelectedPlanPortfolioAsync()
    {
        if (resolvedCompanyId is not Guid companyId || plansState.SelectedPlan is null || plansState.PendingPortfolio is null) return;
        isSaving = true; errorMessage = null;
        try
        {
            await MarketingApi.CommitCampaignPortfolioAsync(companyId, plansState.SelectedPlan.Summary.Id, plansState.PendingPortfolio);
            plansState.PortfolioProposal = null; plansState.PendingPortfolio = null;
            await LoadAsync();
        }
        catch (Exception ex) { errorMessage = SafeMessage(ex); }
        finally { isSaving = false; }
    }

    private static string SafeMessage(Exception ex) => ex is MarketingApiException ? ex.Message : "Marketing could not complete that request. Refresh and try again.";
    private Task SubmitContentAsync(Guid id) => ExecuteAndReloadAsync(companyId => MarketingApi.SubmitContentAsync(companyId, id));
    private async Task PreflightContentAsync(Guid id)
    {
        if (resolvedCompanyId is not Guid companyId) return;
        try { contentState.ContentPreflight = await MarketingApi.PreflightContentAsync(companyId, id); }
        catch (MarketingApiException ex) { errorMessage = ex.Message; }
    }
    private Task ActivateQualificationAsync(Guid id) => ExecuteAndReloadAsync(async companyId => await MarketingApi.ActivateQualificationDefinitionAsync(companyId, id));
    private Task ReviewContentAsync(Guid id, bool approved) => ExecuteAndReloadAsync(companyId => MarketingApi.ReviewContentVersionAsync(companyId, id, approved, dashboard!.Content.Single(x => x.Id == id).Version));
    private async Task GenerateContentVariantsAsync()
    {
        if (resolvedCompanyId is not Guid companyId || marketingAgentId is not Guid agentId || contentState.ContentGenerationForm.BriefId == Guid.Empty) return;
        isSaving = true; errorMessage = null;
        try
        {
            contentState.ContentGenerationResult = await MarketingApi.GenerateContentVariantsAsync(companyId,
                contentState.ContentGenerationForm.BriefId, new(agentId, contentState.ContentGenerationForm.ContentFormat,
                    contentState.ContentGenerationForm.VariantCount, contentState.ContentGenerationForm.Instructions,
                    $"content:{contentState.ContentGenerationForm.BriefId:N}:{Guid.NewGuid():N}"));
            await LoadAsync();
        }
        catch (MarketingApiException ex) { errorMessage = ex.Message; }
        finally { isSaving = false; }
    }
    private Task DecideHandoffAsync(Guid id, bool accepted) => ExecuteAndReloadAsync(async companyId => await MarketingApi.DecideHandoffAsync(companyId, id, accepted, accepted ? "Accepted by Marketing workspace user." : "Declined by Marketing workspace user."));
    private Task ActivateExperimentAsync(Guid id) => ExecuteAndReloadAsync(async companyId => await MarketingApi.ActivateExperimentAsync(companyId, id));
    private Task CompleteExperimentAsync(Guid id) => ExecuteAndReloadAsync(async companyId => await MarketingApi.CompleteExperimentAsync(companyId, id, "Completed after review of the primary metric and guardrail."));
    private async Task RunAnalysisAsync()
    {
        if (resolvedCompanyId is not Guid companyId || marketingAgentId is not Guid agentId) return;
        try { overviewState.Analysis = await MarketingApi.AnalyzeAsync(companyId, agentId, "Prioritize measurable marketing work for the next 30 days."); }
        catch (MarketingApiException ex) { errorMessage = ex.Message; }
    }
    private Task RequestOperatingRunAsync() => ExecuteAndReloadAsync(async companyId =>
    {
        if (marketingAgentId is Guid agentId) await MarketingApi.RequestOperatingRunAsync(companyId, agentId, "Operator requested a governed review of company goals and current Marketing evidence.");
    });
    private async Task SelectOperatingRunAsync(MarketingOperatingRunViewModel run)
    {
        operationsState.SelectedOperatingRun = run;
        if (resolvedCompanyId is Guid companyId) operationsState.OperatingActions = await MarketingApi.GetOperatingRunActionsAsync(companyId, run.Id);
    }
    private Task RetryOperatingActionAsync(MarketingOperatingActionViewModel action) => ExecuteAndReloadAsync(async companyId =>
        await MarketingApi.RetryOperatingActionAsync(companyId, action.MarketingOperatingRunId, action.Id,
            "The operator reviewed the blocker and requested a governed retry."));
    private Task CancelOperatingActionAsync(MarketingOperatingActionViewModel action) => ExecuteAndReloadAsync(async companyId =>
        await MarketingApi.CancelOperatingActionAsync(companyId, action.MarketingOperatingRunId, action.Id,
            "The operator cancelled this action; no external effect should occur."));
    private Task SynchronizeChannelActionAsync(Guid actionId) => ExecuteAndReloadAsync(async companyId => await MarketingApi.SynchronizeChannelActionAsync(companyId, actionId));
    private async Task SelectIntelligenceAsync(MarketingIntelligenceViewModel item)
    {
        intelligenceState.SelectedIntelligence = item;
        if (resolvedCompanyId is Guid companyId)
            intelligenceState.IntelligenceReviews = await MarketingApi.GetIntelligenceReviewsAsync(companyId, item.Id);
    }
    private Task ReviewIntelligenceAsync(bool verified) => ExecuteAndReloadAsync(async companyId =>
    {
        if (intelligenceState.SelectedIntelligence is null) return;
        intelligenceState.SelectedIntelligence = await MarketingApi.ReviewIntelligenceAsync(companyId, intelligenceState.SelectedIntelligence.Id,
            new ReviewMarketingIntelligenceViewModel(verified, intelligenceState.IntelligenceReviewRationale, intelligenceState.SelectedIntelligence.Version));
    });
    private Task ArchiveIntelligenceAsync() => ExecuteAndReloadAsync(async companyId =>
    {
        if (intelligenceState.SelectedIntelligence is null) return;
        await MarketingApi.ArchiveIntelligenceAsync(companyId, intelligenceState.SelectedIntelligence.Id, intelligenceState.SelectedIntelligence.Version);
        intelligenceState.SelectedIntelligence = null; intelligenceState.IntelligenceReviews = [];
    });
    private Task CancelChannelActionAsync(Guid actionId) => ExecuteAndReloadAsync(async companyId => await MarketingApi.CancelChannelActionAsync(companyId, actionId));
    private Task ReconcileChannelActionAsync(Guid actionId) => ExecuteAndReloadAsync(async companyId => await MarketingApi.ReconcileChannelActionAsync(companyId, actionId));
    private Task RefreshDestinationsAsync(Guid connectionId) => ExecuteAndReloadAsync(async companyId => await MarketingApi.RefreshChannelDestinationsAsync(companyId, connectionId));
    private Task DisconnectChannelAsync(Guid connectionId) => ExecuteAndReloadAsync(async companyId => await MarketingApi.DisconnectChannelAsync(companyId, connectionId));
    private async Task StartChannelOAuthAsync(string provider)
    {
        if (resolvedCompanyId is not Guid companyId) return;
        isSaving = true; errorMessage = null;
        try
        {
            var callback = new Uri(new Uri(Navigation.BaseUri), "api/marketing/channel-oauth/callback").ToString();
            var start = await MarketingApi.StartChannelOAuthAsync(companyId, provider, callback);
            Navigation.NavigateTo(start.AuthorizationUri.ToString(), true);
        }
        catch (MarketingApiException ex) { errorMessage = ex.Message; }
        finally { isSaving = false; }
    }
    private Task ResolveEventAsync(Guid eventId) => ExecuteAndReloadAsync(async companyId => await MarketingApi.ResolveEventAsync(companyId, eventId));
    private Task GenerateCreativeAsync() => ExecuteAndReloadAsync(async companyId => await MarketingApi.GenerateCreativeAssetAsync(companyId,
        new(creativeState.CreativeForm.BriefId, null, creativeState.CreativeForm.Name, creativeState.CreativeForm.Prompt, creativeState.CreativeForm.Dimensions,
            creativeState.CreativeForm.Language, creativeState.CreativeForm.BrandProfileVersion, creativeState.CreativeForm.AltText,
            $"creative:{creativeState.CreativeForm.BriefId:N}:{Guid.NewGuid():N}", creativeState.CreativeForm.Quality, "png")));
    private Task SubmitCreativeAsync(Guid assetId) => ExecuteAndReloadAsync(async companyId => await MarketingApi.SubmitCreativeAssetAsync(companyId, assetId));
    private Task ReviewCreativeAsync(Guid assetId, bool approved) => ExecuteAndReloadAsync(async companyId => await MarketingApi.ReviewCreativeAssetAsync(companyId, assetId, approved));
    private Task RequestCreativeChangesAsync(Guid assetId) => ExecuteAndReloadAsync(async companyId => await MarketingApi.RequestCreativeAssetChangesAsync(companyId, assetId));
    private Task RescanCreativeAsync(Guid assetId) => ExecuteAndReloadAsync(async companyId => await MarketingApi.RescanCreativeAssetAsync(companyId, assetId));
    private Task RetireCreativeAsync(Guid assetId) => ExecuteAndReloadAsync(async companyId => await MarketingApi.RetireCreativeAssetAsync(companyId, assetId));

    private string WithCompany(string path) => resolvedCompanyId is Guid id
        ? DashboardRoutes.EnsureWorkspaceContext(path, id, WorkspaceOrigin) + "&recordReturnUrl=" + Uri.EscapeDataString(PreviousView)
        : path;
    private void ShowObjectives() => activeTab = "Objectives";
    private void ClosePlanForm() => plansState.ShowPlanForm = false;
    private void OpenPlanForm() { plansState.ShowPlanForm = true; plansState.InitializePlanFormSelections(); }
}
