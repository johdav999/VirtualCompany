using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Authorize(Policy = CompanyPolicies.AccountingAdmin)]
[RequireCompanyContext]
[Route("api/companies/{companyId:guid}/finance/close-compliance-release-readiness")]
public sealed class CloseComplianceReleaseReadinessController(
    ICloseComplianceReleaseReadinessService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CloseComplianceReleaseReadinessDto>> GetAsync(
        Guid companyId,
        [FromQuery] Guid? fiscalPeriodId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.GetAsync(new(companyId, fiscalPeriodId), cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}
