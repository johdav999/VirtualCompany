using VirtualCompany.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/sales/narration")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class SalesNarrationController(ISalesNarrationService service,
    ICompanyContextAccessor context) : ControllerBase
{
    [HttpGet("sessions/{sessionId:guid}")]
    public Task<IActionResult> Get(Guid sessionId, CancellationToken ct) =>
        Run(async () => Ok(await service.GetAsync(Company(), UserId(), sessionId, ct)));

    [HttpPost("sessions/{sessionId:guid}/prepare")]
    public Task<IActionResult> Prepare(Guid sessionId, PrepareSalesNarration command, CancellationToken ct) =>
        Run(async () => Ok(await service.PrepareAsync(Company(), UserId(), sessionId, command, ct)));

    [HttpPost("revisions/{revisionId:guid}/{action}")]
    public Task<IActionResult> Decide(Guid revisionId, string action, SalesNarrationDecision command, CancellationToken ct) =>
        Run(async () => { await service.DecideAsync(Company(), UserId(), revisionId, action, command, ct); return NoContent(); });

    [HttpGet("sessions/{sessionId:guid}/revisions/{revisionId:guid}/segments/{segmentId:guid}/preview")]
    public Task<IActionResult> Preview(Guid sessionId, Guid revisionId, Guid segmentId, [FromQuery] Guid audienceId, CancellationToken ct) =>
        Run(async () => {
            var preview = await service.PreviewAsync(Company(), UserId(), new(revisionId, segmentId, sessionId, audienceId, 0, 1), ct);
            return File(preview.Audio, preview.ContentType);
        });

    private Guid Company() => context.CompanyId ?? throw new UnauthorizedAccessException();
    private Guid UserId() => context.UserId ?? throw new UnauthorizedAccessException();
    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        try { return await action(); }
        catch (SalesNarrationException e) { return Problem(statusCode: e.StatusCode, title: "Narration unavailable", detail: e.Message); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (DbUpdateException) { return Problem(statusCode: 409, title: "Narration changed", detail: "Reload and retry the command."); }
    }
}


