using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/finance/operating-mode")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed class FinanceOperatingModeController(IFinanceOperatingModeService operatingModeService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<FinanceOperatingModeDecisionDto>> GetAsync(
        Guid companyId,
        [FromQuery] DateOnly? asOfDate,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await operatingModeService.GetAsync(
                new GetFinanceOperatingModeQuery(companyId, asOfDate), cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}
