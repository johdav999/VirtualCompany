using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/workspace/monthly")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class MonthlyWorkspaceController(IMonthlyWorkspaceQueryService workspace) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MonthlyWorkspaceDto>> GetAsync(
        Guid companyId,
        [FromQuery] string? lens,
        [FromQuery] int? year,
        [FromQuery] int? month,
        CancellationToken cancellationToken)
    {
        try { return Ok(await workspace.GetAsync(new(companyId, lens, year, month), cancellationToken)); }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError("period", exception.Message);
            return ValidationProblem(ModelState);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
    }
}
