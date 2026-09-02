using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/finance/autonomy/budgets")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed class FinanceAutonomyBudgetsController(IFinanceAutonomyBudgetService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<FinanceAutonomyBudgetQueryResult>> Get(Guid companyId,
        [FromQuery] int take = 100, CancellationToken cancellationToken = default) =>
        await Execute(() => service.GetAsync(companyId, take, cancellationToken));

    [HttpPut("policies")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<FinanceAutonomyBudgetPolicyDto>> UpsertPolicy(Guid companyId,
        [FromBody] UpsertFinanceAutonomyBudgetPolicyCommand command,
        CancellationToken cancellationToken = default) =>
        await Execute(() => service.UpsertPolicyAsync(companyId, command, cancellationToken));

    [HttpPost("circuits/{circuitId:guid}/reset")]
    [Authorize(Policy = CompanyPolicies.CompanyManager)]
    public async Task<ActionResult<FinanceAutonomyCircuitBreakerDto>> ResetCircuit(Guid companyId,
        Guid circuitId, [FromQuery] long expectedVersion, CancellationToken cancellationToken = default) =>
        await Execute(() => service.ResetCircuitAsync(companyId, circuitId, expectedVersion, cancellationToken));

    private async Task<ActionResult<T>> Execute<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (FinanceAutonomyBudgetValidationException ex)
        { return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors, StringComparer.OrdinalIgnoreCase))); }
        catch (FinanceAutonomyBudgetConcurrencyException ex)
        { return Conflict(new ProblemDetails { Title = "Finance autonomy budget changed", Detail = ex.Message, Status = StatusCodes.Status409Conflict }); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }
}
