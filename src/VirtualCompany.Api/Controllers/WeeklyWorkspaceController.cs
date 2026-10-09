using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/workspace/weekly")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class WeeklyWorkspaceController(IWeeklyWorkspaceQueryService workspace) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<WeeklyWorkspaceDto>> GetAsync(Guid companyId, [FromQuery] string? lens,
        [FromQuery] DateOnly? week, CancellationToken token)
    {
        try { return Ok(await workspace.GetAsync(new(companyId, lens, week), token)); }
        catch (ArgumentException ex) { ModelState.AddModelError("week", ex.Message); return ValidationProblem(ModelState); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
    }
}
