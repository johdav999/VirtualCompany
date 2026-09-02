using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/finance/autonomy/triggers")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed class FinanceAutonomyTriggersController(IFinanceAutonomyTriggerService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<FinanceAutonomyTriggerQueryResult>> Get(
        Guid companyId, [FromQuery] int take = 100, CancellationToken cancellationToken = default) =>
        await Execute(() => service.GetOperationalStateAsync(companyId, take, cancellationToken));

    [HttpPost("{cursorId:guid}/retry")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<FinanceAutonomyTriggerCursorDto>> Retry(
        Guid companyId, Guid cursorId, [FromQuery] long expectedVersion,
        CancellationToken cancellationToken = default) =>
        await Execute(() => service.RetryDeadLetterAsync(companyId, cursorId, expectedVersion, cancellationToken));

    private async Task<ActionResult<T>> Execute<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (FinanceAutonomyRunValidationException ex)
        { return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors, StringComparer.OrdinalIgnoreCase))); }
        catch (FinanceAutonomyRunConcurrencyException ex)
        { return Conflict(new ProblemDetails { Title = "Finance autonomy trigger changed", Detail = ex.Message, Status = StatusCodes.Status409Conflict }); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }
}
