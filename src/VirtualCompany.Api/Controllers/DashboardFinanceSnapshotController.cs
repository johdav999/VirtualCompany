using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class DashboardFinanceSnapshotController : ControllerBase
{
    private readonly IDashboardFinanceSnapshotService _financeSnapshotService;
    private readonly ICompanyContextAccessor _companyContextAccessor;

    public DashboardFinanceSnapshotController(
        IDashboardFinanceSnapshotService financeSnapshotService,
        ICompanyContextAccessor companyContextAccessor)
    {
        _financeSnapshotService = financeSnapshotService;
        _companyContextAccessor = companyContextAccessor;
    }

    [HttpGet("finance-snapshot")]
    public async Task<ActionResult<DashboardFinanceSnapshotDto>> GetAsync(
        [FromQuery] Guid companyId,
        [FromQuery] DateTime? asOfUtc,
        CancellationToken cancellationToken,
        [FromQuery] int upcomingWindowDays = 30)
    {
        var resolvedCompanyId = companyId != Guid.Empty ? companyId : _companyContextAccessor.CompanyId;
        if (resolvedCompanyId is not Guid effectiveCompanyId)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["companyId"] = ["companyId is required."]
            })
            {
                Title = "Validation failed",
                Status = StatusCodes.Status400BadRequest
            });
        }

        return Ok(await _financeSnapshotService.GetAsync(
            effectiveCompanyId,
            asOfUtc,
            upcomingWindowDays,
            cancellationToken));
    }
}
