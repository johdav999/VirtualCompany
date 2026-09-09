using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Api.Controllers;

[ApiController]
public sealed class TeamsMediaHostController(ITeamsMediaHostRuntime runtime) : ControllerBase
{
    [HttpGet("api/platform/teams-media-host")]
    [Authorize(Policy = CompanyPolicies.PlatformAdministration)]
    public ActionResult<TeamsMediaHostRuntimeStatus> GetStatus() => Ok(runtime.GetStatus());

    [HttpPost("api/platform/teams-media-host/drain")]
    [Authorize(Policy = CompanyPolicies.PlatformAdministration)]
    public async Task<ActionResult<TeamsMediaHostDrainResult>> DrainAsync(
        [FromBody] TeamsMediaHostDrainRequest request,
        CancellationToken cancellationToken)
    {
        var deadline = request.DeadlineMinutes.HasValue
            ? TimeSpan.FromMinutes(Math.Clamp(request.DeadlineMinutes.Value, 1, 120))
            : (TimeSpan?)null;
        return Ok(await runtime.BeginDrainAsync(deadline, request.Reason ?? "operator_requested", cancellationToken));
    }

    [HttpGet("health/media-host/upgrade")]
    [AllowAnonymous]
    public async Task<IActionResult> UpgradeReadiness([FromServices] ITeamsMediaHostUpgradeReadiness readiness, CancellationToken ct)
    {
        if (HttpContext.Connection.LocalPort != 8080 || HttpContext.Connection.RemoteIpAddress is not { } address ||
            !System.Net.IPAddress.IsLoopback(address)) return NotFound();
        return await readiness.IsDrainedAsync(ct)
            ? Ok(new { status = "drained" }) : StatusCode(503, new { status = "drain_required" });
    }

    [HttpGet("health/media-host/live")]
    [AllowAnonymous]
    public IActionResult Liveness()
    {
        var status = runtime.GetStatus();
        return status.State == TeamsMediaHostStates.Stopped
            ? StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "stopped" })
            : Ok(new { status = "live" });
    }

    [HttpGet("health/media-host/ready")]
    [AllowAnonymous]
    public IActionResult Readiness()
    {
        var status = runtime.GetStatus();
        if (status.AcceptingNewCalls) return Ok(new { status = "ready" });
        var reason = status.State == TeamsMediaHostStates.Draining
            ? TeamsMediaHostProblemCodes.Draining
            : status.Checks.FirstOrDefault(check => !check.Ready)?.ReasonCode ?? TeamsMediaHostProblemCodes.CapacityReached;
        return StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "not_ready", reasonCode = reason });
    }
}
