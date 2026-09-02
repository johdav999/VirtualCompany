using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/workspace/today")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class TodayWorkspaceController(ITodayWorkspaceQueryService workspace) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TodayWorkspaceDto>> GetAsync(
        Guid companyId,
        [FromQuery] string? lens,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await workspace.GetAsync(new GetTodayWorkspaceQuery(companyId, lens), cancellationToken));
        }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(nameof(lens), exception.Message);
            return ValidationProblem(ModelState);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
