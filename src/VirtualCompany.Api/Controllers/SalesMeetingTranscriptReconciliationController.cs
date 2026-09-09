using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Api.ProblemHandling;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
[Route("api/sales/meeting-sessions/{sessionId:guid}/transcript-reconciliation")]
public sealed class SalesMeetingTranscriptReconciliationController(
    ISalesMeetingTranscriptSubscriptionService subscriptions,
    ISalesMeetingTranscriptReconciliationQuery query,
    ICompanyContextAccessor companyContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SalesMeetingTranscriptReconciliationStatusDto>> GetAsync(Guid sessionId,
        CancellationToken cancellationToken)
    {
        var value = await query.GetStatusAsync(CompanyId(), UserId(), sessionId, cancellationToken);
        return value is null ? NotFound() : Ok(value);
    }

    [HttpPost("subscription")]
    public Task<ActionResult<SalesMeetingTranscriptSubscriptionDto>> EnsureSubscriptionAsync(Guid sessionId,
        [FromBody] CreateSalesMeetingTranscriptSubscriptionRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => subscriptions.EnsureAsync(CompanyId(), UserId(), sessionId, request,
            HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("subscription/{subscriptionId:guid}/renew")]
    public Task<ActionResult<SalesMeetingTranscriptSubscriptionDto>> RenewSubscriptionAsync(Guid sessionId,
        Guid subscriptionId, CancellationToken cancellationToken) => ExecuteAsync(() => subscriptions.RenewAsync(
            CompanyId(), UserId(), sessionId, subscriptionId, HttpContext.TraceIdentifier, cancellationToken));

    private async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<T?>> action) where T : class
    {
        try
        {
            var value = await action();
            return value is null ? NotFound() : Ok(value);
        }
        catch (SalesMeetingTranscriptPolicyException e)
        {
            return Conflict(StableProblemDetails.Create(HttpContext, StatusCodes.Status409Conflict, e.Code,
                "Meeting transcript reconciliation unavailable", e.Message));
        }
        catch (MeetingTranscriptProviderException e)
        {
            return Conflict(StableProblemDetails.Create(HttpContext, StatusCodes.Status409Conflict,
                SalesMeetingTranscriptProblemCodes.ProviderPermissionRequired,
                "Microsoft Graph transcript access unavailable", e.Message));
        }
    }

    private Guid CompanyId() => companyContext.CompanyId is { } value && value != Guid.Empty ? value : throw new UnauthorizedAccessException("A resolved company is required.");
    private Guid UserId() => companyContext.UserId is { } value && value != Guid.Empty ? value : throw new UnauthorizedAccessException("A resolved user is required.");
}
