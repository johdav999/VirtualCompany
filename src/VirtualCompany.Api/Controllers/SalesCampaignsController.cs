using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Api.ProblemHandling;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/sales/campaigns")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class SalesCampaignsController : ControllerBase
{
    private readonly ICompanyContextAccessor _companyContextAccessor;
    private readonly IOutboundCampaignService _campaigns;
    private readonly ISequenceExecutionService _sequenceExecution;
    private readonly ICampaignPlanningService _planning;

    public SalesCampaignsController(
        ICompanyContextAccessor companyContextAccessor,
        IOutboundCampaignService campaigns,
        ISequenceExecutionService sequenceExecution,
        ICampaignPlanningService planning)
    {
        _companyContextAccessor = companyContextAccessor;
        _campaigns = campaigns;
        _sequenceExecution = sequenceExecution;
        _planning = planning;
    }

    [HttpGet]
    public Task<IReadOnlyList<OutboundCampaignSummaryResponse>> ListAsync(CancellationToken cancellationToken) =>
        _campaigns.ListCampaignsAsync(CompanyId(), cancellationToken);

    [HttpGet("audience-options")]
    public Task<OutboundAudienceOptionsResponse> AudienceOptionsAsync(CancellationToken cancellationToken) =>
        _campaigns.GetAudienceOptionsAsync(CompanyId(), cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OutboundCampaignDetailResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var campaign = await _campaigns.GetCampaignAsync(CompanyId(), id, cancellationToken);
        return campaign is null ? NotFound() : Ok(campaign);
    }

    [HttpPost]
    public async Task<ActionResult<OutboundCampaignDetailResponse>> CreateAsync([FromBody] CreateOutboundCampaignRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var campaign = await _campaigns.CreateCampaignAsync(CompanyId(), UserId(), request, cancellationToken);
            return CreatedAtAction(nameof(GetAsync), new { id = campaign.Id }, campaign);
        }
        catch (SalesValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
    }

    [HttpPost("{id:guid}/launch")]
    public async Task<ActionResult<OutboundCampaignDetailResponse>> LaunchAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var campaign = await _campaigns.LaunchCampaignAsync(CompanyId(), UserId(), id, cancellationToken);
            return campaign is null ? NotFound() : Ok(campaign);
        }
        catch (SalesValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(title: "Campaign could not be launched.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpPost("{id:guid}/pause")]
    public async Task<ActionResult<OutboundCampaignDetailResponse>> PauseAsync(Guid id, CancellationToken cancellationToken)
    {
        var campaign = await _campaigns.PauseCampaignAsync(CompanyId(), UserId(), id, cancellationToken);
        return campaign is null ? NotFound() : Ok(campaign);
    }

    [HttpPost("{id:guid}/stop")]
    public async Task<ActionResult<OutboundCampaignDetailResponse>> StopAsync(Guid id, [FromBody] StopCampaignRequest request, CancellationToken cancellationToken)
    {
        var campaign = await _campaigns.StopCampaignAsync(CompanyId(), UserId(), id, request.Reason, cancellationToken);
        return campaign is null ? NotFound() : Ok(campaign);
    }

    [HttpGet("{id:guid}/initiative")]
    public async Task<ActionResult<CampaignInitiativeResponse>> InitiativeAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var response = await _planning.GetInitiativeAsync(CompanyId(), id, cancellationToken);
        return response is null ? NotFound() : Ok(response);
    }

    [HttpPut("{id:guid}/initiative")]
    public async Task<ActionResult<CampaignInitiativeResponse>> ConfigureInitiativeAsync(
        Guid id, [FromBody] ConfigureCampaignInitiativeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _planning.ConfigureInitiativeAsync(CompanyId(), UserId(), id, request, cancellationToken);
            return response is null ? NotFound() : Ok(response);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            return Problem(title: "Campaign changed.", detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(title: "Campaign could not be updated.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpGet("{id:guid}/readiness")]
    public async Task<ActionResult<CampaignReadinessResponse>> ReadinessAsync(Guid id, CancellationToken cancellationToken)
    {
        var response = await _planning.GetReadinessAsync(CompanyId(), id, cancellationToken);
        return response is null ? NotFound() : Ok(response);
    }

    [HttpPost("{id:guid}/readiness")]
    public async Task<ActionResult<CampaignInitiativeResponse>> RequestReadinessAsync(
        Guid id, [FromBody] CampaignVersionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _planning.RequestReadinessAsync(CompanyId(), UserId(), id, request.ExpectedVersion, cancellationToken);
            return response is null ? NotFound() : Ok(response);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            return Problem(title: "Campaign changed.", detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(title: "Campaign is not ready.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpGet("segments")]
    public Task<IReadOnlyList<CampaignSegmentResponse>> ListSegmentsAsync(CancellationToken cancellationToken) =>
        _planning.ListSegmentsAsync(CompanyId(), cancellationToken);

    [HttpPost("segments")]
    public Task<CampaignSegmentResponse> CreateSegmentAsync(
        [FromBody] CreateCampaignSegmentRequest request, CancellationToken cancellationToken) =>
        _planning.CreateSegmentAsync(CompanyId(), UserId(), request, cancellationToken);

    [HttpGet("segments/{segmentId:guid}/preview")]
    public Task<CampaignAudiencePreviewResponse> PreviewSegmentAsync(Guid segmentId, CancellationToken cancellationToken) =>
        _planning.PreviewSegmentAsync(CompanyId(), segmentId, cancellationToken);

    [HttpPost("{id:guid}/audience-snapshots")]
    public async Task<ActionResult<CampaignAudienceSnapshotResponse>> CaptureAudienceAsync(
        Guid id, [FromBody] CaptureCampaignAudienceRequest request, CancellationToken cancellationToken)
    {
        var response = await _planning.CaptureAudienceAsync(CompanyId(), UserId(), id, request.SegmentId, cancellationToken);
        return response is null ? NotFound() : Ok(response);
    }

    [HttpGet("{id:guid}/activities")]
    public Task<IReadOnlyList<CampaignActivityResponse>> ActivitiesAsync(Guid id, CancellationToken cancellationToken) =>
        _planning.ListActivitiesAsync(CompanyId(), id, cancellationToken);

    [HttpPost("{id:guid}/activities")]
    public async Task<ActionResult<CampaignActivityResponse>> AddActivityAsync(
        Guid id, [FromBody] CreateCampaignActivityRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _planning.AddActivityAsync(CompanyId(), UserId(), id, request, cancellationToken);
            return response is null ? NotFound() : Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(title: "Campaign activity could not be added.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpGet("{id:guid}/performance")]
    public async Task<ActionResult<CampaignPerformanceResponse>> PerformanceAsync(Guid id, CancellationToken cancellationToken)
    {
        var response = await _planning.GetPerformanceAsync(CompanyId(), id, cancellationToken);
        return response is null ? NotFound() : Ok(response);
    }

    [HttpPost("{id:guid}/performance-snapshots")]
    public async Task<ActionResult<CampaignPerformanceResponse>> CapturePerformanceSnapshotAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var response = await _planning.CapturePerformanceSnapshotAsync(CompanyId(), UserId(), id, cancellationToken);
        return response is null ? NotFound() : Ok(response);
    }

    [HttpPut("{id:guid}/steps/{stepId:guid}/draft")]
    public async Task<ActionResult<SequenceExecutionStepResponse>> SaveDraftAsync(Guid id, Guid stepId, [FromBody] SaveSequenceDraftRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var draft = await _sequenceExecution.SaveDraftAsync(CompanyId(), UserId(), id, stepId, request, cancellationToken);
            return draft is null ? NotFound() : Ok(draft);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(title: "Draft could not be saved.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpPost("provider/delivery")]
    public async Task<IActionResult> DeliveryAsync([FromBody] OutboundDeliveryStatusRequest request, CancellationToken cancellationToken)
    {
        await _sequenceExecution.HandleDeliveryStatusAsync(CompanyId(), request, cancellationToken);
        return Accepted();
    }

    [HttpPost("provider/bounce")]
    public async Task<IActionResult> BounceAsync([FromBody] OutboundBounceRequest request, CancellationToken cancellationToken)
    {
        await _sequenceExecution.HandleBounceAsync(CompanyId(), request, cancellationToken);
        return Accepted();
    }

    [HttpPost("provider/reply")]
    public async Task<ActionResult<StopConditionResponse>> ReplyAsync([FromBody] OutboundReplyReceived request, CancellationToken cancellationToken)
    {
        await _sequenceExecution.QueueReplyReceivedAsync(CompanyId(), request, cancellationToken);
        return Accepted(new StopConditionResponse(0));
    }

    [HttpPost("contacts/{contactId:guid}/deal-created")]
    public async Task<ActionResult<StopConditionResponse>> DealCreatedAsync(Guid contactId, [FromBody] DealCreatedStopRequest request, CancellationToken cancellationToken)
    {
        await _sequenceExecution.QueueDealCreatedAsync(CompanyId(), contactId, request.DealId, cancellationToken);
        return Accepted(new StopConditionResponse(0));
    }

    private Guid CompanyId() =>
        _companyContextAccessor.CompanyId is { } companyId && companyId != Guid.Empty ? companyId : throw new UnauthorizedAccessException("A resolved company is required.");

    private Guid UserId() =>
        _companyContextAccessor.UserId is { } userId && userId != Guid.Empty ? userId : throw new UnauthorizedAccessException("A resolved user is required.");

    private ActionResult ValidationProblem(IReadOnlyDictionary<string, string[]> errors) =>
        base.ValidationProblem(StableProblemDetails.CreateValidation(HttpContext, errors, ApiProblemCodes.SalesRequestInvalid));

    public sealed record StopCampaignRequest(string? Reason);
    public sealed record DealCreatedStopRequest(Guid DealId);
    public sealed record StopConditionResponse(int CancelledPendingSteps);
    public sealed record CampaignVersionRequest(long ExpectedVersion);
    public sealed record CaptureCampaignAudienceRequest(Guid SegmentId);
}
