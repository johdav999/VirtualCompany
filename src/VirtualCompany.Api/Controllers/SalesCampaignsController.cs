using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    public SalesCampaignsController(ICompanyContextAccessor companyContextAccessor, IOutboundCampaignService campaigns, ISequenceExecutionService sequenceExecution)
    {
        _companyContextAccessor = companyContextAccessor;
        _campaigns = campaigns;
        _sequenceExecution = sequenceExecution;
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
}
