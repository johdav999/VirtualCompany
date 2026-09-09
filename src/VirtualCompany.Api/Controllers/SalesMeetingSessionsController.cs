using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Api.ProblemHandling;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/sales")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class SalesMeetingSessionsController(
    ISalesMeetingSessionService sessions,
    ISalesMeetingPresentationPreparationQuery preparation,
    ICompanyContextAccessor companyContext) : ControllerBase
{
    [HttpGet("meeting-invitations/{invitationId:guid}/presentation-preparation")]
    public async Task<ActionResult<SalesMeetingPresentationPreparationResponse>> GetPresentationPreparationAsync(
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        var result = await preparation.GetAsync(CompanyId(), invitationId, cancellationToken);
        return result is null
            ? NotFound(StableProblemDetails.Create(
                HttpContext,
                StatusCodes.Status404NotFound,
                ApiProblemCodes.ResourceNotFound,
                "Sales meeting invitation not found",
                "The Sales meeting invitation was not found."))
            : Ok(result);
    }

    [HttpGet("meeting-invitations/{invitationId:guid}/session")]
    public async Task<ActionResult<SalesMeetingSessionResponse>> GetByInvitationAsync(
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        var result = await sessions.GetByInvitationAsync(CompanyId(), invitationId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("meeting-sessions/{sessionId:guid}")]
    public async Task<ActionResult<SalesMeetingSessionResponse>> GetAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var result = await sessions.GetAsync(CompanyId(), sessionId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPut("meeting-invitations/{invitationId:guid}/session")]
    public async Task<ActionResult<SalesMeetingSessionResponse>> CreateOrUpdateAsync(
        Guid invitationId,
        [FromBody] CreateOrUpdateSalesMeetingSessionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await sessions.CreateOrUpdateAsync(
                CompanyId(), UserId(), invitationId, request,
                HttpContext.TraceIdentifier, cancellationToken));
        }
        catch (SalesValidationException exception)
        {
            return BadRequest(StableProblemDetails.CreateValidation(
                HttpContext,
                exception.Errors,
                ApiProblemCodes.SalesRequestInvalid));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (SalesMeetingSessionConflictException exception)
        {
            return Conflict(StableProblemDetails.Create(
                HttpContext,
                StatusCodes.Status409Conflict,
                exception.Code,
                "Sales meeting session conflict",
                exception.Message));
        }
    }

    [HttpPost("meeting-sessions/{sessionId:guid}/transitions")]
    public async Task<ActionResult<SalesMeetingSessionResponse>> TransitionAsync(
        Guid sessionId,
        [FromBody] TransitionSalesMeetingSessionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await sessions.TransitionAsync(
                CompanyId(), UserId(), sessionId, request,
                HttpContext.TraceIdentifier, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (SalesValidationException exception)
        {
            return BadRequest(StableProblemDetails.CreateValidation(
                HttpContext,
                exception.Errors,
                ApiProblemCodes.SalesRequestInvalid));
        }
        catch (SalesMeetingSessionConflictException exception)
        {
            return Conflict(StableProblemDetails.Create(
                HttpContext,
                StatusCodes.Status409Conflict,
                exception.Code,
                "Sales meeting session conflict",
                exception.Message));
        }
    }

    private Guid CompanyId() => companyContext.CompanyId is { } id && id != Guid.Empty
        ? id
        : throw new UnauthorizedAccessException("A resolved company is required.");

    private Guid UserId() => companyContext.UserId is { } id && id != Guid.Empty
        ? id
        : throw new UnauthorizedAccessException("A resolved user is required.");
}
