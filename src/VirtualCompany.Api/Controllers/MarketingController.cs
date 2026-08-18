using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Application.Agents;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/marketing")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class MarketingController : ControllerBase
{
    private readonly ICompanyContextAccessor _context;
    private readonly IMarketingOperationsService _marketing;
    private readonly IMarketingAgentAnalysisService _analysis;
    private readonly IMarketingStrategyService _strategy;
    private readonly IMarketingOperatingLoopService _operatingLoop;
    private readonly IMarketingCompanyOrchestrationService _companyOrchestration;
    private readonly IMarketingDeliveryService _delivery;
    private readonly IMarketingPolicyService _policies;
    private readonly IMarketingChannelConnectionService _channelConnections;
    private readonly IMarketingChannelDispatchService _channelDispatch;
    private readonly IMarketingJourneyInboundEventService _journeyInboundEvents;
    private readonly IMarketingMeasurementService _measurement;
    private readonly IMarketingBriefingService _briefings;
    public MarketingController(ICompanyContextAccessor context, IMarketingOperationsService marketing,
        IMarketingAgentAnalysisService analysis, IMarketingStrategyService strategy,
        IMarketingOperatingLoopService operatingLoop, IMarketingCompanyOrchestrationService companyOrchestration,
        IMarketingDeliveryService delivery, IMarketingPolicyService policies,
        IMarketingChannelConnectionService channelConnections, IMarketingChannelDispatchService channelDispatch,
        IMarketingJourneyInboundEventService journeyInboundEvents, IMarketingMeasurementService measurement,
        IMarketingBriefingService briefings)
    {
        _context = context;
        _marketing = marketing;
        _analysis = analysis;
        _strategy = strategy;
        _operatingLoop = operatingLoop;
        _companyOrchestration = companyOrchestration;
        _delivery = delivery;
        _policies = policies;
        _channelConnections = channelConnections;
        _channelDispatch = channelDispatch;
        _journeyInboundEvents = journeyInboundEvents;
        _measurement = measurement;
        _briefings = briefings;
    }

    [HttpGet("dashboard")]
    public Task<MarketingDashboardDto> DashboardAsync([FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, CancellationToken ct)
    {
        var to = toUtc ?? DateTime.UtcNow.AddDays(1);
        return _marketing.GetDashboardAsync(CompanyId(), fromUtc ?? to.AddDays(-30), to, ct);
    }

    [HttpGet("objectives")]
    public Task<IReadOnlyList<MarketingObjectiveDto>> ObjectivesAsync(CancellationToken ct) =>
        _marketing.ListObjectivesAsync(CompanyId(), ct);
    [HttpPost("objectives")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingObjectiveDto>> CreateObjectiveAsync(CreateMarketingObjectiveRequest request, CancellationToken ct) =>
        Ok(await _marketing.CreateObjectiveAsync(CompanyId(), UserId(), request, ct));
    [HttpPost("objectives/{objectiveId:guid}/activate")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingObjectiveDto>> ActivateObjectiveAsync(Guid objectiveId, CancellationToken ct)
    {
        var result = await _marketing.ActivateObjectiveAsync(CompanyId(), objectiveId, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("plans")]
    public Task<IReadOnlyList<MarketingPlanDto>> PlansAsync(CancellationToken ct) =>
        _marketing.ListPlansAsync(CompanyId(), ct);
    [HttpPost("plans")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingPlanDto>> CreatePlanAsync(CreateMarketingPlanRequest request, CancellationToken ct) =>
        Ok(await _marketing.CreatePlanAsync(CompanyId(), UserId(), request, ct));
    [HttpPost("plans/proposal")]
    public async Task<ActionResult<MarketingPlanProposalDto>> PreparePlanProposalAsync(CreateMarketingPlanRequest request, CancellationToken ct) =>
        Ok(await _marketing.PreparePlanProposalAsync(CompanyId(), request, ct));
    [HttpPost("plans/commit")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingPlanDto>> CommitPlanAsync(CommitMarketingPlanRequest request, CancellationToken ct) =>
        Ok(await _marketing.CommitPlanAsync(CompanyId(), UserId(), request, ct));
    [HttpPost("plans/{planId:guid}/activate")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingPlanDto>> ActivatePlanAsync(Guid planId, CancellationToken ct)
    {
        var result = await _marketing.ActivatePlanAsync(CompanyId(), planId, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("content")]
    public Task<IReadOnlyList<MarketingContentBriefDto>> ContentAsync(CancellationToken ct) =>
        _marketing.ListContentAsync(CompanyId(), ct);
    [HttpPost("content")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingContentBriefDto>> CreateContentAsync(CreateMarketingContentBriefRequest request, CancellationToken ct) =>
        Ok(await _marketing.CreateContentBriefAsync(CompanyId(), UserId(), request, ct));
    [HttpPost("content/{briefId:guid}/variants")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingContentVariantDto>> AddVariantAsync(Guid briefId, CreateMarketingContentVariantRequest request, CancellationToken ct)
    {
        var result = await _marketing.AddContentVariantAsync(CompanyId(), briefId, request, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("plan-portfolio")]
    public Task<MarketingPlanListItemDto[]> PlanPortfolioAsync(CancellationToken ct) =>
        _marketing.ListPlanPortfolioAsync(CompanyId(), ct);

    [HttpGet("plans/{planId:guid}/portfolio")]
    public async Task<ActionResult<MarketingPlanDetailDto>> PlanPortfolioDetailAsync(Guid planId, CancellationToken ct)
    {
        var result = await _marketing.GetPlanPortfolioAsync(CompanyId(), planId, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("plans/grounded/readiness")]
    public Task<MarketingPolicyDecisionDto> PlanReadinessAsync(CreateGroundedMarketingPlanRequest request, CancellationToken ct) =>
        _marketing.AssessPlanReadinessAsync(CompanyId(), request, ct);

    [HttpPost("plans/grounded")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public Task<MarketingPlanDetailDto> CreateGroundedPlanAsync(CreateGroundedMarketingPlanRequest request, CancellationToken ct) =>
        _marketing.CreateGroundedPlanAsync(CompanyId(), UserId(), request, ct);

    [HttpPost("plans/{planId:guid}/campaign-portfolio/proposal")]
    public Task<MarketingCampaignPortfolioProposalDto> PrepareCampaignPortfolioAsync(Guid planId,
        PrepareMarketingCampaignPortfolioRequest request, CancellationToken ct)
    {
        if (planId != request.PlanId) throw new ArgumentException("Plan route and request must match.");
        return _marketing.PrepareCampaignPortfolioAsync(CompanyId(), request, ct);
    }

    [HttpPost("plans/{planId:guid}/campaign-portfolio/commit")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public Task<MarketingCampaignPortfolioResultDto> CommitCampaignPortfolioAsync(Guid planId,
        CommitMarketingCampaignPortfolioRequest request, CancellationToken ct)
    {
        if (planId != request.Portfolio.PlanId) throw new ArgumentException("Plan route and request must match.");
        return _marketing.CommitCampaignPortfolioAsync(CompanyId(), UserId(), request, ct);
    }

    [HttpGet("daily-review")]
    public Task<MarketingDailyReviewDto?> DailyReviewAsync([FromQuery] DateTime? dateUtc, CancellationToken ct) =>
        _marketing.GetDailyReviewAsync(CompanyId(), dateUtc ?? DateTime.UtcNow, ct);

    [HttpPost("plans/{planId:guid}/submit")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingPlanDetailDto>> SubmitGroundedPlanAsync(Guid planId, [FromQuery] int expectedVersion, CancellationToken ct)
    { var result = await _marketing.SubmitPlanForReviewAsync(CompanyId(), UserId(), planId, expectedVersion, ct); return result is null ? NotFound() : Ok(result); }

    [HttpPost("plans/{planId:guid}/activate-grounded")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingPlanDetailDto>> ActivateGroundedPlanAsync(Guid planId, [FromQuery] int expectedVersion, CancellationToken ct)
    { var result = await _marketing.ActivateGroundedPlanAsync(CompanyId(), UserId(), planId, expectedVersion, ct); return result is null ? NotFound() : Ok(result); }

    [HttpPost("plans/{planId:guid}/complete")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingPlanDetailDto>> CompleteGroundedPlanAsync(Guid planId, TransitionMarketingPlanRequest request, CancellationToken ct)
    { var result = await _marketing.CompletePlanAsync(CompanyId(), planId, request, ct); return result is null ? NotFound() : Ok(result); }

    [HttpPost("plans/{planId:guid}/cancel")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingPlanDetailDto>> CancelGroundedPlanAsync(Guid planId, TransitionMarketingPlanRequest request, CancellationToken ct)
    { var result = await _marketing.CancelPlanAsync(CompanyId(), planId, request, ct); return result is null ? NotFound() : Ok(result); }
    [HttpPost("content-variants/{variantId:guid}/versions")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingContentVariantDto>> CreateVariantVersionAsync(Guid variantId,
        CreateMarketingContentVariantVersionRequest request, CancellationToken ct)
    { var result = await _marketing.CreateContentVariantVersionAsync(CompanyId(), variantId, request, ct); return result is null ? NotFound() : Ok(result); }
    [HttpPost("content-variants/{variantId:guid}/retire")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<IActionResult> RetireVariantAsync(Guid variantId, CancellationToken ct) =>
        await _marketing.RetireContentVariantAsync(CompanyId(), variantId, ct) ? NoContent() : NotFound();
    [HttpPost("content/{briefId:guid}/generate")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<GenerateMarketingContentVariantsResult>> GenerateVariantsAsync(Guid briefId,
        GenerateMarketingContentVariantsRequest request, CancellationToken ct) =>
        Ok(await _marketing.GenerateContentVariantsAsync(CompanyId(), UserId(), briefId, request, ct));
    [HttpPost("content/{briefId:guid}/review")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<IActionResult> ReviewContentAsync(Guid briefId, ReviewMarketingContentRequest request, CancellationToken ct) =>
        await _marketing.ReviewContentAsync(CompanyId(), briefId, request, ct) ? NoContent() : NotFound();
    [HttpPost("content/{briefId:guid}/submit")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<IActionResult> SubmitContentAsync(Guid briefId, CancellationToken ct) =>
        await _marketing.SubmitContentAsync(CompanyId(), briefId, ct) ? NoContent() : NotFound();
    [HttpGet("content/{briefId:guid}/preflight")]
    public async Task<ActionResult<MarketingContentPreflightDto>> PreflightContentAsync(Guid briefId, CancellationToken ct)
    {
        var result = await _marketing.PreflightContentAsync(CompanyId(), briefId, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("handoffs")]
    public Task<IReadOnlyList<MarketingSalesHandoffDto>> HandoffsAsync(CancellationToken ct) =>
        _marketing.ListHandoffsAsync(CompanyId(), ct);
    [HttpPost("handoffs")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingSalesHandoffDto>> CreateHandoffAsync(CreateMarketingSalesHandoffRequest request, CancellationToken ct) =>
        Ok(await _marketing.CreateHandoffAsync(CompanyId(), request, ct));
    [HttpPost("handoffs/{handoffId:guid}/decision")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingSalesHandoffDto>> DecideHandoffAsync(Guid handoffId, DecideMarketingSalesHandoffRequest request, CancellationToken ct)
    {
        var result = await _marketing.DecideHandoffAsync(CompanyId(), handoffId, request, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("observations")]
    public Task<IReadOnlyList<MarketingObservationDto>> ObservationsAsync([FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, CancellationToken ct) =>
        _marketing.ListObservationsAsync(CompanyId(), fromUtc ?? DateTime.UtcNow.AddDays(-30), toUtc ?? DateTime.UtcNow.AddDays(1), ct);
    [HttpPost("observations")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingObservationDto>> RecordObservationAsync(CreateMarketingObservationRequest request, CancellationToken ct) =>
        Ok(await _marketing.RecordObservationAsync(CompanyId(), request, ct));

    [HttpGet("experiments")]
    public Task<IReadOnlyList<MarketingExperimentDto>> ExperimentsAsync(CancellationToken ct) =>
        _marketing.ListExperimentsAsync(CompanyId(), ct);
    [HttpPost("experiments")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingExperimentDto>> CreateExperimentAsync(CreateMarketingExperimentRequest request, CancellationToken ct) =>
        Ok(await _marketing.CreateExperimentAsync(CompanyId(), request, ct));
    [HttpPost("experiments/{experimentId:guid}/activate")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingExperimentDto>> ActivateExperimentAsync(Guid experimentId, CancellationToken ct)
    {
        var result = await _marketing.ActivateExperimentAsync(CompanyId(), experimentId, ct);
        return result is null ? NotFound() : Ok(result);
    }
    [HttpPost("experiments/{experimentId:guid}/complete")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingExperimentDto>> CompleteExperimentAsync(Guid experimentId, CompleteMarketingExperimentRequest request, CancellationToken ct)
    {
        var result = await _marketing.CompleteExperimentAsync(CompanyId(), experimentId, request, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("qualification-definitions")]
    public Task<IReadOnlyList<MarketingQualificationDefinitionDto>> QualificationDefinitionsAsync(CancellationToken ct) =>
        _marketing.ListQualificationDefinitionsAsync(CompanyId(), ct);
    [HttpPost("qualification-definitions")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingQualificationDefinitionDto>> CreateQualificationDefinitionAsync(
        CreateMarketingQualificationDefinitionRequest request, CancellationToken ct) =>
        Ok(await _marketing.CreateQualificationDefinitionAsync(CompanyId(), UserId(), request, ct));
    [HttpPost("qualification-definitions/{definitionId:guid}/activate")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingQualificationDefinitionDto>> ActivateQualificationDefinitionAsync(
        Guid definitionId, CancellationToken ct)
    {
        var result = await _marketing.ActivateQualificationDefinitionAsync(CompanyId(), definitionId, ct);
        return result is null ? NotFound() : Ok(result);
    }
    [HttpGet("qualification-evaluations")]
    public Task<IReadOnlyList<MarketingQualificationEvaluationDto>> QualificationEvaluationsAsync(CancellationToken ct) =>
        _marketing.ListQualificationEvaluationsAsync(CompanyId(), ct);
    [HttpPost("qualification-evaluations")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingQualificationEvaluationDto>> EvaluateContactAsync(
        EvaluateMarketingContactRequest request, CancellationToken ct) =>
        Ok(await _marketing.EvaluateContactAsync(CompanyId(), request, ct));
    [HttpPost("qualification-evaluations/{evaluationId:guid}/feedback")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingQualificationFeedbackDto>> RecordQualificationFeedbackAsync(
        Guid evaluationId, RecordMarketingQualificationFeedbackRequest request, CancellationToken ct) =>
        Ok(await _marketing.RecordQualificationFeedbackAsync(CompanyId(), UserId(), evaluationId, request, ct));

    [HttpPost("agents/{agentId:guid}/analysis")]
    public async Task<ActionResult<RoleAgentAnalysisResult>> AnalyzeAsync(Guid agentId, RoleAgentAnalysisRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _analysis.AnalyzeAsync(CompanyId(), agentId, UserId(), request, ct));
        }
        catch (MarketingAgentAccessException exception)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Marketing agent unavailable",
                detail: exception.Message,
                extensions: new Dictionary<string, object?> { ["reasonCode"] = exception.ReasonCode });
        }
    }

    [HttpGet("strategies")]
    public Task<IReadOnlyList<MarketingStrategyDto>> StrategiesAsync(CancellationToken ct) =>
        _strategy.ListStrategiesAsync(CompanyId(), ct);
    [HttpGet("strategies/{strategyId:guid}")]
    public async Task<ActionResult<MarketingStrategyDto>> StrategyAsync(Guid strategyId, CancellationToken ct)
    { var result = await _strategy.GetStrategyAsync(CompanyId(), strategyId, ct); return result is null ? NotFound() : Ok(result); }
    [HttpPost("strategies")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingStrategyDto>> CreateStrategyAsync(SaveMarketingStrategyRequest request, CancellationToken ct) =>
        Ok(await _strategy.CreateStrategyAsync(CompanyId(), UserId(), request, ct));
    [HttpPost("strategies/proposal")]
    public async Task<ActionResult<MarketingStrategyProposalDto>> PrepareStrategyProposalAsync(
        PrepareMarketingStrategyProposalRequest request, CancellationToken ct) =>
        Ok(await _strategy.PrepareStrategyProposalAsync(CompanyId(), UserId(), request, ct));
    [HttpPost("strategies/proposal/commit")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingStrategyDto>> CommitStrategyProposalAsync(
        CommitMarketingStrategyProposalRequest request, CancellationToken ct) =>
        Ok(await _strategy.CommitStrategyProposalAsync(CompanyId(), UserId(), request, ct));
    [HttpPost("strategies/decomposition/preview")]
    public async Task<ActionResult<MarketingDecompositionProposalDto>> PrepareDecompositionAsync(
        PrepareMarketingDecompositionRequest request, CancellationToken ct) =>
        Ok(await _strategy.PrepareDecompositionAsync(CompanyId(), request, ct));
    [HttpPost("strategies/decomposition/commit")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingDecompositionResultDto>> CommitDecompositionAsync(
        CommitMarketingDecompositionRequest request, CancellationToken ct) =>
        Ok(await _strategy.CommitDecompositionAsync(CompanyId(), UserId(), request, ct));
    [HttpPut("strategies/{strategyId:guid}")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingStrategyDto>> UpdateStrategyAsync(Guid strategyId, SaveMarketingStrategyRequest request, CancellationToken ct)
    { try { var result = await _strategy.UpdateStrategyAsync(CompanyId(), UserId(), strategyId, request, ct); return result is null ? NotFound() : Ok(result); } catch (InvalidOperationException ex) { return Conflict(new ProblemDetails { Status = 409, Title = "Strategy cannot be changed", Detail = ex.Message }); } }
    [HttpPost("strategies/{strategyId:guid}/submit")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingStrategyDto>> SubmitStrategyAsync(Guid strategyId, CancellationToken ct)
    { var result = await _strategy.SubmitStrategyAsync(CompanyId(), UserId(), strategyId, ct); return result is null ? NotFound() : Ok(result); }
    [HttpPost("strategies/{strategyId:guid}/activate")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingStrategyDto>> ActivateStrategyAsync(Guid strategyId, CancellationToken ct)
    { try { var result = await _strategy.ActivateStrategyAsync(CompanyId(), UserId(), strategyId, ct); return result is null ? NotFound() : Ok(result); } catch (InvalidOperationException ex) { return Conflict(new ProblemDetails { Status = 409, Title = "Strategy cannot be activated", Detail = ex.Message }); } }
    [HttpPost("strategies/{strategyId:guid}/cancel")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingStrategyDto>> CancelStrategyAsync(Guid strategyId, CancelMarketingStrategyRequest request, CancellationToken ct)
    { try { var result = await _strategy.CancelStrategyAsync(CompanyId(), UserId(), strategyId, request, ct); return result is null ? NotFound() : Ok(result); } catch (InvalidOperationException ex) { return Conflict(new ProblemDetails { Status = 409, Title = "Strategy cannot be cancelled", Detail = ex.Message }); } }

    [HttpGet("intelligence")]
    public Task<IReadOnlyList<MarketingIntelligenceDto>> IntelligenceAsync([FromQuery] bool freshnessQueue, CancellationToken ct) =>
        _strategy.ListIntelligenceAsync(CompanyId(), freshnessQueue, ct);
    [HttpGet("intelligence/{intelligenceId:guid}")]
    public async Task<ActionResult<MarketingIntelligenceDto>> IntelligenceDetailAsync(Guid intelligenceId, CancellationToken ct)
    { var result = await _strategy.GetIntelligenceAsync(CompanyId(), intelligenceId, ct); return result is null ? NotFound() : Ok(result); }
    [HttpPost("intelligence")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingIntelligenceDto>> CreateIntelligenceAsync(CreateMarketingIntelligenceRequest request, CancellationToken ct) =>
        Ok(await _strategy.CreateIntelligenceAsync(CompanyId(), UserId(), request, ct));
    [HttpPut("intelligence/{intelligenceId:guid}")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingIntelligenceDto>> UpdateIntelligenceAsync(Guid intelligenceId,
        UpdateMarketingIntelligenceRequest request, CancellationToken ct)
    { try { var result = await _strategy.UpdateIntelligenceAsync(CompanyId(), UserId(), intelligenceId, request, ct); return result is null ? NotFound() : Ok(result); } catch (InvalidOperationException ex) { return Conflict(new ProblemDetails { Status = 409, Title = "Intelligence record cannot be updated", Detail = ex.Message }); } }
    [HttpPost("intelligence/{intelligenceId:guid}/review")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingIntelligenceDto>> ReviewIntelligenceAsync(Guid intelligenceId,
        ReviewMarketingIntelligenceRequest request, CancellationToken ct)
    { try { var result = await _strategy.ReviewIntelligenceAsync(CompanyId(), UserId(), intelligenceId, request, ct); return result is null ? NotFound() : Ok(result); } catch (InvalidOperationException ex) { return Conflict(new ProblemDetails { Status = 409, Title = "Intelligence review is stale", Detail = ex.Message }); } }
    [HttpPost("intelligence/{intelligenceId:guid}/archive")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingIntelligenceDto>> ArchiveIntelligenceAsync(Guid intelligenceId,
        ArchiveMarketingIntelligenceRequest request, CancellationToken ct)
    { try { var result = await _strategy.ArchiveIntelligenceAsync(CompanyId(), UserId(), intelligenceId, request, ct); return result is null ? NotFound() : Ok(result); } catch (InvalidOperationException ex) { return Conflict(new ProblemDetails { Status = 409, Title = "Intelligence record cannot be archived", Detail = ex.Message }); } }
    [HttpGet("intelligence/{intelligenceId:guid}/reviews")]
    public Task<IReadOnlyList<MarketingIntelligenceReviewDto>> IntelligenceReviewsAsync(Guid intelligenceId, CancellationToken ct) =>
        _strategy.ListIntelligenceReviewsAsync(CompanyId(), intelligenceId, ct);

    [HttpGet("segments")]
    public Task<IReadOnlyList<MarketingSegmentDto>> SegmentsAsync(CancellationToken ct) => _strategy.ListSegmentsAsync(CompanyId(), ct);
    [HttpPost("segments/proposal")]
    public async Task<ActionResult<MarketingSegmentProposalDto>> PrepareSegmentProposalAsync(PrepareMarketingSegmentProposalRequest request, CancellationToken ct) =>
        Ok(await _strategy.PrepareSegmentProposalAsync(CompanyId(), UserId(), request, ct));
    [HttpPost("segments/proposal/commit")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingSegmentVersionDto>> CommitSegmentProposalAsync(CommitMarketingSegmentProposalRequest request, CancellationToken ct)
    { try { return Ok(await _strategy.CommitSegmentProposalAsync(CompanyId(), UserId(), request, ct)); } catch (InvalidOperationException ex) { return Conflict(new ProblemDetails { Status = 409, Title = "Segment proposal cannot be committed", Detail = ex.Message }); } }
    [HttpPost("segments")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingSegmentDto>> CreateSegmentAsync(CreateMarketingSegmentRequest request, CancellationToken ct) => Ok(await _strategy.CreateSegmentAsync(CompanyId(), UserId(), request, ct));
    [HttpPost("segments/{segmentId:guid}/versions")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingSegmentVersionDto>> CreateSegmentVersionAsync(Guid segmentId, CreateMarketingSegmentVersionRequest request, CancellationToken ct)
    { var result = await _strategy.CreateSegmentVersionAsync(CompanyId(), UserId(), segmentId, request, ct); return result is null ? NotFound() : Ok(result); }
    [HttpPost("segment-versions/{versionId:guid}/submit")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingSegmentVersionDto>> SubmitSegmentVersionAsync(Guid versionId, CancellationToken ct)
    { var result = await _strategy.SubmitSegmentVersionAsync(CompanyId(), UserId(), versionId, ct); return result is null ? NotFound() : Ok(result); }
    [HttpPost("segment-versions/{versionId:guid}/activate-target")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingSegmentVersionDto>> ActivateTargetAsync(Guid versionId, ActivateMarketingTargetRequest request, CancellationToken ct)
    { try { var result = await _strategy.ActivateTargetAsync(CompanyId(), versionId, request, ct); return result is null ? NotFound() : Ok(result); } catch (InvalidOperationException ex) { return Conflict(new ProblemDetails { Status = 409, Title = "Target selection cannot be activated", Detail = ex.Message }); } }
    [HttpGet("segment-versions/{versionId:guid}/impact")]
    public async Task<ActionResult<MarketingSegmentImpactDto>> SegmentImpactAsync(Guid versionId, CancellationToken ct)
    { var result = await _strategy.GetSegmentImpactAsync(CompanyId(), versionId, ct); return result is null ? NotFound() : Ok(result); }
    [HttpGet("segment-versions/{versionId:guid}/dimensions")]
    public Task<IReadOnlyList<MarketingSegmentDimensionDto>> SegmentDimensionsAsync(Guid versionId, CancellationToken ct) =>
        _strategy.ListSegmentDimensionsAsync(CompanyId(), versionId, ct);
    [HttpGet("segment-versions/{versionId:guid}/decision-data")]
    public async Task<ActionResult<MarketingSegmentDecisionDataDto>> SegmentDecisionDataAsync(Guid versionId, CancellationToken ct)
    { var result = await _strategy.GetSegmentDecisionDataAsync(CompanyId(), versionId, ct); return result is null ? NotFound() : Ok(result); }
    [HttpPost("segment-versions/{versionId:guid}/target-recommendations")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingSegmentTargetDecisionDto>> RecommendTargetAsync(Guid versionId,
        CreateMarketingSegmentTargetDecisionRequest request, CancellationToken ct)
    { var result = await _strategy.RecommendTargetAsync(CompanyId(), UserId(), versionId, request, ct); return result is null ? NotFound() : Ok(result); }
    [HttpPost("segment-versions/{versionId:guid}/mappings")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingSegmentArtifactMappingDto>> MapSegmentArtifactAsync(Guid versionId,
        CreateMarketingSegmentArtifactMappingRequest request, CancellationToken ct)
    { try { var result = await _strategy.MapSegmentArtifactAsync(CompanyId(), versionId, request, ct); return result is null ? NotFound() : Ok(result); } catch (InvalidOperationException ex) { return Conflict(new ProblemDetails { Status = 409, Title = "Segment mapping cannot be created", Detail = ex.Message }); } }

    [HttpGet("operating-runs")]
    public Task<IReadOnlyList<MarketingOperatingRunDto>> OperatingRunsAsync([FromQuery] int take, CancellationToken ct) =>
        _operatingLoop.ListAsync(CompanyId(), take == 0 ? 25 : take, ct);
    [HttpPost("agents/{agentId:guid}/operating-runs")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingOperatingRunDto>> RequestOperatingRunAsync(Guid agentId, RequestMarketingOperatingRun request, CancellationToken ct)
    { try { return Ok(await _operatingLoop.RunAsync(CompanyId(), agentId, request, ct)); } catch (MarketingAssignmentException ex) { return Conflict(new ProblemDetails { Status = 409, Title = "Marketing assignment cannot be accepted", Detail = ex.Message, Extensions = { ["reasonCode"] = ex.ReasonCode } }); } }
    [HttpGet("operating-runs/{runId:guid}/actions")]
    public Task<IReadOnlyList<MarketingOperatingActionDto>> OperatingRunActionsAsync(Guid runId, CancellationToken ct) =>
        _operatingLoop.ListActionsAsync(CompanyId(), runId, ct);
    [HttpPost("operating-runs/{runId:guid}/actions/{actionId:guid}/retry")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingOperatingActionDto>> RetryOperatingActionAsync(Guid runId, Guid actionId,
        RetryMarketingOperatingActionRequest request, CancellationToken ct)
    { var result = await _operatingLoop.RetryActionAsync(CompanyId(), runId, actionId, request, ct); return result is null ? NotFound() : Ok(result); }
    [HttpPost("operating-runs/{runId:guid}/actions/{actionId:guid}/cancel")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingOperatingActionDto>> CancelOperatingActionAsync(Guid runId, Guid actionId,
        CancelMarketingOperatingActionRequest request, CancellationToken ct)
    { var result = await _operatingLoop.CancelActionAsync(CompanyId(), runId, actionId, request, ct); return result is null ? NotFound() : Ok(result); }
    [HttpPost("agents/{agentId:guid}/assignment-context")]
    public async Task<ActionResult<MarketingAssignmentContextDto>> ResolveAssignmentAsync(Guid agentId, RequestMarketingOperatingRun request, CancellationToken ct)
    { try { return Ok(await _companyOrchestration.ResolveAssignmentAsync(CompanyId(), agentId, request, ct)); } catch (MarketingAssignmentException ex) { return Conflict(new ProblemDetails { Status = 409, Title = "Marketing assignment is unavailable", Detail = ex.Message, Extensions = { ["reasonCode"] = ex.ReasonCode } }); } }
    [HttpGet("work-evidence")]
    public Task<IReadOnlyList<MarketingWorkEvidenceDto>> WorkEvidenceAsync([FromQuery] Guid? runId, CancellationToken ct) =>
        _companyOrchestration.ListWorkEvidenceAsync(CompanyId(), runId, ct);
    [HttpPost("work-evidence/progress")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingWorkEvidenceDto>> ReportProgressAsync(ReportMarketingWorkCommand request, CancellationToken ct) =>
        Ok(await _companyOrchestration.ReportProgressAsync(CompanyId(), request, ct));
    [HttpPost("work-evidence/outcomes")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingWorkEvidenceDto>> ReportOutcomeAsync(ReportMarketingWorkCommand request, CancellationToken ct) =>
        Ok(await _companyOrchestration.ReportOutcomeAsync(CompanyId(), request, ct));
    [HttpGet("company-signals")]
    public Task<IReadOnlyList<MarketingCompanySignalDto>> CompanySignalsAsync(CancellationToken ct) =>
        _companyOrchestration.ListSignalsAsync(CompanyId(), ct);
    [HttpPost("company-signals")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingCompanySignalDto>> RaiseCompanySignalAsync(RaiseMarketingCompanySignalCommand request, CancellationToken ct) =>
        Ok(await _companyOrchestration.RaiseSignalAsync(CompanyId(), request, ct));

    [HttpGet("channel-connections")]
    public Task<IReadOnlyList<MarketingChannelConnectionDto>> ChannelConnectionsAsync(CancellationToken ct) => _delivery.ListConnectionsAsync(CompanyId(), ct);
    [HttpPost("channel-connections")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingChannelConnectionDto>> ConnectChannelAsync(ConnectMarketingChannelRequest request, CancellationToken ct) => Ok(await _delivery.ConnectAsync(CompanyId(), UserId(), request, ct));
    [HttpPost("channel-connections/oauth/start")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingChannelOAuthStartDto>> StartChannelOAuthAsync(
        StartMarketingChannelOAuthRequest request,CancellationToken ct) =>
        Ok(await _channelConnections.StartOAuthAsync(CompanyId(),UserId(),request,ct));
    [HttpGet("channel-destinations")]
    public Task<IReadOnlyList<MarketingChannelDestinationDto>> ChannelDestinationsAsync([FromQuery] Guid? connectionId,CancellationToken ct) =>
        _channelConnections.ListDestinationsAsync(CompanyId(),connectionId,ct);
    [HttpPost("channel-connections/{connectionId:guid}/refresh-destinations")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<IReadOnlyList<MarketingChannelDestinationDto>>> RefreshChannelDestinationsAsync(Guid connectionId,CancellationToken ct) =>
        Ok(await _channelConnections.RefreshDestinationsAsync(CompanyId(),connectionId,ct));
    [HttpPost("channel-connections/{connectionId:guid}/disconnect")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<IActionResult> DisconnectChannelAsync(Guid connectionId,CancellationToken ct) =>
        await _channelConnections.DisconnectAsync(CompanyId(),connectionId,ct)?NoContent():NotFound();
    [HttpGet("channel-actions")]
    public Task<IReadOnlyList<MarketingChannelActionDto>> ChannelActionsAsync(CancellationToken ct) => _delivery.ListActionsAsync(CompanyId(), ct);
    [HttpPost("channel-actions")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingChannelActionDto>> PrepareChannelActionAsync(PrepareMarketingChannelActionRequest request, CancellationToken ct)
    { try { return Ok(await _delivery.PrepareActionAsync(CompanyId(), request, ct)); } catch (InvalidOperationException ex) { return Conflict(new ProblemDetails { Status=409, Title="Channel action unavailable", Detail=ex.Message }); } }
    [HttpPost("channel-actions/{actionId:guid}/submit")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingChannelActionDto>> SubmitChannelActionAsync(Guid actionId, CancellationToken ct)
    { var result=await _delivery.SubmitActionAsync(CompanyId(),UserId(),actionId,ct);return result is null?NotFound():Ok(result); }
    [HttpPost("channel-actions/{actionId:guid}/synchronize-approval")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingChannelActionDto>> SynchronizeChannelActionAsync(Guid actionId, CancellationToken ct)
    { var result=await _delivery.SynchronizeApprovedActionAsync(CompanyId(),actionId,ct);return result is null?NotFound():Ok(result); }
    [HttpPost("channel-actions/{actionId:guid}/cancel")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingChannelActionDto>> CancelChannelActionAsync(Guid actionId, CancellationToken ct)
    { var result=await _delivery.CancelActionAsync(CompanyId(),actionId,ct);return result is null?NotFound():Ok(result); }
    [HttpPost("channel-actions/{actionId:guid}/reconcile")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingChannelActionDto>> ReconcileChannelActionAsync(Guid actionId, CancellationToken ct)
    { try { var result=await _channelDispatch.ReconcileAsync(CompanyId(),actionId,ct);return result is null?NotFound():Ok(result); } catch(InvalidOperationException ex) { return Conflict(new ProblemDetails{Status=409,Title="Channel action cannot be reconciled",Detail=ex.Message}); } }
    [HttpGet("journeys")]
    public Task<IReadOnlyList<MarketingJourneyDto>> JourneysAsync(CancellationToken ct)=>_delivery.ListJourneysAsync(CompanyId(),ct);
    [HttpPost("journeys")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingJourneyDto>> CreateJourneyAsync(CreateMarketingJourneyRequest request,CancellationToken ct)=>Ok(await _delivery.CreateJourneyAsync(CompanyId(),UserId(),request,ct));
    [HttpPost("journeys/{journeyId:guid}/versions")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingJourneyDto>> CreateJourneyVersionAsync(Guid journeyId, CreateMarketingJourneyVersionRequest request, CancellationToken ct)
    { var result = await _delivery.CreateJourneyVersionAsync(CompanyId(), UserId(), journeyId, request, ct); return result is null ? NotFound() : Ok(result); }
    [HttpPost("journeys/{journeyId:guid}/validate")]
    public async Task<ActionResult<MarketingJourneyValidationDto>> ValidateJourneyAsync(Guid journeyId, CancellationToken ct)
    { var result = await _delivery.ValidateJourneyAsync(CompanyId(), journeyId, ct); return result is null ? NotFound() : Ok(result); }
    [HttpGet("journeys/{journeyId:guid}/audience-preview")]
    public async Task<ActionResult<MarketingJourneyAudiencePreviewDto>> PreviewJourneyAudienceAsync(Guid journeyId, [FromQuery] int sampleSize, CancellationToken ct)
    { var result = await _delivery.PreviewJourneyAudienceAsync(CompanyId(), journeyId, sampleSize <= 0 ? 20 : sampleSize, ct); return result is null ? NotFound() : Ok(result); }
    [HttpPost("journeys/{journeyId:guid}/submit")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingJourneyDto>> SubmitJourneyAsync(Guid journeyId,CancellationToken ct)
    { var result=await _delivery.SubmitJourneyAsync(CompanyId(),UserId(),journeyId,ct);return result is null?NotFound():Ok(result); }
    [HttpPost("journeys/{journeyId:guid}/synchronize-approval")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingJourneyDto>> SynchronizeJourneyAsync(Guid journeyId,CancellationToken ct)
    { try { var result=await _delivery.SynchronizeApprovedJourneyAsync(CompanyId(),journeyId,ct);return result is null?NotFound():Ok(result); }
      catch(InvalidOperationException ex){return Conflict(new ProblemDetails{Status=409,Title="Journey is not approved",Detail=ex.Message});} }
    [HttpPost("journeys/{journeyId:guid}/pause")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingJourneyDto>> PauseJourneyAsync(Guid journeyId, CancellationToken ct)
    { try { var result = await _delivery.PauseJourneyAsync(CompanyId(), journeyId, ct); return result is null ? NotFound() : Ok(result); } catch (InvalidOperationException ex) { return Conflict(new ProblemDetails { Status = 409, Title = "Journey cannot pause", Detail = ex.Message }); } }
    [HttpPost("journeys/{journeyId:guid}/resume")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingJourneyDto>> ResumeJourneyAsync(Guid journeyId, CancellationToken ct)
    { try { var result = await _delivery.ResumeJourneyAsync(CompanyId(), journeyId, ct); return result is null ? NotFound() : Ok(result); } catch (InvalidOperationException ex) { return Conflict(new ProblemDetails { Status = 409, Title = "Journey cannot resume", Detail = ex.Message }); } }
    [HttpPost("journeys/{journeyId:guid}/complete")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingJourneyDto>> CompleteJourneyAsync(Guid journeyId, CancellationToken ct)
    { try { var result = await _delivery.CompleteJourneyAsync(CompanyId(), journeyId, ct); return result is null ? NotFound() : Ok(result); } catch (InvalidOperationException ex) { return Conflict(new ProblemDetails { Status = 409, Title = "Journey cannot complete", Detail = ex.Message }); } }
    [HttpPost("journeys/{journeyId:guid}/cancel")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingJourneyDto>> CancelJourneyAsync(Guid journeyId, CancellationToken ct)
    { try { var result = await _delivery.CancelJourneyAsync(CompanyId(), journeyId, ct); return result is null ? NotFound() : Ok(result); } catch (InvalidOperationException ex) { return Conflict(new ProblemDetails { Status = 409, Title = "Journey cannot be cancelled", Detail = ex.Message }); } }
    [HttpGet("journey-enrollments")]
    public Task<IReadOnlyList<MarketingJourneyEnrollmentDto>> JourneyEnrollmentsAsync(CancellationToken ct)=>_delivery.ListJourneyEnrollmentsAsync(CompanyId(),ct);
    [HttpPost("journeys/{journeyId:guid}/enrollments")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingJourneyEnrollmentDto>> EnrollJourneyAsync(Guid journeyId,EnrollMarketingJourneyRequest request,CancellationToken ct)
    { try{return Ok(await _delivery.EnrollJourneyAsync(CompanyId(),journeyId,request,ct));}
      catch(InvalidOperationException ex){return Conflict(new ProblemDetails{Status=409,Title="Contact cannot enter this journey",Detail=ex.Message});} }
    [HttpPost("journey-inbound-events")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingJourneyInboundEventDto>> ProcessJourneyInboundEventAsync(
        ProcessMarketingJourneyInboundEventRequest request, CancellationToken ct)
    { try { return Ok(await _journeyInboundEvents.ProcessAsync(CompanyId(), request, ct)); } catch (InvalidOperationException ex) { return Conflict(new ProblemDetails { Status = 409, Title = "Journey event cannot be processed", Detail = ex.Message }); } }

    [HttpGet("creative-assets")]
    public Task<IReadOnlyList<MarketingCreativeAssetDto>> CreativeAssetsAsync(CancellationToken ct)=>_delivery.ListCreativeAssetsAsync(CompanyId(),ct);
    [HttpPost("creative-assets")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingCreativeAssetDto>> RegisterCreativeAssetAsync(RegisterMarketingCreativeAssetRequest request,CancellationToken ct)=>Ok(await _delivery.RegisterCreativeAssetAsync(CompanyId(),UserId(),request,ct));
    [HttpPost("creative-assets/generate")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingCreativeAssetDto>> GenerateCreativeAssetAsync(GenerateMarketingCreativeAssetRequest request,CancellationToken ct)
    { try { return Ok(await _delivery.GenerateCreativeAssetAsync(CompanyId(),UserId(),request,ct)); }
      catch(InvalidOperationException ex){return Conflict(new ProblemDetails{Status=409,Title="Creative image unavailable",Detail=ex.Message});} }
    [HttpPost("creative-assets/upload")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    [RequestSizeLimit(26_214_400)]
    public async Task<ActionResult<MarketingCreativeAssetDto>> UploadCreativeAssetAsync(
        [FromForm] UploadMarketingCreativeAssetForm request, CancellationToken ct)
    {
        await using var content = request.File.OpenReadStream();
        return Ok(await _delivery.UploadCreativeAssetAsync(CompanyId(), UserId(),
            new UploadMarketingCreativeAssetRequest(request.BriefId, request.CampaignId, request.Name,
                request.File.FileName, request.File.ContentType, request.File.Length, content, request.Dimensions,
                request.Language, request.BrandProfileVersion, request.AltText, request.IdempotencyKey), ct));
    }
    [HttpGet("creative-assets/{assetId:guid}/content")]
    public async Task<IActionResult> CreativeAssetContentAsync(Guid assetId,CancellationToken ct)
    { try { var result=await _delivery.GetCreativeAssetContentAsync(CompanyId(),assetId,ct);return result is null?NotFound():File(result.Content,result.ContentType,enableRangeProcessing:true); }
      catch(InvalidOperationException ex){return Conflict(new ProblemDetails{Status=409,Title="Creative asset is quarantined",Detail=ex.Message});} }
    [HttpGet("creative-assets/{assetId:guid}/scans")]
    public Task<IReadOnlyList<MarketingCreativeAssetScanDto>> CreativeAssetScansAsync(Guid assetId,CancellationToken ct) =>
        _delivery.ListCreativeAssetScansAsync(CompanyId(),assetId,ct);
    [HttpPost("creative-assets/{assetId:guid}/rescan")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingCreativeAssetScanDto>> RescanCreativeAssetAsync(Guid assetId,CancellationToken ct)
    { var result=await _delivery.RescanCreativeAssetAsync(CompanyId(),UserId(),assetId,ct);return result is null?NotFound():Ok(result); }
    [HttpPost("creative-assets/{assetId:guid}/submit")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingCreativeAssetDto>> SubmitCreativeAssetAsync(Guid assetId,CancellationToken ct)
    { var result=await _delivery.SubmitCreativeAssetAsync(CompanyId(),assetId,ct);return result is null?NotFound():Ok(result); }
    [HttpPost("creative-assets/{assetId:guid}/review")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingCreativeAssetDto>> ReviewCreativeAssetAsync(Guid assetId,[FromQuery] bool approved,CancellationToken ct)
    { var result=await _delivery.ReviewCreativeAssetAsync(CompanyId(),assetId,approved,ct);return result is null?NotFound():Ok(result); }
    [HttpPost("creative-assets/{assetId:guid}/request-changes")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingCreativeAssetDto>> RequestCreativeAssetChangesAsync(Guid assetId,CancellationToken ct)
    { var result=await _delivery.RequestCreativeAssetChangesAsync(CompanyId(),assetId,ct);return result is null?NotFound():Ok(result); }
    [HttpPut("creative-assets/{assetId:guid}/metadata")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingCreativeAssetDto>> UpdateCreativeAssetMetadataAsync(Guid assetId,
        UpdateMarketingCreativeAssetMetadataRequest request,CancellationToken ct)
    { var result=await _delivery.UpdateCreativeAssetMetadataAsync(CompanyId(),assetId,request,ct);return result is null?NotFound():Ok(result); }
    [HttpPost("creative-assets/{assetId:guid}/retire")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingCreativeAssetDto>> RetireCreativeAssetAsync(Guid assetId,CancellationToken ct)
    { var result=await _delivery.RetireCreativeAssetAsync(CompanyId(),assetId,ct);return result is null?NotFound():Ok(result); }

    [HttpGet("attribution")]
    public Task<IReadOnlyList<MarketingAttributionDto>> AttributionAsync(CancellationToken ct)=>_delivery.ListAttributionAsync(CompanyId(),ct);
    [HttpGet("metric-catalog")]
    public ActionResult<IReadOnlyList<MarketingMetricDefinitionDto>> MetricCatalog()=>Ok(_delivery.ListMetricCatalog());
    [HttpPost("attribution")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingAttributionDto>> RecordAttributionAsync(RecordMarketingAttributionRequest request,CancellationToken ct)=>Ok(await _delivery.RecordAttributionAsync(CompanyId(),request,ct));
    [HttpPost("measurement/touches")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingAttributionTouchDto>> RecordAttributionTouchAsync(RecordMarketingAttributionTouchRequest request,CancellationToken ct)=>Ok(await _measurement.RecordTouchAsync(CompanyId(),request,ct));
    [HttpPost("measurement/attribution-models")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingAttributionModelDto>> CreateAttributionModelAsync(CreateMarketingAttributionModelRequest request,CancellationToken ct)=>Ok(await _measurement.CreateModelAsync(CompanyId(),request,ct));
    [HttpPost("measurement/attribution-runs")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingAttributionRunDto>> RunAttributionAsync(RunMarketingAttributionRequest request,CancellationToken ct)=>Ok(await _measurement.RunAttributionAsync(CompanyId(),request,ct));
    [HttpPost("measurement/experiment-exposures")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<IActionResult> RecordExperimentExposureAsync(RecordMarketingExperimentExposureRequest request,CancellationToken ct){await _measurement.RecordExposureAsync(CompanyId(),request,ct);return NoContent();}
    [HttpPost("measurement/experiment-decisions")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingExperimentDecisionDto>> EvaluateExperimentAsync(EvaluateMarketingExperimentRequest request,CancellationToken ct)=>Ok(await _measurement.EvaluateExperimentAsync(CompanyId(),request,ct));
    [HttpPost("measurement/segment-learning")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingSegmentLearningProposalDto>> ProposeSegmentLearningAsync(CreateMarketingSegmentLearningProposalRequest request,CancellationToken ct)=>Ok(await _measurement.ProposeSegmentLearningAsync(CompanyId(),request,ct));

    [HttpGet("events")]
    public Task<IReadOnlyList<MarketingEventTriggerDto>> EventsAsync(CancellationToken ct)=>_delivery.ListEventsAsync(CompanyId(),ct);
    [HttpGet("briefings/{cadence}")]
    public Task<MarketingBriefingDto> BriefingAsync(string cadence,CancellationToken ct)=>_briefings.BuildAsync(CompanyId(),cadence,DateTime.UtcNow,ct);
    [HttpPost("events")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingEventTriggerDto>> CreateEventAsync(CreateMarketingEventTriggerRequest request,CancellationToken ct)=>Ok(await _delivery.CreateEventAsync(CompanyId(),request,ct));
    [HttpPost("events/{eventId:guid}/process/{agentId:guid}")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingEventTriggerDto>> ProcessEventAsync(Guid eventId,Guid agentId,CancellationToken ct)
    { var result=await _delivery.ProcessEventAsync(CompanyId(),eventId,agentId,ct);return result is null?NotFound():Ok(result); }
    [HttpPost("events/{eventId:guid}/resolve")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<MarketingEventTriggerDto>> ResolveEventAsync(Guid eventId,CancellationToken ct)
    { var result=await _delivery.ResolveEventAsync(CompanyId(),eventId,ct);return result is null?NotFound():Ok(result); }

    [HttpPost("governance/preview")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public ActionResult<MarketingPolicyDecision> PreviewPolicy(MarketingPolicyRequest request) => Ok(_policies.Evaluate(request));

    private Guid CompanyId() => _context.CompanyId is { } id && id != Guid.Empty
        ? id : throw new UnauthorizedAccessException("A resolved company is required.");
    private Guid UserId() => _context.UserId is { } id && id != Guid.Empty
        ? id : throw new UnauthorizedAccessException("A resolved user is required.");
}

public sealed class UploadMarketingCreativeAssetForm
{
    public Guid BriefId { get; set; }
    public Guid? CampaignId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Dimensions { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string BrandProfileVersion { get; set; } = string.Empty;
    public string AltText { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public IFormFile File { get; set; } = null!;
}
