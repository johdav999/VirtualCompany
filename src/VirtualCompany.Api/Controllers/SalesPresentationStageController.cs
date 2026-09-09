using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Api.ProblemHandling;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
public sealed class SalesPresentationStageController(
    ISalesPresentationStageAccessService stage,
    ICompanyContextAccessor companyContext) : ControllerBase
{
    [HttpPost("api/sales/meeting-sessions/{sessionId:guid}/stage-access")]
    [Authorize(Policy = CompanyPolicies.CompanyMember)]
    [RequireCompanyContext]
    public async Task<ActionResult<SalesPresentationStageAccessGrantDto>> IssueAsync(
        Guid sessionId, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await stage.IssueAsync(CompanyId(), UserId(), sessionId, cancellationToken));
        }
        catch (SalesPresentationStageAccessException exception)
        {
            return StageProblem(exception);
        }
    }

    [HttpGet("api/sales/meeting-stage/{sessionId:guid}/snapshot")]
    [AllowAnonymous]
    public async Task<ActionResult<SalesPresentationStageSnapshotDto>> SnapshotAsync(
        Guid sessionId, [FromQuery] string accessToken, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        try
        {
            return Ok(await stage.GetSnapshotAsync(sessionId, accessToken, cancellationToken));
        }
        catch (SalesPresentationStageAccessException exception)
        {
            return StageProblem(exception);
        }
    }

    [HttpGet("api/sales/meeting-stage/{sessionId:guid}/decks/{deckId:guid}/versions/{deckVersion:int:min(1)}/slides/{slideNumber:int:min(1)}/image")]
    [AllowAnonymous]
    public async Task<IActionResult> SlideImageAsync(Guid sessionId, Guid deckId, int deckVersion,
        int slideNumber, [FromQuery] string accessToken, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        try
        {
            var asset = await stage.OpenSlideAsync(sessionId, accessToken, deckId, deckVersion,
                slideNumber, cancellationToken);
            return File(asset.Content, asset.ContentType, enableRangeProcessing: false);
        }
        catch (SalesPresentationStageAccessException exception)
        {
            return StageProblem(exception);
        }
    }

    private ObjectResult StageProblem(SalesPresentationStageAccessException exception)
    {
        var status = exception.Code switch
        {
            SalesPresentationStageAccessProblemCodes.InvalidGrant => StatusCodes.Status401Unauthorized,
            SalesPresentationStageAccessProblemCodes.ExpiredGrant => StatusCodes.Status410Gone,
            SalesPresentationStageAccessProblemCodes.OrganizerRequired => StatusCodes.Status403Forbidden,
            SalesPresentationStageAccessProblemCodes.Disabled => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status409Conflict
        };
        return StatusCode(status, StableProblemDetails.Create(HttpContext, status, exception.Code,
            "Meeting stage unavailable", exception.Message));
    }

    private Guid CompanyId() => companyContext.CompanyId is { } id && id != Guid.Empty
        ? id : throw new UnauthorizedAccessException("A resolved company is required.");
    private Guid UserId() => companyContext.UserId is { } id && id != Guid.Empty
        ? id : throw new UnauthorizedAccessException("A resolved user is required.");
}
