using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("internal/companies/{companyId:guid}/finance/fiscal-periods/{fiscalPeriodId:guid}/reporting")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed class ReportingPeriodsController : ControllerBase
{
    private readonly IReportingPeriodCloseService _service;

    public ReportingPeriodsController(IReportingPeriodCloseService service)
    {
        _service = service;
    }

    [HttpPost("validation")]
    public async Task<ActionResult<ReportingPeriodCloseValidationResultDto>> ValidateAsync(
        Guid companyId,
        Guid fiscalPeriodId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.ValidateAsync(
                new ValidateReportingPeriodCloseQuery(companyId, fiscalPeriodId),
                cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(CreateProblemDetails("Fiscal period was not found.", ex.Message, StatusCodes.Status404NotFound, "fiscal_period_not_found"));
        }
    }

    [HttpPost("lock")]
    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    public async Task<ActionResult<ReportingPeriodLockStateDto>> LockAsync(
        Guid companyId,
        Guid fiscalPeriodId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.LockAsync(
                new LockReportingPeriodCommand(companyId, fiscalPeriodId),
                cancellationToken));
        }
        catch (ReportingPeriodOperationException ex)
        {
            return Conflict(CreateProblemDetails(ex.Title, ex.Message, StatusCodes.Status409Conflict, ex.Code));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(CreateProblemDetails("Fiscal period was not found.", ex.Message, StatusCodes.Status404NotFound, "fiscal_period_not_found"));
        }
    }

    [HttpPost("unlock")]
    [Authorize(Policy = CompanyPolicies.CompanyOwnerOrAdmin)]
    public async Task<ActionResult<ReportingPeriodLockStateDto>> UnlockAsync(
        Guid companyId,
        Guid fiscalPeriodId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.UnlockAsync(
                new UnlockReportingPeriodCommand(companyId, fiscalPeriodId),
                cancellationToken));
        }
        catch (ReportingPeriodOperationException ex)
        {
            return Conflict(CreateProblemDetails(ex.Title, ex.Message, StatusCodes.Status409Conflict, ex.Code));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(CreateProblemDetails("Fiscal period was not found.", ex.Message, StatusCodes.Status404NotFound, "fiscal_period_not_found"));
        }
    }

    [HttpPost("stored-statements/regenerate")]
    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    public async Task<ActionResult<ReportingPeriodRegenerationRequestResultDto>> RegenerateAsync(
        Guid companyId,
        Guid fiscalPeriodId,
        [FromBody] RegenerateStoredReportingStatementsRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.RegenerateStoredStatementsAsync(
                new RegenerateStoredReportingStatementsCommand(
                    companyId,
                    fiscalPeriodId,
                    request?.RunInBackground ?? false),
                cancellationToken);

            return result.Queued
                ? Accepted(result)
                : Ok(result);
        }
        catch (ReportingPeriodOperationException ex)
        {
            return Conflict(CreateProblemDetails(ex.Title, ex.Message, StatusCodes.Status409Conflict, ex.Code));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(CreateProblemDetails("Fiscal period was not found.", ex.Message, StatusCodes.Status404NotFound, "fiscal_period_not_found"));
        }
    }

    private ProblemDetails CreateProblemDetails(
        string title,
        string detail,
        int status,
        string code)
    {
        var problem = new ProblemDetails
        {
            Title = title,
            Detail = detail,
            Status = status,
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = code;
        return problem;
    }
}

public sealed record RegenerateStoredReportingStatementsRequest(bool RunInBackground = false);