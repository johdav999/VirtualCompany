using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/finance/reset")]
[Authorize(Policy = CompanyPolicies.FinanceSandboxAdmin)]
[RequireCompanyContext]
public sealed class FinanceMaintenanceController : ControllerBase
{
    private readonly IFinanceMaintenanceService _financeMaintenanceService;

    public FinanceMaintenanceController(IFinanceMaintenanceService financeMaintenanceService)
    {
        _financeMaintenanceService = financeMaintenanceService;
    }

    [HttpPost]
    public async Task<ActionResult<FinanceDataResetResultDto>> ResetAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _financeMaintenanceService.ResetFinancialDataAsync(companyId, cancellationToken));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Company was not found.",
                Detail = ex.Message
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid finance reset request.",
                Detail = ex.Message
            });
        }
    }
}
