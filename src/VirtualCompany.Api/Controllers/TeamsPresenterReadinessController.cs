using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/platform/teams-presenter/readiness")]
[Authorize(Policy = CompanyPolicies.PlatformAdministration)]
public sealed class TeamsPresenterReadinessController(ITeamsPresenterReadinessService service) : ControllerBase
{
    [HttpGet]
    public Task<TeamsPresenterReadinessDto> GetAsync(
        [FromQuery] Guid? companyId,
        CancellationToken cancellationToken) =>
        service.GetReadinessAsync(companyId, cancellationToken);
}
