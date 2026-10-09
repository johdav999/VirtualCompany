using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/authority-explanation")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class AuthorityExplanationController(IAuthorityExplanationQueryService query) : ControllerBase
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<AuthorityExplanationDto>> Get(Guid companyId, [FromQuery] Guid? agentId,
        [FromQuery] string? workKind, [FromQuery] Guid? workId, CancellationToken cancellationToken)
    {
        try { return Ok(await query.GetAsync(companyId, agentId, workKind, workId, cancellationToken)); }
        catch (ArgumentException ex) { return BadRequest(new ProblemDetails { Title = "Invalid authority context", Detail = ex.Message }); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
    }
}
