using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.ExecutionExceptions;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/execution-exceptions")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class ExecutionExceptionsController : ControllerBase
{
    private readonly IExecutionExceptionQueryService _exceptions;

    public ExecutionExceptionsController(IExecutionExceptionQueryService exceptions)
    {
        _exceptions = exceptions;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ExecutionExceptionDto>>> ListAsync(
        Guid companyId,
        [FromQuery] string? status,
        [FromQuery] string? kind,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _exceptions.ListAsync(companyId, status, kind, cancellationToken));
        }
        catch (ArgumentOutOfRangeException)
        {
            return BadRequest();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("{exceptionId:guid}")]
    public async Task<ActionResult<ExecutionExceptionDto>> GetAsync(
        Guid companyId,
        Guid exceptionId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _exceptions.GetAsync(companyId, exceptionId, cancellationToken));
        }
        catch (ArgumentOutOfRangeException)
        {
            return BadRequest();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}