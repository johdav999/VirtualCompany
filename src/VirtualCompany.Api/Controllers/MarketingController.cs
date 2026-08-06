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
    public MarketingController(ICompanyContextAccessor context, IMarketingOperationsService marketing,
        IMarketingAgentAnalysisService analysis)
    {
        _context = context;
        _marketing = marketing;
        _analysis = analysis;
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
    public async Task<ActionResult<MarketingObjectiveDto>> CreateObjectiveAsync(CreateMarketingObjectiveRequest request, CancellationToken ct) =>
        Ok(await _marketing.CreateObjectiveAsync(CompanyId(), UserId(), request, ct));
    [HttpPost("objectives/{objectiveId:guid}/activate")]
    public async Task<ActionResult<MarketingObjectiveDto>> ActivateObjectiveAsync(Guid objectiveId, CancellationToken ct)
    {
        var result = await _marketing.ActivateObjectiveAsync(CompanyId(), objectiveId, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("plans")]
    public Task<IReadOnlyList<MarketingPlanDto>> PlansAsync(CancellationToken ct) =>
        _marketing.ListPlansAsync(CompanyId(), ct);
    [HttpPost("plans")]
    public async Task<ActionResult<MarketingPlanDto>> CreatePlanAsync(CreateMarketingPlanRequest request, CancellationToken ct) =>
        Ok(await _marketing.CreatePlanAsync(CompanyId(), UserId(), request, ct));
    [HttpPost("plans/proposal")]
    public async Task<ActionResult<MarketingPlanProposalDto>> PreparePlanProposalAsync(CreateMarketingPlanRequest request, CancellationToken ct) =>
        Ok(await _marketing.PreparePlanProposalAsync(CompanyId(), request, ct));
    [HttpPost("plans/commit")]
    public async Task<ActionResult<MarketingPlanDto>> CommitPlanAsync(CommitMarketingPlanRequest request, CancellationToken ct) =>
        Ok(await _marketing.CommitPlanAsync(CompanyId(), UserId(), request, ct));
    [HttpPost("plans/{planId:guid}/activate")]
    public async Task<ActionResult<MarketingPlanDto>> ActivatePlanAsync(Guid planId, CancellationToken ct)
    {
        var result = await _marketing.ActivatePlanAsync(CompanyId(), planId, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("content")]
    public Task<IReadOnlyList<MarketingContentBriefDto>> ContentAsync(CancellationToken ct) =>
        _marketing.ListContentAsync(CompanyId(), ct);
    [HttpPost("content")]
    public async Task<ActionResult<MarketingContentBriefDto>> CreateContentAsync(CreateMarketingContentBriefRequest request, CancellationToken ct) =>
        Ok(await _marketing.CreateContentBriefAsync(CompanyId(), UserId(), request, ct));
    [HttpPost("content/{briefId:guid}/variants")]
    public async Task<ActionResult<MarketingContentVariantDto>> AddVariantAsync(Guid briefId, CreateMarketingContentVariantRequest request, CancellationToken ct)
    {
        var result = await _marketing.AddContentVariantAsync(CompanyId(), briefId, request, ct);
        return result is null ? NotFound() : Ok(result);
    }
    [HttpPost("content/{briefId:guid}/review")]
    public async Task<IActionResult> ReviewContentAsync(Guid briefId, ReviewMarketingContentRequest request, CancellationToken ct) =>
        await _marketing.ReviewContentAsync(CompanyId(), briefId, request, ct) ? NoContent() : NotFound();
    [HttpPost("content/{briefId:guid}/submit")]
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
    public async Task<ActionResult<MarketingSalesHandoffDto>> CreateHandoffAsync(CreateMarketingSalesHandoffRequest request, CancellationToken ct) =>
        Ok(await _marketing.CreateHandoffAsync(CompanyId(), request, ct));
    [HttpPost("handoffs/{handoffId:guid}/decision")]
    public async Task<ActionResult<MarketingSalesHandoffDto>> DecideHandoffAsync(Guid handoffId, DecideMarketingSalesHandoffRequest request, CancellationToken ct)
    {
        var result = await _marketing.DecideHandoffAsync(CompanyId(), handoffId, request, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("observations")]
    public Task<IReadOnlyList<MarketingObservationDto>> ObservationsAsync([FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, CancellationToken ct) =>
        _marketing.ListObservationsAsync(CompanyId(), fromUtc ?? DateTime.UtcNow.AddDays(-30), toUtc ?? DateTime.UtcNow.AddDays(1), ct);
    [HttpPost("observations")]
    public async Task<ActionResult<MarketingObservationDto>> RecordObservationAsync(CreateMarketingObservationRequest request, CancellationToken ct) =>
        Ok(await _marketing.RecordObservationAsync(CompanyId(), request, ct));

    [HttpGet("experiments")]
    public Task<IReadOnlyList<MarketingExperimentDto>> ExperimentsAsync(CancellationToken ct) =>
        _marketing.ListExperimentsAsync(CompanyId(), ct);
    [HttpPost("experiments")]
    public async Task<ActionResult<MarketingExperimentDto>> CreateExperimentAsync(CreateMarketingExperimentRequest request, CancellationToken ct) =>
        Ok(await _marketing.CreateExperimentAsync(CompanyId(), request, ct));
    [HttpPost("experiments/{experimentId:guid}/activate")]
    public async Task<ActionResult<MarketingExperimentDto>> ActivateExperimentAsync(Guid experimentId, CancellationToken ct)
    {
        var result = await _marketing.ActivateExperimentAsync(CompanyId(), experimentId, ct);
        return result is null ? NotFound() : Ok(result);
    }
    [HttpPost("experiments/{experimentId:guid}/complete")]
    public async Task<ActionResult<MarketingExperimentDto>> CompleteExperimentAsync(Guid experimentId, CompleteMarketingExperimentRequest request, CancellationToken ct)
    {
        var result = await _marketing.CompleteExperimentAsync(CompanyId(), experimentId, request, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("qualification-definitions")]
    public Task<IReadOnlyList<MarketingQualificationDefinitionDto>> QualificationDefinitionsAsync(CancellationToken ct) =>
        _marketing.ListQualificationDefinitionsAsync(CompanyId(), ct);
    [HttpPost("qualification-definitions")]
    public async Task<ActionResult<MarketingQualificationDefinitionDto>> CreateQualificationDefinitionAsync(
        CreateMarketingQualificationDefinitionRequest request, CancellationToken ct) =>
        Ok(await _marketing.CreateQualificationDefinitionAsync(CompanyId(), UserId(), request, ct));
    [HttpPost("qualification-definitions/{definitionId:guid}/activate")]
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
    public async Task<ActionResult<MarketingQualificationEvaluationDto>> EvaluateContactAsync(
        EvaluateMarketingContactRequest request, CancellationToken ct) =>
        Ok(await _marketing.EvaluateContactAsync(CompanyId(), request, ct));
    [HttpPost("qualification-evaluations/{evaluationId:guid}/feedback")]
    public async Task<ActionResult<MarketingQualificationFeedbackDto>> RecordQualificationFeedbackAsync(
        Guid evaluationId, RecordMarketingQualificationFeedbackRequest request, CancellationToken ct) =>
        Ok(await _marketing.RecordQualificationFeedbackAsync(CompanyId(), UserId(), evaluationId, request, ct));

    [HttpPost("agents/{agentId:guid}/analysis")]
    public Task<RoleAgentAnalysisResult> AnalyzeAsync(Guid agentId, RoleAgentAnalysisRequest request, CancellationToken ct) =>
        _analysis.AnalyzeAsync(CompanyId(), agentId, UserId(), request, ct);

    private Guid CompanyId() => _context.CompanyId is { } id && id != Guid.Empty
        ? id : throw new UnauthorizedAccessException("A resolved company is required.");
    private Guid UserId() => _context.UserId is { } id && id != Guid.Empty
        ? id : throw new UnauthorizedAccessException("A resolved user is required.");
}
