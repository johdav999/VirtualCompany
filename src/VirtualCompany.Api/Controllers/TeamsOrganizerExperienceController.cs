using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/sales/meeting-sessions/{sessionId:guid}/teams-presenter")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class TeamsOrganizerExperienceController(
    ITeamsOrganizerExperienceService service,
    ICompanyContextAccessor company) : ControllerBase
{
    [HttpGet("state")]
    public async Task<ActionResult<TeamsOrganizerPresenterStateDto>> GetAsync(Guid sessionId, CancellationToken ct)
    {
        try
        {
            Guid? tenantId = Guid.TryParse(User.FindFirstValue("tid"), out var parsed) ? parsed : null;
            return Ok(await service.GetAsync(CompanyId(), UserId(), sessionId, tenantId, ct));
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    private Guid CompanyId() => company.CompanyId is { } id && id != Guid.Empty ? id :
        throw new UnauthorizedAccessException("A resolved company is required.");
    private Guid UserId() => company.UserId is { } id && id != Guid.Empty ? id :
        throw new UnauthorizedAccessException("A resolved user is required.");
}
