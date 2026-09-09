using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Api.ProblemHandling;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/sales/meeting-sessions/{sessionId:guid}/voice")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class SalesMeetingRealtimeController(
    ISalesMeetingRealtimeService service,
    ICompanyContextAccessor companyContext) : ControllerBase
{
    [HttpGet("status")]
    public Task<ActionResult<SalesMeetingRealtimeStatusDto>> GetStatusAsync(Guid sessionId, CancellationToken cancellationToken) =>
        ExecuteAsync(() => service.GetStatusAsync(CompanyId(), UserId(), sessionId, cancellationToken));

    [HttpPost("sessions")]
    public Task<ActionResult<SalesMeetingRealtimeStartResult>> StartAsync(Guid sessionId,
        [FromBody] StartSalesMeetingRealtimeRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => service.StartAsync(CompanyId(), UserId(), sessionId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("events")]
    public Task<ActionResult<SalesMeetingRealtimeEventResult>> EventAsync(Guid sessionId,
        [FromBody] SubmitSalesMeetingRealtimeEventRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => service.ProcessEventAsync(CompanyId(), UserId(), sessionId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("responses/cancel")]
    public Task<ActionResult<SalesMeetingRealtimeCancelResult>> CancelAsync(Guid sessionId,
        [FromBody] CancelSalesMeetingRealtimeResponseRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => service.CancelResponseAsync(CompanyId(), UserId(), sessionId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("sessions/{voiceSessionId:guid}/stop")]
    public Task<ActionResult<SalesMeetingRealtimeStatusDto>> StopAsync(Guid sessionId, Guid voiceSessionId,
        [FromBody] StopSalesMeetingRealtimeRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => service.StopAsync(CompanyId(), UserId(), sessionId, voiceSessionId, request,
            HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("sessions/{voiceSessionId:guid}/revoke-consent")]
    public Task<ActionResult<SalesMeetingRealtimeStatusDto>> RevokeConsentAsync(Guid sessionId, Guid voiceSessionId,
        [FromBody] StopSalesMeetingRealtimeRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => service.RevokeConsentAsync(CompanyId(), UserId(), sessionId, voiceSessionId,
            request.ExpectedVersion, HttpContext.TraceIdentifier, cancellationToken));

    private async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<T?>> action) where T : class
    {
        try
        {
            var result = await action();
            return result is null ? NotFound() : Ok(result);
        }
        catch (SalesMeetingRealtimeValidationException exception)
        {
            return BadRequest(StableProblemDetails.CreateValidation(HttpContext, exception.Errors,
                SalesMeetingRealtimeProblemCodes.InvalidRequest));
        }
        catch (SalesMeetingRealtimeConflictException exception)
        {
            var status = exception.Code switch
            {
                SalesMeetingRealtimeProblemCodes.QuotaExceeded => StatusCodes.Status429TooManyRequests,
                SalesMeetingRealtimeProblemCodes.Disabled or SalesMeetingRealtimeProblemCodes.Unavailable => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status409Conflict
            };
            return StatusCode(status, StableProblemDetails.Create(HttpContext, status, exception.Code,
                "Sales meeting voice unavailable", exception.Message));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    private Guid CompanyId() => companyContext.CompanyId is { } id && id != Guid.Empty
        ? id : throw new UnauthorizedAccessException("A resolved company is required.");
    private Guid UserId() => companyContext.UserId is { } id && id != Guid.Empty
        ? id : throw new UnauthorizedAccessException("A resolved user is required.");
}
