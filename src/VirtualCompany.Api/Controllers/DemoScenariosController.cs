using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Api.ProblemHandling;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/demo-scenarios")]
[Authorize(Policy = CompanyPolicies.AuthenticatedUser)]
public sealed class DemoScenariosController(
    IDemoScenarioCatalog catalog,
    IDemoScenarioService scenarios,
    ICurrentUserCompanyService currentUsers,
    ICompanyContextAccessor companyContext) : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<DemoScenarioDefinitionDto>> List() => Ok(catalog.List());

    [HttpPost("provision")]
    public async Task<ActionResult<ProvisionDemoScenarioResult>> ProvisionAsync(
        [FromBody] ProvisionDemoScenarioRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await currentUsers.GetCurrentUserAsync(cancellationToken)
                ?? throw new UnauthorizedAccessException("A resolved user is required.");
            return Ok(await scenarios.ProvisionAsync(user.Id, request, cancellationToken));
        }
        catch (DemoScenarioException exception) { return Problem(exception); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpGet("current")]
    [Authorize(Policy = CompanyPolicies.DemoScenarioControl)]
    [RequireCompanyContext]
    public async Task<ActionResult<DemoScenarioStatusDto>> GetCurrentAsync(CancellationToken cancellationToken)
    {
        try
        {
            var status = await scenarios.GetStatusAsync(CompanyId(), UserId(), cancellationToken);
            return status is null ? NotFound() : Ok(status);
        }
        catch (DemoScenarioException exception) { return Problem(exception); }
    }

    [HttpPost("current/link-meeting")]
    [Authorize(Policy = CompanyPolicies.DemoScenarioControl)]
    [RequireCompanyContext]
    public async Task<ActionResult<DemoScenarioStatusDto>> LinkMeetingAsync(
        [FromBody] LinkDemoScenarioMeetingRequest request,
        CancellationToken cancellationToken)
    {
        try { return Ok(await scenarios.LinkMeetingAsync(CompanyId(), UserId(), request, cancellationToken)); }
        catch (DemoScenarioException exception) { return Problem(exception); }
        catch (InvalidOperationException exception) { return ConflictProblem(DemoScenarioProblemCodes.MeetingMismatch, exception.Message); }
    }

    [HttpPost("current/start")]
    [Authorize(Policy = CompanyPolicies.DemoScenarioControl)]
    [RequireCompanyContext]
    public async Task<ActionResult<DemoScenarioStatusDto>> StartAsync(CancellationToken cancellationToken)
    {
        try { return Ok(await scenarios.StartAsync(CompanyId(), UserId(), cancellationToken)); }
        catch (DemoScenarioException exception) { return Problem(exception); }
        catch (InvalidOperationException exception) { return ConflictProblem(DemoScenarioProblemCodes.CommandOutOfOrder, exception.Message); }
    }

    [HttpGet("current/reset-preview")]
    [Authorize(Policy = CompanyPolicies.DemoScenarioControl)]
    [RequireCompanyContext]
    public async Task<ActionResult<DemoScenarioResetPreviewDto>> PreviewResetAsync(
        [FromQuery] string scenarioKey,
        [FromQuery] int scenarioVersion,
        CancellationToken cancellationToken)
    {
        try { return Ok(await scenarios.PreviewResetAsync(CompanyId(), UserId(), scenarioKey, scenarioVersion, cancellationToken)); }
        catch (DemoScenarioException exception) { return Problem(exception); }
    }

    [HttpPost("current/reset")]
    [Authorize(Policy = CompanyPolicies.DemoScenarioControl)]
    [RequireCompanyContext]
    public async Task<ActionResult<DemoScenarioStatusDto>> ResetAsync(
        [FromBody] ResetDemoScenarioRequest request,
        CancellationToken cancellationToken)
    {
        try { return Ok(await scenarios.ResetAsync(CompanyId(), UserId(), request, cancellationToken)); }
        catch (DemoScenarioException exception) { return Problem(exception); }
    }

    [HttpPost("current/commands")]
    [Authorize(Policy = CompanyPolicies.DemoScenarioControl)]
    [RequireCompanyContext]
    public async Task<ActionResult<DemoScenarioCommandResultDto>> ExecuteAsync(
        [FromBody] ExecuteDemoScenarioCommandRequest request,
        CancellationToken cancellationToken)
    {
        try { return Ok(await scenarios.ExecuteAsync(CompanyId(), UserId(), request, cancellationToken)); }
        catch (DemoScenarioException exception) { return Problem(exception); }
        catch (InvalidOperationException exception) { return ConflictProblem(DemoScenarioProblemCodes.CommandOutOfOrder, exception.Message); }
    }

    private ActionResult Problem(DemoScenarioException exception)
    {
        var status = exception.Code switch
        {
            DemoScenarioProblemCodes.Disabled => StatusCodes.Status503ServiceUnavailable,
            DemoScenarioProblemCodes.PermissionDenied => StatusCodes.Status403Forbidden,
            DemoScenarioProblemCodes.NotDemoTenant => StatusCodes.Status409Conflict,
            DemoScenarioProblemCodes.ScenarioMismatch => StatusCodes.Status409Conflict,
            DemoScenarioProblemCodes.ResetPreviewStale => StatusCodes.Status409Conflict,
            DemoScenarioProblemCodes.CommandOutOfOrder => StatusCodes.Status409Conflict,
            DemoScenarioProblemCodes.MeetingMismatch => StatusCodes.Status409Conflict,
            DemoScenarioProblemCodes.CommandNotAllowed => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status400BadRequest
        };
        return StatusCode(status, StableProblemDetails.Create(
            HttpContext, status, exception.Code, "Demo scenario request rejected", exception.Message));
    }

    private ActionResult ConflictProblem(string code, string detail) => StatusCode(
        StatusCodes.Status409Conflict,
        StableProblemDetails.Create(HttpContext, StatusCodes.Status409Conflict, code, "Demo scenario conflict", detail));

    private Guid CompanyId() => companyContext.CompanyId is { } id && id != Guid.Empty
        ? id : throw new UnauthorizedAccessException("A resolved company is required.");
    private Guid UserId() => companyContext.UserId is { } id && id != Guid.Empty
        ? id : throw new UnauthorizedAccessException("A resolved user is required.");
}

