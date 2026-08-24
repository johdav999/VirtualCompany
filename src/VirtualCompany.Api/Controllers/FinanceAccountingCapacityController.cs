using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/finance/accounting-capacity")]
[Authorize(Policy = CompanyPolicies.AccountingView)]
[RequireCompanyContext]
public sealed class FinanceAccountingCapacityController(IAccountingCapacityService capacity) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AccountingCapacityReadModel>> GetAsync(Guid companyId,
        [FromQuery] string profile = AccountingCapacityProfileKeys.Small,
        CancellationToken cancellationToken = default) =>
        Ok(await capacity.GetAsync(new GetAccountingCapacityQuery(companyId, profile), cancellationToken));

    [HttpPost("retention/preview")]
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    public async Task<ActionResult<AccountingRetentionPreviewDto>> PreviewRetentionAsync(Guid companyId,
        [FromBody] AccountingRetentionPreviewRequest request,
        CancellationToken cancellationToken) =>
        Ok(await capacity.PreviewRetentionAsync(
            new PreviewAccountingRetentionCommand(companyId, request.BatchSize), cancellationToken));

    [HttpPost("retention/run")]
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    public async Task<ActionResult<AccountingRetentionCleanupResultDto>> RunRetentionAsync(Guid companyId,
        [FromBody] AccountingRetentionCleanupRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await capacity.RunRetentionCleanupAsync(new RunAccountingRetentionCleanupCommand(
                companyId, request.PreviewToken, request.BatchSize, ResolveActorUserId(), request.Reason,
                request.CorrelationId), cancellationToken));
        }
        catch (AccountingLifecycleException exception)
        {
            var status = exception.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
            var problem = new ProblemDetails
            {
                Status = status,
                Title = exception.IsConflict ? "Cleanup preview changed" : "Cleanup is not eligible",
                Detail = exception.Message
            };
            problem.Extensions["code"] = exception.ReasonCode;
            problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
            return StatusCode(status, problem);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid accounting cleanup request",
                Detail = exception.Message,
                Extensions = { ["traceId"] = HttpContext.TraceIdentifier }
            });
        }
        catch (FormatException)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid accounting cleanup request",
                Detail = "The cleanup preview token is invalid.",
                Extensions = { ["traceId"] = HttpContext.TraceIdentifier }
            });
        }
    }

    private Guid ResolveActorUserId()
    {
        var value = User.FindFirstValue(CurrentUserClaimTypes.UserId) ??
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out var userId) && userId != Guid.Empty
            ? userId
            : throw new UnauthorizedAccessException("An authenticated user id is required for accounting cleanup.");
    }
}

public sealed record AccountingRetentionPreviewRequest(int BatchSize = 100);
public sealed record AccountingRetentionCleanupRequest(
    string PreviewToken,
    int BatchSize,
    string Reason,
    string? CorrelationId = null);
