using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Api.ProblemHandling;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/sales/meeting-sessions/{sessionId:guid}/presentation")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class SalesPresentationRuntimeController(
    ISalesPresentationRuntimeService runtime,
    ICompanyContextAccessor companyContext) : ControllerBase
{
    [HttpGet("current-slide")]
    public async Task<ActionResult<SalesPresentationAuthoritativeSnapshotDto>> GetCurrentSlideAsync(
        Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await runtime.GetCurrentAsync(CompanyId(), UserId(), sessionId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("slides/search")]
    public async Task<ActionResult<IReadOnlyList<SalesPresentationSearchResultDto>>> SearchSlidesAsync(
        Guid sessionId, [FromQuery] string query, CancellationToken cancellationToken) =>
        Ok(await runtime.SearchAsync(CompanyId(), UserId(), sessionId, query, cancellationToken));

    [HttpPost("commands/{toolName}")]
    public async Task<ActionResult<SalesPresentationCommandResultDto>> ExecuteAsync(
        Guid sessionId, string toolName, [FromBody] SalesPresentationCommandRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await runtime.ExecuteAsync(
                CompanyId(), UserId(), sessionId, toolName, request,
                HttpContext.TraceIdentifier, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(StableProblemDetails.Create(
                HttpContext, StatusCodes.Status400BadRequest,
                SalesPresentationRuntimeProblemCodes.InvalidCommand,
                "Invalid presentation command", exception.Message));
        }
        catch (SalesPresentationRuntimeConflictException exception)
        {
            var problem = StableProblemDetails.Create(
                HttpContext, StatusCodes.Status409Conflict, exception.Code,
                "Presentation synchronization conflict", exception.Message);
            problem.Extensions["authoritativeSnapshot"] = exception.Snapshot;
            return Conflict(problem);
        }
    }

    [HttpPut("control-mode")]
    public async Task<ActionResult<SalesPresentationControlModeDto>> SetControlModeAsync(
        Guid sessionId, [FromBody] SetSalesPresentationControlModeRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await runtime.SetControlModeAsync(
                CompanyId(), UserId(), sessionId, request, HttpContext.TraceIdentifier, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(StableProblemDetails.Create(
                HttpContext, StatusCodes.Status400BadRequest,
                SalesPresentationRuntimeProblemCodes.InvalidCommand,
                "Invalid presentation control mode", exception.Message));
        }
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden, StableProblemDetails.Create(
                HttpContext, StatusCodes.Status403Forbidden,
                SalesPresentationRuntimeProblemCodes.InvalidCommand,
                "Organizer permission required", exception.Message));
        }
        catch (SalesPresentationRuntimeConflictException exception)
        {
            var problem = StableProblemDetails.Create(
                HttpContext, StatusCodes.Status409Conflict, exception.Code,
                "Presentation synchronization conflict", exception.Message);
            problem.Extensions["authoritativeSnapshot"] = exception.Snapshot;
            return Conflict(problem);
        }
    }

    private Guid CompanyId() => companyContext.CompanyId is { } id && id != Guid.Empty
        ? id : throw new UnauthorizedAccessException("A resolved company is required.");

    private Guid UserId() => companyContext.UserId is { } id && id != Guid.Empty
        ? id : throw new UnauthorizedAccessException("A resolved user is required.");
}
