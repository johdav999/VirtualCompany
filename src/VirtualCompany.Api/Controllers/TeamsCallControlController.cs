using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using VirtualCompany.Api.ProblemHandling;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/sales/meeting-sessions/{sessionId:guid}/teams-call")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class TeamsCallControlController(
    ITeamsCallControlService calls,
    ITeamsPresenterRolloutPolicy rollout,
    ICompanyContextAccessor company) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TeamsMeetingCallDto>> GetAsync(Guid sessionId, CancellationToken ct)
    { var value = await calls.GetAsync(CompanyId(), UserId(), sessionId, ct); return value is null ? NotFound() : Ok(value); }

    [HttpPost("join")]
    public Task<ActionResult<TeamsMeetingCallDto>> JoinAsync(Guid sessionId, [FromBody] RequestTeamsCallJoin request, CancellationToken ct) =>
        ExecuteRolloutGatedAsync(() => calls.RequestJoinAsync(CompanyId(), UserId(), sessionId, request, HttpContext.TraceIdentifier, ct), ct);

    [HttpPost("leave")]
    public Task<ActionResult<TeamsMeetingCallDto>> LeaveAsync(Guid sessionId, [FromBody] RequestTeamsCallLeave request, CancellationToken ct) =>
        ExecuteNullableAsync(() => calls.RequestLeaveAsync(CompanyId(), UserId(), sessionId, request, HttpContext.TraceIdentifier, ct));

    [HttpPost("reconcile")]
    public Task<ActionResult<TeamsMeetingCallDto>> ReconcileAsync(Guid sessionId, [FromBody] RequestTeamsCallReconciliation request, CancellationToken ct) =>
        ExecuteNullableAsync(() => calls.RequestReconciliationAsync(CompanyId(), UserId(), sessionId, request, HttpContext.TraceIdentifier, ct));

    [HttpPost("audio/start")]
    public Task<ActionResult<TeamsMeetingCallDto>> StartAudioAsync(Guid sessionId, [FromBody] RequestTeamsCallMediaStart request, CancellationToken ct) =>
        ExecuteRolloutGatedAsync(() => calls.AuthorizeMediaStartAsync(CompanyId(), UserId(), sessionId, request, HttpContext.TraceIdentifier, ct), ct);

    [HttpPost("audio/stop")]
    public Task<ActionResult<TeamsMeetingCallDto>> StopAudioAsync(Guid sessionId, [FromBody] RequestTeamsCallMediaStop request, CancellationToken ct) =>
        ExecuteAsync(() => calls.StopMediaAsync(CompanyId(), UserId(), sessionId, request, HttpContext.TraceIdentifier, ct));

    [HttpPost("consent/revoke")]
    public Task<ActionResult<TeamsMeetingCallDto>> RevokeConsentAsync(Guid sessionId, [FromBody] RequestTeamsCallConsentRevocation request, CancellationToken ct) =>
        ExecuteAsync(() => calls.RevokeConsentAsync(CompanyId(), UserId(), sessionId, request, HttpContext.TraceIdentifier, ct));

    private async Task<ActionResult<TeamsMeetingCallDto>> ExecuteAsync(Func<Task<TeamsMeetingCallDto>> action)
    { try { return Ok(await action()); } catch (TeamsCallControlException e) { return Problem(e); } catch (KeyNotFoundException) { return NotFound(); } }
    private async Task<ActionResult<TeamsMeetingCallDto>> ExecuteRolloutGatedAsync(
        Func<Task<TeamsMeetingCallDto>> action, CancellationToken ct)
    {
        var decision = await rollout.EvaluateAsync(CompanyId(), UserId(), RequestTenantId(), ct,
            Guid.TryParse(RouteData.Values["sessionId"]?.ToString(), out var meetingId) ? meetingId : null);
        if (!decision.Allowed) return Problem(new TeamsCallControlException(decision.ReasonCode, decision.Message));
        return await ExecuteAsync(action);
    }
    private async Task<ActionResult<TeamsMeetingCallDto>> ExecuteNullableAsync(Func<Task<TeamsMeetingCallDto?>> action)
    { try { var value = await action(); return value is null ? NotFound() : Ok(value); } catch (TeamsCallControlException e) { return Problem(e); } }
    private ActionResult Problem(TeamsCallControlException e) => Conflict(StableProblemDetails.Create(HttpContext,
        StatusCodes.Status409Conflict, e.Code, "Teams call control conflict", e.Message));
    private Guid CompanyId() => company.CompanyId is { } id && id != Guid.Empty ? id : throw new UnauthorizedAccessException("A resolved company is required.");
    private Guid UserId() => company.UserId is { } id && id != Guid.Empty ? id : throw new UnauthorizedAccessException("A resolved user is required.");
    private Guid? RequestTenantId() => Guid.TryParse(User.FindFirstValue("tid"), out var tenantId) ? tenantId : null;
}
